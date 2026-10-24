using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Kumo.Core.Cloud;

namespace Kumo.Assistant.Rendering;

/// <summary>Las partículas de Kumo (estrellitas, ondas, «z», lluvia, niebla, notas…), en píxeles de pantalla.</summary>
internal static class ParticlePainter
{
    private static readonly Typeface Rounded = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Color Star = Color.FromRgb(255, 244, 200);
    private static readonly Color Gold = Color.FromRgb(255, 201, 60);
    private static readonly Color Soft = Color.FromRgb(255, 207, 92);
    private static readonly Color ZColor = Color.FromRgb(246, 196, 83);
    private static readonly Color RainColor = Color.FromRgb(157, 180, 210);
    private static readonly Color HeartColor = Color.FromRgb(255, 112, 120);
    private static readonly Color MistAmber = Color.FromRgb(255, 200, 96);
    private static readonly Color MistGrey = Color.FromRgb(170, 182, 200);
    private static readonly Dictionary<(Color, int), Brush> Solids = [];
    private static readonly Dictionary<int, Brush> Mists = [];
    private static readonly Dictionary<char, Geometry> Glyphs = [];

    public static void Draw(DrawingContext dc, IReadOnlyList<Particle> particles, double unit)
    {
        foreach (var p in particles)
        {
            var f = p.Fraction;
            var a = 1 - f;
            switch (p.Kind)
            {
                case ParticleKind.Mist:
                    dc.DrawEllipse(Mist(p.Grey, p.Alpha * a), null, new Point(p.X, p.Y), p.Size, p.Size);
                    break;
                case ParticleKind.Star or ParticleKind.Sparkle:
                {
                    var s = p.Size * (1 - f * 0.3);
                    dc.PushTransform(new RotateTransform(p.T * 170 + p.Side * 57, p.X, p.Y));
                    dc.DrawGeometry(Solid(p.Kind == ParticleKind.Star ? Star : Color.FromRgb(255, 255, 255), a), null, StarShape(p.X, p.Y, s, p.Kind == ParticleKind.Star ? 4 : 4, 0.32));
                    dc.Pop();
                    break;
                }

                case ParticleKind.Ring:
                    dc.DrawEllipse(null, new Pen(Solid(Gold, a), 3 * a + 0.5), new Point(p.X, p.Y), p.Size * (0.25 + f), p.Size * (0.25 + f));
                    break;
                case ParticleKind.Wave:
                {
                    var r = p.Size * (1 + f);
                    var center = new Point(p.X - p.Side * p.Size, p.Y);
                    var baseAngle = p.Side > 0 ? 0 : Math.PI;
                    var pen = new Pen(Solid(Soft, a * 0.8), Math.Max(1.2, unit * 0.05)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    dc.DrawGeometry(null, pen, Arc(center, r, baseAngle - 0.6, baseAngle + 0.6));
                    break;
                }

                case ParticleKind.Z:
                    DrawGlyph(dc, 'z', p.X, p.Y, p.Size * (1 + f * 0.6), Solid(ZColor, a));
                    break;
                case ParticleKind.Question:
                    DrawGlyph(dc, '?', p.X, p.Y, p.Size * (0.8 + 0.2 * Math.Min(1, f * 5)), Solid(Gold, Math.Min(1, a * 2)));
                    break;
                case ParticleKind.Exclamation:
                    DrawGlyph(dc, '!', p.X, p.Y, p.Size * (0.8 + 0.2 * Math.Min(1, f * 5)), Solid(Gold, Math.Min(1, a * 2)));
                    break;
                case ParticleKind.Note:
                    DrawGlyph(dc, '♪', p.X, p.Y, p.Size * 1.3, Solid(ZColor, a));
                    break;
                case ParticleKind.Rain:
                {
                    var pen = new Pen(Solid(RainColor, a * 0.9), Math.Max(1.3, unit * 0.045)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    dc.DrawLine(pen, new Point(p.X, p.Y), new Point(p.X, p.Y + p.Size));
                    break;
                }

                case ParticleKind.Heart:
                    dc.DrawGeometry(Solid(HeartColor, a), null, HeartShape(p.X, p.Y, p.Size));
                    break;
            }
        }
    }

    private static Brush Solid(Color color, double alpha)
    {
        var level = (int)Math.Clamp(Math.Round(alpha * 32), 0, 32);
        if (!Solids.TryGetValue((color, level), out var brush))
        {
            brush = new SolidColorBrush(Color.FromArgb((byte)(level * 255 / 32), color.R, color.G, color.B));
            brush.Freeze();
            Solids[(color, level)] = brush;
        }

        return brush;
    }

    private static Brush Mist(bool grey, double alpha)
    {
        var level = (int)Math.Clamp(Math.Round(alpha * 64), 0, 64) + (grey ? 1000 : 0);
        if (!Mists.TryGetValue(level, out var brush))
        {
            var c = grey ? MistGrey : MistAmber;
            var al = (byte)Math.Round(level % 1000 / 64.0 * 255);
            brush = new RadialGradientBrush([new GradientStop(Color.FromArgb(al, c.R, c.G, c.B), 0), new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1)]);
            brush.Freeze();
            Mists[level] = brush;
        }

        return brush;
    }

    /// <summary>Un carácter como geometría (una vez por carácter, a 100 px; luego se escala).</summary>
    private static void DrawGlyph(DrawingContext dc, char c, double x, double y, double size, Brush brush)
    {
        if (!Glyphs.TryGetValue(c, out var geometry))
        {
            var text = new FormattedText(c.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Rounded, 100, Brushes.Black, 1);
            geometry = text.BuildGeometry(new Point(-text.Width / 2, -text.Baseline * 0.72));
            geometry.Freeze();
            Glyphs[c] = geometry;
        }

        dc.PushTransform(new MatrixTransform(size / 100, 0, 0, size / 100, x, y));
        dc.DrawGeometry(brush, null, geometry);
        dc.Pop();
    }

    private static Geometry StarShape(double x, double y, double size, int points, double inner)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (var i = 0; i < points * 2; i++)
            {
                var r = i % 2 == 1 ? size * inner : size;
                var angle = i * Math.PI / points - Math.PI / 2;
                var p = new Point(x + Math.Cos(angle) * r, y + Math.Sin(angle) * r);
                if (i == 0)
                {
                    g.BeginFigure(p, true, true);
                }
                else
                {
                    g.LineTo(p, true, true);
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry HeartShape(double x, double y, double size)
    {
        var s = size;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(x, y + s * 0.45), true, true);
            g.BezierTo(new Point(x - s * 0.9, y - s * 0.1), new Point(x - s * 0.45, y - s * 0.75), new Point(x, y - s * 0.3), true, true);
            g.BezierTo(new Point(x + s * 0.45, y - s * 0.75), new Point(x + s * 0.9, y - s * 0.1), new Point(x, y + s * 0.45), true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry Arc(Point center, double radius, double from, double to)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(center.X + Math.Cos(from) * radius, center.Y + Math.Sin(from) * radius), false, false);
            g.ArcTo(new Point(center.X + Math.Cos(to) * radius, center.Y + Math.Sin(to) * radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, true);
        }

        geometry.Freeze();
        return geometry;
    }
}
