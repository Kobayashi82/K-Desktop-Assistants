using Lander.Core.Geometry;
using Lander.Core.Racing;
using Lander.Core.Terrain;

namespace Lander.Core.Game;

/// <summary>
/// Piloto de demostración (<c>--demo-manual</c> y <c>--demo-race</c>): «pulsa» las teclas del modo manual
/// como lo haría una persona, saltando de plataforma en plataforma o siguiendo las balizas de una carrera.
/// Sirve para probar y grabar el modo manual sin tocar el teclado de verdad.
/// </summary>
public sealed class DemoPilot(IRandom? random = null)
{
    private readonly IRandom _random = random ?? new GameRandom();
    private long _targetOwner;
    private double _targetLocalX;
    private double _restUntil;

    public ManualControls Next(LanderGame game, World world)
    {
        var body = game.Body;
        var racing = game.Races.Current is { IsActive: true, NextCheckpoint: not null };
        if (racing)
        {
            TargetCheckpoint(game, world);
        }

        if (body.IsLanded)
        {
            // Posado en otro sitio (una plataforma en medio del camino): vale igual, se elige otro destino. Si
            // no, despegaría, volvería a caer en la misma y así sin parar.
            if (_targetOwner == 0 || body.Support?.OwnerId == _targetOwner || !racing)
            {
                if (_restUntil == 0)
                {
                    _restUntil = game.Time + (racing ? 0.3 : 2);
                }

                if (game.Time < _restUntil)
                {
                    return default;
                }

                if (!racing)
                {
                    PickTarget(game, world);
                }

                _restUntil = 0;
            }

            return new ManualControls(Up: true, Down: false, Left: false, Right: false, Boost: false);
        }

        if (!world.TryGetOwnerOrigin(_targetOwner, out var origin)
            || world.FindSegment(_targetOwner, origin.X + _targetLocalX) is not { } platform)
        {
            // Destino perdido: mantenerse en el aire mientras se elige otro.
            if (!racing)
            {
                PickTarget(game, world);
            }

            return new ManualControls(Up: body.Velocity.Y > -20 * body.Scale, Down: false, Left: false, Right: false, Boost: false);
        }

        var scale = body.Scale;
        var target = new Vec2(origin.X + _targetLocalX, platform.Y - body.HalfHeight);
        var dx = target.X - body.Position.X;
        var above = target.Y - body.Position.Y;
        var aligned = Math.Abs(dx) < body.Height * 0.4;
        var top = world.MonitorAt(target)?.Bounds.Top ?? double.MinValue;
        var cruiseHeight = Math.Min(body.Height * 1.5, target.Y - top - body.Height * 0.8);

        // Primero ganar altura por encima del destino, luego desplazarse y por último bajar suave.
        double desiredVy;
        if (above < cruiseHeight && !aligned)
        {
            desiredVy = -120 * scale;
        }
        else if (!aligned)
        {
            desiredVy = 0;
        }
        else
        {
            desiredVy = above > body.Height * 2 ? 90 * scale : 30 * scale;
        }

        var desiredVx = Math.Clamp(dx * 1.4, -160 * scale, 160 * scale);
        var vx = body.Velocity.X;
        return new ManualControls(
            Up: body.Velocity.Y > desiredVy,
            Down: false,
            Left: false,
            Right: false,
            Boost: false,
            SlideLeft: vx > desiredVx + 12 * scale,
            SlideRight: vx < desiredVx - 12 * scale);
    }

    /// <summary>En carrera, el destino es siempre la próxima baliza.</summary>
    private void TargetCheckpoint(LanderGame game, World world)
    {
        var body = game.Body;
        var checkpoint = game.Races.Current!.NextCheckpoint!;
        Platform? platform;
        double x;
        if (checkpoint.IsBase)
        {
            // Vale cualquier suelo: el que esté justo debajo o el más cercano.
            platform = world.NearestGround(body.Position);
            if (platform is null)
            {
                return;
            }

            if (platform.OwnerId == _targetOwner)
            {
                return;
            }

            // El punto del suelo más cercano al que se pueda bajar sin topar con otra ventana.
            var margin = body.Height;
            var from = body.Position.Y - body.Height * 2;
            x = platform.CenterX;
            var best = double.MaxValue;
            for (var candidate = platform.Left + margin; candidate <= platform.Right - margin; candidate += body.Height * 0.5)
            {
                var distance = Math.Abs(candidate - body.Position.X);
                if (distance < best && world.HasClearDescent(candidate, from, platform.Y, body.HalfWidth * 1.5, platform.OwnerId))
                {
                    best = distance;
                    x = candidate;
                }
            }
        }
        else
        {
            if (checkpoint.OwnerId == _targetOwner)
            {
                return;
            }

            platform = Race.TargetPlatform(world, checkpoint);
            if (platform is null)
            {
                return;
            }

            x = platform.CenterX;
        }

        _targetOwner = platform.OwnerId;
        _targetLocalX = x - platform.OwnerOrigin.X;
    }

    private void PickTarget(LanderGame game, World world)
    {
        var body = game.Body;
        var monitor = world.MonitorAt(body.Position)?.Index ?? -1;
        var margin = body.Height;
        var options = world.Platforms
            .Where(p => p.MonitorIndex == monitor && p.Width > margin * 3)
            .ToList();
        var elsewhere = options.Where(p => p.OwnerId != body.Support?.OwnerId).ToList();
        if (elsewhere.Count > 0)
        {
            options = elsewhere;
        }

        if (options.Count == 0)
        {
            return;
        }

        // Un punto lejos de donde está, para que el salto merezca la pena, y al que se pueda bajar sin topar
        // con otra plataforma (los bordes del contenido de una página suelen estar uno encima de otro).
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var platform = _random.Pick(options);
            var x = _random.Range(platform.Left + margin, platform.Right - margin);
            if (Math.Abs(x - body.Position.X) < body.Height * 4)
            {
                x = body.Position.X < platform.CenterX ? platform.Right - margin : platform.Left + margin;
            }

            var from = Math.Min(platform.Y - body.Height * 3, body.FeetY - 1);
            if (attempt < 7 && !world.HasClearDescent(x, from, platform.Y, body.HalfWidth * 1.5, platform.OwnerId))
            {
                continue;
            }

            _targetOwner = platform.OwnerId;
            _targetLocalX = x - platform.OwnerOrigin.X;
            return;
        }
    }
}
