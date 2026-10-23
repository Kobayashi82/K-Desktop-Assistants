using KDesktop.Assistant.Sdk;
using Kobii.Assistant.Audio;
using Kobii.Assistant.Rendering;
using Kobii.Core.Animation;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using AnimGesture = Kobii.Core.Animation.Gesture;
using SdkGesture = KDesktop.Assistant.Sdk.Gesture;
using SdkRect = KDesktop.Assistant.Sdk.PixelRect;
using SdkWindow = KDesktop.Assistant.Sdk.WindowInfo;

namespace Kobii.Assistant;

/// <summary>
/// Las maniobras de las órdenes de K-Desktop, a lo Kobii (que anda y trepa, no vuela): lanza la pelota al botón (la
/// ventana cambia justo cuando le da: ahí va el <see cref="IGestureContext.Commit"/>), llama a una ventana silbando y
/// luego sube a ella, señala con la mano, clava una banderita, lee la pantalla con sus gafas, «teclea» y, al abrir algo,
/// mira a ver dónde sale y se sube a la ventana nueva. Si está lejos, se acerca andando (o corriendo) lo que le da
/// tiempo por su plataforma. En «Rápido» no se mueve del sitio y nada pasa de medio segundo.
/// </summary>
public sealed partial class KobiiAssistant
{
    /// <summary>Lo que sigue «de encargo» tras una maniobra (el siguiente paso suele llegar antes).</summary>
    private const double LingerSeconds = 1.2;

    /// <summary>Lo que espera, tras abrir algo, a que aparezca su ventana.</summary>
    private const double AwaitWindowSeconds = 8;

    /// <summary>Lo que espera a estar de pie para subirse a la ventana tras la orden; después lo deja.</summary>
    private const double PerchWaitSeconds = 4;

    private const uint MinimizeColor = 0x42A5F5;
    private const uint MaximizeColor = 0x66BB6A;
    private const uint CloseColor = 0xFF7043;
    private const uint CallColor = 0xFFCA28;
    private const uint PointColor = 0xFFCA28;
    private const uint ScanColor = 0xFFE082;

    private readonly List<TaskCompletionSource> _frames = [];
    private ManoeuvreLayer _layer = null!;
    private KobiiAudio? _audio;
    private int _manoeuvres;
    private double _lingerUntil = double.NaN;
    private long _perchAfter;
    private double _perchUntil;
    private (string? Process, HashSet<long> Before, double Until)? _awaitedWindow;
    private bool _perchOnAppear;

    /// <summary>Cómo de vistosa es cada maniobra según la rapidez elegida en K-Desktop.</summary>
    /// <param name="Walk">Segundos andando como mucho para acercarse (0 = desde donde está).</param>
    /// <param name="Run">Se acerca corriendo.</param>
    /// <param name="Prepare">Lo que tarda en sacar la pelota o las gafas.</param>
    /// <param name="BallSpeed">Píxeles por segundo de la pelota (a escala 1).</param>
    /// <param name="MinFlight">Lo que tarda la pelota como poco, aunque esté cerca (que se vea volar).</param>
    /// <param name="MaxFlight">Lo que tarda la pelota como mucho, aunque esté lejos.</param>
    /// <param name="Show">Lo que dura lo que se ve (señalar, el escáner, el tecleo).</param>
    /// <param name="Range">A cuántas alturas de Kobii ya está «a tiro» y no hace falta acercarse.</param>
    private readonly record struct Pace(double Walk, bool Run, double Prepare, double BallSpeed, double MinFlight, double MaxFlight, double Show, double Range);

    private static Pace PaceOf(ManoeuvreSpeed speed) => speed switch
    {
        ManoeuvreSpeed.Fast => new(0, true, 0.03, 2600, 0.11, 0.22, 0.25, double.PositiveInfinity),
        ManoeuvreSpeed.Showy => new(2.6, false, 0.3, 560, 0.5, 1.25, 1.4, 6),
        _ => new(1.4, true, 0.18, 800, 0.35, 0.9, 0.9, 10),
    };

