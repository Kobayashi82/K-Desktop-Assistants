using System.Diagnostics;
using Lander.Core.Geometry;
using Lander.Core.Vision;
using SharpGen.Runtime;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace Lander.Assistant.Vision;

/// <summary>
/// Duplicación del escritorio de DXGI para un monitor: lo mismo que ve el usuario (solo lo visible, sin
/// las ventanas excluidas de la captura y sin aviso amarillo), en la GPU. Cada fotograma nuevo se copia a
/// una textura propia para poder leer cualquier región más tarde aunque la pantalla no cambie. Solo se
/// lee de vuelta a la CPU la región que se pide. Se usa siempre desde el hilo del motor.
/// </summary>
internal sealed unsafe class DesktopDuplicator : IDisposable
{
    /// <summary>Cambios recordados (rectángulos modificados), para saber si una región ha cambiado.</summary>
    private const double ChangeMemory = 4;

    private const int MaxChanges = 384;

    private static readonly FeatureLevel[] FeatureLevels = [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly bool _ownsDevice;
    private readonly IDXGIOutputDuplication _duplication;
    private readonly List<(double Time, PixelRect Rect)> _changes = [];
    private readonly double _created;
    private double _forgottenBefore = double.NegativeInfinity;
    private ID3D11Texture2D? _latest;
    private ID3D11Texture2D? _staging;
    private int _stagingWidth;
    private int _stagingHeight;
    private PixelRect _pendingRegion;
    private RawRect[] _dirty = new RawRect[32];
    private OutduplMoveRect[] _moves = new OutduplMoveRect[8];

    private DesktopDuplicator(ID3D11Device device, bool ownsDevice, IDXGIOutputDuplication duplication, PixelRect bounds, double now)
    {
        _device = device;
        _ownsDevice = ownsDevice;
        _context = device.ImmediateContext;
        _duplication = duplication;
        _created = now;
        Bounds = bounds;
    }

    /// <summary>Límites del monitor en píxeles físicos del escritorio virtual.</summary>
    public PixelRect Bounds { get; }

    /// <summary>Ya hay una imagen del escritorio que leer.</summary>
    public bool HasFrame => _latest is not null;

    /// <summary>La duplicación se ha perdido (cambio de modo, escritorio seguro...): hay que crear otra.</summary>
    public bool IsLost { get; private set; }

    /// <summary>Momento del último fotograma con cambios en la imagen.</summary>
    public double LastFrameTime { get; private set; } = double.NegativeInfinity;

    /// <summary>Tiempo acumulado leyendo regiones (copiar, esperar a la GPU y convertir a gris).</summary>
    public long ReadTicks { get; set; }

    public int Reads { get; set; }

    /// <summary>
    /// Duplica el monitor con esos límites. Reutiliza <paramref name="renderDevice"/> si está en el mismo
    /// adaptador que el monitor; si no, crea un dispositivo propio. Devuelve null si no es posible.
    /// </summary>
    public static DesktopDuplicator? TryCreate(ID3D11Device? renderDevice, PixelRect bounds, double now)
    {
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
            {
                using (adapter)
                {
                    for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                    {
                        using (output)
                        {
                            var description = output.Description;
                            var rect = description.DesktopCoordinates;
                            if (new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom) != bounds)
                            {
                                continue;
                            }

                            if (description.Rotation is not (ModeRotation.Identity or ModeRotation.Unspecified))
                            {
                                Log.Info($"Contenido: el monitor {bounds} está girado; no se analiza.");
                                return null;
                            }

                            return Create(renderDevice, adapter, output, bounds, now);
                        }
                    }
                }
            }

            Log.Warn($"Contenido: no se encuentra la salida de vídeo del monitor {bounds}.");
        }
        catch (SharpGenException ex)
        {
            Log.Warn("Contenido: no se puede duplicar el escritorio.", ex);
        }

