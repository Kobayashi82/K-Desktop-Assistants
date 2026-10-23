using System.Windows;
using System.Windows.Media;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;

namespace Kobii.Assistant.Rendering;

/// <summary>Una mariposa en un fotograma: dónde está (interpolada), su aleteo y su color.</summary>
internal readonly record struct ButterflyPose(Vec2 Position, double Flap, double Tilt, double Scale, int Palette, double Opacity, bool Resting)
{
    /// <summary>La mariposa entre el paso anterior (0) y el actual (1).</summary>
    public static ButterflyPose Of(Butterfly butterfly, double alpha) => new(
        Vec2.Lerp(butterfly.PreviousPosition, butterfly.Position, alpha),
        butterfly.PreviousFlap + (butterfly.Flap - butterfly.PreviousFlap) * alpha,
        butterfly.Tilt,
        butterfly.Scale,
        butterfly.Palette,
        butterfly.Opacity,
        butterfly.Mode == ButterflyMode.Perch);

    /// <summary>Lo que ocupa (con sitio para las antenas al inclinarse).</summary>
    public double Box => Butterfly.WingspanDip * 1.7 * Scale;
}

/// <summary>
/// Lo que hay por el escritorio además de Kobii (de Kobii AI, pasado de Direct2D a WPF): la mariposa, la comida y las
/// bebidas (en el suelo o en sus manos, con sus mordiscos) y la pelota que rueda.
/// </summary>
internal static partial class ScenePainter
{
    private const uint ButterflyBody = 0x3B2D27;
    private const uint AppleSkin = 0xE53935;
    private const uint AppleFlesh = 0xFFF1D0;
    private const uint CookieDough = 0xD9A066;
    private const uint CookieCrumb = 0xF2CB8E;
    private const uint BerrySkin = 0x3C4AA0;
    private const uint BerryOutline = 0x1F2557;

    /// <summary>Colores de cada mariposa: ala de delante, ala de detrás, borde, manchas y contorno.</summary>
    private static readonly (uint Fore, uint Hind, uint Edge, uint Spot, uint Outline)[] ButterflyColors =
    [
        (0xFFE45C, 0xFFD54F, 0xF2B51F, 0xFF8A1F, 0x8A5A00), // limonera
        (0xFF9433, 0xF7801E, 0x2E241E, 0xFFFFFF, 0x241A14), // monarca
        (0x6CC4FF, 0x4AA8F0, 0x2364B8, 0xFFFFFF, 0x163F78), // azul
        (0xFFA9CF, 0xF68BBC, 0xE0609A, 0xFFF4F9, 0x93305F), // rosa
        (0xCDB0FF, 0xB592F7, 0x8A63DB, 0xFFE680, 0x52358F), // lila
    ];

    /// <summary>Ala de delante derecha (la mariposa mide 1 de punta a punta; el centro del cuerpo en el origen).</summary>
    private static readonly Geometry Forewing = Frozen("M0.03,-0.04 C0.10,-0.30 0.30,-0.47 0.47,-0.40 C0.54,-0.37 0.53,-0.20 0.45,-0.10 C0.36,0.00 0.15,0.03 0.03,0.01 Z");

    /// <summary>Ala de detrás derecha: un lóbulo redondo por debajo de la de delante.</summary>
    private static readonly Geometry Hindwing = Frozen("M0.03,-0.01 C0.20,-0.06 0.40,0.00 0.39,0.14 C0.38,0.27 0.22,0.34 0.12,0.30 C0.06,0.27 0.03,0.16 0.03,0.06 Z");

    private static readonly Geometry Antennae = Frozen("M0.015,-0.18 Q0.02,-0.32 0.12,-0.37 M-0.015,-0.18 Q-0.02,-0.32 -0.12,-0.37");

    /// <summary>Lo que lleva en la mano o puesto y lo que tiene en las manos (comida o un juguete), por delante del cuerpo.</summary>
    public static void DrawExtras(DrawingContext dc, in PropPose props, in HeldThing held)
    {
        DrawProps(dc, props);
        if (held.Food is { } food)
        {
            DrawFood(dc, new Point(held.Center.X, held.Center.Y), food, held.Bites, held.BiteSide, held.Remaining, held.Scale, held.Rotation, shadow: false);
        }
        else if (held.Toy is { } toy)
        {
            DrawToy(dc, toy, new Point(held.Center.X, held.Center.Y), Toy.BallDiameterDip / 2 * held.Scale, held.Spin, 1, grounded: false);
        }
    }

