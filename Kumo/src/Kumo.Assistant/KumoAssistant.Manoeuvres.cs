using KDesktop.Assistant.Sdk;
using Kumo.Assistant.Audio;
using Kumo.Core.Cloud;
using Kumo.Core.Geometry;
using SdkGesture = KDesktop.Assistant.Sdk.Gesture;
using SdkRect = KDesktop.Assistant.Sdk.PixelRect;
using SdkWindow = KDesktop.Assistant.Sdk.WindowInfo;

namespace Kumo.Assistant;

/// <summary>
/// Las maniobras de las órdenes de K-Desktop, a lo Kumo (que vuela): se acerca volando lo que le da tiempo y estira un dedo de
/// nube hasta el botón (la ventana cambia justo cuando lo toca: ahí va el <see cref="IGestureContext.Commit"/>), señala o llama
/// con un brazo de nube, va a la bandeja, marca un sitio con destellos, pasa por encima de lo que lee soltando una llovizna de
/// luz, teclea con el dedo y, al abrir algo, mira a ver dónde sale. Las ventanas que hay que mover las abraza por la barra de
/// título y se las lleva (<see cref="CloudPose.Hug"/>). En «Rápido» no se mueve del sitio y nada pasa de medio segundo.
/// Acabada la orden, al rato vuelve a donde estaba.
/// </summary>
public sealed partial class KumoAssistant
{
    /// <summary>Lo que espera, tras abrir algo, a que aparezca su ventana.</summary>
    private const double AwaitWindowSeconds = 8;

    /// <summary>Lo que se queda donde acabó la orden antes de volver a su sitio.</summary>
    private const double StayAfterOrder = 2.5;

    private (string? Process, HashSet<long> Before, double Until, bool Visit)? _awaitedWindow;
    private Vec2? _returnTo;
    private double _returnAt = double.NaN;

    /// <summary>Cómo de vistosa es cada maniobra según la rapidez elegida en K-Desktop.</summary>
    /// <param name="Fly">Píxeles por segundo volando (a escala 1).</param>
    /// <param name="MaxFly">Segundos volando como mucho para acercarse (0 = desde donde está).</param>
    /// <param name="Reach">Lo que tarda el dedo en llegar.</param>
    /// <param name="Show">Lo que dura lo que se ve (señalar, la llovizna de leer, el tecleo).</param>
    private readonly record struct Pace(double Fly, double MaxFly, double Reach, double Show);