        return null;
    }

    private static DesktopDuplicator? Create(ID3D11Device? renderDevice, IDXGIAdapter1 adapter, IDXGIOutput output, PixelRect bounds, double now)
    {
        var device = renderDevice is not null && SameAdapter(renderDevice, adapter) ? renderDevice : null;
        var ownsDevice = device is null;
        if (device is null)
        {
            var result = D3D11.D3D11CreateDevice(adapter.NativePointer, DriverType.Unknown, DeviceCreationFlags.None, FeatureLevels, out var created);
            if (result.Failure || created is null)
            {
                Log.Warn($"Contenido: no se pudo crear un dispositivo para duplicar el monitor (0x{result.Code:X8}).");
                return null;
            }

            device = created;
        }

        try
        {
            IDXGIOutputDuplication? duplication = null;
            using (var output5 = output.QueryInterfaceOrNull<IDXGIOutput5>())
            {
                if (output5 is not null)
                {
                    // Pide siempre 8 bits por canal, también con HDR: Windows convierte la imagen. (En Vortice,
                    // el entero de las otras sobrecargas es el número de formatos, no los flags.)
                    try
                    {
                        duplication = output5.DuplicateOutput1(device, new[] { Format.B8G8R8A8_UNorm });
                    }
                    catch (SharpGenException ex)
                    {
                        Log.Debug($"Contenido: DuplicateOutput1 falló (0x{ex.HResult:X8}); se prueba DuplicateOutput.");
                    }
                }
            }

            if (duplication is null)
            {
                using var output1 = output.QueryInterface<IDXGIOutput1>();
                duplication = output1.DuplicateOutput(device);
            }

            if (ownsDevice)
            {
                Log.Info($"Contenido: duplicando el monitor {bounds} con un dispositivo propio.");
            }

            return new DesktopDuplicator(device, ownsDevice, duplication, bounds, now);
        }
        catch (SharpGenException ex)
        {
            Log.Warn($"Contenido: no se puede duplicar el monitor {bounds} (0x{ex.HResult:X8}).", ex);
            if (ownsDevice)
            {
                device.Dispose();
            }

            return null;
        }
    }

    private static bool SameAdapter(ID3D11Device device, IDXGIAdapter1 adapter)
    {
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        using var own = dxgi.GetAdapter();
        return own.Description.Luid.Equals(adapter.Description1.Luid);
    }

    /// <summary>
    /// Recoge el fotograma nuevo si lo hay (sin esperar). Devuelve true si la imagen del escritorio ha
    /// cambiado desde la última vez.
    /// </summary>
    public bool Poll(double now)
    {
        if (IsLost)
        {
            return false;
        }

        var result = _duplication.AcquireNextFrame(0, out var info, out var resource);
        if (result == Vortice.DXGI.ResultCode.WaitTimeout)
        {
            return false;
        }

        if (result.Failure)
        {
            IsLost = true;
            Log.Info($"Contenido: duplicación del escritorio perdida (0x{result.Code:X8}).");
            return false;
        }

        try
        {
            // Sin hora de presentación solo se ha movido el ratón.
            if (info.LastPresentTime == 0 && _latest is not null)
            {
                return false;
            }

            using var texture = resource.QueryInterface<ID3D11Texture2D>();
            var description = texture.Description;
            if (description.Format is not (Format.B8G8R8A8_UNorm or Format.B8G8R8A8_UNorm_SRgb))
            {
                IsLost = true;
                Log.Warn($"Contenido: formato de escritorio no admitido ({description.Format}).");
                return false;
            }

            if (_latest is null || _latest.Description.Width != description.Width || _latest.Description.Height != description.Height)
            {
                _latest?.Dispose();
                _latest = _device.CreateTexture2D(new Texture2DDescription(
                    description.Format, description.Width, description.Height, 1, 1, BindFlags.None, ResourceUsage.Default, CpuAccessFlags.None));
                RecordChange(now, Bounds);
            }

            _context.CopyResource(_latest, texture);
            RecordChanges(now, info.TotalMetadataBufferSize);
            LastFrameTime = now;
            return true;
        }
        finally
        {
            resource.Dispose();
            _duplication.ReleaseFrame();
        }
    }

    /// <summary>
    /// Fracción (0-1) de <paramref name="region"/> que ha cambiado después de <paramref name="since"/>. Si no
    /// se sabe (la duplicación es más reciente), se supone que ha cambiado entera.
    /// </summary>
    public double ChangedFraction(PixelRect region, double since)
    {
        var area = region.Intersect(Bounds).Area;
        if (area == 0)
        {
            return 0;
        }

        if (since < _created || since < _forgottenBefore)
        {
            return 1;
        }

        long covered = 0;
        foreach (var (time, rect) in _changes)
        {
            if (time > since)
            {
                covered += rect.Intersect(region).Area;
            }
        }

        return Math.Min(1, (double)covered / area);
    }

    /// <summary>Hay una lectura encolada esperando a la GPU.</summary>
    public bool IsReading { get; private set; }

    /// <summary>
    /// Encola la copia de <paramref name="region"/> (coordenadas de pantalla) de la última imagen. No espera:
    /// el resultado se recoge con <see cref="TryFinishRead"/> en un ciclo posterior.
    /// </summary>
    public bool BeginRead(PixelRect region)
    {
        var clipped = region.Intersect(Bounds);
        if (_latest is null || clipped.IsEmpty || IsLost || IsReading)
        {
            return false;
        }

        EnsureStaging(clipped.Width, clipped.Height);
        var left = clipped.Left - Bounds.Left;
        var top = clipped.Top - Bounds.Top;
        _context.CopySubresourceRegion(_staging!, 0, 0, 0, 0, _latest, 0, new Box(left, top, 0, left + clipped.Width, top + clipped.Height, 1));
        _context.Flush();
        _pendingRegion = clipped;
        IsReading = true;
        return true;
    }

    /// <summary>
    /// Recoge la lectura encolada, en escala de grises, si la GPU ya la ha terminado (si no, devuelve false
    /// sin esperar). <paramref name="region"/> es la región leída.
    /// </summary>
    public bool TryFinishRead(GrayImage target, out PixelRect region)
    {
        ArgumentNullException.ThrowIfNull(target);
        region = _pendingRegion;
        if (!IsReading || _staging is null)
        {
            return false;
        }

        var start = Stopwatch.GetTimestamp();
        var result = _context.Map(_staging, 0, MapMode.Read, MapFlags.DoNotWait, out var mapped);
        if (result == Vortice.DXGI.ResultCode.WasStillDrawing)
        {
            return false;
        }

        IsReading = false;
        if (result.Failure)
        {
            return false;
        }

        try
        {
            var pitch = (int)mapped.RowPitch;
            var length = pitch * (region.Height - 1) + region.Width * 4;
            target.LoadBgra(new ReadOnlySpan<byte>((void*)mapped.DataPointer, length), region.Width, region.Height, pitch);
        }
        finally
        {
            _context.Unmap(_staging, 0);
        }

        ReadTicks += Stopwatch.GetTimestamp() - start;
        Reads++;
        return true;
    }

    /// <summary>Olvida la lectura encolada (la ventana ha cambiado mientras tanto).</summary>
    public void CancelRead() => IsReading = false;

    private void EnsureStaging(int width, int height)
    {
        if (_staging is not null && _stagingWidth >= width && _stagingHeight >= height)
        {
            return;
        }

        // Crece de golpe hasta lo que se pida (como mucho, el monitor entero) para no recrearla a menudo.
        _staging?.Dispose();
        _stagingWidth = Math.Min(Bounds.Width, Math.Max(width, _stagingWidth));
        _stagingHeight = Math.Min(Bounds.Height, Math.Max(height, _stagingHeight));
        _staging = _device.CreateTexture2D(new Texture2DDescription(
            _latest!.Description.Format, (uint)_stagingWidth, (uint)_stagingHeight, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
    }

    private void RecordChanges(double now, uint metadataSize)
    {
        Prune(now);
        if (metadataSize == 0)
        {
            return;
        }

        var size = (uint)(_dirty.Length * sizeof(RawRect));
        var result = _duplication.GetFrameDirtyRects(size, _dirty, out var required);
        if (result == Vortice.DXGI.ResultCode.MoreData)
        {
            _dirty = new RawRect[required / sizeof(RawRect) + 8];
            result = _duplication.GetFrameDirtyRects((uint)(_dirty.Length * sizeof(RawRect)), _dirty, out required);
        }

        if (result.Failure)
        {
            RecordChange(now, Bounds);
            return;
        }

        for (var i = 0; i < required / sizeof(RawRect); i++)
        {
            var r = _dirty[i];
            RecordChange(now, new PixelRect(r.Left + Bounds.Left, r.Top + Bounds.Top, r.Right + Bounds.Left, r.Bottom + Bounds.Top));
        }

        // Lo que se ha movido (scroll acelerado por el sistema) también ha cambiado en su destino.
        size = (uint)(_moves.Length * sizeof(OutduplMoveRect));
        result = _duplication.GetFrameMoveRects(size, _moves, out required);
        if (result == Vortice.DXGI.ResultCode.MoreData)
        {
            _moves = new OutduplMoveRect[required / sizeof(OutduplMoveRect) + 4];
            result = _duplication.GetFrameMoveRects((uint)(_moves.Length * sizeof(OutduplMoveRect)), _moves, out required);
        }

        if (result.Failure)
        {
            RecordChange(now, Bounds);
            return;
        }

        for (var i = 0; i < required / sizeof(OutduplMoveRect); i++)
        {
            var r = _moves[i].DestinationRect;
            RecordChange(now, new PixelRect(r.Left + Bounds.Left, r.Top + Bounds.Top, r.Right + Bounds.Left, r.Bottom + Bounds.Top));
        }
    }

    private void RecordChange(double now, PixelRect rect)
    {
        if (_changes.Count >= MaxChanges)
        {
            // Demasiados trocitos: se resume en «ha cambiado todo».
            _changes.Clear();
            rect = Bounds;
        }

        _changes.Add((now, rect));
    }

    private void Prune(double now)
    {
        var cutoff = now - ChangeMemory;
        var keep = 0;
        _forgottenBefore = cutoff;
        while (keep < _changes.Count && _changes[keep].Time < cutoff)
        {
            keep++;
        }

        if (keep > 0)
        {
            _changes.RemoveRange(0, keep);
        }
    }

    public void Dispose()
    {
        _staging?.Dispose();
        _latest?.Dispose();
        _duplication.Dispose();

        // El contexto inmediato es del dispositivo (Vortice lo comparte): no se libera aquí.
        if (_ownsDevice)
        {
            _device.Dispose();
        }
    }
}
