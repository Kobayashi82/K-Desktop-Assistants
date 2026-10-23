using KDesktop.Assistant.Sdk;
using Lander.Assistant.Audio;
using Lander.Assistant.Rendering;
using Lander.Core.Geometry;
using PixelRect = KDesktop.Assistant.Sdk.PixelRect;

namespace Lander.Assistant;

/// <summary>
/// Estirar una ventana (fase 12): cuando una orden la cambia sobre todo de tamaño y casi no la mueve, se pone encima,
/// engancha dos esquinas opuestas (arriba a la izquierda y abajo a la derecha) y la ventana pasa de un tamaño al otro
/// con suavidad mientras los cables siguen a las esquinas y la nave tira desde arriba. Al acabar la deja al píxel.
/// </summary>
public sealed partial class LanderAssistant
{
    /// <summary>Lo que tiene que cambiar el tamaño (en proporción) para estirarla en vez de remolcarla.</summary>
    private const double StretchChange = 0.12;

    /// <summary>Lo que puede moverse el centro (en proporción de su lado mayor) para que siga siendo «estirar».</summary>
    private const double StretchDrift = 0.4;

    private static bool IsStretch(PixelRect from, PixelRect to)
    {
        if (from.IsEmpty || to.IsEmpty)
        {
            return false;
        }

        var resized = Math.Abs(to.Width - from.Width) > StretchChange * from.Width || Math.Abs(to.Height - from.Height) > StretchChange * from.Height;
        var drift = Vec2.Distance(new Vec2(from.Center.X, from.Center.Y), new Vec2(to.Center.X, to.Center.Y));
        return resized && drift < StretchDrift * Math.Max(from.Width, from.Height) && drift < 700;
    }

    /// <summary>Se ven las dos esquinas donde van los ganchos.</summary>
    private bool CornersSeen(long handle, PixelRect bounds) =>
        Seen(handle, new Vec2(bounds.Left + 4, bounds.Top + 4)) && Seen(handle, new Vec2(bounds.Right - 4, bounds.Bottom - 4));

    private async Task<GestureOutcome> StretchAsync(long handle, PixelRect bounds, PixelRect destination, Pace pace, IGestureContext ctx,
        double deadline, CancellationToken ct)
    {
        var h = _game.Body.Height;
        var above = AboveCenter(bounds);
        var approach = pace with { Flight = Math.Min(pace.Flight, pace.Flight > 3 ? 1.8 : 1.0) };
        await ApproachAsync(above, new Vec2(bounds.Center.X, bounds.Top), approach, ctx, 2.6, ct, range: 9);

        // Los dos ganchos, uno tras otro.
        var first = await ShootHookAsync(handle, b => new Vec2(b.Left + 4, b.Top + 4), ct);
        if (first is null)
        {
            _host.Log.Debug("Maniobra: no se ha podido enganchar la esquina de arriba");
            return GestureOutcome.NotPerformed;
        }

        var second = await ShootHookAsync(handle, b => new Vec2(b.Right - 4, b.Bottom - 4), ct);
        if (second is null)
        {
            _ = ReelInAsync(first, CancellationToken.None);
            _host.Log.Debug("Maniobra: no se ha podido enganchar la esquina de abajo");
            return GestureOutcome.NotPerformed;
        }

        try
        {
            var start = WindowBounds(handle) ?? bounds;
            var change = Math.Max(Math.Abs(destination.Width - start.Width), Math.Abs(destination.Height - start.Height));
            var seconds = Math.Clamp(change / 700.0, 0.6, 1.2) * (pace.Flight > 3 ? 1.35 : 1);
            seconds = Math.Min(seconds, Math.Max(0.25, deadline - _now - 0.2));
            var began = _now;
            var last = _now;
            PlayAt(Sound.Scrape, 0.6, start.Center.X);
            while (_now - began < seconds)
            {
                await NextFrameAsync(ct);
                if (!_game.CanRunErrands)
                {
                    _host.Log.Debug("Maniobra: el estirón se interrumpe");
                    return GestureOutcome.NotPerformed;
                }

                var k = Math.Clamp((_now - began) / seconds, 0, 1);
                var e = k * k * (3 - 2 * k);
                var now = Lerp(start, destination, e);
                if (!_host.Windows.TryMove(handle, now))
                {
                    _host.Log.Debug("Maniobra: la ventana no se deja estirar");
                    return GestureOutcome.NotPerformed;
                }

                // La nave tira desde encima del centro que va quedando; los cables siguen a las esquinas.
                _game.BeginErrand(AboveCenter(now), 1.3, new Vec2(now.Center.X, now.Top));
                var dt = _now - last;
                last = _now;
                Hold(first, new Vec2(now.Left + 4, now.Top + 4), dt);
                Hold(second, new Vec2(now.Right - 4, now.Bottom - 4), dt);
            }

            _host.Log.Debug($"Maniobra: estirada de {start.Width}x{start.Height} a {destination.Width}x{destination.Height} en {_now - began:0.00} s");
            if (!_host.Windows.TryMove(handle, destination))
            {
                return GestureOutcome.NotPerformed;
            }

            PlayAt(Sound.LegClick, 0.7, destination.Center.X);
            _perchAfter = handle;
            return GestureOutcome.PerformedSelf;
        }
        finally
        {
            _ = ReelInAsync(first, CancellationToken.None);
            _ = ReelInAsync(second, CancellationToken.None);
        }
    }

    /// <summary>Un cable enganchado que sigue a su punto: el torno recoge o suelta lo justo para ir casi tenso.</summary>
    private void Hold(ManoeuvreEffect line, Vec2 at, double dt)
    {
        var rope = line.Line!;
        rope.Start = ToPoint(Winch());
        rope.PinEnd(ToPoint(at));
        rope.MaxLength = Math.Max(rope.MaxLength, rope.Span * 1.3);
        rope.Length = Math.Max(2 * _game.Body.Height, rope.Span * 1.02);
        rope.Step(dt);
    }

    /// <summary>Encima del centro de la ventana, sin pegarse al borde de arriba de la pantalla.</summary>
    private Vec2 AboveCenter(PixelRect bounds)
    {
        var h = _game.Body.Height;
        var point = new Vec2(bounds.Center.X, bounds.Top - 2.8 * h);
        var monitor = _world.MonitorAt(new Vec2(bounds.Center.X, bounds.Top)) ?? _world.NearestMonitor(point);
        return monitor is null ? point : point with { Y = Math.Max(point.Y, monitor.Bounds.Top + 2.6 * h) };
    }

    private static PixelRect Lerp(PixelRect a, PixelRect b, double t) => new(
        (int)Math.Round(a.Left + (b.Left - a.Left) * t),
        (int)Math.Round(a.Top + (b.Top - a.Top) * t),
        (int)Math.Round(a.Right + (b.Right - a.Right) * t),
        (int)Math.Round(a.Bottom + (b.Bottom - a.Bottom) * t));
}
