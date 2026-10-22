using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kobii.Core;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using Expression = Kobii.Core.Creature.Expression;
using Kobii.Core.Terrain;

namespace Kobii.Assistant.Rendering;

/// <summary>Una escena de la hoja: lo que hace y cuánto tiempo se deja correr antes de la foto.</summary>
internal sealed record SheetScene(string Name, Action<KobiiGame> Setup, double Seconds, Expression? Face = null, string? Color = null);

/// <summary>
/// Hojas con el dibujo vectorial de Kobii (el mismo que en el escritorio) para revisar de un vistazo colores, caras y
/// posturas sin K-Desktop. Se dibujan con WPF en memoria.
/// </summary>
internal static class VectorSheet
{
    private static readonly Typeface Title = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface Label = new("Segoe UI");

    /// <summary>Varias filas de escenas con un título encima de cada grupo.</summary>
    public static BitmapSource Sheet(IReadOnlyList<(string Title, IReadOnlyList<SheetScene> Scenes)> groups, int cell = 190, int columns = 6, double creature = 0.44, bool dark = false)
    {
        const int header = 34;
        const int caption = 22;
        var height = 16 + groups.Sum(g => header + (g.Scenes.Count + columns - 1) / columns * (cell + caption));
        var width = columns * cell;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = dark ? Color.FromRgb(0x20, 0x22, 0x27) : Color.FromRgb(0xF6, 0xF5, 0xF2);
            var ink = dark ? Color.FromRgb(0xE6, 0xE6, 0xE6) : Color.FromRgb(0x44, 0x44, 0x48);
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
            var y = 16.0;
            foreach (var (title, scenes) in groups)
            {
                dc.DrawText(Text(title, Title, 17, ink), new Point(14, y + 4));
                y += header;
                for (var i = 0; i < scenes.Count; i++)
                {
                    var x = i % columns * cell;
                    var top = y + i / columns * (cell + caption);
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(dark ? (byte)18 : (byte)10, 0, 0, 0)), null, new Rect(x + 4, top + 2, cell - 8, cell + caption - 6));
                    dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(40, ink.R, ink.G, ink.B)), 1), new Point(x + 16, top + cell * 0.80), new Point(x + cell - 16, top + cell * 0.80));
                    Draw(dc, scenes[i], new Vec2(x + cell / 2.0, top + cell * 0.80), cell * creature, dark);
                    var label = Text(scenes[i].Name, Label, 13, Color.FromArgb(200, ink.R, ink.G, ink.B));
                    dc.DrawText(label, new Point(x + (cell - label.Width) / 2, top + cell - 4));
                }

                y += (scenes.Count + columns - 1) / columns * (cell + caption);
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Kobii solo, centrado en un cuadrado (icono, galería de colores).</summary>
    public static BitmapSource Portrait(int size, string color, Expression face = Expression.Happy, double fill = 0.62)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            Draw(dc, new SheetScene("", _ => { }, 0.5, face, color), new Vec2(size / 2.0, size * 0.90), size * fill, dark: false, look: 0.3);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Draw(DrawingContext dc, SheetScene scene, Vec2 feet, double heightPx, bool dark, double? look = null)
    {
        var (game, world) = Stage(heightPx / Species.HeightDip, scene.Setup, scene.Seconds);
        var frame = new KobiiFrame { Palette = Species.Palette(scene.Color), Dark = dark };
        var body = game.Body;
        var ground = body.IsGrounded ? body.Feet.Y : world.PlatformBelow(body.Feet.X, body.Feet.Y)?.Y ?? body.Feet.Y;
        var at = feet + new Vec2(0, body.Feet.Y - ground);
        frame.Update(game, at, game.Time, 0, feet.Y, 1, 1);
        if (scene.Face is { } face)
        {
            frame.Look.Expression = (double)face;
        }

        if (look is { } x)
        {
            frame.Look.LookX = x;
            frame.Look.LookY = 0;
        }

        new KobiiPainter().Draw(dc, frame);
    }

    /// <summary>Un escritorio de un monitor y Kobii en su suelo, tras <paramref name="seconds"/> de simulación.</summary>
    private static (KobiiGame Game, World World) Stage(double size, Action<KobiiGame> setup, double seconds)
    {
        var monitor = new MonitorInfo(0, new Core.Geometry.PixelRect(0, 0, 4000, 3000), new Core.Geometry.PixelRect(0, 0, 4000, 3000), 1, true);
        var world = TerrainBuilder.Build([monitor], [], new TerrainOptions { CreatureHeightDip = Species.HeightDip * size });
        var game = new KobiiGame(new GameOptions { Size = size, Liveliness = 0.0001, FollowCursor = false }, new GameRandom(7));
        game.Initialize(world, 0.5);
        var sensors = new Sensors { LocalTime = new DateTime(2026, 10, 22, 12, 0, 0), UserIdleSeconds = 1 };
        setup(game);
        for (var t = 0.0; t < seconds; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
        }

        return (game, world);
    }

    private static FormattedText Text(string text, Typeface typeface, double size, Color color) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, new SolidColorBrush(color), 1);

    public static void Save(BitmapSource image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
