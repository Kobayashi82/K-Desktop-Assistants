using System.Windows;
using System.Windows.Media;
using Kobii.Core.Animation;
using Kobii.Core.Creature;

namespace Kobii.Assistant.Rendering;

/// <summary>
/// La ropa de dormir y el pelo recién cepillado (lo que el shader de Kobii AI hacía con campos de distancia, aquí en
/// vectorial): el pijama de rayas con cuello en pico, ribete, dos botones y mangas; el gorro de dormir que cae hacia un lado
/// meciéndose, con su pompón, y que al ponérselo baja desde arriba; y los destellos que titilan sobre el pelo cepillado.
/// </summary>
internal sealed partial class KobiiPainter
{
    private Clothes? _clothes;

    private Clothes ClothesFor(Palette palette)
    {
        if (_clothes?.Palette != palette)
        {
            _clothes = new Clothes(palette);
        }

        return _clothes;
    }

    /// <summary>
    /// La camiseta: todo el cuerpo por debajo de un cuello en pico (que sigue a la cara cuando se gira), con su ribete y dos
    /// botones; por encima, la misma sombra de abajo que el cuerpo para que no quede plana.
    /// </summary>
    private void DrawPajamas(DrawingContext dc, Geometry silhouette, in KobiiLook c, Ink ink, Palette palette)
    {
        if (c.Pajamas <= 0.01)
        {
            return;
        }

        var cloth = ClothesFor(palette);
        var cx = (c.Facing * 0.16 + c.LookX * 0.05) * 0.8;
        var collar = new StreamGeometry();
        using (var g = collar.Open())
        {
            g.BeginFigure(new Point(-3, -0.68), true, true);
            g.LineTo(new Point(cx - 0.42, -0.68), false, false);
            g.LineTo(new Point(cx, -0.52), false, false);
            g.LineTo(new Point(cx + 0.42, -0.68), false, false);
            g.LineTo(new Point(3, -0.68), false, false);
            g.LineTo(new Point(3, 1), false, false);
            g.LineTo(new Point(-3, 1), false, false);
        }

        collar.Freeze();
        var pushed = 1;
        dc.PushClip(silhouette);
        if (c.Pajamas < 0.99)
        {
            dc.PushOpacity(c.Pajamas);
            pushed++;
        }

        dc.DrawGeometry(cloth.Stripes, null, collar);
        dc.DrawRectangle(ink.Under, null, new Rect(-2, -2.5, 4, 2.6));
        dc.DrawGeometry(null, ink.Rim2, silhouette);

        // Ribete del cuello y botones.
        var trim = new StreamGeometry();
        using (var g = trim.Open())
        {
            g.BeginFigure(new Point(-3, -0.68), false, false);
            g.LineTo(new Point(cx - 0.42, -0.68), true, true);
            g.LineTo(new Point(cx, -0.52), true, true);
            g.LineTo(new Point(cx + 0.42, -0.68), true, true);
            g.LineTo(new Point(3, -0.68), true, true);
        }

        trim.Freeze();
        dc.DrawGeometry(null, cloth.Trim, trim);
        dc.DrawEllipse(cloth.Button, null, new Point(cx * 0.7, -0.36), 0.045, 0.045);
        dc.DrawEllipse(cloth.Button, null, new Point(cx * 0.6, -0.16), 0.045, 0.045);
        for (var i = 0; i < pushed; i++)
        {
            dc.Pop();
        }
    }

    /// <summary>La manga del pijama sobre un brazo (hasta la muñeca: la mano se vuelve a pintar encima).</summary>
    private void DrawSleeve(DrawingContext dc, ArmShape arm, in KobiiLook c, Ink ink, Palette palette)
    {
        if (c.Pajamas <= 0.01 || arm.Scale < 0.02)
        {
            return;
        }

        var cloth = ClothesFor(palette);
        if (c.Pajamas < 0.99)
        {
            dc.PushOpacity(c.Pajamas);
        }

        dc.DrawGeometry(cloth.Stripes, null, arm.Tube());
        dc.DrawGeometry(ink.Body, null, arm.Hand());
        if (c.Pajamas < 0.99)
        {
            dc.Pop();
        }
    }

