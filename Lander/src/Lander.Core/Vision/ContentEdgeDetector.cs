using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Lander.Core.Geometry;

namespace Lander.Core.Vision;

/// <summary>Parámetros del detector, en píxeles de la imagen analizada.</summary>
public sealed record ContentEdgeOptions
{
    /// <summary>Longitud mínima de un borde (unas tres veces el ancho del lander).</summary>
    public int MinLength { get; init; } = 108;

    /// <summary>Filas libres (fondo liso) que debe haber encima del borde: la altura del lander.</summary>
    public int Clearance { get; init; } = 48;

    /// <summary>
    /// Salto de brillo mínimo entre una fila y la siguiente para contar como borde. Con 24 se ven los marcos
    /// de los temas oscuros (VS Code, GitHub); por debajo empiezan a salir bordes dentro de fotos y vídeos.
    /// </summary>
    public int MinContrast { get; init; } = 24;

    /// <summary>Variación máxima entre píxeles vecinos para que una zona cuente como fondo liso.</summary>
    public int FlatTolerance { get; init; } = 10;

    /// <summary>Huecos que se toleran dentro de un borde (antialias, juntas).</summary>
    public int MaxGap { get; init; } = 2;

    /// <summary>Máximo de bordes por imagen; se quedan los más largos y marcados.</summary>
    public int MaxEdges { get; init; } = 24;
}

/// <summary>
/// Busca bordes horizontales útiles para posarse en una imagen en escala de grises: filas donde el brillo
/// salta con fuerza respecto a la de encima, en un tramo largo y recto, con fondo liso por encima (no
/// texto ni imagen). No depende de Windows: recibe los píxeles y devuelve los bordes.
/// </summary>
/// <remarks>
/// Recorre la imagen una sola vez de arriba abajo. Por cada fila calcula, de 16 en 16 píxeles, tres
/// máscaras de bits: borde fuerte hacia más claro, borde fuerte hacia más oscuro y «ocupado» (cualquier
/// variación con la fila de encima o con el vecino de la derecha). Los tramos largos de las dos primeras
/// son candidatos; un candidato vale donde las filas ocupadas de encima, guardadas en un búfer circular,
/// dejan libre la altura pedida. Reutiliza sus búferes entre llamadas: no es seguro usar la misma instancia
/// desde varios hilos.
/// </remarks>
public sealed class ContentEdgeDetector
{
    private ulong[] _rise = [];
    private ulong[] _fall = [];
    private ulong[] _busyRing = [];
    private ulong[] _blocked = [];
    private ulong[] _unknownRow = [];
    private bool _rowMasked;
    private int _words;
    private int _ringRows;
    private readonly List<ContentEdge> _found = [];

    public IReadOnlyList<ContentEdge> Detect(GrayImage image, ContentEdgeOptions options, IReadOnlyList<PixelRect>? unknown = null)
    {
        var results = new List<ContentEdge>();
        Detect(image, options, results, unknown);
        return results;
    }

    /// <summary>Añade a <paramref name="results"/> los bordes encontrados, ordenados de arriba abajo.</summary>
    /// <param name="unknown">
    /// Zonas de la imagen que no se deben juzgar (por ejemplo, donde está dibujado el propio lander cuando no
    /// se puede excluir de la captura): no cuentan como ocupadas y un borde puede cruzarlas, pero cada borde
    /// necesita su longitud mínima fuera de ellas.
    /// </param>
    /// <remarks>Se compila optimizado desde la primera llamada: se usa pocas veces, pero cada una cuenta.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Detect(GrayImage image, ContentEdgeOptions options, List<ContentEdge> results, IReadOnlyList<PixelRect>? unknown = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(results);

        var width = image.Width;
        var height = image.Height;
        var clearance = Math.Max(2, options.Clearance);
        var minLength = Math.Max(2, options.MinLength);
        if (width < minLength || height <= clearance)
        {
            return;
        }

        var strong = (byte)Math.Clamp(options.MinContrast, 1, 255);
        var flat = (byte)Math.Clamp(options.FlatTolerance, 1, 255);
        Prepare(width, clearance);
        _found.Clear();

        var pixels = image.Pixels;
        for (var y = 1; y < height; y++)
        {
            var previous = pixels.Slice((y - 1) * width, width);
            var current = pixels.Slice(y * width, width);
            var busy = _busyRing.AsSpan(y % _ringRows * _words, _words);
            ComputeMasks(previous, current, strong, flat, _rise, _fall, busy);
            if (unknown is { Count: > 0 })
            {
                MaskUnknown(unknown, y, width, busy);
            }

            // La zona de encima necesita «clearance» filas completas dentro de la imagen.
            if (y < clearance)
            {
                continue;
            }

            if (HasCandidates(_rise, width, minLength))
            {
                CollectEdges(_rise, y, rising: true, previous, current, options, minLength, clearance);
            }

            if (HasCandidates(_fall, width, minLength))
            {
                CollectEdges(_fall, y, rising: false, previous, current, options, minLength, clearance);
            }
        }

