using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Terrain;

namespace Lander.Core.Autopilot;

/// <summary>Criterios para elegir un destino de exploración.</summary>
public sealed record DestinationQuery
{
    public double Energy { get; init; } = 1;

    public double Curiosity { get; init; }

    /// <summary>Peso extra del suelo (base): mayor cuando falta combustible o hay daños.</summary>
    public double BaseBias { get; init; } = 0.6;

    /// <summary>Dueño que se excluye (normalmente la plataforma actual).</summary>
    public long? ExcludeOwner { get; init; }

    /// <summary>Ventanas recién abiertas que despiertan curiosidad.</summary>
    public IReadOnlySet<long> NewWindows { get; init; } = new HashSet<long>();

    public IReadOnlySet<int> AvoidMonitors { get; init; } = new HashSet<int>();

    /// <summary>Ventana en primer plano: sus bordes de contenido se prefieren a los de las demás.</summary>
    public long ActiveWindow { get; init; }

    /// <summary>Si hay combustible para llegar a una plataforma (y volver luego a la base).</summary>
    public Func<Platform, Vec2, bool>? CanReach { get; init; }

    /// <summary>Donde están quietos otros asistentes: ahí no se posa.</summary>
    public IReadOnlyList<PixelRect> Occupied { get; init; } = [];
}

public static class DestinationPicker
{
    private const int SamplesPerPlatform = 3;

    /// <summary>
    /// Peso de los bordes de contenido: van «de vez en cuando». Cada ventana reparte el suyo entre sus bordes
    /// (crece con la raíz de cuántos tiene), así que una página llena de líneas no acapara los vuelos.
    /// </summary>
    private const double ContentBias = 0.3;

    /// <summary>Los bordes de la ventana activa, que es la que el usuario está mirando, valen más.</summary>
    private const double ActiveContentBias = 1.8;

    /// <summary>Margen a cada lado del punto de aterrizaje para que las dos patas apoyen.</summary>
    public static double LandingMargin(LanderBody body) => body.FootOffset + 10 * body.Scale;

    public static Destination? PickExploration(World world, LanderBody body, DestinationQuery query, IRandom random)
    {
        var candidates = new List<Destination>();
        var weights = new List<double>();
        var margin = LandingMargin(body);
        var height = body.Height;
        var currentMonitor = world.MonitorAt(body.Position)?.Index ?? -1;
        var reach = (700 + 900 * query.Energy) * body.Scale;
        Dictionary<long, int>? contentCounts = null;

        foreach (var platform in world.Platforms)
        {
            if (platform.OwnerId == query.ExcludeOwner || query.AvoidMonitors.Contains(platform.MonitorIndex)
                || platform.Width < margin * 2 + 4)
            {
                continue;
            }

            if (!TryPickX(world, body, platform, random, query.Occupied, out var x))
            {
                continue;
            }

            var target = new Vec2(x, platform.Y - body.HalfHeight);
            var distance = Vec2.Distance(target, body.Position);
            if (distance < height * 2 || query.CanReach?.Invoke(platform, target) == false || !FlightEnvelope.HasHeadroom(world, body, target))
            {
                continue;
            }

            var weight = Math.Exp(-distance / reach);

            // Cansado: prefiere sitios amplios y cómodos.
            weight *= 1 + Math.Min(platform.Width / (height * 12), 1.5) * (1 - query.Energy);

            if (platform.Kind == PlatformKind.Ground)
            {
                weight *= query.BaseBias;
            }
            else if (platform.Kind == PlatformKind.Content)
            {
                contentCounts ??= CountContentByWindow(world);
                var window = platform.WindowHandle;
                weight *= ContentBias / Math.Sqrt(contentCounts[window]) * (window == query.ActiveWindow ? ActiveContentBias : 1);
            }

            if (query.NewWindows.Contains(platform.OwnerId))
            {
                weight *= 1 + query.Curiosity * 4;
            }

            if (platform.MonitorIndex != currentMonitor)
            {
                weight *= 0.5;
            }

            candidates.Add(Destination.On(platform, x));
            weights.Add(weight);
        }

        var index = random.PickWeighted(weights);
        return index < 0 ? null : candidates[index];
    }

    /// <summary>Destino sobre alguna de las plataformas de <paramref name="ownerId"/> (ventana nueva, misión...).</summary>
    public static Destination? PickOnOwner(World world, LanderBody body, long ownerId, IRandom random, IReadOnlyList<PixelRect>? occupied = null)
    {
        var margin = LandingMargin(body);
        var segments = world.SegmentsOf(ownerId).Where(p => p.Width >= margin * 2 + 4).ToList();
        if (segments.Count == 0)
        {
            return null;
        }

        var widest = segments.MaxBy(p => p.Width)!;
        return TryPickX(world, body, widest, random, occupied ?? [], out var x) ? Destination.On(widest, x) : null;
    }

    /// <summary>Base más cercana, aterrizando cerca de la vertical actual si es posible.</summary>
    public static Destination? PickBase(World world, LanderBody body, IRandom random, IReadOnlyList<PixelRect>? occupied = null)
    {
        var monitor = world.MonitorAt(body.Position) ?? world.NearestMonitor(body.Position);
        var ground = (monitor is null ? null : world.GroundOf(monitor.Index)) ?? world.NearestGround(body.Position);
        if (ground is null)
        {
            return null;
        }

        var margin = LandingMargin(body);
        var x = Math.Clamp(body.Position.X + random.Range(-1, 1) * body.Height * 3, ground.Left + margin, ground.Right - margin);
        return Destination.On(ground, FreeX(body, ground, x, occupied ?? []) ?? x);
    }

