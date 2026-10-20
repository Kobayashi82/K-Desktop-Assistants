using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Lander.Core.Game;

namespace Lander.Assistant.Rendering;

/// <summary>
/// Bocadillos pequeños y discretos con un icono, con animación de entrada y salida. El único texto es, si hace falta, una
/// tecla a su lado (cómo aceptar una misión).
/// </summary>
internal static class BubbleRenderer
{
    private const double Width = 26;
    private const double Height = 22;
    private const double Radius = 7;

    private static readonly Typeface HintFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Geometry Frame = CreateFrame();
    private static readonly Geometry Shadow = Shifted(0, 1.3);

    /// <param name="anchor">Punta superior del lander, en el mundo.</param>
    /// <param name="topLimit">Borde superior del monitor: si no cabe encima, el bocadillo se aparta a un lado.</param>
    /// <param name="hint">Una tecla que enseñar a su derecha, como una tecla del teclado, o null.</param>
    public static void Draw(DrawingContext dc, Bubble bubble, double age, Point anchor, double scale, bool dark, ImageSource? appIcon, double time, double topLimit = double.MinValue, string? hint = null)
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
        if (hint is not null)
        {
            DrawHint(dc, hint, fill, dark);
        }

        dc.Pop();
        dc.Pop();
    }

    /// <summary>La tecla, como una tecla de verdad: recuadro con el borde del color de acento y una sombra debajo.</summary>
    private static void DrawHint(DrawingContext dc, string hint, Color fill, bool dark)
    {
        var ink = dark ? Gfx.Rgb(0xF2F2F2) : Gfx.Rgb(0x1F2328);
        var text = new FormattedText(hint, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, HintFace, 7, Gfx.Brush(ink), 1.25);
        var rect = new Rect(Width / 2 + 3, -text.Height / 2 - 1.5, text.Width + 7, text.Height + 3);
        dc.DrawRoundedRectangle(Gfx.Brush(Colors.Black.WithAlpha(dark ? 0.35 : 0.16)), null, new Rect(rect.X, rect.Y + 1.2, rect.Width, rect.Height), 3, 3);
        dc.DrawRoundedRectangle(Gfx.Brush(fill), Gfx.Pen(Icons.Accent(dark).WithAlpha(0.8), 0.8), rect, 3, 3);
        dc.DrawText(text, new Point(rect.X + 3.5, rect.Y + 1.5));
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
