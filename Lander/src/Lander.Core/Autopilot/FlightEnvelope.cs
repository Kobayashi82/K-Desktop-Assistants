using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Terrain;

namespace Lander.Core.Autopilot;

/// <summary>
/// Límites de seguridad del vuelo automático: cuánto puede subir o desplazarse el lander sin chocar con
/// el borde superior de la pantalla o con una pared, contando con lo que tarda en frenar.
/// </summary>
public static class FlightEnvelope
{
    /// <summary>Hueco que se deja siempre entre la cabeza del lander y el techo (en alturas del lander).</summary>
    public const double CeilingMargin = 0.18;

    /// <summary>Deceleración lateral con la que se cuenta para frenar ante una pared (a escala 1).</summary>
    private const double LateralBraking = 95;

    /// <summary>Inclinación con la que se cuenta para frenar de lado (radianes).</summary>
    private const double BrakingTilt = 0.45;

    /// <summary>Valor de <c>landOn</c> para <see cref="Limit"/> que desactiva el aviso de proximidad al suelo.</summary>
    public const long NoGroundGuard = long.MaxValue;

    /// <summary>Techo más cercano por encima de <paramref name="position"/>, o -∞ si no hay ninguno.</summary>
    public static double CeilingAbove(World world, Vec2 position, double halfWidth = 0)
    {
        var best = double.NegativeInfinity;
        foreach (var barrier in world.Barriers)
        {
            if (barrier.Kind == BarrierKind.Ceiling && barrier.Position <= position.Y
                && barrier.To >= position.X - halfWidth && barrier.From <= position.X + halfWidth && barrier.Position > best)
            {
                best = barrier.Position;
            }
        }

        return best;
    }

    /// <summary>Altura mínima (Y más pequeña) a la que puede estar el centro del lander bajo ese techo.</summary>
    public static double HighestCenter(World world, LanderBody body, Vec2 position, double extraMargin = 0)
    {
        var ceiling = CeilingAbove(world, position, body.HalfWidth);
        return double.IsNegativeInfinity(ceiling)
            ? double.NegativeInfinity
            : ceiling + body.HalfHeight + body.Height * (CeilingMargin + extraMargin);
    }

    /// <summary>
    /// Como <see cref="HighestCenter"/> en la posición actual, pero contando también el techo de lo que va a
    /// barrer de lado mientras sube por inercia: junto a un monitor más bajo, su techo cuenta antes de llegar.
    /// </summary>
    public static double HighestCenterAhead(World world, LanderBody body, PhysicsConfig config)
    {
        var vx = body.Velocity.X;
        var rising = Math.Max(0, -body.Velocity.Y) / (config.Gravity * body.Scale);
        var reach = Math.Abs(vx) * (rising + 0.3);
        var from = body.Position.X - body.HalfWidth - (vx < 0 ? reach : 0);
        var to = body.Position.X + body.HalfWidth + (vx > 0 ? reach : 0);
        var best = double.NegativeInfinity;
        foreach (var barrier in world.Barriers)
        {
            if (barrier.Kind == BarrierKind.Ceiling && barrier.Position <= body.Position.Y
                && barrier.To >= from && barrier.From <= to && barrier.Position > best)
            {
                best = barrier.Position;
            }
        }

        return double.IsNegativeInfinity(best) ? best : best + body.HalfHeight + body.Height * CeilingMargin;
    }

    /// <summary>Si el lander cabe con su margen bajo el techo estando posado con el centro en <paramref name="center"/>.</summary>
    public static bool HasHeadroom(World world, LanderBody body, Vec2 center) =>
        center.Y >= HighestCenter(world, body, center) - 1;

    /// <summary>Punto más alto que alcanzará el centro si apaga el motor ahora.</summary>
    public static double CoastApex(LanderBody body, PhysicsConfig config)
    {
        var vy = body.Velocity.Y;
        if (vy >= 0)
        {
            return body.Position.Y;
        }

        var gravity = config.Gravity * body.Scale;
        return body.Position.Y - vy * vy / (2 * gravity);
    }

    /// <summary>
    /// Recorta una velocidad deseada para que siempre se pueda frenar antes del techo y de las paredes.
    /// Hacia arriba basta con cortar el motor (la gravedad frena); de lado hace falta inclinarse.
    /// </summary>
    public static Vec2 Limit(World world, LanderBody body, PhysicsConfig config, Vec2 desired, long landOn = ThrustInput.GroundOnly)
    {
        var scale = body.Scale;

        // Hacia abajo: la superficie en la que se posaría al tocarla (el suelo o su destino) tiene que
        // dejar sitio para frenar hasta una velocidad de contacto suave.
        if (desired.Y > 0 && landOn != NoGroundGuard && SurfaceBelow(world, body, landOn) is { } surface)
        {
            var gap = surface - body.FeetY - body.Height * 0.3;
            var braking = (config.MainThrust * FuelPlanner.ThrustFactor(body, config) - config.Gravity) * scale * 0.5;
            var maxDown = Math.Max(36 * scale, Math.Sqrt(2 * Math.Max(1, braking) * Math.Max(0, gap)));
            desired = desired.WithY(Math.Min(desired.Y, maxDown));
        }

        if (desired.Y < 0)
        {
            var highest = HighestCenterAhead(world, body, config);
            if (!double.IsNegativeInfinity(highest))
            {
                var room = Math.Max(0, body.Position.Y - highest);
                var maxUp = Math.Sqrt(2 * config.Gravity * scale * 0.7 * room);
                desired = desired.WithY(Math.Max(desired.Y, -maxUp));
            }
        }

        if (desired.X != 0)
        {
            var wall = desired.X > 0 ? WallRight(world, body) : WallLeft(world, body);
            if (!double.IsInfinity(wall))
            {
                var room = Math.Max(0, Math.Abs(wall - body.Position.X) - body.HalfWidth - body.Height * 0.3);
                // Para frenar de lado primero hay que inclinarse hacia el otro lado, y mientras gira sigue
                // avanzando: más tiempo cuanto más inclinado vaya hacia la pared.
                var toward = body.Angle * Math.Sign(desired.X);
                var lag = 0.2 + 2 * Math.Sqrt(Math.Max(0, toward + BrakingTilt) / config.RotationAcceleration);
                var braking = LateralBraking * scale;
                var maxSide = braking * (Math.Sqrt(lag * lag + 2 * room / braking) - lag);
                desired = desired.WithX(Math.Clamp(desired.X, -maxSide, maxSide));
            }
        }

        return desired;
    }

