using Lander.Core.Geometry;

namespace Lander.Core.Terrain;

/// <summary>
/// Instantánea inmutable del escritorio convertido en terreno: monitores, plataformas y límites.
/// </summary>
public sealed class World
{
    private readonly Dictionary<long, List<Platform>> _segmentsByOwner = [];

    public World(
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<Platform> platforms,
        IReadOnlyList<Barrier> barriers,
        double timestamp = 0)
    {
        Monitors = monitors;
        Platforms = platforms;
        Barriers = barriers;
        Timestamp = timestamp;

        foreach (var platform in platforms)
        {
            if (!_segmentsByOwner.TryGetValue(platform.OwnerId, out var list))
            {
                list = [];
                _segmentsByOwner[platform.OwnerId] = list;
            }

            list.Add(platform);
        }
    }

    public static World Empty { get; } = new([], [], []);

    public IReadOnlyList<MonitorInfo> Monitors { get; }

    public IReadOnlyList<Platform> Platforms { get; }

    public IReadOnlyList<Barrier> Barriers { get; }

    /// <summary>Momento (en segundos de simulación) en que se tomó la instantánea.</summary>
    public double Timestamp { get; }

    public bool IsEmpty => Monitors.Count == 0;

    public MonitorInfo? PrimaryMonitor => Monitors.FirstOrDefault(m => m.IsPrimary) ?? (Monitors.Count > 0 ? Monitors[0] : null);

    public IReadOnlyList<Platform> SegmentsOf(long ownerId) =>
        _segmentsByOwner.TryGetValue(ownerId, out var list) ? list : [];

    public bool HasOwner(long ownerId) => _segmentsByOwner.ContainsKey(ownerId);

    public bool TryGetOwnerOrigin(long ownerId, out Vec2 origin)
    {
        if (_segmentsByOwner.TryGetValue(ownerId, out var list) && list.Count > 0)
        {
            origin = list[0].OwnerOrigin;
            return true;
        }

        origin = default;
        return false;
    }

    /// <summary>Segmento visible de <paramref name="ownerId"/> que contiene la coordenada <paramref name="x"/>.</summary>
    public Platform? FindSegment(long ownerId, double x)
    {
        if (!_segmentsByOwner.TryGetValue(ownerId, out var list))
        {
            return null;
        }

        foreach (var segment in list)
        {
            if (segment.ContainsX(x))
            {
                return segment;
            }
        }

        return null;
    }

    public MonitorInfo? MonitorAt(Vec2 point)
    {
        foreach (var monitor in Monitors)
        {
            if (monitor.Bounds.Contains(point))
            {
                return monitor;
            }
        }

        return null;
    }

    public MonitorInfo? NearestMonitor(Vec2 point)
    {
        MonitorInfo? best = null;
        var bestDistance = double.MaxValue;
        foreach (var monitor in Monitors)
        {
            var distance = monitor.Bounds.DistanceSquaredTo(point);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = monitor;
            }
        }

        return best;
    }

    public MonitorInfo? GetMonitor(int index)
    {
        foreach (var monitor in Monitors)
        {
            if (monitor.Index == index)
            {
                return monitor;
            }
        }

        return null;
    }

    /// <summary>Suelo más ancho del monitor indicado.</summary>
    public Platform? GroundOf(int monitorIndex)
    {
        Platform? best = null;
        foreach (var platform in Platforms)
        {
            if (platform.Kind == PlatformKind.Ground && platform.MonitorIndex == monitorIndex &&
                (best is null || platform.Width > best.Width))
            {
                best = platform;
            }
        }

        return best;
    }

    /// <summary>Suelo del monitor principal: punto de reaparición.</summary>
    public Platform? PrimaryGround =>
        PrimaryMonitor is { } primary ? GroundOf(primary.Index) ?? NearestGround(primary.Bounds.Center) : null;

    public Platform? NearestGround(Vec2 point)
    {
        Platform? best = null;
        var bestDistance = double.MaxValue;
        foreach (var platform in Platforms)
        {
            if (platform.Kind != PlatformKind.Ground)
            {
                continue;
            }

            var x = Math.Clamp(point.X, platform.Left, platform.Right);
            var distance = (new Vec2(x, platform.Y) - point).LengthSquared;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = platform;
            }
        }

        return best;
    }

    /// <summary>
    /// Primera plataforma por debajo de <paramref name="y"/> que contiene <paramref name="x"/>
    /// (con un margen a cada lado), es decir, donde caería algo soltado en ese punto.
    /// </summary>
    public Platform? PlatformBelow(double x, double y, double margin = 0)
    {
        Platform? best = null;
        foreach (var platform in Platforms)
        {
            if (platform.Y >= y && platform.ContainsX(x, margin) && (best is null || platform.Y < best.Y))
            {
                best = platform;
            }
        }

        return best;
    }

    /// <summary>
    /// Indica si una caída vertical en <paramref name="x"/> desde <paramref name="fromY"/> hasta
    /// <paramref name="toY"/> no toparía antes con otra plataforma.
    /// </summary>
    public bool HasClearDescent(double x, double fromY, double toY, double halfWidth, long ignoreOwner)
    {
        foreach (var platform in Platforms)
        {
            if (platform.OwnerId == ignoreOwner)
            {
                continue;
            }

            if (platform.Y > fromY && platform.Y < toY - 0.5 &&
                x + halfWidth > platform.Left && x - halfWidth < platform.Right)
            {
                return false;
            }
        }

        return true;
    }
}
