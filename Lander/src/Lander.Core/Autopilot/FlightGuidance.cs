using Lander.Core.Geometry;
using Lander.Core.Physics;

namespace Lander.Core.Autopilot;

/// <summary>
/// Controlador de vuelo: convierte una velocidad deseada en mandos (inclinación + motor), igual que lo
/// haría un piloto en modo clásico.
/// </summary>
public static class FlightGuidance
{
    public const double DefaultMaxTilt = 0.6;

    private const double VelocityGain = 2.4;
    private const double AngleGain = 30;
    private const double AngularRateGain = 9;

    public static ThrustInput TrackVelocity(
        LanderBody body,
        PhysicsConfig config,
        Vec2 desiredVelocity,
        bool retractLegs = false,
        double maxTilt = DefaultMaxTilt)
    {
        var scale = body.Scale;
        var gravity = config.Gravity * scale;
        var thrustFactor = 1 - config.MaxDamageThrustPenalty * Math.Clamp(body.Damage, 0, 1);
        var maxThrust = config.MainThrust * scale * thrustFactor;

        // Aceleración necesaria, compensando resistencia del aire y viento.
        var acceleration = (desiredVelocity - body.Velocity) * VelocityGain
            + body.Velocity * config.LinearDrag
            - body.ExternalAcceleration;

        // Empuje que hay que generar (y negativa = hacia arriba).
        var required = new Vec2(acceleration.X, acceleration.Y - gravity);

        var desiredAngle = Math.Clamp(Math.Atan2(required.X, Math.Max(-required.Y, gravity * 0.2)), -maxTilt, maxTilt);

        double throttle;
        var cos = Math.Cos(body.Angle);
        if (required.Y >= 0)
        {
            throttle = 0;
        }
        else if (cos > 0.3)
        {
            // Prioridad a la altura: el empuje vertical cubre lo necesario; la inclinación da lo lateral.
            throttle = -required.Y / (maxThrust * cos);
        }
        else
        {
            throttle = Vec2.Dot(required, body.Up) / maxThrust;
        }

        return new ThrustInput(
            Throttle: Math.Clamp(throttle, 0, 1),
            Rotate: RotateToward(body, config, desiredAngle),
            RetractLegs: retractLegs);
    }

    /// <summary>
    /// Como <see cref="TrackVelocity"/>, pero sin inclinarse: el motor principal sostiene y da la altura y
    /// los propulsores de traslación, el desplazamiento lateral, como al atracar una nave.
    /// </summary>
    public static ThrustInput TrackVelocityLevel(LanderBody body, PhysicsConfig config, Vec2 desiredVelocity, bool retractLegs = false)
    {
        var scale = body.Scale;
        var gravity = config.Gravity * scale;
        var thrustFactor = 1 - config.MaxDamageThrustPenalty * Math.Clamp(body.Damage, 0, 1);
        var acceleration = (desiredVelocity - body.Velocity) * VelocityGain
            + body.Velocity * config.LinearDrag
            - body.ExternalAcceleration;

        var up = gravity - acceleration.Y;
        var throttle = up <= 0 ? 0 : up / (config.MainThrust * scale * thrustFactor * Math.Max(0.3, Math.Cos(body.Angle)));
        var lateral = Math.Clamp(acceleration.X / (config.LateralThrust * scale * thrustFactor), -1, 1);
        return new ThrustInput(
            Throttle: Math.Clamp(throttle, 0, 1),
            Rotate: RotateToward(body, config, 0),
            Lateral: Math.Abs(lateral) < 0.04 ? 0 : lateral,
            RetractLegs: retractLegs);
    }

    /// <summary>Mando de los propulsores de giro para llevar la nave a <paramref name="desiredAngle"/>.</summary>
    public static double RotateToward(LanderBody body, PhysicsConfig config, double desiredAngle)
    {
        var angleError = MathUtil.WrapAngle(desiredAngle - body.Angle);
        var angularAcceleration = AngleGain * angleError - AngularRateGain * body.AngularVelocity;
        var rotate = Math.Clamp(angularAcceleration / config.RotationAcceleration, -1, 1);
        return Math.Abs(rotate) < 0.02 ? 0 : rotate;
    }
}
