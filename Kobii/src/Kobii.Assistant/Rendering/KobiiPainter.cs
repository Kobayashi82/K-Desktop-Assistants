using System.Windows;
using System.Windows.Media;
using Kobii.Core.Animation;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using Expression = Kobii.Core.Creature.Expression;

namespace Kobii.Assistant.Rendering;

/// <summary>
/// Dibuja a Kobii en vectorial con la API de dibujo de WPF (el host lo pasa a la GPU con Direct2D, como al Lander). Todo se
/// dibuja en unidades de la criatura con una transformación por encima (giro, aplastamiento, inclinación y tamaño), así los
/// pinceles del sombreado se crean una vez por color y la silueta solo se rehace cuando cambia su forma.
/// </summary>
/// <remarks>
/// Orden: sombra, pies de atrás, brote, contorno de todo lo que es cuerpo (cuerpo, orejas y brazos a los lados) y luego su
/// relleno, de modo que las piezas se funden en una sola silueta; el sombreado y la barriga recortados al cuerpo; la cara;
/// los pies de delante y los brazos que cruzan por delante, con su propio contorno.
/// </remarks>
internal sealed class KobiiPainter
{
    /// <summary>Grosor del contorno (unidades): la mitad queda fuera de la silueta.</summary>
    private const double Outline = 0.10;

    private static readonly Color EyeDark = Color.FromRgb(43, 36, 51);
    private static readonly Color MouthDark = Color.FromRgb(107, 38, 56);
    private static readonly Color TongueColor = Color.FromRgb(255, 140, 153);
    private static readonly Color BlushPink = Color.FromRgb(255, 143, 163);
    private static readonly Color Glow = Color.FromRgb(255, 235, 115);

    private readonly Dictionary<int, Brush> _shadowBrushes = [];

    private Ink? _ink;
    private ShapeKey? _shapeKey;
    private Geometry? _silhouette;
    private readonly double[] _radii = new double[KobiiShape.Rays];
    private bool _traced;

    /// <summary>Kobii entero según <paramref name="frame"/>, en píxeles de pantalla.</summary>
    /// <param name="held">Lo que lleva en la mano (se dibuja entre el cuerpo y las manos que lo agarran).</param>
    public void Draw(DrawingContext dc, KobiiFrame frame, Action<DrawingContext>? held = null)
    {
        var c = frame.Look;
        var rig = frame.Rig;
        var ink = InkFor(frame.Palette);
        var opacity = Math.Clamp(c.Opacity, 0, 1);
        if (opacity <= 0.002)
        {
            return;
        }

        var pushed = 0;
        if (frame.Clip is { } clip)
        {
            dc.PushClip(new RectangleGeometry(ClipRect(clip, frame.Feet, c.Unit)));
            pushed++;
        }

        if (opacity < 0.998)
        {
            dc.PushOpacity(opacity);
            pushed++;
        }

        DrawShadow(dc, frame);
        dc.PushTransform(new MatrixTransform(BodyMatrix(frame)));
        var facing = c.Facing;
        var arms = new ArmShape(rig, facing, -1);
        var armsR = new ArmShape(rig, facing, 1);
        var silhouette = Silhouette(rig, facing);

        // Pies de atrás y brote.
        if (rig.FeetFront < 0.5)
        {
            DrawFeet(dc, rig, ink);
        }

        DrawSprout(dc, frame, ink);

        // Silueta: primero el contorno de todas las piezas y después su relleno (así se funden).
        var sideL = rig.FrontL < 0.5 && arms.Scale > 0.02;
        var sideR = rig.FrontR < 0.5 && armsR.Scale > 0.02;
        if (sideL)
        {
            arms.Draw(dc, null, ink.OutlinePen);
        }

        if (sideR)
        {
            armsR.Draw(dc, null, ink.OutlinePen);
        }

        dc.DrawGeometry(null, ink.OutlinePen, silhouette);
        if (sideL)
        {
            arms.Draw(dc, ink.Body, null);
        }

        if (sideR)
        {
            armsR.Draw(dc, ink.Body, null);
        }

        dc.DrawGeometry(ink.Body, null, silhouette);
        DrawShading(dc, silhouette, rig, facing, ink);
        DrawFace(dc, frame, ink);

        if (rig.FeetFront >= 0.5)
        {
            DrawFeet(dc, rig, ink);
        }

        DrawFrontArm(dc, arms, rig.FrontL, silhouette, ink);
        DrawFrontArm(dc, armsR, rig.FrontR, silhouette, ink);
        dc.Pop();

        if (held is not null)
        {
            held(dc);
            DrawHandsOver(dc, frame, arms, armsR, silhouette, ink);
        }

        for (var i = 0; i < pushed; i++)
        {
            dc.Pop();
        }
    }

