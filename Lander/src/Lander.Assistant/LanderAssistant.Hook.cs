using KDesktop.Assistant.Sdk;
using Lander.Assistant.Audio;
using Lander.Assistant.Rendering;
using Lander.Core.Geometry;
using Lander.Core.Towing;
using PixelRect = KDesktop.Assistant.Sdk.PixelRect;

namespace Lander.Assistant;

/// <summary>
/// Mover una ventana (fase 7): se acerca, dispara el gancho (un proyectil balístico con su cable, la cuerda del SDK) a la
/// barra de título y, enganchado, vuela hacia el destino tirando de ella; la ventana lo sigue con su «masa» (un muelle
/// amortiguado, con un pequeño rebote) y al final queda exactamente en su sitio. Si la ventana se mueve mientras vuela el
/// gancho, falla: lo recoge y lo intenta otra vez (dos veces como mucho). Lo mueve él mismo con los servicios de ventana
/// (también las de administrador, que mueve K-Desktop por él) y termina con <see cref="GestureOutcome.PerformedSelf"/>.
/// </summary>
public sealed partial class LanderAssistant
{
    /// <summary>Intentos de enganchar como mucho (el primero y dos más).</summary>
    private const int HookAttempts = 3;

    /// <summary>Lo que se acelera la física mientras remolca (ver <c>LanderGame.ErrandBoost</c>).</summary>
    private const double TowBoost = 4;

    private const double ShowyTowBoost = 2.5;

    /// <summary>Hasta cuántas alturas (de 48 px) de movimiento se empuja en vez de remolcar.</summary>
    private const double PushReach = 3.5;

    /// <summary>Lo que se acelera la física para ponerse junto al borde antes de empujar.</summary>
    private const double PushBoost = 3;

    /// <summary>
    /// El empujón: se pone junto al borde contrario al movimiento y empuja con los propulsores laterales; la ventana avanza
    /// lo que avanza la nave. Null si no llega a ponerse en su sitio a tiempo (entonces, el gancho).
    /// </summary>
    private async Task<GestureOutcome?> PushAsync(long handle, PixelRect bounds, PixelRect destination, Pace pace, IGestureContext ctx, CancellationToken ct)
    {
        var body = _game.Body;
        var h = body.Height;
        var travel = new Vec2(destination.Left - bounds.Left, destination.Top - bounds.Top);
        var direction = travel.Normalized();
        var sideways = Math.Abs(direction.X) >= Math.Abs(direction.Y);
        var contact = sideways
            ? new Vec2(direction.X > 0 ? bounds.Left - 0.62 * h : bounds.Right + 0.62 * h, bounds.Top + Math.Min(bounds.Height * 0.5, 2.2 * h))
            : new Vec2(bounds.Center.X, direction.Y > 0 ? bounds.Top - 0.62 * h : bounds.Bottom + 0.62 * h);
        // A ponerse en su sitio con prisa (la física acelerada, como al remolcar).
        _game.ErrandBoost = PushBoost;
        try
        {
            await ApproachAsync(contact, contact + direction * h, pace with { Flight = 2.6, Speed = pace.Speed * Math.Sqrt(PushBoost) },
                ctx, 1.2, ct);
        }
        finally
        {
            _game.ErrandBoost = 1;
        }

        if (Vec2.Distance(body.Position, contact) > 1.2 * h)
        {
            return null;
        }

        // Empuja: la ventana avanza suave (arranca y frena) y la nave va pegada a su borde. Es la ventana la que abre
        // camino: para el piloto su borde es una pared.
        var seconds = Math.Clamp(travel.Length / 160, 0.5, 1.2);
        var start = _now;
        _particles.Sparks(contact + direction * 0.5 * h, direction, 5, body.Scale);
        PlayAt(Sound.Scrape, 0.6, contact.X);
        while (_now - start < seconds)
        {
            await NextFrameAsync(ct);
            var k = Math.Clamp((_now - start) / seconds, 0, 1);
            var offset = travel * (k * k * (3 - 2 * k));
            if (!_host.Windows.TryMove(handle, bounds.Offset((int)Math.Round(offset.X), (int)Math.Round(offset.Y))))
            {
                return GestureOutcome.NotPerformed;
            }

            _game.BeginErrand(contact + offset, 1.2, contact + offset + direction * h);
        }

        _host.Log.Debug($"Maniobra: empujón de {travel.Length:0} px");
        _perchAfter = handle;
        return _host.Windows.TryMove(handle, destination) ? GestureOutcome.PerformedSelf : GestureOutcome.NotPerformed;
    }