    /// <summary>
    /// Puede ponerse con una orden: a la vista y sin que lo tengas cogido (si está en el aire de un saltito o trepando,
    /// espera un momento a estar de pie: <see cref="SettleAsync"/>).
    /// </summary>
    private bool CanManoeuvre => _game.IsInitialized && !_hidden && _exitStart is null && !_dragging && !_game.Body.IsHeld;

    /// <summary>Espera (como mucho 0,8 s) a estar de pie para empezar la maniobra.</summary>
    private async Task SettleAsync(CancellationToken ct)
    {
        var until = _now + 0.8;
        while (!_game.CanRunErrands && _now < until)
        {
            await NextFrameAsync(ct);
        }
    }

    private static bool IsManoeuvre(GestureKind kind) => kind is GestureKind.PressButton or GestureKind.ActivateWindow or GestureKind.PointAt
        or GestureKind.GoTo or GestureKind.MoveWindow or GestureKind.Mark or GestureKind.Scan or GestureKind.Typing or GestureKind.WaitForWindow;

    /// <summary>Lo que se le pide está en un monitor con algo a pantalla completa: sin maniobras (lo hace el módulo).</summary>
    private bool OnFullscreen(SdkGesture gesture)
    {
        var point = gesture switch
        {
            SdkGesture.PressButton p => p.ButtonBounds.Center,
            SdkGesture.ActivateWindow a when !a.Window.Bounds.IsEmpty => a.Window.Bounds.Center,
            SdkGesture.MoveWindow m when !m.Window.Bounds.IsEmpty => m.Window.Bounds.Center,
            SdkGesture.Typing { Window: { } w } => w.Bounds.Center,
            SdkGesture.Scan s => s.Area.Center,
            SdkGesture.PointAt p => PlaceCenter(p.Target),
            SdkGesture.GoTo g => PlaceCenter(g.Target),
            SdkGesture.Mark m => PlaceCenter(m.Target),
            _ => (System.Windows.Point?)null,
        };
        var world = _host.World.Current;
        return point is { } at && world.MonitorAt(at) is { } monitor && _host.World.System.FullscreenMonitors.Contains(monitor.Index);
    }

    private async Task<GestureOutcome> ManoeuvreAsync(SdkGesture gesture, IGestureContext ctx, CancellationToken ct)
    {
        _manoeuvres++;
        _lingerUntil = double.NaN;
        _game.BeginErrand();
        var pace = PaceOf(ctx.Speed);
        var committed = false;
        GestureOutcome? outcome = null;
        void Commit()
        {
            committed = true;
            ctx.Commit();
        }

        _host.Log.Debug($"Maniobra {gesture.Kind} ({ctx.Speed}, {ctx.TimeLeft.TotalSeconds:0.0} s), paso {ctx.Step} de {ctx.StepCount}");
        try
        {
            await SettleAsync(ct);
            switch (gesture)
            {
                case SdkGesture.PressButton press:
                    await ThrowAtButtonAsync(press, pace, ctx, Commit, ct);
                    break;
                case SdkGesture.ActivateWindow activate:
                    await CallWindowAsync(activate.Window, pace, Commit, ct);
                    break;
                case SdkGesture.PointAt point:
                    await PointAsync(point.Target, pace, Commit, ct);
                    break;
                case SdkGesture.GoTo go:
                    await GoAsync(go.Target, pace, ctx, ct);
                    Commit();
                    break;
                case SdkGesture.Mark mark:
                    await MarkAsync(mark, pace, ctx, Commit, ct);
                    break;
                case SdkGesture.Scan scan:
                    await ScanAsync(scan.Area, pace, ctx, Commit, ct);
                    break;
                case SdkGesture.Typing typing:
                    await TypeAsync(typing.Window, pace, Commit, ct);
                    break;
                case SdkGesture.MoveWindow move:
                    outcome = await DragWindowAsync(move, pace, ctx, Commit, ct);
                    break;
                case SdkGesture.WaitForWindow wait:
                    await WaitForWindowAsync(wait.Process, pace, Commit, ct);
                    break;
            }

            return outcome ?? (committed ? GestureOutcome.Committed : GestureOutcome.NotPerformed);
        }
        catch (OperationCanceledException)
        {
            // Se acabó su tiempo o se canceló la orden: lo que ya ha pasado, ha pasado.
            return committed ? GestureOutcome.Committed : GestureOutcome.NotPerformed;
        }
        finally
        {
            _manoeuvres--;
            if (_manoeuvres == 0)
            {
                _game.HeldProp = Prop.None;
                _lingerUntil = _now + (ctx.Speed == ManoeuvreSpeed.Fast ? 0.3 : LingerSeconds);
                _host.RequestTick();
            }
        }
    }

