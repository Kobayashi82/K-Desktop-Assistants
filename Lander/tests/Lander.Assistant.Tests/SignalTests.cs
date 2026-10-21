using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lander.Assistant.Rendering;
using Lander.Assistant.Rendering.Skins;

namespace Lander.Assistant.Tests;

/// <summary>
/// La luz de la antena (fase 5 de K-Desktop): cuando el módulo escucha, piensa o habla se enciende en la punta de la
/// antena de cada aspecto, y apagada no cambia nada del dibujo.
/// </summary>
public sealed class SignalTests
{
    private const int Size = 160;
    private const double Scale = 2.5;

    [Theory]
    [InlineData("classic")]
    [InlineData("apollo")]
    [InlineData("capsule")]
    public void The_light_shines_at_the_tip_of_the_antenna(string skin)
    {
        var listening = Gfx.Rgb(0x38E1FF);
        var on = Render(skin, new LanderPose { Signal = 1, SignalColor = listening });
        var off = Render(skin, new LanderPose());
        var tip = SkinCatalog.Get(skin).SignalPoint;
        var (x, y) = ((int)Math.Round(Size / 2 + tip.X * Scale), (int)Math.Round(Size / 2 + tip.Y * Scale));

        // Cerca de la punta, con la luz encendida, hay más azul y verde que sin ella (el halo es cian).
        var lit = Around(on, x, y);
        var dark = Around(off, x, y);
        Assert.True(lit.B > dark.B + 20 && lit.G > dark.G + 10, $"{skin}: con luz {lit}, sin luz {dark}");
    }

    [Theory]
    [InlineData("saucer")]
    [InlineData("pixel")]
    public void Every_skin_draws_something_when_the_light_is_on(string skin)
    {
        var on = Render(skin, new LanderPose { Signal = 1, SignalColor = Gfx.Rgb(0x38E1FF) });
        var off = Render(skin, new LanderPose());
        Assert.NotEqual(Convert.ToBase64String(off), Convert.ToBase64String(on));
    }

    [Fact]
    public void Off_it_draws_the_same_as_before()
    {
        var off = Render("classic", new LanderPose { Signal = 0, SignalColor = Colors.Red });
        var plain = Render("classic", new LanderPose());
        Assert.Equal(Convert.ToBase64String(plain), Convert.ToBase64String(off));
    }

    /// <summary>La media de color de los 5×5 píxeles alrededor de un punto.</summary>
    private static (int R, int G, int B) Around(byte[] pixels, int cx, int cy)
    {
        int r = 0, g = 0, b = 0, n = 0;
        for (var y = cy - 2; y <= cy + 2; y++)
        {
            for (var x = cx - 2; x <= cx + 2; x++)
            {
                var i = (y * Size + x) * 4;
                b += pixels[i];
                g += pixels[i + 1];
                r += pixels[i + 2];
                n++;
            }
        }

        return (r / n, g / n, b / n);
    }

    /// <summary>El lander de un aspecto, centrado y ampliado, sobre negro (BGRA). En un hilo STA, como pide WPF.</summary>
    private static byte[] Render(string skin, LanderPose pose)
    {
        byte[]? result = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, Size, Size));
                    dc.PushTransform(new TranslateTransform(Size / 2, Size / 2));
                    dc.PushTransform(new ScaleTransform(Scale, Scale));
                    SkinCatalog.Get(skin).Draw(dc, pose);
                    dc.Pop();
                    dc.Pop();
                }

                var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var pixels = new byte[Size * Size * 4];
                bitmap.CopyPixels(pixels, Size * 4, 0);
                result = pixels;
                if (Environment.GetEnvironmentVariable("LANDER_SIGNAL_PNG") is { Length: > 0 } folder)
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(folder, $"{skin}-{(pose.Signal > 0 ? "on" : "off")}.png"));
                    encoder.Save(file);
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("No se pudo dibujar", failure);
        }

        return result!;
    }
}
