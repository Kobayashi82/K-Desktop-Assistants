using System.Windows.Media;
using KDesktop.Assistant.Sdk;
using Lander.Assistant.Audio;
using Lander.Assistant.Rendering;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using PixelRect = KDesktop.Assistant.Sdk.PixelRect;
using SdkWindow = KDesktop.Assistant.Sdk.WindowInfo;

namespace Lander.Assistant;

/// <summary>
/// Las maniobras de las órdenes de K-Desktop (fase 6): vuela cerca y dispara al botón (la ventana cambia justo en el
/// impacto: ahí va el <see cref="IGestureContext.Commit"/>), llama a una ventana con ondas y se posa en ella, va a la
/// bandeja, señala con el foco, deja una baliza, escanea, «teclea» y, al abrir algo, despega y se posa en la ventana
/// nueva cuando aparece. Siempre dentro del tiempo del paso: si el vuelo no llega, dispara desde donde esté. En «Rápido»
/// no vuela y nada pasa de medio segundo. Al acabar se queda un momento en el aire (por si hay otro paso) y se posa.
/// </summary>
public sealed partial class LanderAssistant
{
    /// <summary>Lo que espera en el aire tras una maniobra antes de posarse (el siguiente paso suele llegar antes).</summary>
    private const double LingerSeconds = 1.2;

    /// <summary>Lo que espera, tras abrir algo, a que aparezca su ventana para posarse en ella.</summary>
    private const double AwaitWindowSeconds = 8;

    private static readonly Color ShotColor = Gfx.Rgb(0x38E1FF);
    private static readonly Color GreenShot = Gfx.Rgb(0x6DFFA8);
    private static readonly Color ChargedShot = Gfx.Rgb(0xFF6A3D);
    private static readonly Color LightColor = Gfx.Rgb(0xFFE7A0);

    private readonly List<TaskCompletionSource> _frames = [];
    private readonly List<(Vec2 At, double Until)> _errandMarks = [];
    private ManoeuvreLayer _effects = null!;
    private int _manoeuvres;
    private double _lingerUntil = double.NaN;
    private long _perchAfter;
    private (string? Process, HashSet<long> Before, double Until)? _awaitedWindow;

    /// <summary>Cómo de vistosa es cada maniobra según la rapidez elegida en K-Desktop.</summary>
    /// <param name="Flight">Segundos de vuelo como mucho para acercarse (0 = dispara desde donde está).</param>
    /// <param name="Speed">Rapidez del vuelo del encargo.</param>
    /// <param name="Aim">Lo que tarda en apuntar antes de disparar.</param>
    /// <param name="BoltSpeed">Píxeles por segundo del proyectil (a escala 1).</param>
    /// <param name="MaxBolt">Lo que tarda el proyectil como mucho, aunque esté lejos.</param>
    /// <param name="Charge">Carga del disparo fuerte (cerrar).</param>
    /// <param name="Show">Lo que dura lo que se ve (foco, escáner, tecleo).</param>
    /// <param name="Range">A cuántas alturas del lander ya está «a tiro» y no hace falta acercarse más.</param>
    private readonly record struct Pace(double Flight, double Speed, double Aim, double BoltSpeed, double MaxBolt, double Charge, double Show, double Range);

    private static Pace PaceOf(ManoeuvreSpeed speed) => speed switch
    {
        ManoeuvreSpeed.Fast => new(0, 1.6, 0.04, 4200, 0.16, 0, 0.25, double.PositiveInfinity),
        ManoeuvreSpeed.Showy => new(3.6, 1.0, 0.32, 1500, 0.5, 0.55, 1.4, 5),
        _ => new(2.2, 1.25, 0.16, 2200, 0.34, 0.3, 0.9, 9),
    };

    /// <summary>Puede moverse para una orden: en automático, a la vista, sin carrera y sin que lo tengan agarrado.</summary>
    private bool CanManoeuvre =>
        _game.IsInitialized && !_hidden && _exitStart is null && !_dragging && _game.Mode == ControlMode.Auto && !_game.Races.IsActive
        && _game.Body.State is not (BodyState.Destroyed or BodyState.Held);