    /// <summary>De unidades de la criatura a píxeles: giro del esqueleto, aplastamiento alrededor de los pies, inclinación y tamaño.</summary>
    public static Matrix BodyMatrix(KobiiFrame frame)
    {
        var c = frame.Look;
        var rig = frame.Rig;
        var squash = (double)c.Squash;
        var sx = 1 / Math.Sqrt(Math.Max(squash, 0.3));
        var m = Matrix.Identity;
        m.Translate(-Rig.Pivot.X, -Rig.Pivot.Y);
        m.Rotate(rig.Spin * 180 / Math.PI);
        m.Translate(Rig.Pivot.X + rig.OffsetX, Rig.Pivot.Y + rig.OffsetY);
        m.Scale(sx, squash);
        m.Rotate(c.Lean * 180 / Math.PI);
        m.Scale(c.Unit, c.Unit);
        m.Translate(frame.Feet.X, frame.Feet.Y);
        return m;
    }

    /// <summary>Del espacio de la cara al del cuerpo: inclinación de la cabeza, encorvado, derretido y asentir o negar.</summary>
    private static Matrix FaceMatrix(in Rig rig, double facing)
    {
        var hc = KobiiShape.HeadCenter;
        var shift = KobiiShape.HunchShift(rig, facing);
        var m = Matrix.Identity;
        m.Translate(rig.FaceX - hc.X, rig.FaceY - hc.Y);
        m.Rotate(rig.HeadTilt * 180 / Math.PI);
        m.Translate(hc.X + shift.X, hc.Y + shift.Y + KobiiShape.MeltDrop(rig.Melt));
        return m;
    }

    private static Matrix HeadMatrix(in Rig rig, double facing)
    {
        var hc = KobiiShape.HeadCenter;
        var shift = KobiiShape.HunchShift(rig, facing);
        var m = Matrix.Identity;
        m.Translate(-hc.X, -hc.Y);
        m.Rotate(rig.HeadTilt * 180 / Math.PI);
        m.Translate(hc.X + shift.X, hc.Y + shift.Y + KobiiShape.MeltDrop(rig.Melt));
        return m;
    }

    private Geometry Silhouette(in Rig rig, double facing)
    {
        var key = ShapeKey.Of(rig, facing);
        if (_silhouette is null || _shapeKey != key)
        {
            _shapeKey = key;
            _silhouette = KobiiShape.Silhouette(rig, facing, _radii, near: _traced);
            _traced = true;
        }

        return _silhouette;
    }

    private Ink InkFor(Palette palette)
    {
        if (_ink?.Palette != palette)
        {
            _ink = new Ink(palette);
        }

        return _ink;
    }

    private static Rect ClipRect(ClipLine clip, Vec2 feet, double unit)
    {
        // Lo que queda al otro lado de la línea no se ve.
        var big = Math.Max(4000, unit * 40);
        if (clip.Horizontal)
        {
            return clip.Side > 0
                ? new Rect(feet.X - big, clip.Position - big * 2, big * 2, big * 2)
                : new Rect(feet.X - big, clip.Position, big * 2, big * 2);
        }

        return clip.Side > 0
            ? new Rect(clip.Position - big * 2, feet.Y - big, big * 2, big * 2)
            : new Rect(clip.Position, feet.Y - big, big * 2, big * 2);
    }

