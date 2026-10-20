using Lander.Core.Geometry;

namespace Lander.Core.Terrain;

public sealed record TerrainOptions
{
    /// <summary>Altura del lander en DIP (se multiplica por el escalado de cada monitor).</summary>
    public double LanderHeightDip { get; init; } = 48;

    /// <summary>Radio de las esquinas redondeadas de Windows 11: se recorta de cada extremo.</summary>
    public double CornerRadiusDip { get; init; } = 8;

    /// <summary>Anchura mínima para que un segmento cuente como plataforma.</summary>
    public double MinPlatformWidthDip { get; init; } = 56;

    /// <summary>Hueco libre adicional sobre el lander cuando reposa bajo el borde superior.</summary>
    public double TopMarginDip { get; init; } = 16;

    /// <summary>Un borde de contenido debe medir al menos tres veces el ancho del lander (¾ de su altura).</summary>
    public double MinContentWidthDip => LanderHeightDip * 0.75 * 3;
}

/// <summary>
/// Convierte monitores y ventanas (en orden Z, de la más alta a la más baja) en un <see cref="World"/>; si se
/// le pasan, añade también los bordes de contenido vistos dentro de las ventanas.
/// </summary>
public static class TerrainBuilder
{
    public static World Build(
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<WindowInfo> windowsTopToBottom,
        TerrainOptions? options = null,
        double timestamp = 0,
        IReadOnlyList<ContentSurface>? contents = null)
    {
        options ??= new TerrainOptions();
        if (monitors.Count == 0)
        {
            return World.Empty;
        }

        var platforms = new List<Platform>();
        AddGrounds(monitors, platforms, options);
        AddWindowPlatforms(monitors, windowsTopToBottom, platforms, options);
        if (contents is { Count: > 0 })
        {
            AddContentPlatforms(monitors, windowsTopToBottom, contents, platforms, options);
        }

        var barriers = BuildBarriers(monitors);
        return new World(monitors, platforms, barriers, timestamp);
    }

    private static void AddGrounds(IReadOnlyList<MonitorInfo> monitors, List<Platform> platforms, TerrainOptions options)
    {
        foreach (var monitor in monitors)
        {
            var work = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
            var y = work.Bottom;
            var pieces = new List<Interval> { new(work.Left, work.Right) };

            // Sin barra de tareas abajo, el borde inferior se abre donde hay otro monitor debajo.
            if (work.Bottom >= monitor.Bounds.Bottom)
            {
                foreach (var other in monitors)
                {
                    if (other != monitor && other.Bounds.Top == monitor.Bounds.Bottom)
                    {
                        pieces = Intervals.Subtract(pieces, new Interval(other.Bounds.Left, other.Bounds.Right));
                    }
                }
            }

            var minWidth = options.MinPlatformWidthDip * monitor.DpiScale;
            var segment = 0;
            foreach (var piece in pieces)
            {
                if (piece.Length < minWidth)
                {
                    continue;
                }

                platforms.Add(new Platform(
                    Platform.GroundOwnerId(monitor.Index, segment++),
                    PlatformKind.Ground,
                    piece.Start,
                    piece.End,
                    y,
                    monitor.Index,
                    new Vec2(work.Left, y)));
            }
        }
    }

