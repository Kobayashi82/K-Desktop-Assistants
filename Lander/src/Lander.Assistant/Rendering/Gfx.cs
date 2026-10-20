using System.Windows;
using System.Windows.Media;

namespace Lander.Assistant.Rendering;

/// <summary>
/// Pinceles, lápices y geometrías congeladas (inmutables y reutilizables). Las cachés son por hilo
/// porque dibujan tanto el hilo del motor como el de la interfaz.
/// </summary>
internal static class Gfx
{
    [ThreadStatic]
    private static Dictionary<uint, SolidColorBrush>? _solidBrushes;

    [ThreadStatic]
    private static Dictionary<(uint Color, double Width), Pen>? _pens;

    private static Dictionary<uint, SolidColorBrush> SolidBrushes => _solidBrushes ??= [];

    private static Dictionary<(uint Color, double Width), Pen> Pens => _pens ??= [];

    public static Color Rgb(uint rgb, byte alpha = 255) =>
        Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static Color WithAlpha(this Color color, double alpha) =>
        Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), color.R, color.G, color.B);

    public static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    public static SolidColorBrush Brush(Color color)
    {
        var key = (uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B);
        if (!SolidBrushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(color);
            brush.Freeze();
            if (SolidBrushes.Count < 4096)
            {
                SolidBrushes[key] = brush;
            }
        }

        return brush;
    }

    public static SolidColorBrush Brush(uint rgb, byte alpha = 255) => Brush(Rgb(rgb, alpha));

    public static Pen Pen(Color color, double width, bool round = true)
    {
        var key = ((uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B), width + (round ? 0 : 1000));
        if (!Pens.TryGetValue(key, out var pen))
        {
            pen = new Pen(Brush(color), width);
            if (round)
            {
                pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
                pen.LineJoin = PenLineJoin.Round;
            }

            pen.Freeze();
            if (Pens.Count < 4096)
            {
                Pens[key] = pen;
            }
        }

        return pen;
    }

    public static Pen Pen(uint rgb, double width, byte alpha = 255) => Pen(Rgb(rgb, alpha), width);

    public static LinearGradientBrush Vertical(params (double Offset, Color Color)[] stops) =>
        Gradient(new Point(0, 0), new Point(0, 1), stops);

    public static LinearGradientBrush Gradient(Point start, Point end, params (double Offset, Color Color)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var (offset, color) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }

        brush.Freeze();
        return brush;
    }

    public static RadialGradientBrush Radial(params (double Offset, Color Color)[] stops)
    {
        var brush = new RadialGradientBrush();
        foreach (var (offset, color) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }

        brush.Freeze();
        return brush;
    }

    /// <summary>Polígono cerrado a partir de pares (x, y).</summary>
    public static StreamGeometry Polygon(params double[] coordinates)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(coordinates[0], coordinates[1]), true, true);
            for (var i = 2; i < coordinates.Length; i += 2)
            {
                context.LineTo(new Point(coordinates[i], coordinates[i + 1]), true, true);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Polígono con las esquinas redondeadas (radio constante).</summary>
    public static StreamGeometry RoundedPolygon(double radius, params double[] coordinates)
    {
        var count = coordinates.Length / 2;
        var points = new Point[count];
        for (var i = 0; i < count; i++)
        {
            points[i] = new Point(coordinates[i * 2], coordinates[i * 2 + 1]);
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var i = 0; i < count; i++)
            {
                var previous = points[(i - 1 + count) % count];
                var current = points[i];
                var next = points[(i + 1) % count];
                var toPrevious = previous - current;
                var toNext = next - current;
                var r = Math.Min(radius, Math.Min(toPrevious.Length, toNext.Length) / 2);
                toPrevious.Normalize();
                toNext.Normalize();
                var start = current + toPrevious * r;
                var end = current + toNext * r;
                if (i == 0)
                {
                    context.BeginFigure(start, true, true);
                }
                else
                {
                    context.LineTo(start, true, true);
                }

                context.QuadraticBezierTo(current, end, true, true);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Congela un objeto para poder compartirlo entre hilos.</summary>
    public static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    public static Rect Centered(double x, double y, double width, double height) =>
        new(x - width / 2, y - height / 2, width, height);
}
