using System.Runtime.InteropServices;
using KDesktop.Assistant.Sdk;
using Kobii.Core.Geometry;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Kobii.Assistant.Rendering;

/// <summary>
/// Dispositivos propios con WARP y una textura fuera de pantalla: dibuja exactamente lo mismo que en la capa del host
/// (las mismas piezas, por un <see cref="GpuTarget"/> hecho aquí) y deja leer los píxeles. Para el icono, las imágenes
/// de los ajustes y las pruebas.
/// </summary>
internal sealed class Offscreen : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID2D1Factory1 _factory;
    private readonly ID2D1Device _d2dDevice;
    private readonly ID2D1DeviceContext _d2d;
    private readonly ID3D11Texture2D _texture;
    private readonly ID3D11Texture2D _staging;
    private readonly ID3D11RenderTargetView _view;
    private readonly ID2D1Bitmap1 _bitmap;
    private Gpu? _gpu;

    public Offscreen(int width, int height, CompiledShader shader, bool hardware = false)
    {
        Width = width;
        Height = height;
        Shader = shader;
        D3D11.D3D11CreateDevice(IntPtr.Zero, hardware ? DriverType.Hardware : DriverType.Warp, DeviceCreationFlags.BgraSupport, [Vortice.Direct3D.FeatureLevel.Level_11_0], out var device, out var context).CheckError();
        _device = device!;
        _context = context!;
        using var dxgi = _device.QueryInterface<IDXGIDevice>();
        _factory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        _d2dDevice = _factory.CreateDevice(dxgi);
        _d2d = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget,
        };
        _texture = _device.CreateTexture2D(description);
        _view = _device.CreateRenderTargetView(_texture);
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        _staging = _device.CreateTexture2D(description);
        using var surface = _texture.QueryInterface<IDXGISurface>();
        _bitmap = _d2d.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96,
            BitmapOptions.Target | BitmapOptions.CannotDraw));
    }

    public int Width { get; }

    public int Height { get; }

    public CompiledShader Shader { get; }

    /// <summary>Espera a que la GPU acabe lo pedido (para medir).</summary>
    public void Finish()
    {
        _context.Flush();
        using var query = _device.CreateQuery(new QueryDescription(QueryType.Event));
        _context.End(query);
        while (!_context.IsDataAvailable(query))
        {
            Thread.Yield();
        }
    }

    /// <summary>Un fotograma de Kobii en el lienzo, que empieza en <paramref name="origin"/> (en el mundo).</summary>
    public void Draw(KobiiFrame frame, Vec2 origin, IReadOnlyList<Particle>? particles = null)
    {
        _context.ClearRenderTargetView(_view, new Color4(0, 0, 0, 0));
        frame.SetCanvas(origin, Width, Height);
        var target = new GpuTarget(_device.NativePointer, _context.NativePointer, _view.NativePointer, _d2d.NativePointer, Width, Height,
            new System.Windows.Point(origin.X, origin.Y), _bitmap.NativePointer, 1);
        var gpu = Gpu.For(ref _gpu, target);
        gpu.Renderer ??= new CreatureRenderer(gpu.Device, gpu.Context, Shader);
        gpu.Painter ??= new ScenePainter(gpu);
        FramePainter.DrawBody(gpu, _view, _bitmap, frame, origin, Width, Height);
        if (particles is { Count: > 0 })
        {
            FramePainter.DrawParticles(gpu, _bitmap, origin, particles);
        }
    }

    /// <summary>Los píxeles (BGRA premultiplicado, filas de arriba abajo).</summary>
    public byte[] ReadPixels()
    {
        _context.CopyResource(_staging, _texture);
        var mapped = _context.Map(_staging, 0, MapMode.Read);
        try
        {
            var pixels = new byte[Width * Height * 4];
            for (var y = 0; y < Height; y++)
            {
                Marshal.Copy(mapped.DataPointer + (nint)(y * mapped.RowPitch), pixels, y * Width * 4, Width * 4);
            }

            return pixels;
        }
        finally
        {
            _context.Unmap(_staging, 0);
        }
    }

    public void Dispose()
    {
        _gpu?.Dispose();
        _bitmap.Dispose();
        _view.Dispose();
        _staging.Dispose();
        _texture.Dispose();
        _d2d.Dispose();
        _d2dDevice.Dispose();
        _factory.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
