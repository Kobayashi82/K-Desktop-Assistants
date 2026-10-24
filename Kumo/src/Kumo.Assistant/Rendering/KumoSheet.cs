using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kumo.Core;
using Kumo.Core.Cloud;
using Kumo.Core.Geometry;

namespace Kumo.Assistant.Rendering;

/// <summary>Una escena de la hoja: lo que hace y cuánto se deja correr antes de la foto.</summary>
internal sealed record SheetScene(string Name, Action<KumoCloud> Setup, double Seconds, KDesktop.Assistant.Sdk.AssistantActivity Activity = KDesktop.Assistant.Sdk.AssistantActivity.None);

/// <summary>
/// Hojas con el dibujo de Kumo (el mismo que en el escritorio) para revisar de un vistazo formas, estados y caras sin K-Desktop.
/// Se dibujan con WPF en memoria.
/// </summary>
internal static class KumoSheet
{
    private static readonly Typeface Title = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface Label = new("Segoe UI");

    public static BitmapSource Sheet(IReadOnlyList<(string Title, IReadOnlyList<SheetScene> Scenes)> groups, int cell = 220, int columns = 5, double unit = 52, bool dark = false)
    {
        const int header = 34;
        const int caption = 22;
        var height = 16 + groups.Sum(g => header + (g.Scenes.Count + columns - 1) / columns * (cell + caption));
        var width = columns * cell;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = dark ? Color.FromRgb(0x18, 0x20, 0x2F) : Color.FromRgb(0xDC, 0xE6, 0xF1);
            var ink = dark ? Color.FromRgb(0xE6, 0xE6, 0xE6) : Color.FromRgb(0x2A, 0x31, 0x42);
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
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(dark ? (byte)22 : (byte)14, 0, 0, 0)), null, new Rect(x + 4, top + 2, cell - 8, cell + caption - 6));
                    dc.PushClip(new RectangleGeometry(new Rect(x + 4, top + 2, cell - 8, cell + caption - 6)));
                    Draw(dc, scenes[i], new Vec2(x + cell / 2.0, top + cell * 0.5), unit, dark);
                    dc.Pop();
                    var label = Text(scenes[i].Name, Label, 13, Color.FromArgb(220, ink.R, ink.G, ink.B));
                    dc.DrawText(label, new Point(x + (cell - label.Width) / 2, top + cell - 4));
                }

                y += (scenes.Count + columns - 1) / columns * (cell + caption);
            }
        }

        return Render(visual, width, height);
    }

    /// <summary>Kumo solo, centrado en un cuadrado (el icono).</summary>
    public static BitmapSource Portrait(int size, double fill = 0.27)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            Draw(dc, new SheetScene("", c => c.Cursor = new Vec2(size * 0.62, size * 0.62), 1.2), new Vec2(size / 2.0, size * 0.52), size * fill, dark: false, shadow: false);
        }

        return Render(visual, size, size);
    }

    public static KumoCloud Cloud(double unit, Vec2 at, int seed = 3)
    {
        var cloud = new KumoCloud(new GameRandom(seed), unit);
        cloud.Place(at);
        return cloud;
    }

    private static void Draw(DrawingContext dc, SheetScene scene, Vec2 at, double unit, bool dark, bool shadow = true)
    {
        var cloud = Cloud(unit, at);
        scene.Setup(cloud);
        var step = 1.0 / 60;
        for (var t = 0.0; t < scene.Seconds; t += step)
        {
            cloud.Step(step);
        }

        // Lo que se ha movido (volando) se trae al centro de la casilla.
        var shift = at - cloud.Position;
        dc.PushTransform(new TranslateTransform(shift.X, shift.Y));
        var frame = new KumoFrame { Dark = dark };
        frame.Update(cloud, 1);
        new KumoPainter().Draw(dc, frame);
        if (scene.Activity != KDesktop.Assistant.Sdk.AssistantActivity.None)
        {
            // La burbujita ya del todo fuera, a la derecha.
            var badge = new ActivityBadge();
            badge.Set(scene.Activity, 0);
            var body = cloud.BodyExtent();
            var c = cloud.RenderPosition;
            var center = ActivityBadge.Place(c, body, unit, left: false, cloud.Time);
            var from = new Vec2(c.X + body.Right * 0.38 * unit, c.Y + body.Top * 0.62 * unit);
            badge.Draw(dc, center, from, unit, 10, dark, 1);
        }

        ParticlePainter.Draw(dc, cloud.Particles.Items, unit);
        dc.Pop();
    }

    private static BitmapSource Render(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static void Save(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static FormattedText Text(string text, Typeface typeface, double size, Color color) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, new SolidColorBrush(color), 1);
}
