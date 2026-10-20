using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Terrain;

namespace Lander.Core.Autopilot;

/// <summary>
/// Punto de aterrizaje elegido, relativo a su dueño: si la ventana se mueve, el destino se mueve con ella.
/// </summary>
public sealed record Destination(long OwnerId, double LocalX, PlatformKind Kind, int MonitorIndex)
{
    public static Destination On(Platform platform, double x) =>
        new(platform.OwnerId, x - platform.OwnerOrigin.X, platform.Kind, platform.MonitorIndex);

    /// <summary>Calcula dónde debe quedar el centro del lander al posarse.</summary>
    public bool TryResolve(World world, LanderBody body, out Vec2 center, out Platform segment)
    {
        center = default;
        segment = null!;
        if (!world.TryGetOwnerOrigin(OwnerId, out var origin))
        {
            return false;
        }

        var x = origin.X + LocalX;
        if (world.FindSegment(OwnerId, x) is not { } found)
        {
            return false;
        }

        segment = found;
        center = new Vec2(x, found.Y - body.HalfHeight);
        return true;
    }
}