    /// <summary>Lo que hay por el escritorio (la pelota, la comida, la mariposa), entre el paso anterior (0) y el actual (1).</summary>
    public static void DrawThings(DrawingContext dc, KobiiGame game, double alpha)
    {
        foreach (var toy in game.Playground.Toys)
        {
            if (toy.Held)
            {
                continue;
            }

            var feet = Vec2.Lerp(toy.PreviousFeet, toy.Body.Feet, alpha);
            var spin = toy.PreviousSpin + (toy.Spin - toy.PreviousSpin) * alpha;
            DrawToy(dc, toy.Kind, new Point(feet.X, feet.Y - toy.Radius), toy.Radius, spin, toy.Squash, toy.Body.IsGrounded);
        }

        foreach (var food in game.FoodItems)
        {
            if (!food.Held && !food.Eaten)
            {
                var s = food.Body.Height / 2;
                DrawFood(dc, new Point(food.Body.Feet.X, food.Body.Feet.Y - s), food.Kind, food.Bites, food.BiteSide, food.Remaining, food.Body.Scale, 0, food.Body.IsGrounded);
            }
        }

        if (game.Butterfly is { Visible: true } butterfly)
        {
            DrawButterfly(dc, ButterflyPose.Of(butterfly, alpha));
        }
    }

    // La mariposa --------------------------------------------------------------------------------------

    /// <summary>
    /// Una mariposa vista desde arriba, con las alas de delante y de detrás de cada lado. Al aletear, las alas se estrechan
    /// hasta verse de canto y vuelven a abrirse (las de detrás, un poco después); posada, las abre y las cierra despacio.
    /// </summary>
    public static void DrawButterfly(DrawingContext dc, in ButterflyPose pose)
    {
        var alpha = Math.Clamp(pose.Opacity, 0, 1);
        if (alpha <= 0.001)
        {
            return;
        }

        var span = Butterfly.WingspanDip * pose.Scale;
        var colors = ButterflyColors[Math.Clamp(pose.Palette, 0, ButterflyColors.Length - 1)];
        var place = new Matrix();
        place.Scale(span, span);
        place.Rotate(Deg(pose.Tilt));
        place.Translate(pose.Position.X, pose.Position.Y);
        var outline = Math.Max(1.1 / span, 0.045);

        var resting = pose.Resting;
        double Open(double phase) => resting
            ? 0.42 + 0.58 * (0.5 + 0.5 * Math.Cos(phase))
            : 0.08 + 0.92 * Math.Pow(0.5 + 0.5 * Math.Cos(phase), 0.8);

        var fore = Open(pose.Flap);
        var hind = Open(pose.Flap - 0.45);
        dc.PushTransform(new MatrixTransform(place));
        if (alpha < 1)
        {
            dc.PushOpacity(alpha);
        }

        foreach (var side in new[] { -1.0, 1.0 })
        {
            DrawWing(dc, Hindwing, side * hind, colors.Hind, colors, outline, front: false);
        }

        foreach (var side in new[] { -1.0, 1.0 })
        {
            DrawWing(dc, Forewing, side * fore, colors.Fore, colors, outline, front: true);
        }

        // Cuerpo, cabeza y antenas con una bolita en la punta.
        var body = Fill(ButterflyBody);
        dc.DrawGeometry(null, Stroke(ButterflyBody, outline * 0.9), Antennae);
        dc.DrawEllipse(body, null, new Point(0.12, -0.37), 0.026, 0.026);
        dc.DrawEllipse(body, null, new Point(-0.12, -0.37), 0.026, 0.026);
        dc.DrawEllipse(body, null, new Point(0, 0.05), 0.042, 0.17);
        dc.DrawEllipse(body, null, new Point(0, -0.15), 0.05, 0.05);
        dc.DrawEllipse(Fill(0xFFFFFF, 0.35), null, new Point(-0.012, -0.02), 0.012, 0.07);
        if (alpha < 1)
        {
            dc.Pop();
        }

        dc.Pop();
    }