    /// <summary>Lo que se le pide está en un monitor con algo a pantalla completa: sin maniobras (lo dice el módulo).</summary>
    private bool OnFullscreen(Gesture gesture)
    {
        var point = gesture switch
        {
            Gesture.PressButton p => p.ButtonBounds.Center,
            Gesture.ActivateWindow a => a.Window.Bounds.Center,
            Gesture.MoveWindow w when _host.World.Current.MonitorAt(w.Destination.Center) is { } there && _sensors.FullscreenMonitors.Contains(there.Index) =>
                w.Destination.Center,
            Gesture.MoveWindow w => w.Window.Bounds.Center,
            Gesture.Typing { Window: { } w } => w.Bounds.Center,
            Gesture.Scan s => s.Area.Center,
            Gesture.PointAt p => PlaceCenter(p.Target),
            Gesture.GoTo g => PlaceCenter(g.Target),
            Gesture.Mark m => PlaceCenter(m.Target),
            _ => (System.Windows.Point?)null,
        };
        return point is { } at && _host.World.Current.MonitorAt(at) is { } monitor && _sensors.FullscreenMonitors.Contains(monitor.Index);
    }

    private static bool IsManoeuvre(GestureKind kind) => kind is GestureKind.PressButton or GestureKind.ActivateWindow or GestureKind.PointAt
        or GestureKind.GoTo or GestureKind.Mark or GestureKind.Scan or GestureKind.Typing or GestureKind.WaitForWindow or GestureKind.MoveWindow;