    // La pelota al botón -----------------------------------------------------------------------------------------

    /// <summary>
    /// Se acerca si está lejos, saca la pelota y la lanza en arco al botón: al darle, la ventana cambia (y la pelota rebota
    /// y cae desvaneciéndose). Cerrar va con más fuerza (y en naranja).
    /// </summary>
    private async Task ThrowAtButtonAsync(SdkGesture.PressButton press, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        var target = ToVec(press.ButtonBounds.Center);
        if (!Seen(press.Window.Handle, target))
        {
            // El botón está tapado por otra ventana: la pelota la atravesaría. Lo pulsa K-Desktop sin más.
            _host.Log.Debug($"Maniobra: el botón de «{press.Window.Title}» está tapado; lo pulsa K-Desktop");
            commit();
            return;
        }

        var color = press.Button switch
        {
            CaptionButton.Close => CloseColor,
            CaptionButton.Minimize => MinimizeColor,
            _ => MaximizeColor,
        };

        await ApproachAsync(target, pace, ctx, pace.Prepare + Gestures.ThrowReleaseAt + pace.MaxFlight + 0.4, ct);
        var strong = press.Button == CaptionButton.Close;
        var hit = await ThrowAsync(target, pace, ctx, strong, flag: false, ct);
        commit();
        Impact(target, color, hit);
        _ = ReactToResultAsync(ctx.Committed);
    }

    /// <summary>Saca la pelota (o la banderita), la lanza y espera a que llegue. Devuelve la velocidad con la que llega.</summary>
    private async Task<Vec2> ThrowAsync(Vec2 target, Pace pace, IGestureContext ctx, bool strong, bool flag, CancellationToken ct)
    {
        var body = _game.Body;
        var side = target.X < body.Feet.X ? -1 : 1;
        _game.LookAt(target);
        if (!flag)
        {
            _game.HeldProp = Prop.Ball;
            Play(Sound.Pop, 0.6);
        }

        await DelayAsync(pace.Prepare, ct);
        _game.PlayGesture(AnimGesture.Throw, Gestures.ThrowSeconds, side);

        // Con prisa (o poco tiempo) la suelta antes de echar el brazo atrás del todo: el golpe tiene que llegar dentro del paso.
        var release = Math.Clamp(ctx.TimeLeft.TotalSeconds - pace.MaxFlight - 0.12, 0.08, Gestures.ThrowReleaseAt);
        await DelayAsync(release, ct);

        // ¡Allá va! Arco más alto cuanto más lejos y si el botón está por encima.
        _game.HeldProp = Prop.None;
        var from = _game.HandPoint;
        var distance = Vec2.Distance(from, target);
        var speed = pace.BallSpeed * body.Scale * (strong ? 1.25 : 1);
        var flight = Math.Clamp(distance / speed, pace.MinFlight, pace.MaxFlight);
        var peak = Math.Clamp(distance * 0.22, 24 * body.Scale, 240 * body.Scale) + Math.Max(0, from.Y - target.Y) * 0.15;
        _layer.Add(new ManoeuvreEffect(flag ? EffectKind.FlagFlight : EffectKind.Ball, _now, flight, from, target, PointColor, body.Scale, Peak: peak));
        Play(Sound.Whoosh, strong ? 0.8 : 0.6, from.X);
        if (strong)
        {
            _game.Visual.AddEmote(EmoteKind.Sparkle, from, body.Scale, 3);
        }

        await DelayAsync(flight, ct);

        // Velocidad al llegar (la derivada del arco): para que la pelota rebote hacia atrás.
        var arrival = (target - from) / flight + new Vec2(0, 4 * peak / flight);
        return arrival;
    }