    /// <summary>Lo deprisa que recoge cable el torno mientras remolca (px/s a escala 1).</summary>
    private const double WinchSpeed = 900;

    /// <summary>Lo que tarda en recoger el cable al soltar o al fallar.</summary>
    private const double ReelSeconds = 0.35;

    private async Task<GestureOutcome> TowAsync(Gesture.MoveWindow move, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        var handle = move.Window.Handle;
        var destination = move.Destination;
        if (pace.Flight <= 0 || !_game.CanRunErrands || destination.IsEmpty)
        {
            // Rápido o en pausa: que la mueva K-Desktop ya.
            commit();
            return GestureOutcome.Committed;
        }

        var deadline = _now + ctx.TimeLeft.TotalSeconds - 0.35;
        _host.World.WatchClosely(ctx.TimeLeft.TotalSeconds);
        var bounds = WindowBounds(handle) ?? move.Window.Bounds;
        if (bounds.IsEmpty)
        {
            commit();
            return GestureOutcome.Committed;
        }

        var h = _game.Body.Height;

        // Un poco nada más y del mismo tamaño: un empujón, sin gancho.
        var shift = new Vec2(destination.Left - bounds.Left, destination.Top - bounds.Top);
        if (shift.Length is > 1 and < PushReach * 48 && Math.Abs(destination.Width - bounds.Width) <= 2 && Math.Abs(destination.Height - bounds.Height) <= 2
            && await PushAsync(handle, bounds, destination, pace, ctx, ct) is { } pushed)
        {
            return pushed;
        }

        var title = TitleOffset(bounds);
        var hook = HookPoint(bounds, title);
        // Cerca, pero sin gastar el tiempo del remolque: el cable da para disparar desde lejos.
        var approach = pace with { Flight = Math.Min(pace.Flight, pace.Flight > 3 ? 1.8 : 1.0) };
        await ApproachAsync(hook + new Vec2(0, -3.2 * h), hook, approach, ctx, 3.0, ct, range: 11);

        var hooked = await ShootHookAsync(handle, title, ct);
        if (hooked is not { } line)
        {
            _host.Log.Debug("Maniobra: no se ha podido enganchar la ventana");
            return GestureOutcome.NotPerformed;
        }

        try
        {
            return await PullAsync(handle, line, title, destination, pace, deadline, ct) ? GestureOutcome.PerformedSelf : GestureOutcome.NotPerformed;
        }
        finally
        {
            _game.ErrandBoost = 1;
            // El cable se recoge aunque el gesto ya haya terminado.
            _ = ReelInAsync(line, CancellationToken.None);
        }
    }

