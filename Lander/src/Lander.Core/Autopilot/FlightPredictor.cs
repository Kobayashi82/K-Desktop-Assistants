using Lander.Core.Geometry;
using Lander.Core.Physics;

namespace Lander.Core.Autopilot;

/// <summary>
/// Simulación rápida del vuelo con las mismas ecuaciones de movimiento que <see cref="PhysicsEngine"/>, pero
/// sin choques ni consumo. Sirve para prever dónde se quedaría quieto el lander si frenara ahora o qué
/// parábola seguirá con el motor apagado.
/// </summary>
public static class FlightPredictor
{
    /// <summary>Paso de la simulación (más grueso que el de la física; de sobra para prever).</summary>
    public const double Step = 1.0 / 30;

    /// <summary>Copia en <paramref name="probe"/> el estado de vuelo de <paramref name="source"/>.</summary>
    public static LanderBody CopyInto(LanderBody probe, LanderBody source)
    {
        probe.Position = source.Position;
        probe.Velocity = source.Velocity;
        probe.Angle = source.Angle;
        probe.AngularVelocity = source.AngularVelocity;
        probe.Scale = source.Scale;
        probe.Fuel = source.Fuel;
        probe.FuelCapacity = source.FuelCapacity;
        probe.Damage = source.Damage;
        probe.LegsDeploy = source.LegsDeploy;
        probe.ExternalAcceleration = source.ExternalAcceleration;
        return probe;
    }

    /// <summary>Avanza <paramref name="body"/> un paso con los mandos dados (igual que en vuelo, sin colisiones).</summary>
    public static void Advance(LanderBody body, PhysicsConfig config, in ThrustInput input, double dt)
    {
        var scale = body.Scale;
        var hasFuel = body.Fuel > 0;
        var thrustFactor = FuelPlanner.ThrustFactor(body, config);

        var angularAcceleration = hasFuel ? Math.Clamp(input.Rotate, -1, 1) * config.RotationAcceleration : 0;
        if (input.Stabilize || !hasFuel)
        {
            angularAcceleration += -body.Angle * config.StabilizerStiffness - body.AngularVelocity * config.StabilizerDamping;
        }

        var angularVelocity = (body.AngularVelocity + angularAcceleration * dt) * Math.Max(0, 1 - config.AngularDrag * dt);
        body.AngularVelocity = Math.Clamp(angularVelocity, -config.MaxAngularSpeed, config.MaxAngularSpeed);
        body.Angle = MathUtil.WrapAngle(body.Angle + body.AngularVelocity * dt);

        var acceleration = new Vec2(0, config.Gravity * scale) + body.ExternalAcceleration;
        if (hasFuel)
        {
            acceleration += body.Up * (Math.Clamp(input.Throttle, 0, 1) * config.MainThrust * scale * thrustFactor);
            acceleration += new Vec2(Math.Clamp(input.Lateral, -1, 1) * config.LateralThrust * scale * thrustFactor, 0);
        }

        body.Velocity = (body.Velocity + acceleration * dt) * Math.Max(0, 1 - config.LinearDrag * dt);
        body.Position += body.Velocity * dt;
    }

    /// <summary>
    /// Dónde se quedaría quieto <paramref name="probe"/> si empezara ya a frenar a fondo, inclinándose como
    /// mucho <paramref name="maxTilt"/>. El estado de <paramref name="probe"/> se pierde.
    /// </summary>
    public static Vec2 StopPoint(LanderBody probe, PhysicsConfig config, double maxTilt, double maxSeconds = 6)
    {
        var stopped = 18 * probe.Scale;
        for (var t = 0.0; t < maxSeconds && probe.Velocity.Length > stopped; t += Step)
        {
            var input = FlightGuidance.TrackVelocity(probe, config, Vec2.Zero, maxTilt: maxTilt);
            Advance(probe, config, input, Step);
        }

        return probe.Position;
    }

    /// <summary>
    /// Trayectoria con el motor apagado desde <paramref name="from"/> con velocidad <paramref name="velocity"/>
    /// hasta volver a bajar a <paramref name="endY"/> (o <paramref name="maxSeconds"/>). Devuelve el punto
    /// final, la Y más alta alcanzada y la duración; si <paramref name="points"/> no es null, añade los puntos.
    /// </summary>
    public static (Vec2 End, double ApexY, double Seconds) Coast(
        Vec2 from,
        Vec2 velocity,
        double endY,
        double gravity,
        PhysicsConfig config,
        List<Vec2>? points = null,
        double maxSeconds = 12)
    {
        var position = from;
        var apex = from.Y;
        var t = 0.0;
        var damping = Math.Max(0, 1 - config.LinearDrag * Step);
        for (; t < maxSeconds; t += Step)
        {
            velocity = (velocity + new Vec2(0, gravity * Step)) * damping;
            position += velocity * Step;
            apex = Math.Min(apex, position.Y);
            points?.Add(position);
            if (velocity.Y > 0 && position.Y >= endY)
            {
                break;
            }
        }

        return (position, apex, t);
    }

    /// <summary>
    /// Velocidad con la que, apagando el motor, una parábola que sale de <paramref name="from"/> sube hasta
    /// <paramref name="apexY"/> y vuelve a bajar pasando por <paramref name="aim"/> (con la resistencia del aire).
    /// </summary>
    public static Vec2 LaunchVelocity(Vec2 from, Vec2 aim, double apexY, double gravity, PhysicsConfig config)
    {
        var rise = Math.Max(1, from.Y - apexY);
        var fall = Math.Max(1, aim.Y - apexY);
        var vy = -Math.Sqrt(2 * gravity * rise);
        var vx = (aim.X - from.X) / (Math.Sqrt(2 * rise / gravity) + Math.Sqrt(2 * fall / gravity));

        // La resistencia acorta la parábola: se corrige disparando unas cuantas veces.
        for (var i = 0; i < 4; i++)
        {
            var (end, apex, _) = Coast(from, new Vec2(vx, vy), aim.Y, gravity, config);
            if (Math.Abs(end.X - from.X) > 1)
            {
                vx *= Math.Clamp((aim.X - from.X) / (end.X - from.X), 0.5, 2);
            }

            if (from.Y - apex > 1)
            {
                vy *= Math.Clamp(Math.Sqrt(rise / (from.Y - apex)), 0.7, 1.4);
            }
        }

        return new Vec2(vx, vy);
    }
}
