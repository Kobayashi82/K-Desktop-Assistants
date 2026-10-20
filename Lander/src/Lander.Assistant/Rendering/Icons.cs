using System.Windows;
using System.Windows.Media;
using Lander.Core.Game;

namespace Lander.Assistant.Rendering;

/// <summary>
/// Iconos vectoriales de los bocadillos, dibujados en una caja de 16×16 centrada en el origen.
/// </summary>
internal static class Icons
{
    private static readonly Geometry ArrowShape = Gfx.RoundedPolygon(0.7, -6, -1.9, 1.2, -1.9, 1.2, -5.2, 7, 0, 1.2, 5.2, 1.2, 1.9, -6, 1.9);
    private static readonly Geometry Heart = Path("M 0,6.2 C -8.5,0.5 -6.5,-7.5 0,-3.2 C 6.5,-7.5 8.5,0.5 0,6.2 Z");
    private static readonly Geometry Star = StarShape(7.2, 3.1);
    private static readonly Geometry House = Gfx.RoundedPolygon(0.6, -7, 0.5, 0, -6.5, 7, 0.5, 5, 0.5, 5, 6.5, -5, 6.5, -5, 0.5);
    private static readonly Geometry Diamond = Gfx.Polygon(0, -7.5, 5.8, -1.8, 0, 7.5, -5.8, -1.8);
    private static readonly Geometry TrophyCup = Path("M -5,-6 L 5,-6 L 4.4,-1 C 4,2 2,3.2 0,3.2 C -2,3.2 -4,2 -4.4,-1 Z");
    private static readonly Geometry QuestionHook = Path("M -3.4,-2.6 C -3.4,-7 3.6,-7 3.6,-2.8 C 3.6,0.2 0,0.4 0,3");
    private static readonly Geometry ZShape = Gfx.Polygon(-3, -3, 3, -3, 3, -1.8, -1, 1.8, 3, 1.8, 3, 3, -3, 3, -3, 1.8, 1, -1.8, -3, -1.8);
    private static readonly Geometry WrenchShape = CreateWrench();
    private static readonly Geometry BellLeft = Path("M -6.6,-2.6 C -7.4,-5.4 -5.4,-7.4 -2.6,-6.6 Z");
    private static readonly Geometry BellRight = Path("M 6.6,-2.6 C 7.4,-5.4 5.4,-7.4 2.6,-6.6 Z");
    private static readonly Geometry Hand = Path(
        "M -3.4,6.5 L -3.6,1.2 C -4.6,0.3 -5.9,-0.9 -5.7,-1.9 C -5.5,-2.8 -4.4,-2.6 -3.7,-1.8 L -3.1,-1.1 " +
        "L -3.1,-5.2 C -3.1,-6.3 -1.6,-6.3 -1.6,-5.2 L -1.6,-1.6 L -1.6,-6.6 C -1.6,-7.7 -0.1,-7.7 -0.1,-6.6 L -0.1,-1.6 " +
        "L 0.2,-6.1 C 0.2,-7.2 1.7,-7.2 1.7,-6.1 L 1.7,-1.4 L 2,-4.6 C 2,-5.6 3.5,-5.6 3.5,-4.6 L 3.5,2.6 C 3.5,4.6 2.6,5.6 2.2,6.5 Z");

    private static readonly Color Red = Gfx.Rgb(0xE5484D);
    private static readonly Color Gold = Gfx.Rgb(0xF5B301);

    public static Color Accent(bool dark) => dark ? Gfx.Rgb(0x60CDFF) : Gfx.Rgb(0x0F6CBD);

