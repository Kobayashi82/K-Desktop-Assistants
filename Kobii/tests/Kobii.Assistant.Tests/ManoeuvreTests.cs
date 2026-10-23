using System.Windows;
using Kobii.Assistant.Rendering;
using Kobii.Core.Geometry;

namespace Kobii.Assistant.Tests;

/// <summary>
/// Los efectos de las maniobras (la pelota, el golpe, las ondas, la línea de señalar, la banderita, el haz de las gafas y
/// el flash) se dibujan donde dicen que están, en cualquier momento de su vida. <c>KOBII_RENDER_PNG</c> = carpeta donde
/// dejar las imágenes para verlas.
/// </summary>
public sealed class ManoeuvreTests
{
    private const int Size = 400;

    public static TheoryData<string> Kinds => new(Enum.GetNames<EffectKind>());

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Each_effect_is_drawn_inside_its_extent(string name)
    {
        var kind = Enum.Parse<EffectKind>(name);
        var effect = new ManoeuvreEffect(kind, 0, 1, new Vec2(60, 340), new Vec2(320, 120), 0x42A5F5, 1,
            Area: new Rect(40, 60, 320, 260), Peak: 80, Velocity: new Vec2(-150, -300), Text: "Siempre visible");
        foreach (var age in new[] { 0.05, 0.3, 0.6, 0.9 })
        {
            var extent = ManoeuvreLayer.Extent(effect, age);
            Assert.False(extent.IsEmpty);
            var (painted, outside) = Draw(effect, age, extent);
            // Mientras se ve (al final algunos ya se han desvanecido).
            Assert.True(painted > 0 || age > 0.5, $"{kind} a {age} s no dibuja nada");

            // Nada (o casi: antialias) fuera de lo que mide: la superficie del host se recorta ahí.
            Assert.True(outside <= painted * 0.02 + 4, $"{kind} a {age} s: {outside} píxeles fuera de {extent}");
        }
    }

    [Fact]
    public void The_ball_flies_in_an_arc_and_lands_on_the_button()
    {
        var effect = new ManoeuvreEffect(EffectKind.Ball, 0, 0.5, new Vec2(0, 500), new Vec2(400, 100), 0, 1, Peak: 120);
        Assert.Equal(new Vec2(0, 500), effect.ArcAt(0));
        Assert.Equal(new Vec2(400, 100), effect.ArcAt(1));

        // A mitad de camino va por encima de la recta (y = 300) la altura del arco.
        Assert.Equal(300 - 120, effect.ArcAt(0.5).Y, 6);
    }

    private static (int Painted, int Outside) Draw(ManoeuvreEffect effect, double age, Rect extent)
    {
        var pixels = OnSta(() =>
        {
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                ManoeuvreLayer.Draw(dc, effect, age, 1);
            }

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(Size, Size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var data = new byte[Size * Size * 4];
            bitmap.CopyPixels(data, Size * 4, 0);
            if (Environment.GetEnvironmentVariable("KOBII_RENDER_PNG") is { Length: > 0 } folder)
            {
                VectorSheet.Save(bitmap, Path.Combine(folder, $"effect-{effect.Kind}-{age:0.00}.png".Replace(',', '.')));
            }

            return data;
        });

        int painted = 0, outside = 0;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (pixels[(y * Size + x) * 4 + 3] < 24)
                {
                    continue;
                }

                painted++;
                if (!extent.Contains(new Point(x + 0.5, y + 0.5)))
                {
                    outside++;
                }
            }
        }

        return (painted, outside);
    }

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
}