    /// <summary>El golpe en el botón y la pelota que rebota y cae.</summary>
    private void Impact(Vec2 at, uint color, Vec2 arrival)
    {
        var scale = _game.Body.Scale;
        _layer.Add(new ManoeuvreEffect(EffectKind.Impact, _now, 0.45, at, at, color, scale));
        var bounce = new Vec2(-arrival.X * 0.25, -Math.Abs(arrival.Y) * 0.2 - 320 * scale);
        _layer.Add(new ManoeuvreEffect(EffectKind.BallFall, _now, 0.8, at, at, color, scale, Velocity: bounce));
        Play(Sound.Bonk, 0.9, at.X);
    }

    /// <summary>Si K-Desktop dice que la acción no salió (la ventana no se dejó), lo nota.</summary>
    private async Task ReactToResultAsync(Task<bool> committed)
    {
        try
        {
            if (!await committed.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(true))
            {
                _game.React(ReactionKind.Failure);
                _host.RequestTick();
            }
        }
        catch (TimeoutException)
        {
            // Sin noticias: no se dice nada.
        }
    }

    // Traer delante, señalar, ir a un sitio -----------------------------------------------------------------------

    /// <summary>
    /// Llama a la ventana silbando y moviendo la mano (unas ondas en su barra de título; minimizada, en la barra de
    /// tareas) y, cuando sale delante, se sube a ella si hay por dónde (salvo en «Rápido»).
    /// </summary>
    private async Task CallWindowAsync(SdkWindow window, Pace pace, Action commit, CancellationToken ct)
    {
        var scale = _game.Body.Scale;
        var title = window.Bounds.IsEmpty
            ? TaskbarPoint() ?? _game.HeadPoint
            : new Vec2(window.Bounds.Center.X, window.Bounds.Top + Math.Min(16 * scale, window.Bounds.Height / 2.0));
        var side = title.X < _game.Body.Feet.X ? -1 : 1;
        _game.LookAt(title);
        _game.PlayGesture(AnimGesture.Beckon, Math.Max(0.6, pace.Show), side);
        Play(Sound.Whistle, 0.7);
        _game.Visual.AddEmote(EmoteKind.Note, _game.HeadPoint, scale, 2);
        await DelayAsync(Math.Min(0.25, pace.Show), ct);
        _layer.Add(new ManoeuvreEffect(EffectKind.Ping, _now, 0.75, title, title, CallColor, scale));
        commit();
        if (pace.Walk > 0)
        {
            _perchAfter = window.Handle;
        }

        await DelayAsync(Math.Min(0.3, pace.Show), ct);
    }

    /// <summary>Señala el sitio con la mano: una línea de puntitos va de su mano hasta allí (el efecto ocurre al llegar).</summary>
    private async Task PointAsync(Place place, Pace pace, Action commit, CancellationToken ct)
    {
        var target = ToVec(PlaceCenter(place) ?? Cursor());
        var side = target.X < _game.Body.Feet.X ? -1 : 1;
        _game.LookAt(target);
        _game.PlayGesture(AnimGesture.Point, pace.Show + 0.3, side);
        await DelayAsync(0.12, ct);
        var pointer = _layer.Add(new ManoeuvreEffect(EffectKind.Pointer, _now, pace.Show, _game.HandPoint, target, PointColor, _game.Body.Scale));
        Play(Sound.Pop, 0.5);
        await FollowAsync(pointer, Math.Min(0.2, pace.Show / 2), ct);
        commit();
        await FollowAsync(pointer, pace.Show - Math.Min(0.2, pace.Show / 2), ct);
    }

