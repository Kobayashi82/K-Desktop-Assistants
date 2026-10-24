using System.Windows;
using System.Windows.Media;
using Kumo.Core.Geometry;

namespace Kumo.Assistant.Rendering;

/// <summary>
/// Lo que va con la nube en algunos momentos, en píxeles de pantalla: el arcoíris que le sale por detrás, la almohada en la
/// que se echa la siesta y la imagen de los archivos que le traes (la que da Windows al arrastrar).
/// </summary>
internal static class ExtrasPainter
{
    /// <summary>De fuera adentro.</summary>
    private static readonly Color[] RainbowColors =
    [
        Color.FromRgb(236, 84, 84), Color.FromRgb(246, 154, 64), Color.FromRgb(248, 214, 72),
        Color.FromRgb(108, 196, 104), Color.FromRgb(84, 156, 232), Color.FromRgb(150, 110, 214),
    ];

    private static readonly Brush[] RainbowBrushes = [.. RainbowColors.Select(c => Frozen(new SolidColorBrush(c)))];

    private static readonly Brush PillowBrush = Frozen(new LinearGradientBrush(
        [new GradientStop(Color.FromRgb(250, 248, 255), 0), new GradientStop(Color.FromRgb(214, 206, 238), 1)], 90));

    private static readonly Pen PillowSeam = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(70, 120, 104, 170))), 1.2));

    private static readonly Brush PictureBack = Frozen(new SolidColorBrush(Color.FromArgb(235, 255, 252, 240)));

    private static readonly Pen PictureRim = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(150, 224, 150, 40))), 1.5));

    /// <summary>El radio de fuera del arcoíris (unidades): sale de detrás de la nube y cae a los dos lados.</summary>
    public const double RainbowRadius = 2.3;

    private const double BandWidth = 0.11;

    /// <summary>Lo que ocupa el arcoíris (píxeles), alrededor de <paramref name="center"/>.</summary>
    public static Rect RainbowBounds(Vec2 center, double unit) =>
        new(center.X - RainbowRadius * unit - 2, center.Y - RainbowRadius * unit - 2, RainbowRadius * 2 * unit + 4, RainbowRadius * unit + 0.6 * unit);

    /// <summary>
    /// El arcoíris: un semicírculo por color que crece de izquierda a derecha (<paramref name="grown"/> 0…1) y se apaga con
    /// <paramref name="opacity"/>. Va detrás de la nube, que tapa sus pies.
    /// </summary>
    public static void DrawRainbow(DrawingContext dc, Vec2 center, double unit, double grown, double opacity)
    {
        if (grown <= 0.01 || opacity <= 0.01)
        {
            return;
        }

        dc.PushOpacity(opacity * 0.8);
        var sweep = Math.PI * Math.Clamp(grown, 0, 1);
        for (var i = 0; i < RainbowColors.Length; i++)
        {
            var r = (RainbowRadius - BandWidth * (i + 0.5)) * unit;
            var pen = new Pen(RainbowBrushes[i], BandWidth * unit * 1.08) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(center.X - r, center.Y + 0.3 * unit), false, false);
                g.LineTo(new Point(center.X - r, center.Y), true, true);
                var end = Math.PI + sweep;
                g.ArcTo(new Point(center.X + Math.Cos(end) * r, center.Y + Math.Sin(end) * r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                if (grown >= 0.999)
                {
                    g.LineTo(new Point(center.X + r, center.Y + 0.3 * unit), true, true);
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        dc.Pop();
    }

    /// <summary>Lo que ocupa la almohada (píxeles).</summary>
    public static Rect PillowBounds(Vec2 at, double unit) => new(at.X - 1.3 * unit, at.Y - 0.35 * unit, 2.6 * unit, 0.75 * unit);

    /// <summary>La almohada de la siesta, centrada en <paramref name="at"/> (debajo de la nube).</summary>
    public static void DrawPillow(DrawingContext dc, Vec2 at, double unit, double opacity)
    {
        if (opacity <= 0.01)
        {
            return;
        }

        dc.PushOpacity(opacity);
        var w = 1.2 * unit;
        var h = 0.3 * unit;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            // Un cojín con las esquinas en pico, algo hinchado por el medio.
            g.BeginFigure(new Point(at.X - w, at.Y - h * 0.8), true, true);
            g.QuadraticBezierTo(new Point(at.X, at.Y - h * 1.5), new Point(at.X + w, at.Y - h * 0.8), true, true);
            g.QuadraticBezierTo(new Point(at.X + w * 0.86, at.Y), new Point(at.X + w, at.Y + h * 0.8), true, true);
            g.QuadraticBezierTo(new Point(at.X, at.Y + h * 1.5), new Point(at.X - w, at.Y + h * 0.8), true, true);
            g.QuadraticBezierTo(new Point(at.X - w * 0.86, at.Y), new Point(at.X - w, at.Y - h * 0.8), true, true);
        }

        geometry.Freeze();
        dc.DrawGeometry(PillowBrush, PillowSeam, geometry);
        dc.Pop();
    }

    /// <summary>La imagen de los archivos en <paramref name="rect"/>, en un marco claro como de papel.</summary>
    public static void DrawPicture(DrawingContext dc, ImageSource image, Rect rect, double opacity)
    {
        if (opacity <= 0.01 || rect.Width < 2 || rect.Height < 2)
        {
            return;
        }

        dc.PushOpacity(opacity);
        var frame = rect;
        frame.Inflate(4, 4);
        dc.DrawRoundedRectangle(PictureBack, PictureRim, frame, 6, 6);
        dc.DrawImage(image, rect);
        dc.Pop();
    }

    /// <summary>
    /// El sitio de la imagen sobre la nube (<paramref name="top"/> = su parte de arriba) cabiendo en
    /// <paramref name="most"/> píxeles de lado, sin agrandarla.
    /// </summary>
    public static Rect PictureRect(ImageSource image, Vec2 top, double most)
    {
        var scale = Math.Min(1, Math.Min(most / Math.Max(1, image.Width), most / Math.Max(1, image.Height)));
        var w = image.Width * scale;
        var h = image.Height * scale;
        return new Rect(top.X - w / 2, top.Y - h - 6, w, h);
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