        if (_found.Count > options.MaxEdges)
        {
            _found.Sort((a, b) => Score(b).CompareTo(Score(a)));
            _found.RemoveRange(options.MaxEdges, _found.Count - options.MaxEdges);
        }

        _found.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Left.CompareTo(b.Left));
        results.AddRange(_found);
    }

    private static long Score(ContentEdge edge) => (long)edge.Length * edge.Contrast;

    private void Prepare(int width, int clearance)
    {
        _words = (width + 63) >> 6;

        // Filas ocupadas que hay que recordar: las «clearance - 1» de encima más la actual.
        _ringRows = clearance;
        if (_rise.Length < _words)
        {
            _rise = new ulong[_words];
            _fall = new ulong[_words];
            _blocked = new ulong[_words];
            _unknownRow = new ulong[_words];
        }

        if (_busyRing.Length < _ringRows * _words)
        {
            _busyRing = new ulong[_ringRows * _words];
        }

        _rowMasked = false;
    }

    /// <summary>Dentro de las zonas desconocidas de esta fila: nada ocupado y borde en las dos polaridades.</summary>
    private void MaskUnknown(IReadOnlyList<PixelRect> unknown, int y, int width, Span<ulong> busy)
    {
        var row = _unknownRow.AsSpan(0, _words);
        row.Clear();
        _rowMasked = false;
        foreach (var rect in unknown)
        {
            if (y >= rect.Top && y < rect.Bottom)
            {
                SetBits(row, Math.Max(0, rect.Left), Math.Min(width, rect.Right));
                _rowMasked = true;
            }
        }

        if (!_rowMasked)
        {
            return;
        }

        for (var w = 0; w < _words; w++)
        {
            busy[w] &= ~row[w];
            _rise[w] |= row[w];
            _fall[w] |= row[w];
        }
    }

    private static void SetBits(Span<ulong> mask, int from, int to)
    {
        for (var x = from; x < to; x++)
        {
            mask[x >> 6] |= 1UL << (x & 63);
        }
    }

    /// <summary>Bits activos en [from, to).</summary>
    internal static int CountBits(ReadOnlySpan<ulong> mask, int from, int to)
    {
        var count = 0;
        for (var x = from; x < to;)
        {
            var word = x >> 6;
            var start = x & 63;
            var end = Math.Min(64, start + (to - x));
            var bits = mask[word] >> start;
            if (end - start < 64)
            {
                bits &= (1UL << (end - start)) - 1;
            }

            count += BitOperations.PopCount(bits);
            x += end - start;
        }

        return count;
    }

    /// <summary>Máscaras de una fila respecto a la de encima.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ComputeMasks(
        ReadOnlySpan<byte> previous,
        ReadOnlySpan<byte> current,
        byte strong,
        byte flat,
        Span<ulong> rise,
        Span<ulong> fall,
        Span<ulong> busy)
    {
        rise.Clear();
        fall.Clear();
        busy.Clear();

        var width = current.Length;
        var x = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var strongLimit = Vector128.Create((byte)(strong - 1));
            var flatLimit = Vector128.Create((byte)(flat - 1));
            ref var up = ref MemoryMarshal.GetReference(previous);
            ref var here = ref MemoryMarshal.GetReference(current);

            // Se lee también el vecino de la derecha (x + 1), así que el último bloque va por la vía escalar.
            for (; x + Vector128<byte>.Count < width; x += Vector128<byte>.Count)
            {
                var a = Vector128.LoadUnsafe(ref here, (nuint)x);
                var b = Vector128.LoadUnsafe(ref up, (nuint)x);
                var c = Vector128.LoadUnsafe(ref here, (nuint)(x + 1));
                var vertical = Vector128.Max(a, b) - Vector128.Min(a, b);
                var horizontal = Vector128.Max(a, c) - Vector128.Min(a, c);
                var isStrong = Vector128.GreaterThan(vertical, strongLimit);
                var brighter = Vector128.GreaterThan(a, b);
                var isBusy = Vector128.GreaterThan(vertical, flatLimit) | Vector128.GreaterThan(horizontal, flatLimit);

                var word = x >> 6;
                var shift = x & 63;
                rise[word] |= (ulong)(isStrong & brighter).ExtractMostSignificantBits() << shift;
                fall[word] |= (ulong)Vector128.AndNot(isStrong, brighter).ExtractMostSignificantBits() << shift;
                busy[word] |= (ulong)isBusy.ExtractMostSignificantBits() << shift;
            }
        }

        for (; x < width; x++)
        {
            int a = current[x];
            int b = previous[x];
            var c = x + 1 < width ? current[x + 1] : a;
            var vertical = Math.Abs(a - b);
            var bit = 1UL << (x & 63);
            if (vertical >= strong)
            {
                if (a > b)
                {
                    rise[x >> 6] |= bit;
                }
                else
                {
                    fall[x >> 6] |= bit;
                }
            }

            if (vertical >= flat || Math.Abs(a - c) >= flat)
            {
                busy[x >> 6] |= bit;
            }
        }
    }

    /// <summary>Descarte rápido: una fila sin suficientes bits activos no puede tener un tramo largo.</summary>
    private static bool HasCandidates(ReadOnlySpan<ulong> mask, int width, int minLength)
    {
        var count = 0;
        foreach (var word in mask)
        {
            count += BitOperations.PopCount(word);
        }

        return count >= minLength * 3 / 4 && width >= minLength;
    }

    private void CollectEdges(
        ReadOnlySpan<ulong> mask,
        int y,
        bool rising,
        ReadOnlySpan<byte> previous,
        ReadOnlySpan<byte> current,
        ContentEdgeOptions options,
        int minLength,
        int clearance)
    {
        var width = current.Length;
        var start = NextSet(mask, 0, width);
        while (start < width)
        {
            // Tramo de bits activos, saltando huecos pequeños.
            var end = NextClear(mask, start, width);
            var next = NextSet(mask, end, width);
            while (next < width && next - end <= options.MaxGap)
            {
                end = NextClear(mask, next, width);
                next = NextSet(mask, end, width);
            }

            if (end - start >= minLength)
            {
                AddFreePieces(y, start, end, rising, previous, current, minLength, clearance);
            }

            start = next;
        }
    }

    /// <summary>Partes del tramo [start, end) con la zona de encima libre durante «clearance» filas.</summary>
    private void AddFreePieces(int y, int start, int end, bool rising, ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current, int minLength, int clearance)
    {
        var firstWord = start >> 6;
        var lastWord = (end - 1) >> 6;
        var blocked = _blocked.AsSpan();
        blocked.Slice(firstWord, lastWord - firstWord + 1).Clear();

        // Filas y-clearance+1 … y-1: cada una se compara con la de encima y con el vecino de la derecha.
        for (var row = y - clearance + 1; row < y; row++)
        {
            var busy = _busyRing.AsSpan(row % _ringRows * _words, _words);
            for (var w = firstWord; w <= lastWord; w++)
            {
                blocked[w] |= busy[w];
            }
        }

        var unknownRow = _unknownRow.AsSpan(0, _words);
        var pieceStart = NextClear(blocked, start, end);
        while (pieceStart < end)
        {
            var pieceEnd = NextSet(blocked, pieceStart, end);
            var hidden = _rowMasked ? CountBits(unknownRow, pieceStart, pieceEnd) : 0;
            if (pieceEnd - pieceStart - hidden >= minLength)
            {
                var sum = 0;
                for (var x = pieceStart; x < pieceEnd; x++)
                {
                    if (!_rowMasked || (unknownRow[x >> 6] & (1UL << (x & 63))) == 0)
                    {
                        sum += Math.Abs(current[x] - previous[x]);
                    }
                }

                _found.Add(new ContentEdge(y, pieceStart, pieceEnd, sum / (pieceEnd - pieceStart - hidden), rising));
            }

            pieceStart = NextClear(blocked, pieceEnd, end);
        }
    }

    /// <summary>Primer bit activo en [from, limit), o <paramref name="limit"/> si no hay.</summary>
    internal static int NextSet(ReadOnlySpan<ulong> mask, int from, int limit)
    {
        if (from >= limit)
        {
            return limit;
        }

        var word = from >> 6;
        var bits = mask[word] & (~0UL << (from & 63));
        while (bits == 0)
        {
            if (++word << 6 >= limit)
            {
                return limit;
            }

            bits = mask[word];
        }

        return Math.Min(limit, (word << 6) + BitOperations.TrailingZeroCount(bits));
    }

    /// <summary>Primer bit inactivo en [from, limit), o <paramref name="limit"/> si no hay.</summary>
    internal static int NextClear(ReadOnlySpan<ulong> mask, int from, int limit)
    {
        if (from >= limit)
        {
            return limit;
        }

        var word = from >> 6;
        var bits = ~mask[word] & (~0UL << (from & 63));
        while (bits == 0)
        {
            if (++word << 6 >= limit)
            {
                return limit;
            }

            bits = ~mask[word];
        }

        return Math.Min(limit, (word << 6) + BitOperations.TrailingZeroCount(bits));
    }
}