    /// <summary>
    /// La plataforma justo debajo (recuperarse de una caída, aterrizar ya), dejando al menos
    /// <paramref name="minDrop"/> píxeles para frenar.
    /// </summary>
    public static Destination? PickBelow(World world, LanderBody body, Vec2 from, double minDrop = 0)
    {
        var margin = LandingMargin(body);
        var feet = from.Y + body.HalfHeight;
        var below = world.PlatformBelow(from.X, feet - 1 + minDrop, margin);
        if (below is not null && Reachable(below, from.X))
        {
            return Destination.On(below, from.X);
        }

        // Nada justo debajo (o pegado al techo, sin sitio para llegar desde arriba): la plataforma más cercana
        // por debajo, o el suelo.
        Platform? best = null;
        var bestDistance = double.MaxValue;
        foreach (var platform in world.Platforms)
        {
            if (platform.Y < feet + minDrop || platform.Width < margin * 2 + 4)
            {
                continue;
            }

            var x = Math.Clamp(from.X, platform.Left + margin, platform.Right - margin);
            if (!Reachable(platform, x))
            {
                continue;
            }

            var distance = (new Vec2(x, platform.Y) - from).LengthSquared;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = platform;
            }
        }

        best ??= world.NearestGround(from);
        return best is null ? null : Destination.On(best, Math.Clamp(from.X, best.Left + margin, best.Right - margin));

        bool Reachable(Platform platform, double x) =>
            platform.Kind == PlatformKind.Ground || FlightEnvelope.HasHeadroom(world, body, new Vec2(x, platform.Y - body.HalfHeight - body.Height * 0.3));
    }

    /// <summary>Un salto lateral sobre la misma plataforma, alejándose de <paramref name="threat"/>.</summary>
    public static Destination? PickDodge(World world, LanderBody body, Vec2 threat, IRandom random)
    {
        if (body.Support is not { } support || world.FindSegment(support.OwnerId, body.Position.X) is not { } platform)
        {
            return null;
        }

        var margin = LandingMargin(body);
        var side = threat.X > body.Position.X ? -1 : 1;
        var hop = body.Height * random.Range(2.5, 4.5);
        var x = body.Position.X + side * hop;
        if (!platform.ContainsX(x, margin))
        {
            // Sin sitio hacia ese lado: al otro, saltando por encima del cursor.
            x = body.Position.X - side * hop;
            if (!platform.ContainsX(x, margin))
            {
                return null;
            }
        }

        return Destination.On(platform, x);
    }

    private static Dictionary<long, int> CountContentByWindow(World world)
    {
        var counts = new Dictionary<long, int>();
        foreach (var platform in world.Platforms)
        {
            if (platform.Kind == PlatformKind.Content)
            {
                counts[platform.WindowHandle] = counts.GetValueOrDefault(platform.WindowHandle) + 1;
            }
        }

        return counts;
    }

    /// <summary>
    /// Posado en <paramref name="x"/> sobre una plataforma a la altura <paramref name="platformY"/>, quedaría encima de otro
    /// asistente que está quieto ahí (pasar por delante sí vale).
    /// </summary>
    public static bool IsTaken(LanderBody body, double platformY, double x, IReadOnlyList<PixelRect> occupied)
    {
        if (occupied.Count == 0)
        {
            return false;
        }

        var half = body.HalfWidth + 6 * body.Scale;
        var spot = new PixelRect((int)(x - half), (int)(platformY - body.Height), (int)Math.Ceiling(x + half), (int)Math.Ceiling(platformY));
        foreach (var other in occupied)
        {
            if (other.Intersects(spot))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>El sitio libre de otros asistentes más cercano a <paramref name="x"/> en esa plataforma, o null si no queda.</summary>
    public static double? FreeX(LanderBody body, Platform platform, double x, IReadOnlyList<PixelRect> occupied)
    {
        var margin = LandingMargin(body);
        var left = platform.Left + margin;
        var right = platform.Right - margin;
        if (right <= left)
        {
            return null;
        }

        x = Math.Clamp(x, left, right);
        var step = Math.Max(4, body.HalfWidth / 2);
        for (var d = 0.0; d <= right - left; d += step)
        {
            if (x + d <= right && !IsTaken(body, platform.Y, x + d, occupied))
            {
                return x + d;
            }

            if (x - d >= left && !IsTaken(body, platform.Y, x - d, occupied))
            {
                return x - d;
            }
        }

        return null;
    }

    private static bool TryPickX(World world, LanderBody body, Platform platform, IRandom random, IReadOnlyList<PixelRect> occupied, out double x)
    {
        var margin = LandingMargin(body);
        var left = platform.Left + margin;
        var right = platform.Right - margin;
        x = 0;
        if (right <= left)
        {
            return false;
        }

        // Se prefiere un punto con el descenso despejado; si no lo hay, el lander pasa por delante de las
        // ventanas que tenga encima (solo aterriza en su destino).
        var approachFrom = platform.Y - body.Height * 3;
        for (var i = 0; i < SamplesPerPlatform; i++)
        {
            // En plataformas estrechas, mejor cerca del centro.
            var t = platform.Width < body.Height * 4 ? random.Range(0.35, 0.65) : random.Range(0.08, 0.92);
            x = MathUtil.Lerp(left, right, t);
            if (world.HasClearDescent(x, approachFrom, platform.Y, body.HalfWidth, platform.OwnerId) && !IsTaken(body, platform.Y, x, occupied))
            {
                break;
            }
        }

        // Otro asistente se ha quedado ahí: al lado, o a otra plataforma si no cabe.
        if (IsTaken(body, platform.Y, x, occupied))
        {
            if (FreeX(body, platform, x, occupied) is not { } free)
            {
                return false;
            }

            x = free;
        }

        return true;
    }
}
