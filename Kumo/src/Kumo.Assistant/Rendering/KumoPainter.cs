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
/// Orden: la sombra; los anillos del borde blando (el mismo degradado cada vez más opaco) y el cuerpo, liso por dentro (los bultos
/// se ven en la silueta: sombreados, sus círculos rodeaban la cara como un disco o unos mofletes); una banda junto al borde
/// iluminada por arriba y oscura por abajo; recortado al cuerpo, el brillo de gota; la cara.
/// </remarks>
internal sealed class KumoPainter
{
    /// <summary>De arriba abajo: luz, medio, sombra y pliegue (ámbar).</summary>
    private static readonly Color[] Amber = [Color.FromRgb(255, 238, 178), Color.FromRgb(252, 194, 62), Color.FromRgb(224, 128, 18), Color.FromRgb(178, 88, 14)];

    /// <summary>Lo mismo en gris (algo salió mal).</summary>
    private static readonly Color[] Grey = [Color.FromRgb(224, 230, 238), Color.FromRgb(172, 184, 200), Color.FromRgb(122, 134, 154), Color.FromRgb(86, 96, 114)];

    /// <summary>Niveles de gris en los que se guardan los pinceles (no hace falta más: el cambio dura un momento).</summary>
    private const int TintLevels = 8;

    /// <summary>Arriba y abajo del degradado del cuerpo (unidades).</summary>
    private const double GradientTop = -0.9;

    private const double GradientBottom = 0.8;

    private readonly Dictionary<int, Brush> _bodyBrushes = [];
    private readonly Dictionary<int, Brush> _ringBrushes = [];
    private readonly Dictionary<int, Brush> _rimBrushes = [];
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

        // Sombra blanda debajo.
        dc.DrawEllipse(ShadowBrush, null, new Point(c.X + 0.1, c.Y + 0.38), ext * 1.15, ext * 0.8);

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
            // El borde: la banda entre el cuerpo y el mismo encogido (pares e impares: lo de dentro queda fuera). Un trazo
            // recortado al cuerpo daba lo mismo, pero al host le cuesta una capa y ensanchar el trazo en cada fotograma.
            dc.DrawGeometry(RimBrush(tint), null, Outline([.. frame.Core, .. frame.Inset], FillRule.EvenOdd));

            var gloss = (int)Math.Round(frame.Smooth * 10);
            if (gloss > 0)
            {
                dc.PushClip(body);
                DrawGloss(dc, frame, gloss);
                dc.Pop();
            }
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
    public static Geometry? Outline(List<Vec2[]> loops, FillRule rule = FillRule.Nonzero)
    {
        if (loops.Count == 0)
        {
            return null;
        }

        var geometry = new StreamGeometry { FillRule = rule };
        using (var g = geometry.Open())
        {
            foreach (var points in loops)
            {
                var n = points.Length;
                g.BeginFigure(P(points[0]), true, true);
                for (var i = 0; i < n; i++)
                {
                    var (c1, c2) = Centripetal(points[(i - 1 + n) % n], points[i], points[(i + 1) % n], points[(i + 2) % n]);
                    g.BezierTo(c1, c2, P(points[(i + 1) % n]), true, true);
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point P(Vec2 v) => new(v.X, v.Y);

    /// <summary>
    /// Los puntos de control del tramo p1→p2 de una Catmull-Rom centrípeta: con puntos a distancias muy distintas (un jirón que
    /// se separa) la uniforme se pasa de largo y hace picos y lazos; esta nunca.
    /// </summary>
    private static (Point, Point) Centripetal(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3)
    {
        var d1 = Math.Sqrt((p1 - p0).Length);
        var d2 = Math.Sqrt((p2 - p1).Length);
        var d3 = Math.Sqrt((p3 - p2).Length);
        if (d1 < 1e-6 || d2 < 1e-6 || d3 < 1e-6)
        {
            return (P(p1 + (p2 - p0) / 6), P(p2 - (p3 - p1) / 6));
        }

        var c1 = (p2 * (d1 * d1) - p0 * (d2 * d2) + p1 * (2 * d1 * d1 + 3 * d1 * d2 + d2 * d2)) / (3 * d1 * (d1 + d2));
        var c2 = (p1 * (d3 * d3) - p3 * (d2 * d2) + p2 * (2 * d3 * d3 + 3 * d3 * d2 + d2 * d2)) / (3 * d3 * (d3 + d2));
        return (P(c1), P(c2));
    }

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

    /// <summary>El borde: iluminado por arriba (el sol le da) y oscuro por debajo.</summary>
    private Brush RimBrush(int tint) => Cached(_rimBrushes, tint, () =>
    {
        var light = Color.FromRgb(255, 250, 226);
        var dark = Mix(Shade(2, tint), Shade(3, tint), 0.4);
        return new LinearGradientBrush(
            [
                new GradientStop(WithAlpha(light, 0.6), 0),
                new GradientStop(WithAlpha(light, 0), 0.42),
                new GradientStop(WithAlpha(dark, 0), 0.6),
                new GradientStop(WithAlpha(dark, 0.5), 1),
            ], new Point(0, GradientTop), new Point(0, GradientBottom))
        { MappingMode = BrushMappingMode.Absolute };
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

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