    private void DrawShadow(DrawingContext dc, KobiiFrame frame)
    {
        var c = frame.Look;
        if (c.ShadowDrop > 50 || c.ShadowAlpha <= 0.001)
        {
            return;
        }

        // Más pequeña y tenue cuanto más alto está; el borde, difuminado.
        var lift = Math.Clamp(c.ShadowDrop / 6.0, 0, 1);
        var alpha = c.ShadowAlpha * (1 - lift);
        var width = (0.85 + 0.35 * frame.Rig.Melt) * (1 - 0.5 * lift) * c.Unit;
        var height = 0.11 * (1 - 0.4 * lift) * c.Unit;
        var center = new Point(frame.Feet.X, frame.Feet.Y + c.ShadowDrop * c.Unit);
        // Un pincel por nivel de opacidad (se reutiliza: el host no tiene que volver a convertirlo).
        var level = (int)Math.Round(alpha * 100);
        if (!_shadowBrushes.TryGetValue(level, out var brush))
        {
            var radial = new RadialGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb((byte)(level * 2.55), 0, 0, 0), 0.55),
                    new(Color.FromArgb(0, 0, 0, 0), 1),
                });
            radial.Freeze();
            _shadowBrushes[level] = brush = radial;
        }
        dc.DrawEllipse(brush, null, center, width * 1.15, height * 1.6);
    }

    private static void DrawFeet(DrawingContext dc, in Rig rig, Ink ink)
    {
        // Al derretirse, los pies se meten en el charco.
        var size = 1 - Smooth(rig.Melt / 0.55);
        if (size < 0.05)
        {
            return;
        }

        for (var side = -1; side <= 1; side += 2)
        {
            var foot = Rig.RestFoot(side) + (side < 0 ? new Vec2(rig.FootLX, rig.FootLY) : new Vec2(rig.FootRX, rig.FootRY));
            var turn = side < 0 ? rig.FootLTurn : rig.FootRTurn;
            var ellipse = new EllipseGeometry(new Point(foot.X, foot.Y - 0.14 * (1 - size)), 0.25 * size, 0.14 * size,
                new RotateTransform(turn * 180 / Math.PI, foot.X, foot.Y));
            dc.DrawGeometry(ink.Feet, ink.OutlinePenThin, ellipse);
        }
    }

    private static void DrawSprout(DrawingContext dc, KobiiFrame frame, Ink ink)
    {
        var c = frame.Look;
        var rig = frame.Rig;
        dc.PushTransform(new MatrixTransform(HeadMatrix(rig, c.Facing)));

        // Tallo curvo con una hoja que se mece; caído si está derretido. La hoja brilla al escuchar y al pensar.
        var top = -1.72 - 0.08 * KobiiShape.Grow;
        var sway = 0.05 * Math.Sin(c.Time * 1.7) + rig.Sprout + 0.5 * rig.Melt;
        var root = new Point(0, top + 0.08);
        var tip = new Point(0.10 + sway, top - 0.24 + 0.06 * Math.Abs(rig.Sprout) + 0.12 * rig.Melt);
        var bend = new Point(0.02 + sway * 0.3, top - 0.10);
        var stem = new StreamGeometry();
        using (var g = stem.Open())
        {
            g.BeginFigure(root, false, false);
            g.QuadraticBezierTo(bend, tip, true, true);
        }

        stem.Freeze();
        dc.DrawGeometry(null, ink.SproutOutline, stem);
        dc.DrawGeometry(null, ink.Stem, stem);

        var angle = -0.6 + sway * 1.4;
        var dir = new Vector(Math.Cos(angle), Math.Sin(angle));
        var across = new Vector(-dir.Y, dir.X);
        var length = 0.44;
        var width = 0.13;
        var end = tip + dir * length;
        var middle = tip + dir * (length * 0.45);
        var leaf = new StreamGeometry();
        using (var g = leaf.Open())
        {
            g.BeginFigure(tip, true, true);
            g.BezierTo(middle - across * width * 1.25 - dir * 0.10, end - across * width * 0.35, end, true, true);
            g.BezierTo(end + across * width * 0.35, middle + across * width * 1.25 - dir * 0.10, tip, true, true);
        }

        leaf.Freeze();
        if (c.Antenna > 0.01)
        {
            var halo = new RadialGradientBrush(Glow.WithAlpha(0.7 * c.Antenna), Glow.WithAlpha(0));
            halo.Freeze();
            dc.DrawEllipse(halo, null, middle, 0.55, 0.55);
        }

        var leafColor = Lerp(ink.Accent, Color.FromRgb(255, 242, 140), c.Antenna);
        dc.DrawGeometry(Brush(leafColor), ink.SproutOutlineThin, leaf);
        dc.DrawLine(Pen(Lerp(leafColor, ink.AccentDark, 0.55), 0.03), tip + dir * 0.05, tip + dir * (length * 0.78));
        dc.Pop();
    }

    private static void DrawShading(DrawingContext dc, Geometry silhouette, in Rig rig, double facing, Ink ink)
    {
        dc.PushClip(silhouette);

        // Luz suave por el borde (vinilo blando) y sombra hacia abajo a la derecha.
        dc.DrawGeometry(null, ink.Rim1, silhouette);
        dc.DrawGeometry(null, ink.Rim2, silhouette);
        dc.DrawRectangle(ink.Under, null, new Rect(-2, -2.5, 4, 2.6));

        // Barriga más clara: sigue a la cara cuando se gira (vista de tres cuartos).
        var belly = new Point(facing * 0.10 + rig.FaceX * 0.6, -0.50 + 0.18 * rig.Melt);
        dc.DrawEllipse(ink.Belly, null, belly, 0.56 + 0.30 * rig.Melt, 0.38 - 0.10 * rig.Melt);

        // Brillo de arriba a la izquierda, que se mueve con la cabeza.
        var drop = KobiiShape.MeltDrop(rig.Melt);
        dc.PushTransform(new RotateTransform(-32, -0.36, -1.42 + drop));
        dc.DrawEllipse(ink.Shine, null, new Point(-0.36, -1.42 + drop), 0.24, 0.13);
        dc.Pop();
        dc.DrawEllipse(ink.ShineDot, null, new Point(-0.10, -1.60 + drop), 0.045, 0.045);
        dc.Pop();
    }

    private static void DrawFace(DrawingContext dc, KobiiFrame frame, Ink ink)
    {
        var c = frame.Look;
        var rig = frame.Rig;
        dc.PushTransform(new MatrixTransform(FaceMatrix(rig, c.Facing)));
        var ex = (Expression)(int)Math.Round(c.Expression);
        var g = KobiiShape.Grow;
        var shift = c.Facing * 0.16 + c.LookX * 0.05;
        var size = new Vector(0.13, 0.17) * 0.92;
        var eyeY = -1.02 - 0.04 * g + c.LookY * 0.04;
        var blink = c.Blink;

        DrawEye(dc, new Point(-0.31 + shift, eyeY), size, ex, blink, -1, new Vector(c.LookX, c.LookY));
        DrawEye(dc, new Point(0.31 + shift, eyeY), size, ex, blink, 1, new Vector(c.LookX, c.LookY));

        // Mejillas sonrosadas (más con los mofletes llenos).
        var cheeks = rig.Cheeks;
        var blush = Math.Clamp(0.30 + 0.45 * c.Blush + (ex is Expression.Love or Expression.Joy ? 0.2 : 0) + (ex == Expression.Love ? 0.2 : 0) + 0.25 * cheeks, 0, 1);
        var cheekSize = new Vector(0.14 * (1 + 0.45 * cheeks), 0.075 * (1 + 0.9 * cheeks));
        var cheekY = -0.80 - 0.03 * g;
        var blushBrush = ink.Blush(blush);
        foreach (var side in (ReadOnlySpan<int>)[-1, 1])
        {
            var at = new Point(side * (0.55 + 0.05 * cheeks) + shift * 0.7, cheekY);
            dc.DrawEllipse(blushBrush, null, at, cheekSize.X * 1.5, cheekSize.Y * 1.6);
            if (cheeks > 0.05)
            {
                dc.DrawEllipse(Brush(Colors.White.WithAlpha(0.6 * cheeks)), null, at + new Vector(-0.03, -0.05), 0.035, 0.035);
            }
        }

        var mouth = new Point(shift * 0.9, -0.76 - 0.03 * g);
        DrawMouth(dc, mouth, ex, c.Mouth, rig, c.Time);
        if (Math.Abs(rig.Tongue) > 0.02)
        {
            // Relamerse: la lengua asoma por un lado de la boca.
            var lick = Math.Abs(rig.Tongue);
            var side = rig.Tongue > 0 ? 1 : -1;
            var at = mouth + new Vector(side * 0.07, 0.02);
            dc.PushTransform(new RotateTransform(side * 0.5 * 180 / Math.PI, at.X, at.Y));
            dc.DrawEllipse(Brush(TongueColor), Pen(MouthDark, 0.032), at + new Vector(0, 0.03 * lick), 0.04 * lick + 0.002, 0.055 * lick + 0.002);
            dc.Pop();
        }

        dc.Pop();
    }

    private static void DrawEye(DrawingContext dc, Point c, Vector size, Expression ex, double blink, int side, Vector look)
    {
        const double line = 0.056;
        var dark = Brush(EyeDark);
        var stroke = Pen(EyeDark, line);
        switch (ex)
        {
            case Expression.Sleeping:
                dc.DrawGeometry(null, stroke, HalfRing(c + new Vector(0, -0.05), size.X * 0.85, lower: true));
                return;

            case Expression.Yawn:
                dc.DrawGeometry(null, Pen(EyeDark, line * 1.25), HalfRing(c + new Vector(0, -0.02), size.X * 0.75, lower: true));
                return;

            case Expression.Joy or Expression.Eating or Expression.Love:
            case Expression.Happy when blink > 0.5:
            case Expression.Wink when side > 0:
                dc.DrawGeometry(null, stroke, HalfRing(c + new Vector(0, 0.07), size.X * 0.85, lower: false));
                return;

            case Expression.Pain:
            case Expression.Disgusted when side < 0:
            {
                // Ojo apretado «>» o «<»: la punta mira hacia la nariz.
                var dir = -side;
                var tip = c + new Vector(0.075 * dir, 0);
                dc.DrawGeometry(null, stroke, Polyline(c + new Vector(-0.055 * dir, -0.075), tip, c + new Vector(-0.055 * dir, 0.075)));
                return;
            }

            case Expression.Dizzy:
                dc.DrawLine(stroke, c + new Vector(-0.09, -0.09), c + new Vector(0.09, 0.09));
                dc.DrawLine(stroke, c + new Vector(-0.09, 0.09), c + new Vector(0.09, -0.09));
                return;

            case Expression.Surprised:
            {
                dc.DrawEllipse(Brushes.White, Pen(EyeDark, 0.05), c, size.X * 1.12, size.Y * 1.12);
                var pupil = c + look * 0.03;
                dc.DrawEllipse(dark, null, pupil, size.X * 0.48, size.X * 0.48);
                dc.DrawEllipse(Brushes.White, null, pupil + new Vector(-0.03, -0.035), 0.022, 0.022);
                return;
            }
        }

        // Ojo normal: óvalo oscuro con dos brillos. Al parpadear se aplasta hasta ser una línea.
        var open = ex switch
        {
            Expression.Sleepy => 0.45,
            Expression.Sick => 0.55,
            Expression.Disgusted => 0.75,
            _ => 1.0,
        };
        if (ex == Expression.Confused && side > 0)
        {
            open *= 0.6;
        }

        open *= 1 - Math.Clamp(blink, 0, 1);
        if (open < 0.12)
        {
            dc.DrawGeometry(null, Pen(EyeDark, line * 0.9), HalfRing(c + new Vector(0, -0.04), size.X * 0.8, lower: true));
            return;
        }

        var lids = ex is Expression.Sad or Expression.Angry or Expression.Sick;
        if (lids)
        {
            // Párpados caídos hacia fuera (tristeza) o hacia dentro (enfado): solo se ve lo que queda por debajo de la línea.
            var slope = (ex == Expression.Angry ? 0.6 : -0.55) * side;
            var y0 = c.Y - size.Y * 0.35;
            var lid = new StreamGeometry();
            using (var g = lid.Open())
            {
                g.BeginFigure(new Point(c.X - 1, y0 + slope), true, true);
                g.LineTo(new Point(c.X + 1, y0 - slope), false, false);
                g.LineTo(new Point(c.X + 1, c.Y + 1), false, false);
                g.LineTo(new Point(c.X - 1, c.Y + 1), false, false);
            }

            lid.Freeze();
            dc.PushClip(lid);
        }

        dc.DrawEllipse(dark, null, c, size.X, size.Y * open);
        dc.DrawEllipse(Brushes.White, null, c + new Vector(-0.045, -0.06 * open), 0.052 * Math.Min(1, open + 0.3), 0.052 * Math.Min(1, open + 0.3));
        dc.DrawEllipse(Brush(Colors.White.WithAlpha(0.85)), null, c + new Vector(0.045, 0.05 * open), 0.024 * open, 0.024 * open);
        if (lids)
        {
            dc.Pop();
        }
    }

    private static void DrawMouth(DrawingContext dc, Point c, Expression ex, double talk, in Rig rig, double time)
    {
        const double line = 0.044;
        var dark = Brush(MouthDark);
        var stroke = Pen(MouthDark, line);
        var open = ex is Expression.Joy or Expression.Love || talk > 0.08;

        // Comiendo: la boca abierta de par en par justo antes del mordisco…
        if (rig.MouthOpen > 0.05)
        {
            var tall = 0.025 + 0.10 * rig.MouthOpen;
            var wide = 0.065 + 0.05 * rig.MouthOpen;
            var center = c + new Vector(0, 0.01 + 0.03 * rig.MouthOpen);
            OpenMouth(dc, new EllipseGeometry(center, wide, tall), center, wide, tall);
            return;
        }

        // …y masticando con la boca llena: cerrada y pequeñita, sube y baja con la mandíbula.
        if (rig.Chew > 0.001 || rig.Cheeks > 0.15)
        {
            var jaw = Math.Clamp(rig.Chew, 0, 1);
            dc.DrawEllipse(dark, null, c + new Vector(0, 0.015 * jaw), 0.05 - 0.012 * jaw + line * 0.2 * (1 - jaw), 0.008 + 0.034 * jaw + line * 0.2 * (1 - jaw));
            return;
        }

        switch (ex)
        {
            case Expression.Yawn:
            {
                var center = c + new Vector(0, 0.02);
                OpenMouth(dc, new EllipseGeometry(center, 0.085, 0.12), center, 0.085, 0.12);
                return;
            }

            case Expression.Pain:
            {
                var teeth = new RectangleGeometry(new Rect(c.X - 0.075, c.Y - 0.032, 0.15, 0.064), 0.012, 0.012);
                dc.DrawGeometry(Brushes.White, stroke, teeth);
                dc.DrawLine(Pen(MouthDark, 0.012), c + new Vector(-0.07, 0), c + new Vector(0.07, 0));
                return;
            }

            case Expression.Disgusted:
                dc.DrawGeometry(Brush(TongueColor), stroke, new EllipseGeometry(c + new Vector(0.025, 0.045), 0.042, 0.045));
                dc.DrawLine(stroke, c + new Vector(-0.07, 0.012), c + new Vector(0.07, -0.008));
                return;

            case Expression.Sick when !open:
            {
                var wave = new StreamGeometry();
                using (var g = wave.Open())
                {
                    g.BeginFigure(c + new Vector(-0.085, 0), false, false);
                    for (var x = -0.08; x <= 0.0851; x += 0.01)
                    {
                        g.LineTo(c + new Vector(x, 0.016 * Math.Sin(x * 60)), true, true);
                    }
                }

                wave.Freeze();
                dc.DrawGeometry(null, Pen(MouthDark, line * 0.9), wave);
                return;
            }

            case Expression.Surprised or Expression.Dizzy:
                dc.DrawEllipse(dark, null, c, 0.055, 0.07);
                return;
        }

        if (open)
        {
            var big = ex is Expression.Joy or Expression.Love;
            var h = big ? Math.Max(0.10, 0.05 + 0.09 * talk) : 0.03 + 0.09 * talk;
            var w = big ? 0.13 : 0.085;
            if (big)
            {
                // Media elipse (la parte de abajo): sonrisa abierta.
                var center = c + new Vector(0, -0.01);
                var d = new StreamGeometry();
                using (var g = d.Open())
                {
                    g.BeginFigure(center + new Vector(-w, 0), true, true);
                    g.ArcTo(center + new Vector(w, 0), new Size(w, h), 0, false, SweepDirection.Counterclockwise, true, true);
                }

                d.Freeze();
                OpenMouth(dc, d, center, w, h);
            }
            else
            {
                OpenMouth(dc, new EllipseGeometry(c, w, h), c, w, h);
            }

            return;
        }

        switch (ex)
        {
            case Expression.Sad:
                dc.DrawGeometry(null, stroke, HalfRing(c + new Vector(0, 0.06), 0.075, lower: false));
                return;

            case Expression.Angry or Expression.Confused:
                dc.DrawLine(stroke, c + new Vector(-0.06, 0.01), c + new Vector(0.06, -0.01));
                return;

            case Expression.Sleeping:
                dc.DrawEllipse(dark, null, c + new Vector(0, 0.01), 0.03, 0.025 + 0.01 * Math.Sin(time * 2));
                return;
        }

        var r = ex is Expression.Happy or Expression.Eating or Expression.Wink ? 0.085 : 0.065;
        dc.DrawGeometry(null, stroke, HalfRing(c + new Vector(0, -0.03), r, lower: true));
    }

    /// <summary>Boca abierta con la lengua asomando por abajo.</summary>
    private static void OpenMouth(DrawingContext dc, Geometry mouth, Point center, double w, double h)
    {
        dc.DrawGeometry(Brush(MouthDark), null, mouth);
        dc.PushClip(mouth);
        dc.DrawEllipse(Brush(TongueColor), null, center + new Vector(0, h * 0.75), w * 0.6, h * 0.55);
        dc.Pop();
    }

    /// <summary>
    /// Un brazo que cruza por delante del cuerpo: brazo y mano en una sola forma, con una sombra suave sobre la barriga, algo
    /// más claro hacia la mano (de donde viene la luz) y con contorno salvo donde nace del cuerpo, que así sale de él.
    /// </summary>
    private static void DrawFrontArm(DrawingContext dc, ArmShape arm, double front, Geometry silhouette, Ink ink)
    {
        var shown = Smooth((front - 0.05) / 0.7);
        if (shown <= 0.01 || arm.Scale < 0.02)
        {
            return;
        }

        if (shown < 0.99)
        {
            dc.PushOpacity(shown);
        }

        var shape = arm.Shape();
        dc.PushClip(silhouette);
        dc.PushTransform(new TranslateTransform(0.03, 0.075));
        dc.DrawGeometry(ink.ArmShadow, null, shape);
        dc.Pop();
        dc.Pop();

        dc.PushClip(arm.WithoutShoulder());
        dc.DrawGeometry(null, ink.OutlinePen, shape);
        dc.Pop();
        dc.DrawGeometry(ink.Body, null, shape);
        var light = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(arm.Shoulder.X, arm.Shoulder.Y),
            EndPoint = new Point(arm.HandAt.X, arm.HandAt.Y),
            GradientStops = { new GradientStop(Colors.White.WithAlpha(0), 0.15), new GradientStop(Colors.White.WithAlpha(0.16), 1) },
        };
        light.Freeze();
        dc.DrawGeometry(light, null, shape);

        if (shown < 0.99)
        {
            dc.Pop();
        }
    }

    /// <summary>Segunda pasada: las manos que agarran algo, otra vez por encima de lo que llevan (así los dedos lo rodean).</summary>
    private static void DrawHandsOver(DrawingContext dc, KobiiFrame frame, ArmShape left, ArmShape right, Geometry silhouette, Ink ink)
    {
        var c = frame.Look;
        if (c.HandOverL < 0.5 && c.HandOverR < 0.5)
        {
            return;
        }

        dc.PushTransform(new MatrixTransform(BodyMatrix(frame)));
        foreach (var arm in (ReadOnlySpan<ArmShape>)[left, right])
        {
            if ((arm.Side < 0 ? c.HandOverL : c.HandOverR) < 0.5)
            {
                continue;
            }

            var hand = arm.Hand();
            dc.DrawGeometry(null, ink.OutlinePen, hand);
            dc.DrawGeometry(ink.Body, null, hand);
        }

        dc.Pop();
    }

    private static Geometry HalfRing(Point center, double r, bool lower)
    {
        var g = new StreamGeometry();
        using (var s = g.Open())
        {
            s.BeginFigure(center + new Vector(-r, 0), false, false);
            s.ArcTo(center + new Vector(r, 0), new Size(r, r), 0, false, lower ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true, true);
        }

        g.Freeze();
        return g;
    }

    private static Geometry Polyline(params Point[] points)
    {
        var g = new StreamGeometry();
        using (var s = g.Open())
        {
            s.BeginFigure(points[0], false, false);
            for (var i = 1; i < points.Length; i++)
            {
                s.LineTo(points[i], true, true);
            }
        }

        g.Freeze();
        return g;
    }

    private static double Smooth(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    internal static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    internal static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    internal static Pen Pen(Color color, double width)
    {
        var pen = new Pen(Brush(color), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }

    /// <summary>Pinceles de un colorido (congelados: el host los convierte una sola vez).</summary>
    private sealed class Ink
    {
        public Ink(Palette palette)
        {
            Palette = palette;
            var body = Rgb(palette.Body);
            var outline = Scale(body, 0.42);
            Accent = Rgb(palette.Accent);
            AccentDark = Scale(Accent, 0.55);

            // Relleno con la luz desde arriba a la izquierda (en unidades: vale para el cuerpo, las orejas y los brazos).
            var fill = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = new Point(-0.22, -1.25),
                GradientOrigin = new Point(-0.38, -1.48),
                RadiusX = 1.55,
                RadiusY = 1.65,
                GradientStops =
                {
                    new GradientStop(Scale(body, 1.10), 0),
                    new GradientStop(body, 0.45),
                    new GradientStop(Scale(body, 0.86), 0.80),
                    new GradientStop(Scale(body, 0.74), 1),
                },
            };
            fill.Freeze();
            Body = fill;
            Feet = Brush(Scale(body, 0.88));
            OutlinePen = Pen(outline, Outline);
            OutlinePenThin = Pen(outline, Outline * 0.8);
            ArmShadow = Brush(Color.FromArgb(34, 0, 0, 0));
            SproutOutline = Pen(outline, 0.07 + 0.09);
            SproutOutlineThin = Pen(outline, 0.05);
            Stem = Pen(Scale(body, 0.9), 0.07);
            Rim1 = Pen(Colors.White.WithAlpha(0.10), 0.16);
            Rim2 = Pen(Colors.White.WithAlpha(0.10), 0.07);

            var under = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, -0.42),
                EndPoint = new Point(0, 0.02),
                GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Color.FromArgb(40, 0, 0, 0), 1) },
            };
            under.Freeze();
            Under = under;

            var bellyColor = Rgb(palette.Belly);
            var belly = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.35, 0.25),
                GradientStops =
                {
                    new GradientStop(bellyColor.WithAlpha(0.95), 0),
                    new GradientStop(Scale(bellyColor, 0.92).WithAlpha(0.92), 0.86),
                    new GradientStop(Scale(bellyColor, 0.92).WithAlpha(0), 1),
                },
            };
            belly.Freeze();
            Belly = belly;

            var shine = new RadialGradientBrush(Colors.White.WithAlpha(0.75), Colors.White.WithAlpha(0));
            shine.Freeze();
            Shine = shine;
            ShineDot = Brush(Colors.White.WithAlpha(0.6));
        }

        private readonly Dictionary<int, Brush> _blush = [];

        public Palette Palette { get; }

        /// <summary>Rubor de las mejillas con esa intensidad (un pincel por centésima, reutilizado).</summary>
        public Brush Blush(double alpha)
        {
            var level = (int)Math.Round(Math.Clamp(alpha, 0, 1) * 100);
            if (!_blush.TryGetValue(level, out var brush))
            {
                var a = level / 100.0;
                var radial = new RadialGradientBrush(BlushPink.WithAlpha(a), BlushPink.WithAlpha(0));
                radial.GradientStops.Insert(1, new GradientStop(BlushPink.WithAlpha(a * 0.8), 0.45));
                radial.Freeze();
                _blush[level] = brush = radial;
            }

            return brush;
        }

        public Brush Body { get; }

        public Brush Feet { get; }

        public Brush ArmShadow { get; }

        public Brush Belly { get; }

        public Brush Under { get; }

        public Brush Shine { get; }

        public Brush ShineDot { get; }

        public Color Accent { get; }

        public Color AccentDark { get; }

        public Pen OutlinePen { get; }

        public Pen OutlinePenThin { get; }

        public Pen SproutOutline { get; }

        public Pen SproutOutlineThin { get; }

        public Pen Stem { get; }

        public Pen Rim1 { get; }

        public Pen Rim2 { get; }

        private static Color Rgb(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        private static Color Scale(Color c, double f) => Color.FromRgb(
            (byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));
    }
}