    /// <summary>
    /// Última comprobación antes de mandar el empuje: si, apagando el motor ya, el lander apenas se queda
    /// por debajo del techo, se apaga. Es la única maniobra que nunca falla hacia arriba.
    /// </summary>
    public static ThrustInput Guard(World world, LanderBody body, PhysicsConfig config, ThrustInput input)
    {
        if (!body.IsFlying || input.Throttle <= 0 || body.Velocity.Y >= 0)
        {
            return input;
        }

        var highest = HighestCenterAhead(world, body, config);
        if (double.IsNegativeInfinity(highest))
        {
            return input;
        }

        // Con el motor encendido, en el siguiente paso aún sube un poco más: se cuenta con ello.
        var dt = 1.0 / 60;
        var lift = input.Throttle * config.MainThrust * body.Scale * Math.Max(0, Math.Cos(body.Angle)) - config.Gravity * body.Scale;
        var vyNext = body.Velocity.Y - Math.Max(0, lift) * dt;
        var apex = body.Position.Y + vyNext * dt - vyNext * vyNext / (2 * config.Gravity * body.Scale);
        return apex < highest ? input with { Throttle = 0, Boost = false } : input;
    }

    /// <summary>
    /// Si el lander puede recorrer los puntos de <paramref name="path"/> con todo el cuerpo dentro de algún
    /// monitor (así no choca con las paredes y techos que quedan entre monitores de distinto tamaño), sin
    /// rozar el techo y sin pasar justo por encima de un suelo más alto que su destino, donde se posaría antes
    /// de tiempo. Los puntos desde <paramref name="ignoreFrom"/> (la llegada) no se comprueban.
    /// </summary>
    public static bool PathIsClear(World world, LanderBody body, IReadOnlyList<Vec2> path, double targetFeetY, int ignoreFrom = int.MaxValue)
    {
        // Con un poco de margen: el lander no sigue el recorrido al píxel.
        var halfWidth = body.HalfWidth + body.Height * 0.2;
        var halfHeight = body.HalfHeight + body.Height * 0.2;
        var count = Math.Min(path.Count, ignoreFrom);
        for (var i = 0; i < count; i++)
        {
            var point = path[i];
            if (world.MonitorAt(point + new Vec2(-halfWidth, -halfHeight)) is null || world.MonitorAt(point + new Vec2(halfWidth, -halfHeight)) is null
                || world.MonitorAt(point + new Vec2(-halfWidth, body.HalfHeight * 0.8)) is null || world.MonitorAt(point + new Vec2(halfWidth, body.HalfHeight * 0.8)) is null)
            {
                return false;
            }

            var highest = HighestCenter(world, body, point);
            if (!double.IsNegativeInfinity(highest) && point.Y < highest - 1)
            {
                return false;
            }

            foreach (var platform in world.Platforms)
            {
                if (platform.Kind == PlatformKind.Ground && platform.Y < targetFeetY - 1
                    && platform.Right >= point.X - body.HalfWidth && platform.Left <= point.X + body.HalfWidth
                    && point.Y + body.HalfHeight + body.Height * 0.5 > platform.Y && point.Y < platform.Y)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Y de la primera superficie bajo las patas en la que el lander se posaría (el suelo y, si se indica,
    /// las plataformas de <paramref name="landOn"/>), o null si no hay ninguna.
    /// </summary>
    public static double? SurfaceBelow(World world, LanderBody body, long landOn = ThrustInput.GroundOnly)
    {
        double? best = null;
        var feet = body.FeetY;
        foreach (var platform in world.Platforms)
        {
            var lands = platform.Kind == PlatformKind.Ground || landOn == ThrustInput.AnyPlatform || platform.OwnerId == landOn;
            if (lands && platform.Y >= feet - 1 && platform.Right >= body.Position.X - body.HalfWidth
                && platform.Left <= body.Position.X + body.HalfWidth && (best is null || platform.Y < best))
            {
                best = platform.Y;
            }
        }

        return best;
    }

    private static double WallLeft(World world, LanderBody body)
    {
        var best = double.NegativeInfinity;
        foreach (var barrier in world.Barriers)
        {
            if (barrier.Kind == BarrierKind.LeftWall && barrier.Position <= body.Position.X && barrier.Spans(body.Position.Y) && barrier.Position > best)
            {
                best = barrier.Position;
            }
        }

        return best;
    }

    private static double WallRight(World world, LanderBody body)
    {
        var best = double.PositiveInfinity;
        foreach (var barrier in world.Barriers)
        {
            if (barrier.Kind == BarrierKind.RightWall && barrier.Position >= body.Position.X && barrier.Spans(body.Position.Y) && barrier.Position < best)
            {
                best = barrier.Position;
            }
        }

        return best;
    }
}
