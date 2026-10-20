using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Terrain;

namespace Lander.Core.Autopilot;

/// <summary>
/// Cuentas de combustible del piloto automático: cuánto cuesta un vuelo y si queda bastante para hacerlo
/// y volver después a la base. Las estimaciones son prudentes (siempre por encima de lo que se gasta).
/// </summary>
public static class FuelPlanner
{
    /// <summary>Reserva que nunca se planea gastar, como fracción del depósito.</summary>
    public const double ReserveFraction = 0.08;

    /// <summary>Combustible por segundo en vuelo estacionario.</summary>
    public static double HoverBurn(LanderBody body, PhysicsConfig config) =>
        config.MainBurnRate * Math.Min(1, config.Gravity / (config.MainThrust * ThrustFactor(body, config)));

    public static double ThrustFactor(LanderBody body, PhysicsConfig config) =>
        1 - config.MaxDamageThrustPenalty * Math.Clamp(body.Damage, 0, 1);

    public static double Reserve(LanderBody body) => body.FuelCapacity * ReserveFraction;

    /// <summary>Duración aproximada (y generosa) de un vuelo entre dos centros.</summary>
    public static double FlightSeconds(LanderBody body, Vec2 from, Vec2 to)
    {
        var scale = body.Scale;
        var dx = Math.Abs(to.X - from.X) / scale;
        var climb = Math.Max(0, from.Y - to.Y) / scale;
        var drop = Math.Max(0, to.Y - from.Y) / scale;

        // Despegue, subida a crucero y aproximación final prudente, más el trayecto.
        // El margen de subida cubre la altura de crucero y la cima de los arcos.
        return 4 + dx / 140 + (climb + 3 * LanderBody.BaseHeight) / 120 + (drop + LanderBody.BaseHeight) / 130;
    }

    /// <summary>Combustible que cuesta volar de <paramref name="from"/> a <paramref name="to"/>.</summary>
    public static double Estimate(LanderBody body, PhysicsConfig config, Vec2 from, Vec2 to)
    {
        // Inclinarse para acelerar y frenar cuesta algo más que mantenerse quieto, y los propulsores de
        // giro también gastan.
        var seconds = FlightSeconds(body, from, to);
        return seconds * (HoverBurn(body, config) * 1.2 + config.RotationBurnRate * 0.3);
    }

    /// <summary>Lo que cuesta llegar desde <paramref name="from"/> a la base más cercana.</summary>
    public static double ToBase(World world, LanderBody body, PhysicsConfig config, Vec2 from)
    {
        var monitor = world.MonitorAt(from) ?? world.NearestMonitor(from);
        var ground = (monitor is null ? null : world.GroundOf(monitor.Index)) ?? world.NearestGround(from);
        if (ground is null)
        {
            return 0;
        }

        var x = Math.Clamp(from.X, ground.Left, ground.Right);
        return Estimate(body, config, from, new Vec2(x, ground.Y - body.HalfHeight));
    }

    /// <summary>
    /// Si se puede volar hasta <paramref name="target"/> y, desde allí, volver a la base sin tocar la
    /// reserva (salvo que el destino sea la base).
    /// </summary>
    public static bool CanReach(World world, LanderBody body, PhysicsConfig config, Platform platform, Vec2 target)
    {
        var cost = Estimate(body, config, body.Position, target);
        var after = body.Fuel - cost;
        if (platform.IsBase)
        {
            return after >= Reserve(body) * 0.5;
        }

        return after >= ToBase(world, body, config, target) + Reserve(body);
    }

    /// <summary>
    /// Combustible para frenar la caída actual y posarse: lo mínimo para no estrellarse desde aquí.
    /// </summary>
    public static double ToStop(LanderBody body, PhysicsConfig config)
    {
        var scale = body.Scale;
        var deceleration = (config.MainThrust * ThrustFactor(body, config) - config.Gravity) * scale;
        var falling = Math.Max(0, body.Velocity.Y);
        var burnSeconds = deceleration > 0 ? falling / deceleration : 10;
        return burnSeconds * config.MainBurnRate + 2.5 * HoverBurn(body, config);
    }
}