/// <summary>
/// Un brazo como en el shader: un tubo que sigue una curva del hombro a la mano (más gordito en el hombro) y una manopla
/// orientada como el antebrazo, con el pulgar hacia arriba; al agarrar algo se cierra en un puño.
/// </summary>
internal sealed class ArmShape
{
    private const int Samples = 20;

    public ArmShape(in Rig rig, double facing, int side)
    {
        Side = side;
        var form = Species.Form;
        var grow = Rig.Grow(form);
        // Derretido, los hombros bajan y se abren con el cuerpo.
        Shoulder = new Vec2(side * (0.70 - 0.04 * grow) * (1 + 0.30 * rig.Melt) + facing * 0.14, -0.92 + 0.85 * KobiiShape.MeltDrop(rig.Melt));
        HandAt = rig.Hand(side, form);
        var reach = HandAt - Shoulder;
        var d = reach.Length;
        var middle = (Shoulder + HandAt) * 0.5;
        if (d < 1e-4)
        {
            Control = middle;
        }
        else
        {
            // En reposo el brazo cuelga casi recto; se dobla del todo al apartar la mano (el mismo cálculo que el shader).
            var h = Math.Sqrt(Math.Max(0, Rig.ArmLength * Rig.ArmLength * 0.25 - d * d * 0.25)) * rig.BendAmount(side, form);
            var normal = new Vec2(-reach.Y, reach.X) / d;
            var outward = KobiiShape.Rotate(new Vec2(side, 0.7), side * (side < 0 ? rig.ElbowL : rig.ElbowR));
            if (Vec2.Dot(normal, outward) < 0)
            {
                normal = -normal;
            }

            Control = middle + normal * h * 2.0;
        }

        var dir = HandAt - Control;
        Dir = dir.LengthSquared > 1e-8 ? dir.Normalized() : new Vec2(side, 0);
        Across = new Vec2(-Dir.Y, Dir.X);
        ThumbSide = Vec2.Dot(Across, new Vec2(-side * 0.6, -1.0)) >= 0 ? 1 : -1;
        Grip = Math.Clamp(rig.Grip(side), 0, 1);

        // Derretido del todo, los brazos se funden con el charco (las manos sobre la barbilla, que van por delante, no).
        var melted = Math.Clamp(rig.Melt / 0.75, 0, 1);
        Scale = rig.Front(side) >= 0.5 ? 1 : 1 - melted * melted * (3 - 2 * melted);
    }

