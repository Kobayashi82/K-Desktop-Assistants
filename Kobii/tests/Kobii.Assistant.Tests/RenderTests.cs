using Kobii.Assistant.Rendering;
using Kobii.Core;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Assistant.Tests;

/// <summary>
/// El dibujo vectorial en memoria con WPF (lo mismo que se manda al host): Kobii se ve de su color, lo que lleva en la
/// mano sale y las partículas también. <c>KOBII_RENDER_PNG</c> = carpeta donde dejar las
/// imágenes para verlas.
/// </summary>
public sealed class RenderTests
{
    private const int Size = 240;
    private static readonly MonitorInfo Monitor = new(0, new PixelRect(0, 0, 2000, 1500), new PixelRect(0, 0, 2000, 1500), 1, true);

    [Fact]
    public void Kobii_is_drawn_in_the_chosen_colour()
    {
        foreach (var palette in Species.Palettes)
        {
            var pixels = Draw(palette, game => { });
            var (coverage, average) = Measure(pixels);
            Assert.InRange(coverage, 0.05, 0.5);

            // El color medio del cuerpo se parece al de su colorido (la barriga y el brillo lo aclaran algo).
            var body = (R: (palette.Body >> 16) & 0xFF, G: (palette.Body >> 8) & 0xFF, B: palette.Body & 0xFF);
            var distance = Math.Abs(average.R - body.R) + Math.Abs(average.G - body.G) + Math.Abs(average.B - body.B);
            Assert.True(distance < 150, $"{palette.Id}: medio {average}, cuerpo {body}");
            Save(pixels, palette.Id);
        }
    }

    [Fact]
    public void What_it_holds_and_its_particles_are_drawn_too()
    {
        var plain = Measure(Draw(Species.Palettes[0], game => { })).Coverage;
        var balloon = Measure(Draw(Species.Palettes[0], game => game.PlayClip("globo"), seconds: 1.5)).Coverage;
        Assert.True(balloon > plain + 0.01, $"globo {balloon:0.000} frente a {plain:0.000}");

        var particles = new ParticleSystem(new GameRandom(1));
        particles.Spawn(new Emote(EmoteKind.Heart, new Vec2(1090, 1360), 3, 4), 0xFFFFFF);
        particles.Update(0.2);
        var hearts = Measure(Draw(Species.Palettes[0], game => { }, particles: particles)).Coverage;
        Assert.True(hearts > plain + 0.005, $"corazones {hearts:0.000} frente a {plain:0.000}");
    }

    private static byte[] Draw(Palette palette, Action<KobiiGame> setup, double seconds = 0.5, ParticleSystem? particles = null)
    {
        var world = TerrainBuilder.Build([Monitor], []);
        var game = new KobiiGame(new GameOptions { Size = 1.2, Liveliness = 0.0001, FollowCursor = false }, new GameRandom(3));
        game.Initialize(world, 0.5);
        var sensors = new Sensors { LocalTime = new DateTime(2026, 10, 22, 12, 0, 0), UserIdleSeconds = 1 };
        setup(game);
        for (var t = 0.0; t < seconds; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
        }

        var frame = new KobiiFrame { Palette = palette };
        var feet = game.Body.Feet;
        frame.Update(game, feet, game.Time, 0, feet.Y, 1, 1);
        return OnSta(() =>
        {
            // Como la superficie del host: coordenadas de pantalla, con el lienzo empezando en origin.
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new System.Windows.Media.TranslateTransform(-(feet.X - Size / 2), -(feet.Y - Size * 0.8)));
                var props = frame.Props;
                new KobiiPainter().Draw(dc, frame, frame.HasExtras ? d => ScenePainter.DrawProps(d, props) : null);
                if (particles is not null)
                {
                    ScenePainter.DrawParticles(dc, particles.Items);
                }

                dc.Pop();
            }

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(Size, Size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[Size * Size * 4];
            bitmap.CopyPixels(pixels, Size * 4, 0);
            return pixels;
        });
    }

    /// <summary>WPF necesita un hilo STA.</summary>
    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return error is null ? result : throw new InvalidOperationException("Falló el dibujo.", error);
    }

    /// <summary>Qué parte del lienzo está pintada y el color medio de lo pintado (sin premultiplicar).</summary>
    private static (double Coverage, (double R, double G, double B) Average) Measure(byte[] pixels)
    {
        double r = 0, g = 0, b = 0;
        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            if (a < 200)
            {
                continue;
            }

            b += pixels[i] * 255.0 / a;
            g += pixels[i + 1] * 255.0 / a;
            r += pixels[i + 2] * 255.0 / a;
            count++;
        }

        return count == 0 ? (0, (0, 0, 0)) : ((double)count / (pixels.Length / 4), (r / count, g / count, b / count));
    }

    private static void Save(byte[] pixels, string name)
    {
        if (Environment.GetEnvironmentVariable("KOBII_RENDER_PNG") is not { Length: > 0 } folder)
        {
            return;
        }

        var image = System.Windows.Media.Imaging.BitmapSource.Create(Size, Size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null, pixels, Size * 4);
        VectorSheet.Save(image, Path.Combine(folder, name + ".png"));
    }
}
