using System.Windows;
using System.Windows.Media;
using Kumo.Core.Cloud;
using Kumo.Core.Geometry;

namespace Kumo.Assistant.Rendering;

/// <summary>
/// Dibuja a Kumo en vectorial con la API de dibujo de WPF (el host lo pasa a la GPU con Direct2D, como a Kobii y al Lander).
/// Todo va en unidades de Kumo con una transformación por encima (posición y tamaño), así los pinceles se crean una vez y solo
/// cambian las geometrías de los contornos.
/// </summary>
/// <remarks>
/// Orden: sombra y halo; los anillos del borde blando (el mismo degradado cada vez más opaco) y el cuerpo; recortado al cuerpo,
/// cada bulto con su degradado esférico (luz arriba a la izquierda, oscuro abajo: donde un bulto se apoya en otro queda el
/// pliegue) y el borde iluminado por arriba y oscuro por abajo; el brillo de gota; la cara.
/// </remarks>
internal sealed class KumoPainter
{
    /// <summary>De arriba abajo: luz, medio, sombra y pliegue (ámbar).</summary>
    private static readonly Color[] Amber = [Color.FromRgb(255, 238, 178), Color.FromRgb(252, 194, 62), Color.FromRgb(224, 128, 18), Color.FromRgb(178, 88, 14)];

    /// <summary>Lo mismo en gris (algo salió mal).</summary>
    private static readonly Color[] Grey = [Color.FromRgb(224, 230, 238), Color.FromRgb(172, 184, 200), Color.FromRgb(122, 134, 154), Color.FromRgb(86, 96, 114)];

    /// <summary>Niveles de gris en los que se guardan los pinceles (no hace falta más: el cambio dura un momento).</summary>
    private const int TintLevels = 8;

    /// <summary>Niveles verticales de los pinceles de los bultos.</summary>
    private const int BallLevels = 10;

    /// <summary>Orden de los bultos: los de abajo primero, los de arriba por delante.</summary>
    private static readonly int[] BallOrder = [8, 9, 7, 5, 6, 1, 2, 0, 3, 4];

    /// <summary>Arriba y abajo del degradado del cuerpo (unidades).</summary>
    private const double GradientTop = -0.9;

    private const double GradientBottom = 0.8;

