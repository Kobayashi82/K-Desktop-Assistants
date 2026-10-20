using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Lander.Core.Vision;

/// <summary>
/// Imagen en escala de grises, un byte por píxel y sin relleno entre filas. El búfer se reutiliza entre
/// capturas: solo crece cuando hace falta.
/// </summary>
public sealed class GrayImage
{
    private byte[] _pixels;

    public GrayImage(int width, int height)
    {
        _pixels = [];
        Resize(width, height);
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Píxeles por filas, de arriba abajo (<see cref="Width"/> × <see cref="Height"/> bytes útiles).</summary>
    public Span<byte> Pixels => _pixels.AsSpan(0, Width * Height);

    public Span<byte> Row(int y) => _pixels.AsSpan(y * Width, Width);

    public byte this[int x, int y]
    {
        get => _pixels[y * Width + x];
        set => _pixels[y * Width + x] = value;
    }

    /// <summary>Cambia el tamaño conservando el búfer si cabe. El contenido queda indefinido.</summary>
    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        var length = width * height;
        if (_pixels.Length < length)
        {
            _pixels = new byte[length];
        }

        Width = width;
        Height = height;
    }

    /// <summary>
    /// Convierte una imagen BGRA de 32 bits (como las de DXGI) a luminancia, redimensionando esta imagen a
    /// <paramref name="width"/> × <paramref name="height"/>.
    /// </summary>
    /// <param name="bgra">Píxeles de origen.</param>
    /// <param name="pitch">Bytes por fila del origen (puede ser mayor que el ancho × 4).</param>
    public void LoadBgra(ReadOnlySpan<byte> bgra, int width, int height, int pitch)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pitch, width * 4);
        if (height > 0 && bgra.Length < (long)pitch * (height - 1) + width * 4)
        {
            throw new ArgumentException("El búfer de origen es demasiado pequeño.", nameof(bgra));
        }

        Resize(width, height);
        for (var y = 0; y < height; y++)
        {
            ConvertRow(MemoryMarshal.Cast<byte, uint>(bgra.Slice(y * pitch, width * 4)), Row(y));
        }
    }

    /// <summary>Luminancia aproximada (Rec. 601 en enteros): (77 R + 150 G + 29 B) / 256.</summary>
    public static byte Luma(byte r, byte g, byte b) => (byte)((r * 77 + g * 150 + b * 29) >> 8);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ConvertRow(ReadOnlySpan<uint> source, Span<byte> target)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var mask = Vector128.Create(0xFFu);
            var wb = Vector128.Create(29u);
            var wg = Vector128.Create(150u);
            var wr = Vector128.Create(77u);
            ref var src = ref MemoryMarshal.GetReference(source);
            ref var dst = ref MemoryMarshal.GetReference(target);
            for (; x + 16 <= source.Length; x += 16)
            {
                var p0 = Luma4(Vector128.LoadUnsafe(ref src, (nuint)x), mask, wb, wg, wr);
                var p1 = Luma4(Vector128.LoadUnsafe(ref src, (nuint)(x + 4)), mask, wb, wg, wr);
                var p2 = Luma4(Vector128.LoadUnsafe(ref src, (nuint)(x + 8)), mask, wb, wg, wr);
                var p3 = Luma4(Vector128.LoadUnsafe(ref src, (nuint)(x + 12)), mask, wb, wg, wr);
                var bytes = Vector128.Narrow(Vector128.Narrow(p0, p1), Vector128.Narrow(p2, p3));
                bytes.StoreUnsafe(ref dst, (nuint)x);
            }
        }

        for (; x < source.Length; x++)
        {
            var p = source[x];
            target[x] = Luma((byte)(p >> 16), (byte)(p >> 8), (byte)p);
        }
    }

    private static Vector128<uint> Luma4(Vector128<uint> p, Vector128<uint> mask, Vector128<uint> wb, Vector128<uint> wg, Vector128<uint> wr) =>
        Vector128.ShiftRightLogical(
            (p & mask) * wb + (Vector128.ShiftRightLogical(p, 8) & mask) * wg + (Vector128.ShiftRightLogical(p, 16) & mask) * wr,
            8);
}