    /// <summary>
    /// Va al sitio: a la bandeja o al reloj andando por la barra de tareas (si está en ella); a una ventana, subiéndose; si
    /// no llega a tiempo, se queda señalándolo.
    /// </summary>
    private async Task GoAsync(Place place, Pace pace, IGestureContext ctx, CancellationToken ct)
    {
        var target = place switch
        {
            Place.Tray or Place.Clock => TrayPoint(place is Place.Clock),
            _ => PlaceCenter(place) is { } p ? ToVec(p) : null,
        };
        if (target is not { } at)
        {
            return;
        }

        _game.LookAt(at);
        var budget = Math.Min(pace.Walk * 1.5, ctx.TimeLeft.TotalSeconds - 1.5);
        if (place is Place.OfWindow w && budget > 0.5 && _game.GoToWindow(w.Window.Handle, run: pace.Run, errand: true))
        {
            await TravelAsync(budget, ct);
            return;
        }

        if (budget > 0.4 && Math.Abs(_game.Body.Feet.Y - at.Y) < _game.Body.Height * 2.5 && _game.WalkTo(at.X, pace.Run))
        {
            await WalkAsync(budget, ct);
        }

        if (Vec2.Distance(_game.Body.Center, at) > _game.Body.Height * 3)
        {
            await PointAsync(place, pace, () => { }, ct);
        }
    }

    /// <summary>El centro de la barra de tareas principal (de donde sale una ventana minimizada).</summary>
    private Vec2? TaskbarPoint()
    {
        var world = _host.World.Current;
        var primary = world.PrimaryMonitor;
        var bar = world.Taskbars.FirstOrDefault(t => t.Monitor == primary?.Index && !t.Bounds.IsEmpty);
        return bar is null ? null : ToVec(bar.Bounds.Center);
    }

    /// <summary>El punto de la bandeja (o del reloj) en la barra de tareas principal; si no se sabe, su esquina.</summary>
    private Vec2? TrayPoint(bool clock)
    {
        var world = _host.World.Current;
        var primary = world.PrimaryMonitor;
        var bar = world.Taskbars.FirstOrDefault(t => t.Monitor == primary?.Index) ?? (world.Taskbars.Count > 0 ? world.Taskbars[0] : null);
        if ((clock ? bar?.Clock ?? bar?.Tray : bar?.Tray ?? bar?.Clock) is { IsEmpty: false } area)
        {
            return ToVec(area.Center);
        }

        return primary is null ? null : new Vec2(primary.WorkArea.Right - 60 * primary.DpiScale, primary.WorkArea.Bottom);
    }

    // Banderita, gafas, tecleo, abrir algo -------------------------------------------------------------------------

    /// <summary>Lanza una banderita que se queda clavada en el sitio un rato (con el texto que traiga, si trae).</summary>
    private async Task MarkAsync(SdkGesture.Mark mark, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        var at = mark.Target switch
        {
            Place.OfWindow w => new Vec2(w.Window.Bounds.Center.X, w.Window.Bounds.Top),
            _ => ToVec(PlaceCenter(mark.Target) ?? Cursor()),
        };
        await ApproachAsync(at, pace, ctx, pace.Prepare + Gestures.ThrowReleaseAt + pace.MaxFlight + 0.3, ct);
        await ThrowAsync(at, pace, ctx, strong: false, flag: true, ct);
        var color = Species.Palette(_settings.Color).Accent;
        _layer.Add(new ManoeuvreEffect(EffectKind.Flag, _now, Math.Max(0.8, mark.Duration.TotalSeconds), at, at, color, _game.Body.Scale, Text: mark.Text));
        Play(Sound.Tap, 0.8, at.X);
        commit();
        await DelayAsync(Math.Min(0.2, pace.Show), ct);
    }