    private static Pace PaceOf(ManoeuvreSpeed speed) => speed switch
    {
        ManoeuvreSpeed.Fast => new(0, 0, 0.12, 0.25),
        ManoeuvreSpeed.Showy => new(700, 2.4, 0.38, 1.4),
        _ => new(1150, 1.4, 0.24, 0.9),
    };

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
        return point is { } at && _host.World.Current.MonitorAt(at) is { } monitor && _host.World.System.FullscreenMonitors.Contains(monitor.Index);
    }

    private async Task<GestureOutcome> ManoeuvreAsync(SdkGesture gesture, IGestureContext ctx, CancellationToken ct)
    {
        _manoeuvres++;
        _returnTo ??= _cloud.Motion == Motion.Hover ? _cloud.Home : _cloud.Position;
        _returnAt = double.NaN;
        EndAction();
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
            switch (gesture)
            {
                case SdkGesture.PressButton press:
                    await PressAsync(press, pace, ctx, Commit, ct);
                    break;
                case SdkGesture.ActivateWindow activate:
                    await CallWindowAsync(activate.Window, pace, ctx, Commit, ct);
                    break;
                case SdkGesture.PointAt point:
                    await PointAsync(ToVec(PlaceCenter(point.Target) ?? CursorPoint()), pace, Commit, ct);
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
                    await TypeAsync(typing.Window, pace, ctx, Commit, ct);
                    break;
                case SdkGesture.WaitForWindow wait:
                    await WaitForWindowAsync(wait.Process, pace, Commit, ct);
                    break;
                case SdkGesture.MoveWindow move:
                    outcome = await CarryWindowAsync(move, pace, ctx, Commit, ct);
                    break;
            }

            return outcome ?? (committed ? GestureOutcome.Committed : GestureOutcome.NotPerformed);
        }
        catch (OperationCanceledException)
        {
            // Se acabó su tiempo o se canceló la orden: lo que ya ha pasado, ha pasado.
            return outcome ?? (committed ? GestureOutcome.Committed : GestureOutcome.NotPerformed);
        }
        finally
        {
            Release();
            _manoeuvres--;
            if (_manoeuvres == 0)
            {
                _cloud.Reach = 1;
                _cloud.SetPose(_basePose);
                _cloud.Hover(_cloud.Position);
                _returnAt = _now + (ctx.Speed == ManoeuvreSpeed.Fast ? 0.6 : StayAfterOrder);
                _host.RequestTick();
            }
        }
    }

    // Acercarse -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Vuela hasta <paramref name="spot"/> lo que le da tiempo (dejando <paramref name="reserve"/> segundos para lo que viene
    /// después) y se queda quieto ahí. En «Rápido», desde donde está.
    /// </summary>
    private async Task ApproachAsync(Vec2 spot, Pace pace, IGestureContext ctx, double reserve, CancellationToken ct)
    {
        spot = InRoom(spot);
        var distance = Vec2.Distance(spot, _cloud.Position);
        var available = Math.Min(pace.MaxFly, ctx.TimeLeft.TotalSeconds - reserve);
        if (pace.MaxFly <= 0 || distance < _unit * 0.3 || available < 0.2)
        {
            _cloud.Hold(_cloud.Position);
            return;
        }

        var speed = Math.Max(pace.Fly * _unit / BaseUnit, distance / available * 1.3);
        _cloud.FlyTo(spot, speed);
        Play(Sound.Whoosh, 0.3);
        var until = _now + available;
        while (_now < until && !_cloud.Arrived)
        {
            await NextFrameAsync(ct);
        }

        _cloud.Hold(_cloud.Arrived ? spot : _cloud.Position);
    }

    /// <summary>Un punto donde cabe su centro (en el monitor del punto o el más cercano).</summary>
    private Vec2 InRoom(Vec2 at)
    {
        var monitor = _host.World.Current.MonitorAt(new System.Windows.Point(at.X, at.Y)) ?? Nearest(at);
        return monitor is null ? at : Clamp(at, monitor);
    }

    /// <summary>
    /// Dónde ponerse para que el brazo llegue a <paramref name="target"/>: a lo largo de un brazo de él, desde el lado por el que
    /// viene (por debajo si puede: la nube no tapa lo de arriba de la ventana).
    /// </summary>
    private Vec2 ArmSpot(Vec2 target, double reach)
    {
        var from = _cloud.Position - target;
        var dir = from.LengthSquared < 1 ? new Vec2(-0.6, 0.8) : from.Normalized();

        // Que venga más bien desde abajo: el dedo apunta hacia arriba (o de lado), casi nunca hacia abajo.
        dir = new Vec2(dir.X, Math.Max(dir.Y, 0.45)).Normalized();
        return InRoom(target + dir * reach);
    }

    // El dedo -----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Se acerca, estira un dedo de nube hasta el botón y, al tocarlo, la ventana cambia (un anillo y un «pop»). Si el botón está
    /// tapado por otra ventana, lo pulsa K-Desktop sin más.
    /// </summary>
    private async Task PressAsync(SdkGesture.PressButton press, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        var target = ToVec(press.ButtonBounds.Center);
        if (!Seen(press.Window.Handle, target))
        {
            _host.Log.Debug($"Maniobra: el botón de «{press.Window.Title}» está tapado; lo pulsa K-Desktop");
            commit();
            return;
        }

        await ApproachAsync(ArmSpot(target, _cloud.FingerLength * 0.85), pace, ctx, pace.Reach + 0.35, ct);
        await PokeAsync(target, pace.Reach, ct);
        commit();
        _cloud.Burst(ParticleKind.Ring, target);
        Play(press.Button == CaptionButton.Close ? Sound.Boop : Sound.Pop, 0.55, target.X);
        await DelayAsync(Math.Min(0.18, pace.Show / 3), ct);
        await RetractAsync(pace.Reach * 0.8, ct);
        _ = ReactToResultAsync(ctx.Committed);
    }

    /// <summary>Estira el dedo hasta <paramref name="target"/> en <paramref name="seconds"/> (la punta llega justo al final).</summary>
    private async Task PokeAsync(Vec2 target, double seconds, CancellationToken ct)
    {
        _cloud.Aim = target;
        var full = ReachFor(target);
        _cloud.Reach = 0.15;
        _cloud.SetPose(CloudPose.Press);
        var start = _now;
        while (_now - start < seconds)
        {
            var f = Math.Clamp((_now - start) / seconds, 0, 1);
            _cloud.Reach = MathUtil.Lerp(0.15, full, 1 - (1 - f) * (1 - f));
            await NextFrameAsync(ct);
        }

        _cloud.Reach = full;

        // Los muelles de las bolas van un pelín detrás: que la punta llegue de verdad antes del efecto.
        await DelayAsync(0.06, ct);
    }

    private async Task RetractAsync(double seconds, CancellationToken ct)
    {
        var from = _cloud.Reach;
        var start = _now;
        while (_now - start < seconds)
        {
            _cloud.Reach = MathUtil.Lerp(from, 0.15, Math.Clamp((_now - start) / seconds, 0, 1));
            await NextFrameAsync(ct);
        }

        _cloud.SetPose(_basePose);
        _cloud.Reach = 1;
    }

    /// <summary>Lo que hay que estirar el brazo (<see cref="KumoCloud.Reach"/>) para que la punta llegue a ese punto.</summary>
    private double ReachFor(Vec2 target)
    {
        var distance = Vec2.Distance(target, _cloud.RenderPosition) / Math.Max(1, _cloud.Unit);
        return Math.Clamp((distance - 0.1) / 1.96, 0.3, 1.25);
    }

    /// <summary>Si K-Desktop dice que la acción no salió (la ventana no se dejó), lo nota.</summary>
    private async Task ReactToResultAsync(Task<bool> committed)
    {
        try
        {
            if (!await committed.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(true))
            {
                Act(CloudPose.Error, 1.4);
                _host.RequestTick();
            }
        }
        catch (TimeoutException)
        {
            // Sin noticias: no se dice nada.
        }
    }

    // El brazo ----------------------------------------------------------------------------------------------------------

    /// <summary>Llama a la ventana con el brazo (un anillo en su barra de título; minimizada, en la barra de tareas).</summary>
    private async Task CallWindowAsync(SdkWindow window, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        var target = window.Bounds.IsEmpty
            ? TaskbarPoint() ?? _cloud.Position
            : new Vec2(window.Bounds.Center.X, window.Bounds.Top + Math.Min(16 * _unit / BaseUnit, window.Bounds.Height / 2.0));
        if (Vec2.Distance(target, _cloud.Position) > _unit * 9)
        {
            await ApproachAsync(ArmSpot(target, _unit * 5), pace, ctx, pace.Show + 0.3, ct);
        }

        _cloud.Aim = target;
        _cloud.SetPose(CloudPose.Point);
        Play(Sound.Bell, 0.45);
        await DelayAsync(Math.Min(0.34, pace.Show), ct);
        _cloud.Burst(ParticleKind.Ring, target);
        commit();
        await DelayAsync(Math.Min(0.3, pace.Show), ct);
    }

    /// <summary>Señala con el brazo de nube (el efecto, si lo hay, cuando el brazo está estirado).</summary>
    private async Task PointAsync(Vec2 target, Pace pace, Action commit, CancellationToken ct)
    {
        _cloud.Aim = target;
        _cloud.SetPose(CloudPose.Point);
        Play(Sound.Pop, 0.35);
        await DelayAsync(Math.Min(0.34, pace.Show), ct);
        commit();
        await DelayAsync(Math.Max(0, pace.Show - 0.34), ct);
    }

    /// <summary>Va al sitio (a la bandeja, encima de la barra de tareas); si no llega a tiempo, lo señala desde donde esté.</summary>
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

        await ApproachAsync(at + new Vec2(-_unit * 0.6, -_unit * 1.6), pace, ctx, 0.3, ct);
        _cloud.Aim = at;
        _cloud.SetPose(CloudPose.Point);
        await DelayAsync(Math.Min(0.4, pace.Show), ct);
    }

    /// <summary>Marca el sitio: se acerca, lo señala y lo deja rodeado de destellos.</summary>
    private async Task MarkAsync(SdkGesture.Mark mark, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        var at = mark.Target switch
        {
            Place.OfWindow w => new Vec2(w.Window.Bounds.Center.X, w.Window.Bounds.Top),
            Place.Clock or Place.Tray => TrayPoint(mark.Target is Place.Clock) ?? ToVec(CursorPoint()),
            _ => ToVec(PlaceCenter(mark.Target) ?? CursorPoint()),
        };
        await ApproachAsync(ArmSpot(at, _unit * 3.2), pace, ctx, pace.Show + 0.4, ct);
        _cloud.Aim = at;
        _cloud.SetPose(CloudPose.Point);
        await DelayAsync(Math.Min(0.34, pace.Show), ct);
        _cloud.Burst(ParticleKind.Ring, at);
        _cloud.Burst(ParticleKind.Sparkle, at, 7);
        Play(Sound.Sparkle, 0.4, at.X);
        commit();
        await DelayAsync(Math.Min(0.5, pace.Show), ct);
    }

    // Leer, teclear, abrir ----------------------------------------------------------------------------------------------

    /// <summary>Pasa por encima de lo que se lee, de un lado a otro, soltando una llovizna de luz sobre ello.</summary>
    private async Task ScanAsync(SdkRect area, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        var top = Math.Max(area.Top - _unit * 0.9, (_host.World.Current.MonitorAt(area.Center)?.WorkArea.Top ?? area.Top) + _unit * 1.1);
        var left = new Vec2(area.Left + Math.Min(area.Width * 0.15, _unit * 2), top);
        var right = new Vec2(area.Right - Math.Min(area.Width * 0.15, _unit * 2), top);
        var (from, to) = Vec2.Distance(_cloud.Position, left) < Vec2.Distance(_cloud.Position, right) ? (left, right) : (right, left);
        await ApproachAsync(from, pace, ctx, pace.Show + 0.2, ct);
        _cloud.SetPose(CloudPose.Busy);
        commit();
        Play(Sound.Shutter, 0.35);
        var start = _now;
        var seconds = Math.Max(0.25, Math.Min(pace.Show * 1.4, ctx.TimeLeft.TotalSeconds - 0.15));
        from = InRoom(from);
        to = InRoom(to);
        while (_now - start < seconds)
        {
            var f = Math.Clamp((_now - start) / seconds, 0, 1);
            _cloud.Hold(Vec2.Lerp(from, to, f * f * (3 - 2 * f)));
            var fall = Math.Max(_unit, area.Bottom - _cloud.Position.Y);
            if (System.Random.Shared.NextDouble() < 0.7)
            {
                _cloud.Particles.Add(new Particle
                {
                    Kind = ParticleKind.Sparkle,
                    X = _cloud.Position.X + (System.Random.Shared.NextDouble() - 0.5) * 1.6 * _unit,
                    Y = _cloud.Position.Y + 0.5 * _unit,
                    VY = fall / 0.9,
                    Life = 0.9,
                    Size = (0.05 + System.Random.Shared.NextDouble() * 0.05) * _unit,
                    Side = System.Random.Shared.NextDouble() * 6,
                });
            }

            await NextFrameAsync(ct);
        }
    }

    /// <summary>Teclea con el dedo de nube en la ventana: unos toquecitos con un anillo cada uno.</summary>
    private async Task TypeAsync(SdkWindow? window, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        var bounds = window?.Bounds is { IsEmpty: false } b ? b : (SdkRect?)null;
        var target = bounds is { } r ? new Vec2(r.Center.X, r.Top + r.Height * 0.62) : ToVec(CursorPoint());
        await ApproachAsync(ArmSpot(target, _cloud.FingerLength * 0.8), pace, ctx, pace.Show + 0.3, ct);
        var taps = ctx.Speed == ManoeuvreSpeed.Fast ? 1 : ctx.Speed == ManoeuvreSpeed.Showy ? 6 : 4;
        for (var i = 0; i < taps; i++)
        {
            var at = target + new Vec2((System.Random.Shared.NextDouble() - 0.5) * _unit * 1.6, (System.Random.Shared.NextDouble() - 0.5) * _unit * 0.4);
            await PokeAsync(at, Math.Min(0.12, pace.Reach), ct);
            if (i == 0)
            {
                commit();
            }

            _cloud.Burst(ParticleKind.Ring, at);
            Play(Sound.Tap, 0.4, at.X);
            _cloud.Reach = Math.Max(0.4, _cloud.Reach * 0.7);
            await DelayAsync(0.06, ct);
        }

        await RetractAsync(0.15, ct);
    }

    /// <summary>
    /// Abrir algo: lo deja abrir y se queda mirando a ver; cuando aparezca la ventana, la mira sorprendido y (salvo en «Rápido»)
    /// va volando a su lado. La espera sigue aunque la orden ya haya terminado.
    /// </summary>
    private async Task WaitForWindowAsync(string? process, Pace pace, Action commit, CancellationToken ct)
    {
        _awaitedWindow = (process, _host.World.Current.Windows.Select(w => w.Handle).ToHashSet(), _now + AwaitWindowSeconds, pace.MaxFly > 0);
        commit();
        _cloud.SetPose(CloudPose.Wait);
        await DelayAsync(Math.Min(0.5, pace.Show), ct);
    }

    /// <summary>Mientras espera la ventana de algo que acaba de abrirse: en cuanto sale, la mira y se va a su lado.</summary>
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

        // Lo mira cuando acabe el gesto de esperar (la ventana puede salir antes).
        if (_manoeuvres > 0)
        {
            return;
        }

        var found = _host.World.Current.Windows.FirstOrDefault(w => !awaited.Before.Contains(w.Handle)
            && (awaited.Process is null || string.Equals(w.Process, awaited.Process, StringComparison.OrdinalIgnoreCase)));
        if (found is null || found.Bounds.IsEmpty || OnFullscreen(new SdkGesture.ActivateWindow(found)))
        {
            return;
        }

        _awaitedWindow = null;
        _host.Log.Debug($"Maniobra: ha aparecido «{found.Title}»");
        var corner = new Vec2(found.Bounds.Right, found.Bounds.Top);
        _cloud.Aim = corner;
        _cloud.Burst(ParticleKind.Exclamation, new Vec2(_cloud.Position.X, _cloud.Position.Y - _unit * 1.1));
        Act(CloudPose.Surprise, 0.6);
        if (awaited.Visit && _manoeuvres == 0 && !_dragging)
        {
            // A su lado, junto a la esquina de arriba (por fuera si cabe), y luego, como siempre, a su sitio.
            var spot = InRoom(corner + new Vec2(_unit * 1.6, _unit * 0.6));
            if (spot.X < found.Bounds.Right)
            {
                spot = InRoom(new Vec2(found.Bounds.Left - _unit * 1.6, found.Bounds.Top + _unit * 0.6));
            }

            _returnTo ??= _cloud.Home;
            Travel(spot, 700);
            _returnAt = _now + StayAfterOrder + 1.5;
        }
    }

    // Llevar una ventana abrazada ---------------------------------------------------------------------------------------

    /// <summary>
    /// Mueve una ventana: vuela a su barra de título, la abraza (la nube se hace una bola alargada alrededor) y se la lleva,
    /// cambiándole el tamaño por el camino si hace falta; la deja al píxel. En «Rápido», con la ventana tapada o sin sitio para
    /// abrazarla, la señala y la mueve K-Desktop.
    /// </summary>
    private async Task<GestureOutcome?> CarryWindowAsync(SdkGesture.MoveWindow move, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        var window = _host.World.Current.Window(move.Window.Handle) ?? move.Window;
        var from = window.Bounds;
        var to = move.Destination;
        var titleHeight = TitleHeight(window);
        var grab = new Vec2(from.Center.X, from.Top + titleHeight / 2);
        if (pace.MaxFly <= 0 || from.IsEmpty || from.Width < _unit * 2 || !Seen(window.Handle, grab))
        {
            await PointAsync(grab, pace, commit, ct);
            return null;
        }

        // Unos 0,7 s para abrazarla y soltarla; lo demás, para acercarse y llevarla.
        await ApproachAsync(grab, pace, ctx, 1.4, ct);
        var hugWidth = Math.Min(from.Width / 2.0 / _unit + 0.15, 3.4);
        _cloud.Bubble = (hugWidth, titleHeight / 2 / _unit + 0.14);
        _cloud.Hold(grab);
        _cloud.SetPose(CloudPose.Hug);
        Play(Sound.Puff, 0.45);
        await DelayAsync(0.3, ct);

        // Dónde la coge (fracción del ancho y altura en la barra de título), igual en el destino.
        var fx = (grab.X - from.Left) / Math.Max(1, from.Width);
        var start = grab;
        var end = new Vec2(to.Left + fx * to.Width, to.Top + titleHeight / 2);
        var distance = Vec2.Distance(start, end) + Math.Abs(to.Width - from.Width) * 0.5;
        var seconds = Math.Clamp(distance / (pace.Fly * 0.75 * _unit / BaseUnit), 0.5, pace.MaxFly + 0.4);
        seconds = Math.Max(0.3, Math.Min(seconds, ctx.TimeLeft.TotalSeconds - 0.6));
        Carrying = window.Handle;
        Play(Sound.Whoosh, 0.35);
        var began = _now;
        var moved = true;
        while (true)
        {
            var f = Math.Clamp((_now - began) / seconds, 0, 1);
            var e = f * f * (3 - 2 * f);
            _cloud.Hold(Vec2.Lerp(start, end, e));

            // La ventana va donde está la nube de verdad (con su muelle), con el tamaño de este momento.
            var width = (int)Math.Round(MathUtil.Lerp(from.Width, to.Width, e));
            var height = (int)Math.Round(MathUtil.Lerp(from.Height, to.Height, e));
            var at = _cloud.Position;
            moved &= _host.Windows.TryMove(window.Handle, SdkRect.FromSize((int)Math.Round(at.X - fx * width), (int)Math.Round(at.Y - titleHeight / 2), width, height));
            if (!moved || (f >= 1 && Vec2.Distance(_cloud.Position, end) < 2))
            {
                break;
            }

            if (f >= 1 && _now - began > seconds + 0.5)
            {
                break;
            }

            await NextFrameAsync(ct);
        }

        if (!moved)
        {
            // No se ha dejado: la deja K-Desktop.
            _host.Log.Debug($"Maniobra: «{window.Title}» no se deja mover; la mueve K-Desktop");
            Release();
            commit();
            return null;
        }

        _host.Windows.TryMove(window.Handle, to);
        Release();
        Play(Sound.Pop, 0.35);
        _cloud.PuffBurst(4, 1.2);
        _cloud.SetPose(CloudPose.Happy);
        await DelayAsync(0.25, ct);
        return GestureOutcome.PerformedSelf;
    }

    /// <summary>Suelta la ventana que lleva (si lleva alguna).</summary>
    private void Release()
    {
        if (Carrying is null)
        {
            return;
        }

        Carrying = null;
        if (_cloud.Pose == CloudPose.Hug)
        {
            _cloud.SetPose(_basePose);
        }
    }

    /// <summary>Alto de la barra de título: el de sus botones si se saben; si no, el de Windows a su escala.</summary>
    private double TitleHeight(SdkWindow window)
    {
        if (window.Buttons is { } buttons && !buttons.Close.IsEmpty)
        {
            return buttons.Close.Height;
        }

        var dpi = _host.World.Current.MonitorAt(window.Bounds.Center)?.DpiScale ?? 1;
        return Math.Min(32 * dpi, window.Bounds.Height / 2.0);
    }

    // Cada ciclo --------------------------------------------------------------------------------------------------------

    /// <summary>La ventana que se espera y, acabada la orden, volver a su sitio.</summary>
    private void UpdateManoeuvres()
    {
        UpdateAwaitedWindow();
        if (_manoeuvres > 0 || double.IsNaN(_returnAt) || _now < _returnAt)
        {
            return;
        }

        _returnAt = double.NaN;
        if (_returnTo is { } home && !_dragging && _speech is null)
        {
            _returnTo = null;
            if (Vec2.Distance(home, _cloud.Position) > _unit * 0.6)
            {
                Travel(InRoom(home));
            }
        }

        _returnTo = null;
    }

    /// <summary>Espera al siguiente ciclo del motor (la nube ya avanzada); como mucho 0,1 s si el host no lo da.</summary>
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

    private System.Windows.Point? PlaceCenter(Place place)
    {
        var world = _host.World.Current;
        return place switch
        {
            Place.OfWindow w => w.Window.Bounds.Center,
            Place.AtPoint p => p.Point,
            Place.Area a => a.Rect.Center,
            Place.Tray => TrayPoint(clock: false) is { } t ? new System.Windows.Point(t.X, t.Y) : null,
            Place.Clock => TrayPoint(clock: true) is { } c ? new System.Windows.Point(c.X, c.Y) : null,
            _ => null,
        };
    }

    private System.Windows.Point CursorPoint() =>
        _host.World.System.Cursor ?? new System.Windows.Point(_cloud.Position.X, _cloud.Position.Y);

    private static Vec2 ToVec(System.Windows.Point p) => new(p.X, p.Y);
}