    /// <summary>Un ala: el color del borde, encima el del ala algo más pequeño (queda una franja), las manchas y el contorno.</summary>
    private static void DrawWing(DrawingContext dc, Geometry wing, double open, uint color, (uint Fore, uint Hind, uint Edge, uint Spot, uint Outline) colors, double outline, bool front)
    {
        var root = front ? new Point(0.05, -0.06) : new Point(0.05, 0.04);
        dc.PushTransform(new ScaleTransform(open, 1));
        dc.DrawGeometry(Fill(colors.Edge), null, wing);
        dc.PushTransform(new ScaleTransform(0.8, 0.8, root.X, root.Y));
        dc.DrawGeometry(Fill(color), null, wing);
        dc.Pop();
        if (front)
        {
            dc.DrawEllipse(Fill(colors.Spot), null, new Point(0.27, -0.25), 0.045, 0.045);
            if (colors.Edge == ButterflyColors[1].Edge)
            {
                // La monarca: puntos blancos a lo largo del borde negro.
                foreach (var dot in new[] { new Point(0.45, -0.33), new Point(0.47, -0.24), new Point(0.43, -0.15) })
                {
                    dc.DrawEllipse(Fill(0xFFFFFF), null, dot, 0.017, 0.017);
                }
            }

            dc.DrawEllipse(Fill(0xFFFFFF, 0.3), null, new Point(0.17, -0.2), 0.07, 0.035);
        }
        else
        {
            dc.DrawEllipse(Fill(colors.Spot, 0.9), null, new Point(0.24, 0.17), 0.034, 0.034);
        }

        dc.DrawGeometry(null, Stroke(colors.Outline, outline), wing);
        dc.Pop();
    }

    // La comida ----------------------------------------------------------------------------------------

    /// <summary>
    /// Comida o bebida con su centro en <paramref name="center"/>: al morderla le faltan trozos (y se ve lo de dentro), las
    /// bayas desaparecen de una en una y, al beber, el vaso se inclina pero el líquido se queda horizontal.
    /// </summary>
    /// <param name="rotation">Inclinación (al beber, y el giro del cuerpo si la lleva en la mano).</param>
    /// <param name="shadow">Está en el suelo (se dibuja su sombra).</param>
    public static void DrawFood(DrawingContext dc, Point center, FoodKind kind, int bites, double biteSide, double remaining, double scale, double rotation, bool shadow)
    {
        var s = scale * FoodItem.SizeDip / 2;
        if (shadow)
        {
            dc.DrawEllipse(Fill(0x000000, 0.18), null, center + new Vector(0, s), s * 0.9, s * 0.22);
        }

        if (kind.IsDrink())
        {
            DrawGlass(dc, center, s, kind, remaining, rotation);
            return;
        }

        dc.PushTransform(new RotateTransform(Deg(rotation), center.X, center.Y));
        switch (kind)
        {
            case FoodKind.Apple:
                DrawBitten(dc, center, s, 0.92, AppleSkin, AppleFlesh, bites, biteSide);
                dc.DrawEllipse(Fill(0xFFFFFF, 0.45), null, center - new Vector(s * 0.35, s * 0.35), s * 0.28, s * 0.2);
                dc.DrawLine(Stroke(0x6D4C41, s * 0.18), center - new Vector(0, s * 0.8), center - new Vector(-s * 0.15, s * 1.3));
                dc.DrawEllipse(Fill(0x66BB6A), null, center - new Vector(-s * 0.45, s * 1.15), s * 0.38, s * 0.18);
                break;

            case FoodKind.Berries:
            {
                // Se las come de una en una.
                var berries = new[] { (0.0, -0.45), (0.5 * biteSide, 0.2), (-0.5 * biteSide, 0.2) };
                for (var i = bites; i < berries.Length; i++)
                {
                    var c = center + new Vector(berries[i].Item1 * s, berries[i].Item2 * s);
                    dc.DrawEllipse(Fill(BerrySkin), Stroke(BerryOutline, Math.Max(0.8, s * 0.08)), c, s * 0.55, s * 0.55);
                    dc.DrawEllipse(Fill(0xFFFFFF, 0.5), null, c - new Vector(s * 0.18, s * 0.18), s * 0.15, s * 0.15);
                }

                break;
            }

            default:
                DrawBitten(dc, center, s, 0.95, CookieDough, CookieCrumb, bites, biteSide);
                foreach (var (dx, dy) in new[] { (-0.4, -0.3), (0.35, -0.1), (-0.1, 0.4), (0.45, 0.45) })
                {
                    var chip = center + new Vector(dx * s, dy * s);
                    if (bites == 0 || (chip - BitePoint(center, s, biteSide, 0)).Length > s * 0.5)
                    {
                        dc.DrawEllipse(Fill(0x5D4037), null, chip, s * 0.15, s * 0.15);
                    }
                }

                break;
        }

        dc.Pop();
    }

    /// <summary>Dónde va el mordisco número <paramref name="i"/> (por el lado de la boca, de arriba abajo).</summary>
    private static Point BitePoint(Point center, double s, double side, int i) => center + new Vector(side * s * 0.95, (-0.40 + 0.48 * i) * s);