    public static void Draw(DrawingContext dc, BubbleIcon icon, double direction, ImageSource? appIcon, double time, bool dark)
    {
        var ink = dark ? Gfx.Rgb(0xE6E6E6) : Gfx.Rgb(0x2B2F36);
        switch (icon)
        {
            case BubbleIcon.Arrow:
                dc.PushTransform(new RotateTransform(direction * 180 / Math.PI));
                dc.DrawGeometry(Gfx.Brush(Accent(dark)), null, ArrowShape);
                dc.Pop();
                break;

            case BubbleIcon.Sleep:
            {
                var bob = Math.Sin(time * 2) * 0.8;
                DrawZ(dc, -2.5, 2.5 + bob, 1, ink);
                DrawZ(dc, 3, -2 + bob * 0.6, 0.7, ink.WithAlpha(0.8));
                DrawZ(dc, 6, -5.5 + bob * 0.3, 0.45, ink.WithAlpha(0.6));
                break;
            }

            case BubbleIcon.Exclamation:
                dc.DrawRoundedRectangle(Gfx.Brush(Red), null, new Rect(-1.6, -7, 3.2, 9), 1.6, 1.6);
                dc.DrawEllipse(Gfx.Brush(Red), null, new Point(0, 5.2), 1.8, 1.8);
                break;

            case BubbleIcon.Question:
                dc.DrawGeometry(null, Gfx.Pen(Accent(dark), 2.3), QuestionHook);
                dc.DrawEllipse(Gfx.Brush(Accent(dark)), null, new Point(0, 6), 1.5, 1.5);
                break;

            case BubbleIcon.Heart:
                dc.DrawGeometry(Gfx.Brush(Red), null, Heart);
                dc.DrawEllipse(Gfx.Brush(Colors.White.WithAlpha(0.45)), null, new Point(-3, -2.2), 1.3, 0.9);
                break;

            case BubbleIcon.Star:
                dc.PushTransform(new RotateTransform(Math.Sin(time * 3) * 8));
                dc.DrawGeometry(Gfx.Brush(Gold), Gfx.Pen(0xC98A00, 0.6), Star);
                dc.Pop();
                break;

            case BubbleIcon.Fuel:
                DrawFuel(dc);
                break;

            case BubbleIcon.Battery:
                dc.DrawRoundedRectangle(null, Gfx.Pen(ink, 1.4), new Rect(-7, -3.8, 12.5, 7.6), 1.5, 1.5);
                dc.DrawRectangle(Gfx.Brush(ink), null, new Rect(5.6, -1.6, 1.6, 3.2));
                var blink = (time % 1) < 0.6 ? 1 : 0.35;
                dc.DrawRoundedRectangle(Gfx.Brush(Red.WithAlpha(blink)), null, new Rect(-5.6, -2.4, 3, 4.8), 0.6, 0.6);
                break;

            case BubbleIcon.Home:
                dc.DrawGeometry(Gfx.Brush(Accent(dark)), null, House);
                dc.DrawRoundedRectangle(Gfx.Brush(dark ? Gfx.Rgb(0x2C2C2C) : Colors.White), null, new Rect(-1.5, 2, 3, 4.5), 0.5, 0.5);
                break;

            case BubbleIcon.Wrench:
                DrawWrench(dc, ink);
                break;

            case BubbleIcon.Crystal:
                DrawCrystal(dc, 1);
                break;

            case BubbleIcon.Package:
                DrawPackage(dc);
                break;

            case BubbleIcon.RoundTrip:
                DrawRoundTrip(dc, Accent(dark));
                break;

            case BubbleIcon.App:
                if (appIcon is not null)
                {
                    dc.DrawImage(appIcon, new Rect(-7.5, -7.5, 15, 15));
                }
                else
                {
                    dc.DrawRoundedRectangle(Gfx.Brush(Accent(dark)), null, new Rect(-6.5, -6.5, 13, 13), 2.5, 2.5);
                    dc.DrawRectangle(Gfx.Brush(Colors.White.WithAlpha(0.85)), null, new Rect(-4.5, -3, 9, 6.5));
                }

                break;

            case BubbleIcon.Trophy:
                DrawTrophy(dc);
                break;

            case BubbleIcon.Flag:
                DrawFlag(dc, ink, time);
                break;

            case BubbleIcon.MedalGold:
                DrawMedal(dc, Gfx.Rgb(0xFFE07A), Gfx.Rgb(0xE0A100), Gfx.Rgb(0xA87400));
                break;

            case BubbleIcon.MedalSilver:
                DrawMedal(dc, Gfx.Rgb(0xF1F5F9), Gfx.Rgb(0xA3AEBD), Gfx.Rgb(0x6B7685));
                break;

            case BubbleIcon.MedalBronze:
                DrawMedal(dc, Gfx.Rgb(0xF2B37A), Gfx.Rgb(0xC0703A), Gfx.Rgb(0x83461E));
                break;

            case BubbleIcon.Clock:
                DrawAlarmClock(dc, Accent(dark), dark, time);
                break;

            case BubbleIcon.Hello:
                DrawWavingHand(dc, time);
                break;
        }
    }