    private async Task<GestureOutcome> ManoeuvreAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct)
    {
        _manoeuvres++;
        _lingerUntil = double.NaN;
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
                case Gesture.PressButton press:
                    await ShootAsync(press, pace, ctx, Commit, ct);
                    break;
                case Gesture.ActivateWindow activate:
                    await CallWindowAsync(activate.Window, pace, ctx, Commit, ct);
                    break;
                case Gesture.PointAt point:
                    await PointAsync(point.Target, pace, ctx, Commit, ct);
                    break;
                case Gesture.GoTo go:
                    await GoAsync(go.Target, pace, ctx, ct);
                    Commit();
                    break;
                case Gesture.Mark mark:
                    await MarkAsync(mark, pace, ctx, Commit, ct);
                    break;
                case Gesture.Scan scan:
                    await ScanAsync(scan.Area, pace, ctx, Commit, ct);
                    break;
                case Gesture.Typing typing:
                    await TypeAsync(typing.Window, pace, ctx, Commit, ct);
                    break;
                case Gesture.WaitForWindow wait:
                    await LiftOffAsync(wait.Process, pace, ctx, Commit, ct);
                    break;
                case Gesture.MoveWindow move:
                    outcome = await TowAsync(move, pace, ctx, Commit, ct);
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
                _lingerUntil = _now + (ctx.Speed == ManoeuvreSpeed.Fast ? 0.3 : LingerSeconds);
                _host.RequestTick();
            }
        }
    }

    // Disparo al botón -------------------------------------------------------------------------------------------

    private async Task ShootAsync(Gesture.PressButton press, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        var target = ToVec(press.ButtonBounds.Center);
        var charged = press.Button == CaptionButton.Close && pace.Charge > 0;
        var color = press.Button switch
        {
            CaptionButton.Close => ChargedShot,
            CaptionButton.Minimize => ShotColor,
            _ => GreenShot,
        };

        await ApproachAsync(FiringPoint(target), target, pace, ctx, pace.Aim + pace.MaxBolt + pace.Charge + 0.5, ct, pace.Range);
        if (charged)
        {
            var body = _game.Body;
            var charge = _effects.Add(new ManoeuvreEffect(EffectKind.Charge, _now, pace.Charge, body.Position, body.Position, color, body.Scale));
            Play(Sound.Charge, 0.8);
            await FollowAsync(charge, pace.Charge, fromBody: false, ct);
        }
        else
        {
            await DelayAsync(pace.Aim, ct);
        }

        await FireAsync(target, color, charged ? 1.6 : 1, pace, commit, ct);
        _ = ReactToResultAsync(ctx.Committed);
    }

    /// <summary>Desde dónde dispara: un poco por debajo y a un lado del botón, del lado por el que llega.</summary>
    private Vec2 FiringPoint(Vec2 target)
    {
        var body = _game.Body;
        var h = body.Height;
        var side = body.Position.X > target.X + h ? 1 : -1;
        var point = target + new Vec2(side * 3.4 * h, 2.4 * h);
        var monitor = _world.MonitorAt(target) ?? _world.NearestMonitor(target);
        if (monitor is not null && (point.X < monitor.Bounds.Left + h * 1.5 || point.X > monitor.Bounds.Right - h * 1.5))
        {
            // Pegado al borde de la pantalla: del otro lado.
            point = target + new Vec2(-side * 3.4 * h, 2.4 * h);
        }

        return point;
    }

    /// <summary>
    /// El proyectil: sale con un fogonazo y retroceso, cruza en línea recta y, al llegar, el impacto y el
    /// <paramref name="commit"/> en el mismo instante.
    /// </summary>
    private async Task FireAsync(Vec2 target, Color color, double size, Pace pace, Action commit, CancellationToken ct)
    {
        var body = _game.Body;
        var scale = body.Scale;
        var direction = (target - body.Position).Normalized();
        var muzzle = body.Position + direction * body.Height * 0.55;
        var travel = Math.Clamp(Vec2.Distance(muzzle, target) / (pace.BoltSpeed * scale), 0.06, pace.MaxBolt);
        _game.Visual.LookTarget = target;
        _particles.NozzleFlash(muzzle, direction, scale);
        _particles.Sparks(muzzle, direction, 6, scale);
        _game.Recoil(-direction * 70 * size);
        Play(Sound.Zap, 0.9);
        _effects.Add(new ManoeuvreEffect(EffectKind.Bolt, _now, travel, muzzle, target, color, scale, Size: size));
        await DelayAsync(travel, ct);

        commit();
        _effects.Add(new ManoeuvreEffect(EffectKind.Impact, _now, 0.45, target, target, color, scale, Size: size));
        PlayAt(Sound.Impact, 0.9, target.X);
    }

    /// <summary>Si K-Desktop dice que la acción no salió (la ventana no se dejó), lo nota.</summary>
    private async Task ReactToResultAsync(Task<bool> committed)
    {
        try
        {
            if (!await committed.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(true))
            {
                React(Reaction.Failure);
            }
        }
        catch (TimeoutException)
        {
            // Sin noticias: no se dice nada.
        }
    }

    // Traer delante, señalar, ir a un sitio -----------------------------------------------------------------------

    /// <summary>
    /// Vuela sobre la ventana, la llama con unas ondas en la barra de título y, cuando sale delante, se posa en ella.
    /// Minimizada, la llama desde la barra de tareas.
    /// </summary>
    private async Task CallWindowAsync(SdkWindow window, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        var h = _game.Body.Height;
        var title = window.Bounds.IsEmpty
            ? TaskbarPoint() ?? new Vec2(_game.Body.Position.X, _game.Body.Position.Y + h)
            : new Vec2(window.Bounds.Center.X, window.Bounds.Top + Math.Min(16 * _game.Body.Scale, window.Bounds.Height / 2.0));
        await ApproachAsync(title + new Vec2(0, -1.6 * h), title, pace, ctx, 0.6, ct);
        _effects.Add(new ManoeuvreEffect(EffectKind.Ping, _now, 0.7, title, title, ShotColor, _game.Body.Scale));
        PlayAt(Sound.Pop, 0.8, title.X);
        commit();
        _perchAfter = window.Handle;
        await DelayAsync(Math.Min(0.25, pace.Show), ct);
    }

    /// <summary>Se pone cerca y lo señala con el foco un momento (el efecto ocurre al encenderlo).</summary>
    private async Task PointAsync(Place place, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        var target = ToVec(PlaceCenter(place) ?? Cursor());
        var h = _game.Body.Height;
        var side = _game.Body.Position.X > target.X ? 1 : -1;
        await ApproachAsync(target + new Vec2(side * 3 * h, -1.8 * h), target, pace, ctx, pace.Show + 0.4, ct, pace.Range);
        var body = _game.Body;
        var light = _effects.Add(new ManoeuvreEffect(EffectKind.Spotlight, _now, pace.Show, body.Position, target, LightColor, body.Scale));
        Play(Sound.Pop, 0.5);
        await DelayAsync(Math.Min(0.15, pace.Show / 2), ct);
        commit();
        await FollowAsync(light, pace.Show - Math.Min(0.15, pace.Show / 2), fromBody: true, ct);
    }

    /// <summary>Va al sitio (la bandeja, el reloj, un punto, una ventana) y espera allí en el aire.</summary>
    private async Task GoAsync(Place place, Pace pace, IGestureContext ctx, CancellationToken ct)
    {
        var h = _game.Body.Height;
        var target = place switch
        {
            Place.Tray or Place.Clock => TrayPoint(place is Place.Clock),
            Place.OfWindow w => ToVec(w.Window.Bounds.Center) with { Y = w.Window.Bounds.Top },
            _ => ToVec(PlaceCenter(place) ?? Cursor()),
        };
        if (target is not { } at)
        {
            return;
        }

        // Lo que viene después (decir qué ha cambiado) también necesita su tiempo.
        await ApproachAsync(at + new Vec2(0, -1.4 * h), at, pace, ctx, 2.0, ct);
    }

    /// <summary>El centro de la barra de tareas principal (de donde sale una ventana minimizada).</summary>
    private Vec2? TaskbarPoint()
    {
        var world = _host.World.Current;
        var primary = world.PrimaryMonitor;
        var bar = world.Taskbars.FirstOrDefault(t => t.Monitor == primary?.Index && !t.Bounds.IsEmpty);
        return bar is null ? null : ToVec(bar.Bounds.Center);
    }

    /// <summary>
    /// El punto de la bandeja (o del reloj) en la barra de tareas principal; si no se sabe, la esquina de la barra según
    /// dónde esté.
    /// </summary>
    private Vec2? TrayPoint(bool clock)
    {
        var world = _host.World.Current;
        var primary = world.PrimaryMonitor;
        var bar = world.Taskbars.FirstOrDefault(t => t.Monitor == primary?.Index) ?? (world.Taskbars.Count > 0 ? world.Taskbars[0] : null);
        if ((clock ? bar?.Clock ?? bar?.Tray : bar?.Tray ?? bar?.Clock) is { IsEmpty: false } area)
        {
            return ToVec(area.Center);
        }

        if (primary is null)
        {
            return null;
        }

        var b = primary.Bounds;
        var w = primary.WorkArea;
        var inset = 60 * primary.DpiScale;
        return w.Bottom < b.Bottom ? new Vec2(b.Right - inset, (w.Bottom + b.Bottom) / 2.0)
            : w.Top > b.Top ? new Vec2(b.Right - inset, (b.Top + w.Top) / 2.0)
            : w.Right < b.Right ? new Vec2((w.Right + b.Right) / 2.0, b.Bottom - inset)
            : w.Left > b.Left ? new Vec2((b.Left + w.Left) / 2.0, b.Bottom - inset)
            : new Vec2(b.Right - inset, b.Bottom - inset);
    }

    // Baliza, escáner, tecleo ---------------------------------------------------------------------------------------

    /// <summary>Deja una baliza parpadeante sobre el sitio (la ventana «siempre visible», por ejemplo).</summary>
    private async Task MarkAsync(Gesture.Mark mark, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        var at = mark.Target switch
        {
            Place.OfWindow w => new Vec2(w.Window.Bounds.Center.X, w.Window.Bounds.Top),
            _ => ToVec(PlaceCenter(mark.Target) ?? Cursor()),
        };
        var h = _game.Body.Height;
        await ApproachAsync(at + new Vec2(-1.8 * h, -1.6 * h), at, pace, ctx, 0.4, ct);
        _errandMarks.Add((at, _now + Math.Max(0.6, mark.Duration.TotalSeconds)));
        _particles.Sparkles(_game.Body.Position, 18 * _game.Body.Scale, ShotColor, 8, _game.Body.Scale);
        PlayAt(Sound.LegClick, 0.8, at.X);
        commit();
        await DelayAsync(Math.Min(0.2, pace.Show), ct);
    }

    /// <summary>
    /// Barre la zona con una línea de luz y, ya apagada (que no salga en la captura), hace la foto: el flash llega después.
    /// </summary>
    private async Task ScanAsync(PixelRect area, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        if (area.IsEmpty)
        {
            commit();
            return;
        }

        var rect = new System.Windows.Rect(area.Left, area.Top, area.Width, area.Height);
        var top = new Vec2(area.Center.X, area.Top);
        await ApproachAsync(top + new Vec2(0, 2 * _game.Body.Height), top, pace, ctx, pace.Show + 0.5, ct);
        _effects.Add(new ManoeuvreEffect(EffectKind.Scan, _now, pace.Show, top, top, ShotColor, _game.Body.Scale, rect));
        Play(Sound.Scan, 0.8);
        await DelayAsync(pace.Show, ct);

        // Un fotograma sin la línea antes de la foto.
        await NextFrameAsync(ct);
        commit();
        _ = FlashAsync(rect, ctx.Committed);
    }

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

        _effects.Add(new ManoeuvreEffect(EffectKind.Flash, _now, 0.35, default, default, Colors.White, _game.Body.Scale, area));
        Play(Sound.Shutter, 0.9);
        _host.RequestTick();
    }

    /// <summary>Se acerca a la ventana y «teclea»: las teclas salen ya; él hace como que las pulsa un momento.</summary>
    private async Task TypeAsync(SdkWindow? window, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        if (window is not null)
        {
            var center = ToVec(window.Bounds.Center);
            await ApproachAsync(center + new Vec2(0, -1.5 * _game.Body.Height), center, pace, ctx, pace.Show + 0.3, ct);
        }

        commit();
        _game.Visual.Flash(Expression.Curious, pace.Show + 0.3);
        var body = _game.Body;
        var keys = Math.Max(1, (int)(pace.Show / 0.13));
        for (var i = 0; i < keys; i++)
        {
            _particles.Sparkles(body.Position + new Vec2(0, body.Height * 0.3), 14 * body.Scale, ShotColor, 2, body.Scale);
            Play(Sound.LegClick, 0.35);
            await DelayAsync(0.13, ct);
        }
    }

    /// <summary>
    /// Abrir algo: despega (ahí va la acción) y, cuando aparezca la ventana nueva, vuela a ella y se posa encima. La
    /// espera sigue aunque la orden ya haya terminado (ver <see cref="UpdateAwaitedWindow"/>).
    /// </summary>
    private async Task LiftOffAsync(string? process, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        _awaitedWindow = (process, _host.World.Current.Windows.Select(w => w.Handle).ToHashSet(), _now + AwaitWindowSeconds);
        if (pace.Flight > 0 && _game.CanRunErrands)
        {
            var body = _game.Body;
            _game.BeginErrand(body.Position + new Vec2(0, -2.2 * body.Height), pace.Speed);
            var until = _now + 0.5;
            while (_now < until && !_game.Body.IsFlying)
            {
                await NextFrameAsync(ct);
            }
        }

        commit();
    }

    /// <summary>Mientras espera la ventana de algo que acaba de abrirse: en cuanto sale, va a posarse en ella.</summary>
    private void UpdateAwaitedWindow()
    {
        if (_awaitedWindow is not { } awaited)
        {
            return;
        }

        if (_now > awaited.Until || !CanManoeuvre)
        {
            _awaitedWindow = null;
            return;
        }

        var found = _host.World.Current.Windows.FirstOrDefault(w => !awaited.Before.Contains(w.Handle)
            && (awaited.Process is null || string.Equals(w.AppId, awaited.Process, StringComparison.OrdinalIgnoreCase)));
        if (found is null || found.Bounds.IsEmpty || OnFullscreen(new Gesture.ActivateWindow(found)))
        {
            return;
        }

        _awaitedWindow = null;
        _host.Log.Debug($"Maniobra: ha aparecido «{found.Title}»; va a posarse en ella");
        var title = new Vec2(found.Bounds.Center.X, found.Bounds.Top);
        _game.BeginErrand(title + new Vec2(0, -1.6 * _game.Body.Height), 1.1, title);
        _perchAfter = found.Handle;
        _lingerUntil = _now + 1.8;
        _game.Visual.Flash(Expression.Curious, 1.5);
    }

    // Vuelo, espera y fin del encargo ---------------------------------------------------------------------------

    /// <summary>
    /// Vuela al punto mirando a <paramref name="look"/> hasta llegar, hasta tenerlo a <paramref name="range"/> alturas (lo
    /// que hace después, como disparar, lo hace en pleno vuelo, y luego sigue hasta el punto) o hasta agotar el vuelo que
    /// le toca, dejando <paramref name="reserve"/> segundos del paso para lo que viene después. En pausa o en «Rápido», no
    /// se mueve.
    /// </summary>
    private async Task ApproachAsync(Vec2 point, Vec2 look, Pace pace, IGestureContext ctx, double reserve, CancellationToken ct,
        double range = 0)
    {
        if (Vec2.Distance(_game.Body.Position, look) < range * _game.Body.Height && !_game.Body.IsLanded)
        {
            _game.Visual.LookTarget = look;
            return;
        }

        _game.Visual.LookTarget = look;
        var budget = Math.Min(pace.Flight, ctx.TimeLeft.TotalSeconds - reserve);
        if (budget < 0.3 || !_game.CanRunErrands)
        {
            return;
        }

        _game.BeginErrand(point, pace.Speed, look);
        var start = _now;
        var from = _game.Body.Position;
        var until = _now + budget;
        while (_now < until && !_game.Autopilot.ErrandArrived
               && (_game.Body.IsLanded || Vec2.Distance(_game.Body.Position, look) >= range * _game.Body.Height))
        {
            await NextFrameAsync(ct);
        }

        _host.Log.Debug($"Maniobra: vuelo de {from} a {_game.Body.Position} (quería {point}) en {_now - start:0.00} s; " +
                        $"{(_game.Autopilot.ErrandArrived ? "ha llegado" : "a tiro o sin tiempo")}, encargo {_game.Autopilot.OnErrand}");
    }

    /// <summary>Un efecto que acompaña al lander mientras dura (el foco sale de él, la carga lo rodea).</summary>
    private async Task FollowAsync(ManoeuvreEffect effect, double seconds, bool fromBody, CancellationToken ct)
    {
        var until = _now + seconds;
        while (_now < until)
        {
            if (fromBody)
            {
                effect.From = _game.Body.Position;
            }
            else
            {
                effect.From = effect.To = _game.Body.Position;
            }

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

    /// <summary>Espera al siguiente ciclo del motor (la física ya avanzada); como mucho 0,1 s si el host no lo da.</summary>
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

    /// <summary>Cada ciclo: la ventana que se espera, el final del encargo, las balizas y los efectos.</summary>
    private void UpdateManoeuvres(double now)
    {
        UpdateAwaitedWindow();
        if (!double.IsNaN(_lingerUntil) && now >= _lingerUntil && _manoeuvres == 0)
        {
            _lingerUntil = double.NaN;
            _game.EndErrand(_perchAfter);
            _perchAfter = 0;
        }

        _errandMarks.RemoveAll(m => now >= m.Until);
        if (_hidden || _exitStart is not null)
        {
            _errandMarks.Clear();
            _effects.Clear();
            return;
        }

        _effects.Render(now);
    }

    /// <summary>Hay una maniobra en marcha, un efecto que dibujar o un encargo por terminar: a ritmo de fotogramas.</summary>
    private bool ManoeuvreActive => _manoeuvres > 0 || _frames.Count > 0 || _effects.IsActive || !double.IsNaN(_lingerUntil)
        || _awaitedWindow is not null || _errandMarks.Count > 0;

    private void PlayAt(Sound sound, double gain, double x)
    {
        if (!_sensors.QuietMode)
        {
            _audio.Play(sound, gain, x, _span);
        }
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
        _sensors.Cursor is { } c ? new System.Windows.Point(c.X, c.Y) : new System.Windows.Point(_game.Body.Position.X, _game.Body.Position.Y);

    private static Vec2 ToVec(System.Windows.Point p) => new(p.X, p.Y);
}