    public int Side { get; }

    public Vec2 Shoulder { get; }

    public Vec2 Control { get; }

    public Vec2 HandAt { get; }

    public Vec2 Dir { get; }

    public Vec2 Across { get; }

    public double ThumbSide { get; }

    public double Grip { get; }

    public double Scale { get; }

    private Geometry? _shape;

    /// <summary>Dibuja el brazo con el relleno o el contorno indicados.</summary>
    public void Draw(DrawingContext dc, Brush? fill, Pen? pen) => dc.DrawGeometry(fill, pen, Shape());

    /// <summary>
    /// Brazo y mano juntos (se hace una vez por fotograma). Sin unir las formas, que es caro: el contorno se dibuja antes que
    /// el relleno, así que lo que queda dentro no se ve.
    /// </summary>
    public Geometry Shape()
    {
        if (_shape is null)
        {
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };
            group.Children.Add(Tube());
            foreach (var part in ((GeometryGroup)Hand()).Children)
            {
                group.Children.Add(part);
            }

            group.Freeze();
            _shape = group;
        }

        return _shape;
    }

    /// <summary>Todo menos el hombro (un agujero con la regla par-impar): recorta el contorno donde el brazo nace del cuerpo.</summary>
    public Geometry WithoutShoulder()
    {
        var clip = new GeometryGroup { FillRule = FillRule.EvenOdd };
        clip.Children.Add(new RectangleGeometry(new Rect(-4, -5, 8, 6)));
        clip.Children.Add(new EllipseGeometry(new Point(Shoulder.X, Shoulder.Y), 0.17 * Scale, 0.17 * Scale));
        clip.Freeze();
        return clip;
    }

    public Geometry Tube()
    {
        // Contorno del tubo: un lado hacia delante y el otro de vuelta, con media vuelta en el hombro.
        var left = new Point[Samples + 1];
        var right = new Point[Samples + 1];
        for (var i = 0; i <= Samples; i++)
        {
            var t = (double)i / Samples;
            var p = Bezier(t);
            var tangent = (Control - Shoulder) * (2 * (1 - t)) + (HandAt - Control) * (2 * t);
            var n = tangent.LengthSquared > 1e-10 ? new Vec2(-tangent.Y, tangent.X).Normalized() : new Vec2(0, 1);
            var r = (0.125 + (0.095 - 0.125) * t) * Scale;
            left[i] = new Point(p.X + n.X * r, p.Y + n.Y * r);
            right[i] = new Point(p.X - n.X * r, p.Y - n.Y * r);
        }

        // Siempre en el sentido de las agujas del reloj en pantalla, como las elipses de la mano: así, juntos en un grupo con
        // la regla de relleno normal, se rellenan como una sola forma (en sentidos opuestos, lo que se solapa quedaría hueco).
        var area = 0.0;
        for (var i = 0; i < Samples; i++)
        {
            area += left[i].X * left[i + 1].Y - left[i + 1].X * left[i].Y;
            area += right[i + 1].X * right[i].Y - right[i].X * right[i + 1].Y;
        }

        area += left[Samples].X * right[Samples].Y - right[Samples].X * left[Samples].Y;
        area += right[0].X * left[0].Y - left[0].X * right[0].Y;
        if (area < 0)
        {
            (left, right) = (right, left);
        }

        var g = new StreamGeometry();
        using (var s = g.Open())
        {
            s.BeginFigure(left[0], true, true);
            for (var i = 1; i <= Samples; i++)
            {
                s.LineTo(left[i], true, true);
            }

            for (var i = Samples; i >= 0; i--)
            {
                s.LineTo(right[i], true, true);
            }

            s.ArcTo(left[0], new Size(0.12 * Scale, 0.12 * Scale), 0, false, SweepDirection.Clockwise, true, true);
        }

        g.Freeze();
        return g;
    }

    public Geometry Hand()
    {
        var s = Scale;
        var palmCenter = HandAt + Dir * (0.03 * (1 - Grip) * s);
        var palm = new Vec2(0.155 + (0.13 - 0.155) * Grip, 0.12 + (0.125 - 0.12) * Grip) * s;
        var angle = Math.Atan2(Dir.Y, Dir.X) * 180 / Math.PI;
        var thumb = HandAt + Dir * ((0.07 * Grip) * s) + Across * (ThumbSide * (0.115 + (0.055 - 0.115) * Grip) * s);
        var thumbR = (0.055 + (0.05 - 0.055) * Grip) * s;
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(new EllipseGeometry(new Point(palmCenter.X, palmCenter.Y), palm.X, palm.Y, new RotateTransform(angle, palmCenter.X, palmCenter.Y)));
        group.Children.Add(new EllipseGeometry(new Point(thumb.X, thumb.Y), thumbR, thumbR));
        group.Freeze();
        return group;
    }

    private Vec2 Bezier(double t) => Shoulder * ((1 - t) * (1 - t)) + Control * (2 * (1 - t) * t) + HandAt * (t * t);
}

internal static class ColorExtensions
{
    public static Color WithAlpha(this Color color, double alpha) =>
        Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), color.R, color.G, color.B);
}