    /// <summary>Despertador con campanas que tiembla como si sonara.</summary>
    private static void DrawAlarmClock(DrawingContext dc, Color accent, bool dark, double time)
    {
        var ring = Math.Sin(time * 38) * 7 * (0.6 + 0.4 * Math.Sin(time * 3));
        dc.PushTransform(new RotateTransform(ring, 0, 1));

        // Patas y campanas
        var metal = Gfx.Pen(Gfx.Lerp(accent, Colors.Black, 0.25), 1.3);
        dc.DrawLine(metal, new Point(-3.6, 5.2), new Point(-5, 7));
        dc.DrawLine(metal, new Point(3.6, 5.2), new Point(5, 7));
        dc.DrawGeometry(Gfx.Brush(accent), null, BellLeft);
        dc.DrawGeometry(Gfx.Brush(accent), null, BellRight);
        dc.DrawRoundedRectangle(Gfx.Brush(Gfx.Lerp(accent, Colors.Black, 0.25)), null, new Rect(-0.8, -7.4, 1.6, 1.6), 0.5, 0.5);

        // Esfera
        var face = dark ? Gfx.Rgb(0x2C2C2C) : Colors.White;
        var center = new Point(0, 1);
        dc.DrawEllipse(Gfx.Brush(face), Gfx.Pen(accent, 1.5), center, 5.6, 5.6);
        var tick = Gfx.Pen(Gfx.Lerp(accent, face, 0.35), 0.7);
        for (var i = 0; i < 4; i++)
        {
            var angle = i * Math.PI / 2;
            var direction = new Vector(Math.Sin(angle), -Math.Cos(angle));
            dc.DrawLine(tick, center + direction * 3.9, center + direction * 4.7);
        }

        // Las doce en punto: las dos agujas arriba, la de los minutos más larga.
        var ink = dark ? Gfx.Rgb(0xE6E6E6) : Gfx.Rgb(0x2B2F36);
        dc.DrawLine(Gfx.Pen(ink, 1.3), center, new Point(0, center.Y - 2.6));
        dc.DrawLine(Gfx.Pen(ink, 0.9), center, new Point(0, center.Y - 4));
        dc.DrawEllipse(Gfx.Brush(Red), null, center, 0.9, 0.9);
        dc.Pop();
    }

    /// <summary>Mano que saluda, balanceándose desde la muñeca.</summary>
    private static void DrawWavingHand(DrawingContext dc, double time)
    {
        var wave = Math.Sin(time * 9) * 16;
        dc.PushTransform(new RotateTransform(wave - 10, 0, 6.5));
        var skin = Gfx.Vertical((0, Gfx.Rgb(0xFFD866)), (1, Gfx.Rgb(0xF5B301)));
        var outline = Gfx.Pen(0xB97A00, 0.6);
        dc.DrawGeometry(skin, outline, Hand);
        dc.DrawLine(Gfx.Pen(0xB97A00, 0.45, 160), new Point(-1.2, -1.2), new Point(-1.2, 0.6));
        dc.DrawLine(Gfx.Pen(0xB97A00, 0.45, 160), new Point(0.7, -1.2), new Point(0.7, 0.6));
        dc.DrawLine(Gfx.Pen(0xB97A00, 0.45, 160), new Point(2.5, -0.8), new Point(2.5, 0.8));
        dc.Pop();

        // Rayitas de movimiento a los lados.
        var swing = Math.Abs(Math.Sin(time * 9));
        var lines = Gfx.Pen(Gfx.Rgb(0xF5B301).WithAlpha(0.35 + 0.5 * swing), 0.8);
        dc.DrawLine(lines, new Point(-7.2, -4.5), new Point(-6.2, -2.8));
        dc.DrawLine(lines, new Point(7.2, -4.5), new Point(6.2, -2.8));
    }