    /// <summary>
    /// Dispara el gancho a la barra de título (hasta <see cref="HookAttempts"/> veces): el cable enganchado (su efecto), o
    /// null si la ventana ya no está o no ha habido manera.
    /// </summary>
    private async Task<ManoeuvreEffect?> ShootHookAsync(long handle, double title, CancellationToken ct)
    {
        var scale = _game.Body.Scale;
        for (var attempt = 0; attempt < HookAttempts; attempt++)
        {
            if (WindowBounds(handle) is not { IsEmpty: false } bounds)
            {
                return null;
            }

            var target = HookPoint(bounds, title);
            var from = Winch();
            var shot = HookShot.Aim(from, target, scale);
            var rope = new Rope(ToPoint(from), ToPoint(from), Math.Max(10, Vec2.Distance(from, target) * 0.4))
            {
                MaxLength = Vec2.Distance(from, target) * 1.4 + 6 * _game.Body.Height,
                Gravity = new System.Windows.Vector(0, 900 * scale),
            };
            var line = _effects.Add(new ManoeuvreEffect(EffectKind.Rope, _now, 600, from, target, ShotColor, scale, Line: rope));
            _game.Visual.LookTarget = target;
            _game.Recoil((from - target).Normalized() * 40);
            Play(Sound.Whoosh, 0.8);

            // Vuela: el cable sale de la nave y se va soltando detrás del gancho.
            var start = _now;
            var last = _now;
            while (_now - start < shot.Duration)
            {
                await NextFrameAsync(ct);
                var t = Math.Min(_now - start, shot.Duration);
                rope.Start = ToPoint(Winch());
                rope.PinEnd(ToPoint(shot.At(t)));
                rope.Length = Math.Max(rope.Length, rope.Span * 1.05);
                rope.Step(_now - last);
                last = _now;
            }

            var now = WindowBounds(handle) is { IsEmpty: false } moved ? HookPoint(moved, title) : (Vec2?)null;
            if (shot.Hits(now))
            {
                rope.PinEnd(ToPoint(now!.Value));
                _particles.Sparks(_game.Body.Position, (target - _game.Body.Position).Normalized(), 4, scale);
                PlayAt(Sound.LegClick, 1, target.X);
                PlayAt(Sound.Impact, 0.5, target.X);
                return line;
            }

            // Ha fallado (la ventana se ha movido): cae, lo recoge y vuelve a probar.
            _host.Log.Debug($"Maniobra: el gancho falla (intento {attempt + 1})");
            rope.ReleaseEnd(ToVector(shot.Heading(shot.Duration) * 300));
            await ReelInAsync(line, ct);
        }

        return null;
    }

    /// <summary>
    /// Enganchada: vuela hacia el destino con la física acelerada y la ventana lo sigue; al acabar (o si se acaba el
    /// tiempo) la deja exacta. False si la ventana no se deja mover.
    /// </summary>
    private async Task<bool> PullAsync(long handle, ManoeuvreEffect line, double title, PixelRect destination, Pace pace, double deadline,
        CancellationToken ct)
    {
        var rope = line.Line!;
        var bounds = WindowBounds(handle) ?? destination;
        var start = new TowRect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        var end = new TowRect(destination.Left, destination.Top, destination.Width, destination.Height);
        var h = _game.Body.Height;

        // La nave acaba por encima del gancho y un poco más allá en la dirección del viaje (tirando).
        var startHook = HookPoint(bounds, title);
        var endHook = new Vec2(end.Left + Math.Min(end.Width * 0.3, 260), end.Top + title);
        var travel = endHook - startHook;
        var lead = travel.LengthSquared < 1 ? Vec2.Zero : travel.Normalized() * 1.2 * h;
        // Más lejos, más empuje (la física más acelerada), para que quepa en el tiempo del paso.
        var far = Math.Clamp(travel.Length / 900, 1, 1.5);
        var boost = (pace.Flight > 3 ? ShowyTowBoost : TowBoost) * far;
        _game.ErrandBoost = boost;
        // Sin pegarse al borde de arriba de la pantalla (allí el piloto no se deja inclinar): como mucho a 2,6 alturas de él.
        var finalPoint = endHook + lead + new Vec2(0, -3 * h);
        if ((_world.MonitorAt(endHook) ?? _world.NearestMonitor(endHook)) is { } monitor)
        {
            finalPoint = finalPoint with { Y = Math.Max(finalPoint.Y, monitor.Bounds.Top + 2.6 * h) };
        }

        _game.BeginErrand(finalPoint, pace.Speed * Math.Sqrt(boost), endHook);
        var landerEnd = _game.Autopilot.ErrandPoint;
        var tow = new WindowTow(start, end, _game.Body.Position, landerEnd, title, arrival: h * 0.8);
        rope.Length = Math.Max(h, Vec2.Distance(Winch(), tow.Hook) * 0.95);
        _host.Log.Debug($"Maniobra: remolca de {start.Left:0},{start.Top:0} a {end.Left:0},{end.Top:0} (frecuencia {tow.Frequency:0.0})");

        var began = _now;
        var last = _now;
        while (!tow.Arrived && _now < deadline)
        {
            await NextFrameAsync(ct);
            if (!_game.CanRunErrands)
            {
                // Lo han cogido, se ha ido a otro modo…: la deja K-Desktop en su sitio.
                _host.Log.Debug("Maniobra: el remolque se interrumpe");
                return false;
            }

            var dt = _now - last;
            last = _now;
            tow.Step(dt, _game.Body.Position);
            if (!_host.Windows.TryMove(handle, Round(tow.Current)))
            {
                _host.Log.Debug("Maniobra: la ventana no se deja mover");
                return false;
            }

            rope.Start = ToPoint(Winch());
            rope.PinEnd(ToPoint(tow.Hook));

            // El torno recoge lo que sobra: casi tenso, nunca más corto que un par de alturas.
            rope.Length = Math.Max(Math.Max(2.2 * h, Math.Min(rope.Length, rope.Span * 1.02)), rope.Length - WinchSpeed * _game.Body.Scale * dt);
            rope.Step(dt);
        }

        // En su sitio, al píxel (K-Desktop lo comprueba igualmente al recibir «done»).
        _host.Log.Debug($"Maniobra: remolque de {_now - began:0.00} s ({(tow.Arrived ? "ha llegado" : "se acabó el tiempo")}, avance {tow.Progress:0.00}, " +
                        $"rebote {tow.Overshoot:0} px, nave en {_game.Body.Position} y quería {landerEnd})");
        tow.Finish();
        if (!_host.Windows.TryMove(handle, destination))
        {
            return false;
        }

        rope.PinEnd(ToPoint(tow.Hook));
        _perchAfter = handle;
        PlayAt(Sound.LegClick, 0.7, tow.Hook.X);
        return true;
    }

