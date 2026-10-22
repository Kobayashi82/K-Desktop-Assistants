using System.Runtime.InteropServices;
using KDesktop.Assistant.Sdk;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectWrite;

namespace Kobii.Assistant.Rendering;

/// <summary>
/// Los dispositivos del host vistos con el Vortice del paquete: los punteros de <see cref="GpuTarget"/> envueltos (con
/// una referencia propia, así que no dependen de que el host los suelte o no) y lo que se hace sobre ellos. Si cambia
/// la generación (se perdió la GPU o vuelve tras un juego a pantalla completa) se suelta todo y se hace de nuevo.
/// </summary>
internal sealed class Gpu : IDisposable
{
    private Gpu(GpuTarget target)
    {
        Generation = target.Generation;
        Device = Wrap(target.D3D11Device, p => new ID3D11Device(p));
        Context = Wrap(target.D3D11DeviceContext, p => new ID3D11DeviceContext(p));
        D2D = Wrap(target.D2D1DeviceContext, p => new ID2D1DeviceContext(p));
        D2DFactory = D2D.Factory.QueryInterface<ID2D1Factory1>();
        DWrite = Vortice.DirectWrite.DWrite.DWriteCreateFactory<IDWriteFactory>();
    }

    public int Generation { get; }

    public ID3D11Device Device { get; }

    public ID3D11DeviceContext Context { get; }

    public ID2D1DeviceContext D2D { get; }

    public ID2D1Factory1 D2DFactory { get; }

    public IDWriteFactory DWrite { get; }

    /// <summary>Lo que se dibuja sobre estos dispositivos (shaders, pinceles), soltado con ellos.</summary>
    public CreatureRenderer? Renderer { get; set; }

    public ScenePainter? Painter { get; set; }

    /// <summary>Los dispositivos de <paramref name="target"/>: los mismos de antes si no ha cambiado la generación.</summary>
    public static Gpu For(ref Gpu? current, GpuTarget target)
    {
        if (current is { } gpu && gpu.Generation == target.Generation && gpu.Device.NativePointer == target.D3D11Device)
        {
            return gpu;
        }

        current?.Dispose();
        current = new Gpu(target);
        return current;
    }

    /// <summary>El bitmap de Direct2D del destino, con su referencia: hay que soltarlo al acabar el dibujo.</summary>
    public static ID2D1Bitmap1? TargetBitmap(GpuTarget target) => target.D2D1TargetBitmap == 0 ? null : Wrap(target.D2D1TargetBitmap, p => new ID2D1Bitmap1(p));

    /// <summary>El destino de Direct3D, con su referencia: hay que soltarlo al acabar el dibujo.</summary>
    public static ID3D11RenderTargetView TargetView(GpuTarget target) => Wrap(target.RenderTargetView, p => new ID3D11RenderTargetView(p));

    public void Dispose()
    {
        Painter?.Dispose();
        Renderer?.Dispose();
        DWrite.Dispose();
        D2DFactory.Dispose();
        D2D.Dispose();
        Context.Dispose();
        Device.Dispose();
    }

    private static T Wrap<T>(nint pointer, Func<nint, T> wrap)
    {
        // Vortice no añade una referencia al envolver un puntero, pero sí la quita al soltarlo: se añade una aquí.
        Marshal.AddRef(pointer);
        return wrap(pointer);
    }
}