    /// <summary>Bandera a cuadros que ondea un poco.</summary>
    public static void DrawFlag(DrawingContext dc, Color pole, double time)
    {
        dc.DrawRoundedRectangle(Gfx.Brush(pole), null, new Rect(-5.6, -7, 1.4, 14), 0.7, 0.7);

        const int columns = 4;
        const int rows = 3;
        const double left = -4.2;
        const double top = -6.6;
        const double cell = 2.6;
        for (var column = 0; column < columns; column++)
        {
            // La tela se ondula: cada columna baja o sube un poco según la fase.
            var wave = Math.Sin(time * 4 - column * 0.9) * 0.55 * (column / (double)columns);
            for (var row = 0; row < rows; row++)
            {
                var dark = (row + column) % 2 == 0;
                var rect = new Rect(left + column * cell, top + row * cell + wave, cell + 0.05, cell + 0.05);
                dc.DrawRectangle(Gfx.Brush(dark ? Gfx.Rgb(0x1F2328) : Colors.White), null, rect);
            }
        }

        dc.DrawRectangle(null, Gfx.Pen(Gfx.Rgb(0x1F2328), 0.5), new Rect(left, top, columns * cell, rows * cell));
    }

    private static void DrawMedal(DrawingContext dc, Color light, Color mid, Color edge)
    {
        // Cinta
        dc.DrawGeometry(Gfx.Brush(0xE5484D), null, Gfx.Polygon(-4.6, -7.6, -1.4, -7.6, 1.2, -1.6, -2, -1.6));
        dc.DrawGeometry(Gfx.Brush(0x3B82F6), null, Gfx.Polygon(4.6, -7.6, 1.4, -7.6, -1.2, -1.6, 2, -1.6));

        // Disco
        var center = new Point(0, 2.4);
        dc.DrawEllipse(Gfx.Gradient(new Point(0.2, 0), new Point(0.8, 1), (0, light), (1, mid)), Gfx.Pen(edge, 0.7), center, 5.2, 5.2);
        dc.DrawEllipse(null, Gfx.Pen(edge.WithAlpha(0.6), 0.5), center, 3.6, 3.6);
        dc.PushTransform(new MatrixTransform(0.42, 0, 0, 0.42, center.X, center.Y));
        dc.DrawGeometry(Gfx.Brush(edge.WithAlpha(0.75)), null, Star);
        dc.Pop();
        ParticleSystem.DrawSparkle(dc, new Point(-2.4, 0.2), 1.5, Colors.White.WithAlpha(0.85));
    }

    public static void DrawCrystal(DrawingContext dc, double alpha)
    {
        var fill = Gfx.Gradient(new Point(0.2, 0), new Point(0.8, 1),
            (0, Gfx.Rgb(0xC9FFFB).WithAlpha(alpha)), (0.5, Gfx.Rgb(0x34D3E0).WithAlpha(alpha)), (1, Gfx.Rgb(0x0E7C9A).WithAlpha(alpha)));
        dc.DrawGeometry(fill, Gfx.Pen(Gfx.Rgb(0x0B6A85).WithAlpha(alpha), 0.6), Diamond);
        var facet = Gfx.Pen(Colors.White.WithAlpha(0.55 * alpha), 0.5);
        dc.DrawLine(facet, new Point(-5.8, -1.8), new Point(5.8, -1.8));
        dc.DrawLine(facet, new Point(-2.4, -1.8), new Point(0, 7.5));
        dc.DrawLine(facet, new Point(2.4, -1.8), new Point(0, 7.5));
        dc.DrawLine(facet, new Point(-2.4, -1.8), new Point(0, -7.5));
        dc.DrawLine(facet, new Point(2.4, -1.8), new Point(0, -7.5));
    }