    /// <summary>Suelta el gancho y recoge el cable hasta la nave; luego desaparece.</summary>
    private async Task ReelInAsync(ManoeuvreEffect line, CancellationToken ct = default)
    {
        var rope = line.Line!;
        if (rope.EndPinned)
        {
            rope.ReleaseEnd();
        }

        Play(Sound.Scrape, 0.5);
        var start = _now;
        var last = _now;
        var length = rope.Length;
        try
        {
            while (_now - start < ReelSeconds)
            {
                await NextFrameAsync(ct);
                var k = Math.Clamp((_now - start) / ReelSeconds, 0, 1);
                rope.Length = Math.Max(2, length * (1 - k));
                rope.MaxLength = Math.Max(2, rope.Length);
                rope.Start = ToPoint(Winch());
                rope.Step(_now - last);
                last = _now;
            }
        }
        catch (OperationCanceledException)
        {
            // Con prisa: desaparece ya.
        }
        finally
        {
            _effects.Remove(line);
        }
    }

    /// <summary>De dónde sale el cable: el torno de debajo de la nave.</summary>
    private Vec2 Winch()
    {
        var body = _game.Body;
        return body.Position - Vec2.UpFromAngle(body.Angle) * body.Height * 0.42;
    }

    /// <summary>Dónde se engancha: en la barra de título, al 30 % del ancho (lejos de los botones).</summary>
    private static Vec2 HookPoint(PixelRect bounds, double title) =>
        new(bounds.Left + Math.Min(bounds.Width * 0.3, 260), bounds.Top + title);

    /// <summary>La mitad de la barra de título, según el escalado del monitor de la ventana.</summary>
    private double TitleOffset(PixelRect bounds) =>
        16 * (_host.World.Current.MonitorAt(bounds.Center)?.DpiScale ?? 1);

    /// <summary>Dónde está la ventana ahora (el mundo del host), o null si ya no está.</summary>
    private PixelRect? WindowBounds(long handle) => _host.World.Current.Window(handle)?.Bounds;

    private static PixelRect Round(TowRect r) =>
        PixelRect.FromSize((int)Math.Round(r.Left), (int)Math.Round(r.Top), (int)Math.Round(r.Width), (int)Math.Round(r.Height));

    private static System.Windows.Point ToPoint(Vec2 v) => new(v.X, v.Y);

    private static System.Windows.Vector ToVector(Vec2 v) => new(v.X, v.Y);
}
