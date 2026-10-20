using Lander.Core.Geometry;

namespace Lander.Core.Physics;

/// <summary>Límites de velocidad e inclinación para clasificar un aterrizaje (a escala 1).</summary>
/// <param name="VerticalSpeed">Velocidad de descenso máxima en px/s.</param>
/// <param name="HorizontalSpeed">Velocidad lateral máxima en px/s.</param>
/// <param name="Angle">Inclinación máxima en radianes.</param>
public readonly record struct LandingThresholds(double VerticalSpeed, double HorizontalSpeed, double Angle)
{
    public LandingThresholds Scaled(double factor) =>
        new(VerticalSpeed * factor, HorizontalSpeed * factor, Angle * factor);
}

/// <summary>
/// Constantes físicas. Las magnitudes en píxeles están expresadas para un lander de 48 px a 100 % de
/// escala; el motor las multiplica por la escala real del lander.
/// </summary>
public sealed record PhysicsConfig
{
    // Movimiento
    public double Gravity { get; init; } = 150;
    public double MainThrust { get; init; } = 360;
    public double LateralThrust { get; init; } = 170;
    public double RotationAcceleration { get; init; } = 7;
    public double MaxAngularSpeed { get; init; } = 3.2;
    public double LinearDrag { get; init; } = 0.12;
    public double AngularDrag { get; init; } = 1.5;
    public double StabilizerStiffness { get; init; } = 16;
    public double StabilizerDamping { get; init; } = 7;

    public double BoostImpulse { get; init; } = 230;
    public double BoostCooldown { get; init; } = 0.7;

    // Combustible
    public double FuelCapacity { get; init; } = 100;
    public double MainBurnRate { get; init; } = 4.5;
    public double LateralBurnRate { get; init; } = 1.6;
    public double RotationBurnRate { get; init; } = 0.6;
    public double BoostFuelCost { get; init; } = 10;
    public double BaseRefuelRate { get; init; } = 30;
    public double TrickleRefuelRate { get; init; } = 1.2;

    // Daño
    public double BaseRepairRate { get; init; } = 0.06;
    public double MaxDamageThrustPenalty { get; init; } = 0.3;
    public double HardLandingDamage { get; init; } = 0.12;
    public double CrashDamage { get; init; } = 0.35;

    // Contactos
    public double LegDeployRate { get; init; } = 5;
    public double CeilingRestitution { get; init; } = 0.3;
    public double WallRestitution { get; init; } = 0.45;

    /// <summary>Aceleración máxima que el agarre de las patas transmite cuando la plataforma se mueve.</summary>
    public double GripAcceleration { get; init; } = 7000;

    /// <summary>Margen de velocidad antes de separarse de una plataforma que acelera hacia abajo o frena al subir.</summary>
    public double DetachTolerance { get; init; } = 120;

    public double MaxLaunchSpeed { get; init; } = 650;

    /// <summary>Cuánto inclina al lander la aceleración de la plataforma (terremoto).</summary>
    public double SwayFactor { get; init; } = 0.15;
    public double LandedStiffness { get; init; } = 60;
    public double LandedDamping { get; init; } = 11;
    public double ToppleAngle { get; init; } = 0.62;

    // Aterrizajes
    public LandingThresholds Perfect { get; init; } = new(70, 45, MathUtil.DegreesToRadians(8));
    public LandingThresholds Hard { get; init; } = new(170, 120, MathUtil.DegreesToRadians(25));
}