    /// <summary>Una fruta o galleta con mordiscos: le faltan trozos y en el borde se ve lo de dentro.</summary>
    private static void DrawBitten(DrawingContext dc, Point center, double s, double squash, uint skin, uint flesh, int bites, double side)
    {
        var whole = new EllipseGeometry(center, s, s * squash);
        if (bites <= 0)
        {
            dc.DrawGeometry(Fill(skin), null, whole);
            return;
        }

        var cuts = new GeometryGroup { FillRule = FillRule.Nonzero };
        var rims = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (var i = 0; i < bites; i++)
        {
            cuts.Children.Add(new EllipseGeometry(BitePoint(center, s, side, i), s * 0.42, s * 0.42));
            rims.Children.Add(new EllipseGeometry(BitePoint(center, s, side, i), s * 0.58, s * 0.58));
        }

        var outer = new CombinedGeometry(GeometryCombineMode.Exclude, whole, cuts);
        var edge = new CombinedGeometry(GeometryCombineMode.Intersect, outer, rims);
        outer.Freeze();
        edge.Freeze();
        dc.DrawGeometry(Fill(skin), null, outer);
        dc.DrawGeometry(Fill(flesh), null, edge);
    }

    /// <summary>Un vaso con agua, leche o zumo. Inclinado, el líquido se queda horizontal (y se va hacia la boca).</summary>
    private static void DrawGlass(DrawingContext dc, Point center, double s, FoodKind kind, double remaining, double rotation)
    {
        var liquid = kind switch
        {
            FoodKind.Milk => 0xFAFAFAu,
            FoodKind.Juice => 0xFFA726u,
            _ => 0x64B5F6u,
        };

        var turn = new RotateTransform(Deg(rotation), center.X, center.Y);
        var glass = new Rect(center.X - s * 0.72, center.Y - s * 1.15, s * 1.44, s * 2.15);
        var radius = s * 0.22;
        var inner = new Rect(glass.Left + s * 0.12, glass.Top + s * 0.25, glass.Width - s * 0.24, glass.Height - s * 0.35);

        dc.PushTransform(turn);
        dc.DrawRoundedRectangle(Fill(0xE3F2FD, 0.55), null, glass, radius, radius);
        dc.Pop();

        if (remaining > 0.02)
        {
            // El líquido: lo que queda del interior del vaso (ya girado) por debajo del nivel, que se queda horizontal.
            var shape = new RectangleGeometry(inner, radius * 0.6, radius * 0.6, turn);
            var bounds = shape.Bounds;
            var level = bounds.Bottom - bounds.Height * Math.Pow(Math.Clamp(remaining, 0, 1), 0.85);
            var below = new RectangleGeometry(new Rect(bounds.Left - s, level, bounds.Width + 2 * s, bounds.Bottom - level + s));
            var fill = new CombinedGeometry(GeometryCombineMode.Intersect, shape, below);
            fill.Freeze();
            dc.DrawGeometry(Fill(liquid, 0.92), null, fill);
        }

        dc.PushTransform(turn);
        dc.DrawRoundedRectangle(null, Stroke(0x90A4AE, s * 0.12), glass, radius, radius);
        dc.DrawLine(Stroke(0xFFFFFF, s * 0.12), new Point(glass.Left + s * 0.3, glass.Top + s * 0.35), new Point(glass.Left + s * 0.3, glass.Bottom - s * 0.4));
        if (kind == FoodKind.Juice)
        {
            // La pajita.
            dc.DrawLine(Stroke(0xEF5350, s * 0.18), new Point(center.X + s * 0.15, glass.Top + s * 0.4), new Point(center.X + s * 0.6, glass.Top - s * 0.6));
        }

        dc.Pop();
    }

    // Los juguetes sueltos -----------------------------------------------------------------------------

    /// <summary>Un juguete en el suelo o por el aire (aplastado al botar), con su sombra si está en el suelo.</summary>
    public static void DrawToy(DrawingContext dc, ToyKind kind, Point center, double radius, double spin, double squash, bool grounded, double alpha = 1)
    {
        if (grounded)
        {
            dc.DrawEllipse(Fill(0x000000, 0.16 * alpha), null, center + new Vector(0, radius), radius * 0.85, radius * 0.22);
        }

        var stretch = Math.Clamp(squash, 0.6, 1.4);
        dc.PushTransform(new ScaleTransform(1 / Math.Sqrt(stretch), stretch, center.X, center.Y + radius));
        switch (kind)
        {
            default:
                DrawBallAt(dc, center, radius, spin, alpha);
                break;
        }

        dc.Pop();
    }
}
