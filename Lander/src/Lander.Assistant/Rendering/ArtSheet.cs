using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lander.Assistant.Rendering.Skins;
using Lander.Core.Geometry;
using Expression = Lander.Core.Game.Expression;

namespace Lander.Assistant.Rendering;

/// <summary>
/// Imágenes del paquete dibujadas con el mismo código que el lander: el icono y las de la galería de modelos y estelas
/// de sus ajustes. Las genera tools/LanderArt; el asistente no las usa en marcha.
/// </summary>
internal static class ArtSheet
{
    /// <summary>El lander centrado en un cuadrado de <paramref name="size"/> píxeles.</summary>
    public static BitmapSource RenderLander(int size, string skinId = "classic", Vec2 look = default) =>
        Render(size, dc =>
        {
            var scale = size / 54.0;
            var pose = new LanderPose { Look = look, Expression = Expression.Neutral };
            SceneRenderer.DrawLander(dc, SkinCatalog.Get(skinId), new Vec2(size / 2.0, size / 2.0 + 1.5 * scale), 0, scale, pose);
        });

    /// <summary>Una bocanada de la estela: bolitas de humo de su color, de más a menos, en diagonal.</summary>
    public static BitmapSource RenderTrail(int size, string trail) =>
        Render(size, dc =>
        {
            const int puffs = 7;
            for (var i = puffs - 1; i >= 0; i--)
            {
                var t = i / (double)(puffs - 1);
                var center = new Point(size * (0.25 + 0.55 * t), size * (0.75 - 0.5 * t));
                var radius = size * (0.2 - 0.11 * t);
                var color = Trails.SmokeColor(trail, t * 2.5, (i % 4) / 4.0);
                dc.DrawEllipse(Gfx.Brush(color.WithAlpha(0.95 - 0.45 * t)), Gfx.Pen(Colors.Black.WithAlpha(0.12), Math.Max(1, size / 96.0)), center, radius, radius);
            }
        });

    private static BitmapSource Render(int size, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            draw(dc);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