    /// <summary>
    /// El gorro de dormir: tapa lo alto de la cabeza y cae hacia un lado, meciéndose, con su pompón. Va en el espacio de la
    /// cabeza (se inclina con ella). Al ponérselo baja desde arriba; al quitárselo, sube.
    /// </summary>
    private void DrawNightcap(DrawingContext dc, KobiiFrame frame, Palette palette)
    {
        var c = frame.Look;
        if (c.Nightcap <= 0.01)
        {
            return;
        }

        var cloth = ClothesFor(palette);
        var shown = Math.Clamp(c.Nightcap * 3, 0, 1);
        var grow = KobiiShape.Grow;
        var headY = -0.98 - 0.05 * grow;
        var headR = 0.80 - 0.04 * grow;
        var brimY = headY - headR * 0.74;
        var w = headR * 0.74;
        var sway = 0.035 * Math.Sin(c.Time * 1.3);
        var lift = (1 - Math.Clamp(c.Nightcap, 0, 1)) * 0.9;

        dc.PushTransform(new MatrixTransform(HeadMatrix(frame.Rig, c.Facing)));
        dc.PushTransform(new TranslateTransform(0, -lift));
        if (shown < 0.99)
        {
            dc.PushOpacity(shown);
        }

        // La tela: sube por la cabeza hasta la punta, que cae hacia la izquierda.
        var tip = new Point(-0.84 + sway * 2, brimY - 0.34);
        var cap = new StreamGeometry();
        using (var g = cap.Open())
        {
            g.BeginFigure(new Point(w, brimY + 0.02), true, true);
            g.BezierTo(new Point(w + 0.05, brimY - 0.42), new Point(0.25, brimY - 0.74), new Point(-0.18 + sway * 0.5, brimY - 0.70), true, true);
            g.BezierTo(new Point(-0.50 + sway, brimY - 0.66), new Point(-0.74 + sway * 1.5, brimY - 0.56), new Point(tip.X, tip.Y), true, true);
            g.BezierTo(new Point(tip.X + 0.06, tip.Y + 0.08), new Point(-0.66 + sway, brimY - 0.30), new Point(-0.50, brimY - 0.28), true, true);
            g.BezierTo(new Point(-0.56, brimY - 0.16), new Point(-w - 0.02, brimY - 0.08), new Point(-w, brimY + 0.02), true, true);
        }

        cap.Freeze();
        var pompom = new Point(tip.X - 0.02, tip.Y + 0.12);
        dc.DrawGeometry(null, cloth.CapOutline, cap);
        dc.DrawEllipse(null, cloth.CapOutline, pompom, 0.12, 0.12);
        dc.DrawGeometry(cloth.CapStripes, null, cap);

        // Luz suave desde arriba a la izquierda y sombra hacia la punta.
        dc.PushClip(cap);
        dc.DrawEllipse(cloth.CapLight, null, new Point(0.05, brimY - 0.55), 0.55, 0.32);
        dc.Pop();

        // Ribete (el borde que apoya en la cabeza) y el pompón.
        dc.DrawEllipse(cloth.TrimFill, cloth.CapOutlineThin, new Point(0, brimY + 0.02), w, 0.10);
        dc.DrawEllipse(cloth.Pompom, null, pompom, 0.12, 0.12);
        dc.DrawEllipse(cloth.PompomLight, null, pompom + new Vector(-0.035, -0.04), 0.05, 0.04);

        if (shown < 0.99)
        {
            dc.Pop();
        }

        dc.Pop();
        dc.Pop();
    }