    /// <summary>
    /// Se pone las gafas y lee la zona: un haz suave sale de ellas hasta la línea que la recorre de arriba abajo; ya
    /// apagado (que no salga en la captura), la foto, y el flash después.
    /// </summary>
    private async Task ScanAsync(SdkRect area, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        if (area.IsEmpty)
        {
            commit();
            return;
        }

        var rect = new System.Windows.Rect(area.Left, area.Top, area.Width, area.Height);
        _game.HeldWear = Wear.Glasses;
        _game.LookAt(ToVec(area.Center));
        Play(Sound.Pop, 0.5);
        await DelayAsync(Math.Min(0.25, pace.Prepare + 0.1), ct);
        var beam = _layer.Add(new ManoeuvreEffect(EffectKind.Scan, _now, pace.Show, EyesPoint(), ToVec(area.Center), ScanColor, _game.Body.Scale, rect));
        var until = _now + pace.Show;
        while (_now < until)
        {
            beam.From = EyesPoint();
            await NextFrameAsync(ct);
        }

        // Un fotograma sin el haz antes de la foto.
        await NextFrameAsync(ct);
        commit();
        _ = FlashAsync(rect, ctx.Committed);
    }

    /// <summary>Entre los ojos (de donde sale el haz de las gafas).</summary>
    private Vec2 EyesPoint() => _game.WorldPoint(new Vec2(_game.Visual.Facing * 0.16, -1.05));

    private async Task FlashAsync(System.Windows.Rect area, Task<bool> committed)
    {
        try
        {
            await committed.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            // Sin noticias: el flash, igual.
        }

        _layer.Add(new ManoeuvreEffect(EffectKind.Flash, _now, 0.35, default, default, 0xFFFFFF, _game.Body.Scale, area));
        Play(Sound.Shutter, 0.8);
        _host.RequestTick();
    }

    /// <summary>«Teclea»: las teclas salen ya; él hace como que las pulsa un momento, mirando a la ventana.</summary>
    private async Task TypeAsync(SdkWindow? window, Pace pace, Action commit, CancellationToken ct)
    {
        if (window is not null)
        {
            _game.LookAt(ToVec(window.Bounds.Center));
        }

        commit();
        _game.PlayGesture(AnimGesture.Type, pace.Show + 0.3, _game.Animator.Side);
        _game.Visual.SetExpression(Expression.Curious, pace.Show + 0.3);
        var keys = Math.Max(1, (int)(pace.Show / 0.11));
        for (var i = 0; i < keys; i++)
        {
            if (i % 3 == 0)
            {
                _game.Visual.AddEmote(EmoteKind.Sparkle, _game.HandPoint, _game.Body.Scale);
            }

            Play(Sound.Tap, 0.35);
            await DelayAsync(0.11, ct);
        }
    }

    /// <summary>
    /// Abrir algo: lo deja abrir y mira a lo lejos con la mano de visera; cuando aparezca la ventana, la mira con un
    /// brinco y (salvo en «Rápido») se sube a ella. La espera sigue aunque la orden ya haya terminado.
    /// </summary>
    private async Task WaitForWindowAsync(string? process, Pace pace, Action commit, CancellationToken ct)
    {
        _awaitedWindow = (process, _host.World.Current.Windows.Select(w => w.Handle).ToHashSet(), _now + AwaitWindowSeconds);
        _perchOnAppear = pace.Walk > 0;
        commit();
        _game.PlayGesture(AnimGesture.Visor, Math.Max(0.4, pace.Show), _game.Animator.Side);
        await DelayAsync(Math.Min(0.4, pace.Show), ct);
    }

    /// <summary>Mientras espera la ventana de algo que acaba de abrirse: en cuanto sale, la mira y va a subirse a ella.</summary>
    private void UpdateAwaitedWindow()
    {
        if (_awaitedWindow is not { } awaited)
        {
            return;
        }

        if (_now > awaited.Until || _hidden)
        {
            _awaitedWindow = null;
            return;
        }

        var found = _host.World.Current.Windows.FirstOrDefault(w => !awaited.Before.Contains(w.Handle)
            && (awaited.Process is null || string.Equals(w.AppId, awaited.Process, StringComparison.OrdinalIgnoreCase)));
        if (found is null || found.Bounds.IsEmpty || OnFullscreen(new SdkGesture.ActivateWindow(found)))
        {
            return;
        }

        _awaitedWindow = null;
        _host.Log.Debug($"Maniobra: ha aparecido «{found.Title}»");
        var top = new Vec2(found.Bounds.Center.X, found.Bounds.Top);
        _game.LookAt(top);
        _game.Hop();
        _game.Visual.SetExpression(Expression.Surprised, 1.2);
        _game.Visual.AddEmote(EmoteKind.Exclamation, _game.HeadPoint, _game.Body.Scale);
        if (!_perchOnAppear)
        {
            return;
        }

        // Se sube en cuanto acabe la orden (o ya, si ha acabado).
        _perchAfter = found.Handle;
        _perchUntil = _now + PerchWaitSeconds;
    }