    public static void DrawPackage(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Gfx.Vertical((0, Gfx.Rgb(0xD8A46A)), (1, Gfx.Rgb(0xB57B42))), Gfx.Pen(0x6B4423, 0.7), new Rect(-6.5, -5, 13, 11), 1.2, 1.2);
        dc.DrawLine(Gfx.Pen(0x8A5A2B, 0.7), new Point(-6.5, -1.5), new Point(6.5, -1.5));
        dc.DrawRectangle(Gfx.Brush(0xF3DDB0), null, new Rect(-1.2, -5, 2.4, 11));
    }

    private static void DrawZ(DrawingContext dc, double x, double y, double scale, Color color)
    {
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, x, y));
        dc.DrawGeometry(Gfx.Brush(color), null, ZShape);
        dc.Pop();
    }

    private static void DrawFuel(DrawingContext dc)
    {
        var body = Gfx.RoundedPolygon(1.2, -5.5, -3.5, -2.5, -6.5, 5.5, -6.5, 5.5, 7, -5.5, 7);
        dc.DrawGeometry(Gfx.Vertical((0, Gfx.Rgb(0xF97316)), (1, Gfx.Rgb(0xC2410C))), Gfx.Pen(0x7C2D12, 0.6), body);
        dc.DrawRoundedRectangle(Gfx.Brush(0x7C2D12), null, new Rect(1, -8.2, 3.4, 2.2), 0.6, 0.6);
        var emboss = Gfx.Pen(0xFFFFFF, 0.8, 150);
        dc.DrawLine(emboss, new Point(-3, -1.5), new Point(3, 4.5));
        dc.DrawLine(emboss, new Point(3, -1.5), new Point(-3, 4.5));
    }

    private static void DrawWrench(DrawingContext dc, Color ink)
    {
        dc.PushTransform(new RotateTransform(45));
        dc.DrawGeometry(Gfx.Brush(ink), null, WrenchShape);
        dc.Pop();
    }

    private static PathGeometry CreateWrench()
    {
        var head = new EllipseGeometry(new Point(0, -4.2), 4.2, 4.2);
        var handle = new RectangleGeometry(new Rect(-1.6, -2, 3.2, 10.5), 1.6, 1.6);
        var jaw = new RectangleGeometry(new Rect(-1.5, -9.5, 3, 5.2));
        var body = new CombinedGeometry(GeometryCombineMode.Union, head, handle);
        var wrench = new CombinedGeometry(GeometryCombineMode.Exclude, body, jaw).GetFlattenedPathGeometry();
        wrench.Freeze();
        return wrench;
    }

    private static void DrawRoundTrip(DrawingContext dc, Color color)
    {
        var pen = Gfx.Pen(color, 1.9);
        var arc = Path("M 5.4,-2.2 A 5.6,5.6 0 1 0 5.2,3.2");
        dc.DrawGeometry(null, pen, arc);
        dc.DrawGeometry(Gfx.Brush(color), null, Gfx.Polygon(7.6, -5.4, 7.2, 0.2, 2.4, -1.6));
    }

    private static void DrawTrophy(DrawingContext dc)
    {
        var gold = Gfx.Vertical((0, Gfx.Rgb(0xFFE07A)), (1, Gfx.Rgb(0xE0A100)));
        var pen = Gfx.Pen(0xA87400, 0.6);
        dc.DrawEllipse(null, Gfx.Pen(0xE0A100, 1.3), new Point(-5, -3), 2.4, 2.4);
        dc.DrawEllipse(null, Gfx.Pen(0xE0A100, 1.3), new Point(5, -3), 2.4, 2.4);
        dc.DrawGeometry(gold, pen, TrophyCup);
        dc.DrawRectangle(gold, null, new Rect(-1, 3, 2, 2.5));
        dc.DrawRoundedRectangle(gold, pen, new Rect(-4.2, 5.2, 8.4, 2.2), 0.8, 0.8);
        ParticleSystem.DrawSparkle(dc, new Point(-2.2, -3.4), 1.6, Colors.White.WithAlpha(0.8));
    }

    private static Geometry Path(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry StarShape(double outer, double inner)
    {
        var coordinates = new double[20];
        for (var i = 0; i < 10; i++)
        {
            var angle = -Math.PI / 2 + i * Math.PI / 5;
            var radius = i % 2 == 0 ? outer : inner;
            coordinates[i * 2] = Math.Cos(angle) * radius;
            coordinates[i * 2 + 1] = Math.Sin(angle) * radius + 0.6;
        }

        return Gfx.RoundedPolygon(0.4, coordinates);
    }
}