    /// <summary>Recién cepillado: destellos de cuatro puntas que titilan sobre el pelo (y un borde más luminoso).</summary>
    private static void DrawGlints(DrawingContext dc, Geometry silhouette, in KobiiLook c)
    {
        if (c.Shine <= 0.05)
        {
            return;
        }

        dc.PushClip(silhouette);
        dc.DrawGeometry(null, KobiiPainter.Pen(Colors.White.WithAlpha(0.22 * c.Shine), 0.10), silhouette);
        ReadOnlySpan<(double X, double Y, double Phase)> glints = [(-0.46, -1.36, 0.0), (0.52, -0.82, 1.7), (0.18, -1.58, 3.1), (-0.62, -0.62, 4.6)];
        foreach (var (x, y, phase) in glints)
        {
            var twinkle = Math.Clamp(Math.Sin(c.Time * 2.3 + phase) * 1.6 - 0.6, 0, 1);
            if (twinkle <= 0.01)
            {
                continue;
            }

            var size = 0.11 * twinkle;
            var brush = KobiiPainter.Brush(Colors.White.WithAlpha(Math.Clamp(c.Shine * twinkle, 0, 1)));
            var at = new Point(x, y);
            dc.DrawEllipse(brush, null, at, size, size * 0.18);
            dc.DrawEllipse(brush, null, at, size * 0.18, size);
        }

        dc.Pop();
    }

    /// <summary>Los pinceles de la ropa de dormir de un colorido: el color de sus detalles, en tono pastel.</summary>
    private sealed class Clothes
    {
        private static readonly Color TrimColor = Color.FromRgb(250, 247, 242);

        public Clothes(Palette palette)
        {
            Palette = palette;
            var accent = Color.FromRgb((byte)(palette.Accent >> 16), (byte)(palette.Accent >> 8), (byte)palette.Accent);
            var cloth = Lerp(accent, Colors.White, 0.45);
            var light = Lerp(cloth, Colors.White, 0.33);

            // Rayas verticales (se repiten cada 2π/22 unidades, como en el shader).
            var stripes = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(Math.Tau / 22, 0),
                SpreadMethod = GradientSpreadMethod.Repeat,
                GradientStops =
                {
                    new GradientStop(cloth, 0), new GradientStop(cloth, 0.38), new GradientStop(light, 0.5),
                    new GradientStop(light, 0.88), new GradientStop(cloth, 1),
                },
            };
            stripes.Freeze();
            Stripes = stripes;

            // Anillos que dan la vuelta al gorro, algo inclinados.
            var rings = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, Math.Tau / 26),
                SpreadMethod = GradientSpreadMethod.Repeat,
                Transform = new RotateTransform(-13),
                GradientStops =
                {
                    new GradientStop(cloth, 0), new GradientStop(cloth, 0.4), new GradientStop(light, 0.5),
                    new GradientStop(light, 0.9), new GradientStop(cloth, 1),
                },
            };
            rings.Freeze();
            CapStripes = rings;

            var capLight = new RadialGradientBrush(Colors.White.WithAlpha(0.22), Colors.White.WithAlpha(0));
            capLight.Freeze();
            CapLight = capLight;
            var outline = Color.FromRgb((byte)(cloth.R * 0.42), (byte)(cloth.G * 0.42), (byte)(cloth.B * 0.42));
            CapOutline = KobiiPainter.Pen(outline, 0.09);
            CapOutlineThin = KobiiPainter.Pen(outline, 0.045);
            Trim = KobiiPainter.Pen(TrimColor, 0.07);
            TrimFill = KobiiPainter.Brush(TrimColor);
            Button = KobiiPainter.Brush(Color.FromRgb((byte)(accent.R * 0.75), (byte)(accent.G * 0.75), (byte)(accent.B * 0.75)));
            Pompom = KobiiPainter.Brush(Colors.White);
            PompomLight = KobiiPainter.Brush(Colors.White.WithAlpha(0.8));
        }

        public Palette Palette { get; }

        public Brush Stripes { get; }

        public Brush CapStripes { get; }

        public Brush CapLight { get; }

        public Pen CapOutline { get; }

        public Pen CapOutlineThin { get; }

        public Pen Trim { get; }

        public Brush TrimFill { get; }

        public Brush Button { get; }

        public Brush Pompom { get; }

        public Brush PompomLight { get; }
    }
}