    // Andar, esperar y fin del encargo ---------------------------------------------------------------------------

    /// <summary>
    /// Si el sitio queda lejos (más de lo que alcanza a tirar), se acerca andando o corriendo por su plataforma lo que le
    /// dé tiempo, dejando <paramref name="reserve"/> segundos del paso para lo que viene después. En «Rápido», no.
    /// </summary>
    private async Task ApproachAsync(Vec2 target, Pace pace, IGestureContext ctx, double reserve, CancellationToken ct)
    {
        var body = _game.Body;
        if (Math.Abs(target.X - body.Feet.X) < pace.Range * body.Height || body.Support is not { } support)
        {
            return;
        }

        var budget = Math.Min(pace.Walk, ctx.TimeLeft.TotalSeconds - reserve);
        if (budget < 0.3)
        {
            return;
        }

        // Hasta quedar a tiro, por su lado (sin pasarse del borde de su plataforma).
        var side = target.X < body.Feet.X ? -1 : 1;
        var x = target.X - side * Math.Min(pace.Range * 0.6, 6) * body.Height;
        x = Math.Clamp(x, support.Left + body.HalfWidth, support.Right - body.HalfWidth);
        if (_game.WalkTo(x, pace.Run))
        {
            await WalkAsync(budget, ct);
        }
    }

    /// <summary>Espera a que llegue andando (o a que se acabe el tiempo; entonces se para donde esté).</summary>
    private async Task WalkAsync(double seconds, CancellationToken ct)
    {
        var until = _now + seconds;
        while (_now < until && _game.Activity == Activity.Walk)
        {
            await NextFrameAsync(ct);
        }

        if (_game.Activity == Activity.Walk)
        {
            _game.StopWalking();
        }
    }

    /// <summary>Espera a que acabe un recorrido (o a que se acabe el tiempo: el recorrido sigue igual).</summary>
    private async Task TravelAsync(double seconds, CancellationToken ct)
    {
        var until = _now + seconds;
        while (_now < until && _game.IsTraveling)
        {
            await NextFrameAsync(ct);
        }
    }

    /// <summary>Un efecto que sale de la mano mientras dura (la línea de señalar).</summary>
    private async Task FollowAsync(ManoeuvreEffect effect, double seconds, CancellationToken ct)
    {
        var until = _now + seconds;
        while (_now < until)
        {
            effect.From = _game.HandPoint;
            await NextFrameAsync(ct);
        }
    }

    private async Task DelayAsync(double seconds, CancellationToken ct)
    {
        var until = _now + seconds;
        while (_now < until)
        {
            await NextFrameAsync(ct);
        }
    }

