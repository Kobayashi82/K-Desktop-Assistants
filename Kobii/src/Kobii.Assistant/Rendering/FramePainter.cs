using Kobii.Core.Geometry;
using Vortice.Direct2D1;
using Vortice.Direct3D11;

namespace Kobii.Assistant.Rendering;

/// <summary>Un fotograma de Kobii sobre un destino ya limpio (el de la superficie del host o uno fuera de pantalla).</summary>
internal static class FramePainter
{
    /// <summary>La criatura con el shader y, encima, lo que lleva en la mano (y otra vez las manos que lo agarran).</summary>
    public static void DrawBody(Gpu gpu, ID3D11RenderTargetView view, ID2D1Bitmap1? bitmap, KobiiFrame frame, Vec2 origin, int width, int height)
    {
        gpu.Renderer!.Draw(view, width, height, frame.Constants);
        if (!frame.HasExtras || bitmap is null)
        {
            return;
        }

        var dc = gpu.D2D;
        dc.Target = bitmap;
        dc.BeginDraw();
        gpu.Painter!.DrawHeld(dc, origin, frame);
        dc.EndDraw().CheckError();
        dc.Target = null;
        if (frame.HandsOver)
        {
            var hands = frame.Constants;
            hands.PassMode = 1;
            gpu.Renderer.Draw(view, width, height, hands);
        }
    }

    /// <summary>Las partículas con Direct2D sobre un destino que empieza en <paramref name="origin"/>.</summary>
    public static void DrawParticles(Gpu gpu, ID2D1Bitmap1 bitmap, Vec2 origin, IReadOnlyList<Particle> particles)
    {
        var dc = gpu.D2D;
        dc.Target = bitmap;
        dc.BeginDraw();
        gpu.Painter!.DrawParticles(dc, origin, particles);
        dc.EndDraw().CheckError();
        dc.Target = null;
    }
}