    private static void AddWindowPlatforms(
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<WindowInfo> windows,
        List<Platform> platforms,
        TerrainOptions options)
    {
        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            var bounds = window.Bounds;
            if (bounds.IsEmpty || FindMonitor(monitors, bounds) is not { } monitor)
            {
                continue;
            }

            var scale = monitor.DpiScale;
            var work = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;

            // Una ventana maximizada no tiene borde superior sobre el que posarse: su barra de título es
            // el techo de la pantalla. Lo mismo vale para las que están pegadas arriba (Snap).
            var y = (double)bounds.Top;
            if (window.IsMaximized || y >= work.Bottom - 1 || y >= bounds.Bottom)
            {
                continue;
            }

            var inset = options.CornerRadiusDip * scale;
            var span = new Interval(bounds.Left + inset, bounds.Right - inset);
            if (span.IsEmpty)
            {
                continue;
            }

            // Solo la parte que cae dentro de algún monitor y que deja sitio por encima para el lander
            // (en otro monitor, si lo hay justo encima).
            var clearance = (options.LanderHeightDip + options.TopMarginDip) * scale;
            var pieces = Intervals.Intersect(Intervals.Intersect([span], ScreenSpansAt(monitors, y)), ScreenSpansAt(monitors, y - clearance));

            // Las ventanas que están por encima en orden Z tapan la barra de título.
            for (var j = 0; j < i && pieces.Count > 0; j++)
            {
                var above = windows[j].Bounds;
                if (above.Top <= y && above.Bottom > y)
                {
                    pieces = Intervals.Subtract(pieces, new Interval(above.Left, above.Right));
                }
            }

            var minWidth = options.MinPlatformWidthDip * scale;
            var origin = new Vec2(bounds.Left, bounds.Top);
            foreach (var piece in pieces)
            {
                if (piece.Length >= minWidth)
                {
                    platforms.Add(new Platform(
                        window.Handle,
                        PlatformKind.Window,
                        piece.Start,
                        piece.End,
                        y,
                        monitor.Index,
                        origin,
                        window.AppId));
                }
            }
        }
    }

    /// <summary>
    /// Bordes de contenido: solo la parte que sigue a la vista, con la altura del lander libre por encima
    /// dentro de la propia ventana (sin otras ventanas delante, ni fuera del monitor, ni bajo la barra de
    /// tareas). Al contrario que la barra de título, el contenido de una ventana maximizada sí vale.
    /// </summary>
    private static void AddContentPlatforms(
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<WindowInfo> windows,
        IReadOnlyList<ContentSurface> contents,
        List<Platform> platforms,
        TerrainOptions options)
    {
        var byWindow = contents.ToLookup(c => c.WindowHandle);
        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            var bounds = window.Bounds;
            if (!byWindow.Contains(window.Handle) || bounds.IsEmpty || FindMonitor(monitors, bounds) is not { } monitor)
            {
                continue;
            }

            var scale = monitor.DpiScale;
            var work = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
            var clearance = options.LanderHeightDip * scale;
            var minWidth = options.MinContentWidthDip * scale;
            foreach (var surface in byWindow[window.Handle])
            {
                var y = (double)bounds.Top + surface.Y;
                var top = y - clearance;
                if (top < bounds.Top || y >= bounds.Bottom || y >= work.Bottom || top < work.Top)
                {
                    continue;
                }

                var span = new Interval(Math.Max(bounds.Left, bounds.Left + surface.Left), Math.Min(bounds.Right, bounds.Left + surface.Right));
                if (span.Length < minWidth)
                {
                    continue;
                }

                var pieces = Intervals.Intersect(Intervals.Intersect([span], ScreenSpansAt(monitors, y)), ScreenSpansAt(monitors, top));
                for (var j = 0; j < i && pieces.Count > 0; j++)
                {
                    var above = windows[j].Bounds;
                    if (above.Top <= y && above.Bottom > top)
                    {
                        pieces = Intervals.Subtract(pieces, new Interval(above.Left, above.Right));
                    }
                }

                var id = Platform.ContentOwnerId(window.Handle, surface.EdgeId);
                var origin = new Vec2(bounds.Left, y);
                foreach (var piece in pieces)
                {
                    if (piece.Length >= minWidth)
                    {
                        platforms.Add(new Platform(id, PlatformKind.Content, piece.Start, piece.End, y, monitor.Index, origin, window.AppId));
                    }
                }
            }
        }
    }

    /// <summary>Monitor con el que la ventana comparte más superficie.</summary>
    private static MonitorInfo? FindMonitor(IReadOnlyList<MonitorInfo> monitors, PixelRect bounds)
    {
        MonitorInfo? best = null;
        long bestArea = 0;
        foreach (var monitor in monitors)
        {
            var area = monitor.Bounds.Intersect(bounds).Area;
            if (area > bestArea)
            {
                bestArea = area;
                best = monitor;
            }
        }

        return best;
    }

    private static List<Interval> ScreenSpansAt(IReadOnlyList<MonitorInfo> monitors, double y)
    {
        var spans = new List<Interval>();
        foreach (var monitor in monitors)
        {
            if (y >= monitor.Bounds.Top && y < monitor.Bounds.Bottom)
            {
                spans.Add(new Interval(monitor.Bounds.Left, monitor.Bounds.Right));
            }
        }

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        return Intervals.Merge(spans);
    }

    private static List<Barrier> BuildBarriers(IReadOnlyList<MonitorInfo> monitors)
    {
        var barriers = new List<Barrier>();
        foreach (var monitor in monitors)
        {
            var b = monitor.Bounds;

            AddPieces(barriers, BarrierKind.Ceiling, b.Top, new Interval(b.Left, b.Right),
                monitors.Where(o => o != monitor && o.Bounds.Bottom == b.Top)
                    .Select(o => new Interval(o.Bounds.Left, o.Bounds.Right)));

            AddPieces(barriers, BarrierKind.Floor, b.Bottom, new Interval(b.Left, b.Right),
                monitors.Where(o => o != monitor && o.Bounds.Top == b.Bottom)
                    .Select(o => new Interval(o.Bounds.Left, o.Bounds.Right)));

            AddPieces(barriers, BarrierKind.LeftWall, b.Left, new Interval(b.Top, b.Bottom),
                monitors.Where(o => o != monitor && o.Bounds.Right == b.Left)
                    .Select(o => new Interval(o.Bounds.Top, o.Bounds.Bottom)));

            AddPieces(barriers, BarrierKind.RightWall, b.Right, new Interval(b.Top, b.Bottom),
                monitors.Where(o => o != monitor && o.Bounds.Left == b.Right)
                    .Select(o => new Interval(o.Bounds.Top, o.Bounds.Bottom)));
        }

        return barriers;
    }

    private static void AddPieces(List<Barrier> barriers, BarrierKind kind, double position, Interval span, IEnumerable<Interval> openings)
    {
        foreach (var piece in Intervals.Subtract(span, openings))
        {
            barriers.Add(new Barrier(kind, position, piece.Start, piece.End));
        }
    }
}
