using System.Windows;
using System.Windows.Media;
using Lander.Core.Game;

namespace Lander.Assistant.Rendering;

/// <summary>Bocadillos pequeños y discretos con un icono (nada de texto), con animación de entrada y salida.</summary>
internal static class BubbleRenderer
{
    private const double Width = 26;
    private const double Height = 22;
    private const double Radius = 7;

    private static readonly Geometry Frame = CreateFrame();
    private static readonly Geometry Shadow = Shifted(0, 1.3);

    /// <param name="anchor">Punta superior del lander, en el mundo.</param>
    /// <param name="topLimit">Borde superior del monitor: si no cabe encima, el bocadillo se aparta a un lado.</param>
    public static void Draw(DrawingContext dc, Bubble bubble, double age, Point anchor, double scale, bool dark, ImageSource? appIcon, double time, double topLimit = double.MinValue)
    {
        var appear = EaseOutBack(Math.Clamp(age / 0.22, 0, 1));
        var vanish = Math.Clamp((bubble.Duration - age) / 0.3, 0, 1);
        var alpha = Math.Min(1, age / 0.12) * vanish;
        if (alpha <= 0.01)
        {
            return;
        }

        var size = scale * (0.55 + 0.45 * appear);
        var bob = Math.Sin(time * 2.2) * 0.6;
        var center = new Point(anchor.X, anchor.Y - (Height / 2 + 7 + bob) * scale);
        if (center.Y - (Height / 2 + 2) * scale < topLimit)
        {
            center = new Point(anchor.X + 30 * scale, topLimit + (Height / 2 + 3) * scale);
        }


        dc.PushTransform(new MatrixTransform(size, 0, 0, size, center.X, center.Y));
        dc.PushOpacity(alpha);

        dc.DrawGeometry(Gfx.Brush(Colors.Black.WithAlpha(dark ? 0.35 : 0.16)), null, Shadow);
        var fill = dark ? Gfx.Rgb(0x2B2B2B, 246) : Gfx.Rgb(0xFFFFFF, 248);
        var stroke = dark ? Gfx.Pen(Colors.White.WithAlpha(0.14), 0.7) : Gfx.Pen(Colors.Black.WithAlpha(0.1), 0.7);
        dc.DrawGeometry(Gfx.Brush(fill), stroke, Frame);

        Icons.Draw(dc, bubble.Icon, bubble.Direction, appIcon, time, dark);

        dc.Pop();
        dc.Pop();
    }

    private static Geometry Shifted(double x, double y)
    {
        var copy = Frame.Clone();
        copy.Transform = new TranslateTransform(x, y);
        copy.Freeze();
        return copy;
    }

    private static PathGeometry CreateFrame()
    {
        var body = new RectangleGeometry(new Rect(-Width / 2, -Height / 2, Width, Height), Radius, Radius);
        var tail = Gfx.Polygon(-3.2, Height / 2 - 1, 3.2, Height / 2 - 1, 0, Height / 2 + 4.2);
        var combined = new CombinedGeometry(GeometryCombineMode.Union, body, tail).GetFlattenedPathGeometry();
        combined.Freeze();
        return combined;
    }

    private static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }
}
