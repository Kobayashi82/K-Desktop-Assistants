using System.Windows;
using System.Windows.Media;
using Lander.Core.Game;
using Expression = Lander.Core.Game.Expression;

namespace Lander.Assistant.Rendering.Skins;

/// <summary>Pixel art de recreativa: una rejilla de 16×16 píxeles gordos (3 unidades cada uno).</summary>
internal sealed class PixelSkin : LanderSkin
{
    private const double Cell = 3;

    // . vacío · W blanco · G gris · D oscuro · Y oro · O ocre · B cristal
    private static readonly string[] Body =
    [
        "........D.......",
        "........D.......",
        ".....WWWWWW.....",
        "....WWWWWWWW....",
        "....WBBBBBBW....",
        "...GWBBBBBBWG...",
        "...GWBBBBBBWG...",
        "....WWWWWWWW....",
        "..YYYYYYYYYYYY..",
        "..YOYYYYYYYYOY..",
        "..YYYYYYYYYYYY..",
        "......DDDD......",
        "......DDDD......",
    ];

    private static readonly string[] LegsDown =
    [
        "...L........L...",
        "..L..........L..",
        "..L..........L..",
        ".L............L.",
        "LL............LL",
    ];

    private static readonly string[] LegsUp =
    [
        "..LL........LL..",
        "..L..........L..",
        ".LL..........LL.",
    ];

    private static readonly Dictionary<char, Brush> Palette = new()
    {
        ['W'] = Gfx.Brush(0xF1F5F9),
        ['G'] = Gfx.Brush(0x94A3B8),
        ['D'] = Gfx.Brush(0x334155),
        ['Y'] = Gfx.Brush(0xFBBF24),
        ['O'] = Gfx.Brush(0xD97706),
        ['B'] = Gfx.Brush(0x0F172A),
        ['L'] = Gfx.Brush(0x64748B),
    };

    private static readonly Brush Eye = Gfx.Brush(0x22D3EE);
    private static readonly Brush EyeDim = Gfx.Brush(0x0E7490);
    private static readonly Brush Lid = Gfx.Brush(0xF1F5F9);
    private static readonly Brush[] FlameColors = [Gfx.Brush(0xFFF7D6), Gfx.Brush(0xFDE047), Gfx.Brush(0xF97316), Gfx.Brush(0xDC2626)];

    public override string Id => "pixel";

    public override Point Nozzle => new(0, 15);

    public override Color[] DebrisColors { get; } = [Gfx.Rgb(0xFBBF24), Gfx.Rgb(0xF1F5F9), Gfx.Rgb(0x334155)];

    protected override void DrawExhaust(DrawingContext dc, in LanderPose pose)
    {
        var strength = Math.Max(pose.MainFlame, pose.BoostFlash / 0.35);
        if (strength < 0.05)
        {
            return;
        }

        var frame = (int)(pose.Time * 14) % 2;
        var rows = 1 + (int)Math.Round(strength * 3) + frame;
        for (var row = 0; row < rows; row++)
        {
            var width = row < 2 ? 4 : 2;
            var startColumn = 8 - width / 2;
            var brush = FlameColors[Math.Min(row, FlameColors.Length - 1)];
            for (var column = startColumn; column < startColumn + width; column++)
            {
                if ((row + column + frame) % 5 == 4 && row > 0)
                {
                    continue;
                }

                DrawCell(dc, brush, column, 13 + row);
            }
        }
    }

    protected override void DrawLegs(DrawingContext dc, in LanderPose pose)
    {
        var legs = pose.LegsDeploy > 0.5 ? LegsDown : LegsUp;
        DrawGrid(dc, legs, 11);
    }

    protected override void DrawBody(DrawingContext dc, in LanderPose pose)
    {
        DrawGrid(dc, Body, 0);

        // Ojo: dos píxeles que se mueven por la ventanilla.
        var column = 7 + (int)Math.Round(Math.Clamp(pose.Look.X, -1, 1) * 1.5);
        var row = 5 + (int)Math.Round(Math.Clamp(pose.Look.Y, -1, 1) * 0.8);
        switch (pose.Expression)
        {
            case Expression.Asleep:
                DrawCell(dc, Lid, 5, 4);
                for (var c = 5; c <= 10; c++)
                {
                    DrawCell(dc, Lid, c, 4);
                    DrawCell(dc, Lid, c, 5);
                }

                DrawCell(dc, EyeDim, 7, 6);
                DrawCell(dc, EyeDim, 8, 6);
                break;
            case Expression.Happy:
                DrawCell(dc, Eye, column - 1, 6);
                DrawCell(dc, Eye, column, 5);
                DrawCell(dc, Eye, column + 1, 5);
                DrawCell(dc, Eye, column + 2, 6);
                break;
            case Expression.Scared:
                DrawCell(dc, Lid, column, row);
                DrawCell(dc, Lid, column + 1, row);
                DrawCell(dc, Eye, column, row + 1);
                DrawCell(dc, Eye, column + 1, row + 1);
                break;
            default:
                if (pose.Blink > 0.5 || pose.Expression == Expression.Tired)
                {
                    for (var c = 5; c <= 10; c++)
                    {
                        DrawCell(dc, Lid, c, 4);
                    }
                }

                if (pose.Blink <= 0.5)
                {
                    DrawCell(dc, Eye, column, row);
                    DrawCell(dc, Eye, column + 1, row);
                }

                break;
        }

        if (pose.Damage > 0.4)
        {
            DrawCell(dc, Gfx.Brush(0x1F1A17, 170), 4, 9);
            DrawCell(dc, Gfx.Brush(0x1F1A17, 170), 10, 2);
        }

        if (pose.CarryingPackage)
        {
            DrawCell(dc, Gfx.Brush(0xC8925A), 4, 1);
            DrawCell(dc, Gfx.Brush(0xC8925A), 5, 1);
            DrawCell(dc, Gfx.Brush(0xF3DDB0), 4, 0);
            DrawCell(dc, Gfx.Brush(0xC8925A), 5, 0);
        }
    }

    protected override void DrawLights(DrawingContext dc, in LanderPose pose)
    {
        var on = pose.Time % 1.2 < 0.6;
        DrawCell(dc, Gfx.Brush(on ? 0xFF4D4DU : 0x7F1D1DU), 2, 9);
        DrawCell(dc, Gfx.Brush(on ? 0x3DFF7AU : 0x14532DU), 13, 9);
        if (pose.Time % 2.2 < 0.3)
        {
            DrawCell(dc, Gfx.Brush(0xFFFFFF), 8, 0);
        }
    }

    private static void DrawGrid(DrawingContext dc, string[] rows, int firstRow)
    {
        for (var row = 0; row < rows.Length; row++)
        {
            var line = rows[row];
            for (var column = 0; column < line.Length; column++)
            {
                if (Palette.TryGetValue(line[column], out var brush))
                {
                    DrawCell(dc, brush, column, firstRow + row);
                }
            }
        }
    }

    /// <summary>Un píxel gordo; se solapa un poco para no dejar costuras al girar.</summary>
    private static void DrawCell(DrawingContext dc, Brush brush, int column, int row) =>
        dc.DrawRectangle(brush, null, new Rect(-24 + column * Cell - 0.05, -24 + row * Cell - 0.05, Cell + 0.1, Cell + 0.1));
}