    private readonly Dictionary<int, Brush> _bodyBrushes = [];
    private readonly Dictionary<int, Brush> _ringBrushes = [];
    private readonly Dictionary<int, Brush> _ballBrushes = [];
    private readonly Dictionary<int, Pen> _rimPens = [];
    private readonly Dictionary<int, Brush> _glowBrushes = [];
    private readonly Dictionary<int, Brush> _glossBrushes = [];
    private readonly Dictionary<int, Brush> _cheekBrushes = [];
    private static readonly Brush ShadowBrush = Frozen(Radial([(0, Color.FromArgb(28, 8, 6, 20)), (1, Color.FromArgb(0, 8, 6, 20))]));
    private static readonly Brush White = Frozen(new SolidColorBrush(Colors.White));
    private static readonly Brush WhiteSoft = Frozen(new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)));
    private static readonly Brush Tongue = Frozen(new SolidColorBrush(Color.FromRgb(232, 112, 90)));

    /// <summary>Kumo entero según <paramref name="frame"/>, en píxeles de pantalla.</summary>
    public void Draw(DrawingContext dc, KumoFrame frame)
    {
        if (frame.Opacity <= 0.003 || frame.Core.Count + frame.Rings[0].Count == 0)
        {
            return;
        }

        var pushed = 0;
        dc.PushTransform(new MatrixTransform(frame.Unit, 0, 0, frame.Unit, frame.Origin.X, frame.Origin.Y));
        pushed++;
        if (frame.Opacity < 0.998)
        {
            dc.PushOpacity(frame.Opacity);
            pushed++;
        }

        var tint = TintIndex(frame.Tint);
        var c = frame.Center;
        var ext = frame.Extent;

        // Sombra blanda debajo y halo cálido alrededor (más fuerte condensado).
        dc.DrawEllipse(ShadowBrush, null, new Point(c.X + 0.1, c.Y + 0.38), ext * 1.15, ext * 0.8);
        var glow = (int)Math.Round(MathUtil.Lerp(0.16, 0.55, frame.Smooth) * 20);
        dc.DrawEllipse(GlowBrush(glow, tint, ext), null, new Point(c.X, c.Y), ext * 1.85, ext * 1.85);

        // Borde blando: anillos del mismo degradado, de fuera adentro, cada vez más opacos; luego el cuerpo.
        for (var i = 0; i < frame.Rings.Length; i++)
        {
            var ring = Outline(frame.Rings[i]);
            if (ring is not null)
            {
                dc.DrawGeometry(RingBrush(i, tint), null, ring);
            }
        }

        var body = Outline(frame.Core);
        if (body is not null)
        {
            dc.DrawGeometry(BodyBrush(tint), null, body);
            dc.PushClip(body);
            var bumpy = 1 - frame.Smooth;
            if (bumpy > 0.02)
            {
                if (bumpy < 0.98)
                {
                    dc.PushOpacity(bumpy);
                }

                foreach (var index in BallOrder)
                {
                    var b = frame.Balls[index];
                    if (b.R < 0.05)
                    {
                        continue;
                    }

                    var level = (int)Math.Clamp(Math.Round((b.Y - GradientTop) / (GradientBottom - GradientTop) * (BallLevels - 1)), 0, BallLevels - 1);
                    var r = b.R * 0.99;
                    dc.DrawEllipse(BallBrush(level, tint), null, new Point(b.X, b.Y), r, r);
                }

                if (bumpy < 0.98)
                {
                    dc.Pop();
                }
            }

            dc.DrawGeometry(null, RimPen(tint), body);
            var gloss = (int)Math.Round(frame.Smooth * 10);
            if (gloss > 0)
            {
                DrawGloss(dc, frame, gloss);
            }

            dc.Pop();
        }

        if (frame.ShowFace)
        {
            DrawFace(dc, frame);
        }

        for (var i = 0; i < pushed; i++)
        {
            dc.Pop();
        }
    }

    /// <summary>El brillo de gota: un reflejo grande arriba a la izquierda y un punto de luz.</summary>
    private void DrawGloss(DrawingContext dc, KumoFrame frame, int level)
    {
        var c = frame.Center;
        var ext = frame.Extent;
        dc.PushTransform(new RotateTransform(-26, c.X - ext * 0.36, c.Y - ext * 0.4));
        dc.DrawEllipse(GlossBrush(level), null, new Point(c.X - ext * 0.36, c.Y - ext * 0.4), ext * 0.36, ext * 0.2);
        dc.Pop();
        if (level >= 4)
        {
            dc.PushOpacity(level / 10.0);
            dc.DrawEllipse(WhiteSoft, null, new Point(c.X - ext * 0.5, c.Y - ext * 0.5), ext * 0.06, ext * 0.045);
            dc.Pop();
        }
    }

    // ---------- la cara ----------

    private void DrawFace(DrawingContext dc, KumoFrame frame)
    {
        var s = frame.FaceScale;
        var fx = frame.Face.X + frame.Look.X;
        var fy = frame.Face.Y + frame.Look.Y;
        var grey = frame.Tint > 0.5;
        var ink = Frozen(new SolidColorBrush(grey ? Color.FromRgb(40, 48, 64) : Color.FromRgb(46, 28, 5)));
        var line = new Pen(ink, 0.034 * s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        line.Freeze();
        dc.PushTransform(new RotateTransform(frame.Tilt * 0.7 * 180 / Math.PI, fx, fy));

        var eyes = frame.Eyes;
        if (eyes != CloudEyes.Closed)
        {
            foreach (var side in (ReadOnlySpan<int>)[-1, 1])
            {
                dc.DrawEllipse(CheekBrush(grey), null, new Point(fx + side * 0.31 * s, fy + 0.1 * s), 0.11 * s, 0.07 * s);
            }
        }

        var open = 1 - 0.92 * frame.Lid;
        foreach (var side in (ReadOnlySpan<int>)[-1, 1])
        {
            var ex = fx + side * 0.17 * s;
            var ey = fy - 0.02 * s;
            var eye = eyes == CloudEyes.Wink && side > 0 ? CloudEyes.Happy : eyes == CloudEyes.Wink ? CloudEyes.Normal : eyes;
            switch (eye)
            {
                case CloudEyes.Closed:
                    dc.DrawGeometry(null, line, Arc(new Point(ex, ey - 0.02 * s), 0.06 * s, 0.15 * Math.PI, 0.85 * Math.PI));
                    continue;
                case CloudEyes.Happy:
                    dc.DrawGeometry(null, line, Arc(new Point(ex, ey + 0.04 * s), 0.065 * s, 1.15 * Math.PI, 1.85 * Math.PI));
                    continue;
            }

            var big = eye switch { CloudEyes.Wide => 1.15, CloudEyes.Sad => 0.88, _ => 1 };
            var squash = eye switch { CloudEyes.Focused => 0.62, CloudEyes.Half => 0.5, _ => 1 };
            var oy = eye switch { CloudEyes.Sad => 0.02 * s, CloudEyes.Half => 0.03 * s, _ => 0 };
            var ry = Math.Max(0.008 * s, 0.1 * s * big * open * squash);
            var cy = ey + oy + (1 - open) * 0.03 * s;
            dc.DrawEllipse(ink, null, new Point(ex, cy), 0.062 * s * big, ry);
            if (open * squash > 0.45)
            {
                dc.DrawEllipse(White, null, new Point(ex - 0.018 * s + frame.Look.X * 0.15, cy - ry * 0.42), 0.024 * s, 0.024 * s);
                dc.DrawEllipse(WhiteSoft, null, new Point(ex + 0.02 * s, cy + ry * 0.35), 0.011 * s, 0.011 * s);
            }

            if (eye == CloudEyes.Sad)
            {
                dc.DrawLine(line, new Point(ex - side * 0.07 * s, ey - 0.1 * s), new Point(ex + side * 0.05 * s, ey - 0.15 * s));
            }
            else if (eye == CloudEyes.Focused)
            {
                dc.DrawLine(line, new Point(ex - side * 0.07 * s, ey - 0.15 * s), new Point(ex + side * 0.05 * s, ey - 0.11 * s));
            }
            else if (eye == CloudEyes.Half)
            {
                dc.DrawLine(line, new Point(ex - 0.07 * s, cy - ry * 0.9), new Point(ex + 0.07 * s, cy - ry * 0.9));
            }
        }

        var mx = fx - frame.Look.X * 0.4;
        var my = fy;
        switch (frame.Mouth)
        {
            case CloudMouth.Smile:
                dc.DrawGeometry(null, line, Arc(new Point(mx, my + 0.065 * s), 0.065 * s, 0.22 * Math.PI, 0.78 * Math.PI));
                break;
            case CloudMouth.Big:
            {
                var mouth = HalfDisc(new Point(mx, my + 0.09 * s), 0.075 * s);
                dc.DrawGeometry(ink, null, mouth);
                dc.PushClip(mouth);
                dc.DrawEllipse(Tongue, null, new Point(mx, my + 0.17 * s), 0.045 * s, 0.035 * s);
                dc.Pop();
                break;
            }

            case CloudMouth.Talk:
                dc.DrawEllipse(ink, null, new Point(mx, my + 0.13 * s), 0.042 * s, (0.016 + 0.036 * frame.Mouthful) * s);
                break;
            case CloudMouth.O:
                dc.DrawEllipse(ink, null, new Point(mx, my + 0.14 * s), 0.038 * s, 0.048 * s);
                break;
            case CloudMouth.Frown:
                dc.DrawGeometry(null, line, Arc(new Point(mx, my + 0.19 * s), 0.06 * s, 1.22 * Math.PI, 1.78 * Math.PI));
                break;
            case CloudMouth.Flat:
                dc.DrawLine(line, new Point(mx - 0.035 * s, my + 0.13 * s), new Point(mx + 0.035 * s, my + 0.12 * s));
                break;
            case CloudMouth.Wavy:
                dc.DrawGeometry(null, line, Wave(new Point(mx, my + 0.13 * s), 0.05 * s, 0.012 * s));
                break;
        }

        dc.Pop();
    }

    // ---------- geometrías ----------

    /// <summary>Los bucles como una sola geometría con curvas suaves (Catmull-Rom); null si no hay ninguno.</summary>
    public static Geometry? Outline(List<Vec2[]> loops)
    {
        if (loops.Count == 0)
        {
            return null;
        }

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var g = geometry.Open())
        {
            foreach (var points in loops)
            {
                var n = points.Length;
                g.BeginFigure(P(points[0]), true, true);
                for (var i = 0; i < n; i++)
                {
                    var p0 = points[(i - 1 + n) % n];
                    var p1 = points[i];
                    var p2 = points[(i + 1) % n];
                    var p3 = points[(i + 2) % n];
                    var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
                    var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
                    g.BezierTo(c1, c2, P(p2), true, true);
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point P(Vec2 v) => new(v.X, v.Y);

    /// <summary>Un arco de circunferencia entre dos ángulos (radianes, en el sentido de las agujas con la y hacia abajo).</summary>
    private static Geometry Arc(Point center, double radius, double from, double to)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(center.X + Math.Cos(from) * radius, center.Y + Math.Sin(from) * radius), false, false);
            g.ArcTo(new Point(center.X + Math.Cos(to) * radius, center.Y + Math.Sin(to) * radius), new Size(radius, radius), 0, to - from > Math.PI, SweepDirection.Clockwise, true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Medio círculo con la parte plana arriba (la boca abierta de alegría).</summary>
    private static Geometry HalfDisc(Point center, double radius)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(center.X - radius, center.Y), true, true);
            g.LineTo(new Point(center.X + radius, center.Y), true, true);
            g.ArcTo(new Point(center.X - radius, center.Y), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Una boca ondulada (sin saber qué decir).</summary>
    private static Geometry Wave(Point center, double half, double height)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(center.X - half, center.Y), false, false);
            g.QuadraticBezierTo(new Point(center.X - half / 2, center.Y - height * 2), new Point(center.X, center.Y), true, true);
            g.QuadraticBezierTo(new Point(center.X + half / 2, center.Y + height * 2), new Point(center.X + half, center.Y), true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    // ---------- pinceles (se crean una vez por nivel) ----------

    private static int TintIndex(double tint) => (int)Math.Clamp(Math.Round(tint * (TintLevels - 1)), 0, TintLevels - 1);

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static Color Shade(int index, int tint) => Mix(Amber[index], Grey[index], tint / (double)(TintLevels - 1));

    private static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    /// <summary>El color del cuerpo a una altura (0 = arriba, 1 = abajo).</summary>
    private static Color BodyAt(double g, int tint) =>
        g < 0.5 ? Mix(Shade(0, tint), Shade(1, tint), g * 2) : Mix(Shade(1, tint), Shade(2, tint), g * 2 - 1);

    private Brush BodyBrush(int tint) => Cached(_bodyBrushes, tint, () => Vertical(tint, 1));

    private Brush RingBrush(int ring, int tint) => Cached(_ringBrushes, ring * 100 + tint, () => Vertical(tint, KumoFrame.RingLevels[ring].Opacity));

    private static LinearGradientBrush Vertical(int tint, double opacity) => new(
        [
            new GradientStop(Shade(0, tint), 0),
            new GradientStop(Shade(1, tint), 0.5),
            new GradientStop(Shade(2, tint), 1),
        ], new Point(0, GradientTop), new Point(0, GradientBottom))
    {
        MappingMode = BrushMappingMode.Absolute,
        Opacity = opacity,
    };

    /// <summary>
    /// El degradado de un bulto a esa altura: más claro arriba a la izquierda y oscuro en el borde de abajo (el pliegue donde se
    /// apoya en el de debajo).
    /// </summary>
    private Brush BallBrush(int level, int tint) => Cached(_ballBrushes, level * 100 + tint, () =>
    {
        var g = level / (double)(BallLevels - 1);
        var baseColor = BodyAt(g, tint);
        var light = Mix(baseColor, Color.FromRgb(255, 248, 222), 0.24);
        return new RadialGradientBrush(
            [
                new GradientStop(light, 0),
                new GradientStop(baseColor, 0.5),
                new GradientStop(WithAlpha(Mix(baseColor, Shade(2, tint), 0.35), 0.95), 0.76),
                new GradientStop(WithAlpha(Mix(baseColor, Shade(3, tint), 0.45), 0.55), 0.9),
                new GradientStop(WithAlpha(Shade(3, tint), 0), 1),
            ])
        {
            // Centrado en el bulto: el último color cae justo en su borde (transparente), así no se ve el círculo; la luz
            // sale de arriba a la izquierda y el pliegue queda abajo a la derecha.
            GradientOrigin = new Point(0.36, 0.28),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
    });

    /// <summary>El borde: iluminado por arriba (el sol le da) y oscuro por debajo; solo se ve la mitad de dentro.</summary>
    private Pen RimPen(int tint) => CachedPen(_rimPens, tint, () =>
    {
        var light = Color.FromRgb(255, 250, 226);
        var dark = Mix(Shade(2, tint), Shade(3, tint), 0.4);
        var brush = new LinearGradientBrush(
            [
                new GradientStop(WithAlpha(light, 0.6), 0),
                new GradientStop(WithAlpha(light, 0), 0.42),
                new GradientStop(WithAlpha(dark, 0), 0.6),
                new GradientStop(WithAlpha(dark, 0.5), 1),
            ], new Point(0, GradientTop), new Point(0, GradientBottom))
        { MappingMode = BrushMappingMode.Absolute };
        return new Pen(brush, 0.12) { LineJoin = PenLineJoin.Round };
    });

    private Brush GlowBrush(int level, int tint, double extent) => Cached(_glowBrushes, level * 100 + tint, () =>
    {
        var color = tint > TintLevels / 2 ? Color.FromRgb(170, 182, 200) : Color.FromRgb(255, 190, 60);
        var alpha = level / 20.0 * 0.4;
        return Radial([(0, WithAlpha(color, 0)), (0.2, WithAlpha(color, alpha)), (1, WithAlpha(color, 0))]);
    });

    private Brush GlossBrush(int level) => Cached(_glossBrushes, level, () =>
        Radial([(0, WithAlpha(Colors.White, 0.85 * level / 10)), (0.55, WithAlpha(Colors.White, 0.35 * level / 10)), (1, WithAlpha(Colors.White, 0))]));

    private Brush CheekBrush(bool grey) => Cached(_cheekBrushes, grey ? 1 : 0, () =>
        grey ? Radial([(0, Color.FromArgb(89, 170, 140, 160)), (1, Color.FromArgb(0, 170, 140, 160))])
             : Radial([(0, Color.FromArgb(107, 255, 112, 72)), (1, Color.FromArgb(0, 255, 112, 72))]));

    private static RadialGradientBrush Radial(IEnumerable<(double Offset, Color Color)> stops) =>
        new([.. stops.Select(s => new GradientStop(s.Color, s.Offset))]);

    private static Brush Cached(Dictionary<int, Brush> cache, int key, Func<Brush> create)
    {
        if (!cache.TryGetValue(key, out var brush))
        {
            brush = create();
            brush.Freeze();
            cache[key] = brush;
        }

        return brush;
    }

    private static Pen CachedPen(Dictionary<int, Pen> cache, int key, Func<Pen> create)
    {
        if (!cache.TryGetValue(key, out var pen))
        {
            pen = create();
            pen.Freeze();
            cache[key] = pen;
        }

        return pen;
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