    /// <summary>Espera al siguiente ciclo del motor (la simulación ya avanzada); como mucho 0,1 s si el host no lo da.</summary>
    private async Task NextFrameAsync(CancellationToken ct)
    {
        var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _frames.Add(frame);
        _host.RequestTick();
        try
        {
            await frame.Task.WaitAsync(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            _frames.Remove(frame);
        }
    }

    /// <summary>Al principio de cada ciclo: lo que esperaba un fotograma sigue (después de este ciclo).</summary>
    private void ReleaseFrames()
    {
        if (_frames.Count == 0)
        {
            return;
        }

        var waiting = _frames.ToArray();
        _frames.Clear();
        foreach (var frame in waiting)
        {
            frame.TrySetResult();
        }
    }

    /// <summary>Cada ciclo: la ventana que se espera, el final del encargo (y subirse a la ventana) y los efectos.</summary>
    private void UpdateManoeuvres(double now)
    {
        UpdateAwaitedWindow();
        if (!double.IsNaN(_lingerUntil) && now >= _lingerUntil && _manoeuvres == 0)
        {
            _lingerUntil = double.NaN;
            _game.EndErrand();
            if (_perchAfter > 0)
            {
                _perchUntil = now + PerchWaitSeconds;
            }
        }

        UpdatePerch(now);

        if (_hidden || _exitStart is not null)
        {
            _layer.Clear();
            return;
        }

        _layer.Render(now, 1);
    }

    /// <summary>
    /// Ya acabada la orden, se sube a la ventana que ha traído delante (o que se acaba de abrir) si hay por dónde: en cuanto
    /// esté de pie (puede estar en mitad de un brinco), y si en unos segundos no ha podido, lo deja.
    /// </summary>
    private void UpdatePerch(double now)
    {
        if (_perchAfter <= 0 || _manoeuvres > 0 || !double.IsNaN(_lingerUntil))
        {
            return;
        }

        if (now > _perchUntil || _game.Body.SupportId == _perchAfter)
        {
            _perchAfter = 0;
            return;
        }

        if (!_game.CanRunErrands || _game.IsTraveling)
        {
            return;
        }

        // Como parte de la orden: aunque siga hablando, no lo deja para atenderte.
        var perch = _perchAfter;
        _perchAfter = 0;
        _host.Log.Debug(_game.GoToWindow(perch, run: true, errand: true) ? "Maniobra: va a subirse a la ventana" : $"Maniobra: no se sube a la ventana: {_game.TravelProblem}");
    }

    /// <summary>Hay una maniobra en marcha, un efecto que dibujar o un encargo por terminar: a ritmo de fotogramas.</summary>
    private bool ManoeuvreActive => _manoeuvres > 0 || _frames.Count > 0 || _layer.IsActive || !double.IsNaN(_lingerUntil)
        || _awaitedWindow is not null || _perchAfter > 0;

    /// <summary>¿Se ve ese punto de la ventana (ninguna otra lo tapa)?</summary>
    private bool Seen(long handle, Vec2 point)
    {
        var at = new System.Windows.Point(point.X, point.Y);
        foreach (var window in _host.World.Current.Windows)
        {
            if (window.Handle == handle)
            {
                return true;
            }

            if (window.Bounds.Contains(at))
            {
                return false;
            }
        }

        return true;
    }

    private void Play(Sound sound, double gain, double? x = null)
    {
        if (_sensors.Quiet || _audio is null)
        {
            return;
        }

        _audio.Play(sound, gain, PanOf(x ?? _game.Body.Feet.X));
    }

    /// <summary>De −0,7 a 0,7 según dónde está en el escritorio entero.</summary>
    private double PanOf(double x)
    {
        var monitors = _host.World.Current.Monitors;
        if (monitors.Count == 0)
        {
            return 0;
        }

        double left = monitors.Min(m => m.Bounds.Left), right = monitors.Max(m => m.Bounds.Right);
        return right > left ? Math.Clamp(((x - left) / (right - left) * 2 - 1) * 0.7, -0.7, 0.7) : 0;
    }

    private System.Windows.Point? PlaceCenter(Place place)
    {
        var world = _host.World.Current;
        return place switch
        {
            Place.OfWindow w => w.Window.Bounds.Center,
            Place.AtPoint p => p.Point,
            Place.Area a => a.Rect.Center,
            Place.Monitor m => world.Monitors.FirstOrDefault(x => x.Index == m.Index)?.Bounds.Center,
            Place.Tray => TrayPoint(clock: false) is { } t ? new System.Windows.Point(t.X, t.Y) : null,
            Place.Clock => TrayPoint(clock: true) is { } c ? new System.Windows.Point(c.X, c.Y) : null,
            _ => null,
        };
    }

    private System.Windows.Point Cursor() =>
        _sensors.Cursor is { } c ? new System.Windows.Point(c.X, c.Y) : new System.Windows.Point(_game.Body.Feet.X, _game.Body.Feet.Y);

    private static Vec2 ToVec(System.Windows.Point p) => new(p.X, p.Y);
}
