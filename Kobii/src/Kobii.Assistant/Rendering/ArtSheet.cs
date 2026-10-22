using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kobii.Core;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Assistant.Rendering;

/// <summary>
/// Imágenes de Kobii dibujadas con el mismo shader y las mismas piezas que en el escritorio (con WARP, fuera de pantalla):
/// el icono del paquete, la galería de colores de sus ajustes y hojas para ver de un vistazo cómo quedan sus posturas.
/// </summary>
internal sealed class ArtSheet : IDisposable
{
    private readonly CompiledShader _shader;
    private readonly Dictionary<(int, int), Offscreen> _targets = [];

    public ArtSheet(string? shaderCache = null) =>
        _shader = CreatureRenderer.Compile(shaderCache ?? Path.Combine(Path.GetTempPath(), "K-Desktop", "Kobii", "shaders"));

    /// <summary>
    /// Kobii de pie en un cuadrado de <paramref name="size"/> píxeles, del color indicado, tras hacer lo que diga
    /// <paramref name="setup"/> durante <paramref name="seconds"/> segundos (por defecto, quieto y contento).
    /// </summary>
    public BitmapSource Portrait(int size, string color = "mint", Action<KobiiGame>? setup = null, double seconds = 0.5,
        Expression? face = null, double look = 0.3, double fill = 0.64)
    {
        var (game, world) = Stage(size * fill / Species.HeightDip, setup, seconds);
        return Shoot(game, world, size, size, color, face, look, feetAt: 0.93);
    }

    /// <summary>Una tira con Kobii haciendo cada cosa de <paramref name="scenes"/>, con su nombre debajo.</summary>
    public BitmapSource Strip(int cell, string color, IReadOnlyList<(string Name, Action<KobiiGame> Setup, double Seconds)> scenes, int columns = 6)
    {
        var rows = (scenes.Count + columns - 1) / columns;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF4)), null, new System.Windows.Rect(0, 0, columns * cell, rows * (cell + 24)));
            for (var i = 0; i < scenes.Count; i++)
            {
                var (name, setup, seconds) = scenes[i];
                var (game, world) = Stage(cell * 0.42 / Species.HeightDip, setup, seconds);
                var image = Shoot(game, world, cell, cell, color, null, null, feetAt: 0.8);
                var x = i % columns * cell;
                var y = i / columns * (cell + 24);
                dc.DrawImage(image, new System.Windows.Rect(x, y, cell, cell));
                var text = new FormattedText(name, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 13, Brushes.DimGray, 1);
                dc.DrawText(text, new System.Windows.Point(x + (cell - text.Width) / 2, y + cell + 2));
            }
        }

        var bitmap = new RenderTargetBitmap(columns * cell, rows * (cell + 24), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static void Save(BitmapSource image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public void Dispose()
    {
        foreach (var target in _targets.Values)
        {
            target.Dispose();
        }
    }

    /// <summary>Un escritorio de un monitor y Kobii en su suelo, con el tamaño indicado, tras <paramref name="seconds"/> de simulación.</summary>
    private static (KobiiGame Game, World World) Stage(double size, Action<KobiiGame>? setup, double seconds)
    {
        var monitor = new MonitorInfo(0, new PixelRect(0, 0, 4000, 3000), new PixelRect(0, 0, 4000, 3000), 1, true);
        var world = TerrainBuilder.Build([monitor], [], new TerrainOptions { CreatureHeightDip = Species.HeightDip * size });
        var game = new KobiiGame(new GameOptions { Size = size, Liveliness = 0.0001, FollowCursor = false }, new GameRandom(7));
        game.Initialize(world, 0.5);
        var sensors = new Sensors { LocalTime = new DateTime(2026, 10, 22, 12, 0, 0), UserIdleSeconds = 1 };
        setup?.Invoke(game);
        for (var t = 0.0; t < seconds; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
        }

        return (game, world);
    }

    private BitmapSource Shoot(KobiiGame game, World world, int width, int height, string color, Expression? face, double? look, double feetAt)
    {
        if (!_targets.TryGetValue((width, height), out var target))
        {
            target = new Offscreen(width, height, _shader);
            _targets[(width, height)] = target;
        }

        var frame = new KobiiFrame { Palette = Species.Palette(color) };
        var feet = game.Body.Feet;
        frame.Update(game, feet, game.Time, 0, game.Body.IsGrounded ? feet.Y : world.PlatformBelow(feet.X, feet.Y)?.Y, 1, 1);
        if (face is { } expression)
        {
            frame.Constants.Expression = (float)expression;
        }

        if (look is { } x)
        {
            frame.Constants.LookX = (float)x;
            frame.Constants.LookY = 0;
        }

        var origin = new Vec2(Math.Floor(feet.X) - width / 2, Math.Floor(feet.Y) - Math.Round(height * feetAt));
        target.Draw(frame, origin);
        var pixels = target.ReadPixels();
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
