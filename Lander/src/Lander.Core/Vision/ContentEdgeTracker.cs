using System.Runtime.CompilerServices;
using Lander.Core.Geometry;

namespace Lander.Core.Vision;

/// <summary>
/// Borde seguido de un escaneo a otro, en coordenadas locales de su ventana (origen en la esquina superior
/// izquierda). Guarda una franja de píxeles alrededor del borde para comprobar que no ha cambiado y para
/// medir cuánto se desplaza con el scroll.
/// </summary>
public sealed class TrackedEdge
{
    internal TrackedEdge(int id, int y, int left, int right, bool rising, int contrast)
    {
        Id = id;
        Y = y;
        Left = left;
        Right = right;
        Rising = rising;
        Contrast = contrast;
    }

    /// <summary>Identificador estable mientras el borde siga ahí (distinto para cada borde de la ventana).</summary>
    public int Id { get; }

    /// <summary>Superficie del borde (fila del elemento) relativa a la ventana.</summary>
    public int Y { get; internal set; }

    public int Left { get; internal set; }

    public int Right { get; internal set; }

    public bool Rising { get; }

    public int Contrast { get; internal set; }

    /// <summary>Escaneos seguidos en los que se ha visto sin cambios.</summary>
    public int Hits { get; internal set; }

    public int Length => Right - Left;

    /// <summary>Muestras de la franja, por filas.</summary>
    internal byte[] Band { get; set; } = [];

    /// <summary>Fila local de la primera fila de la franja.</summary>
    internal int BandTop { get; set; }

    internal int BandRows { get; set; }

    /// <summary>Columna local de la primera muestra y distancia entre muestras.</summary>
    internal int BandLeft { get; set; }

    internal int BandStep { get; set; } = 1;

    internal int BandColumns { get; set; }

    /// <summary>Columnas de la franja que se vieron de verdad (no tapadas por una zona desconocida).</summary>
    internal bool[] BandValid { get; set; } = [];
}

/// <summary>
/// Sigue los bordes de contenido de una ventana entre escaneos: conserva su identidad, los confirma cuando
/// aparecen igual en escaneos seguidos (descarta así vídeos y animaciones) y aplica los desplazamientos del
/// scroll.
/// </summary>
public sealed class ContentEdgeTracker
{
    /// <summary>Filas de la franja por encima de la superficie (fondo liso, sirve para alinear).</summary>
    public const int BandAbove = 6;

    /// <summary>Filas de la franja desde la superficie hacia abajo (lo que distingue un borde de otro).</summary>
    public const int BandBelow = 18;

    /// <summary>
    /// Para saber si es el mismo borde basta con el fondo de encima, el borde y unas pocas filas: lo que haya
    /// más abajo (un texto que cambia, una hora) no debe hacerle perder la identidad.
    /// </summary>
    public const int IdentityRows = 4;

    /// <summary>Desplazamiento máximo entre dos escaneos para reconocer un borde que se ha movido con el scroll.</summary>
    public const int MaxRematchShift = 600;

    /// <summary>Muestras por fila de la franja como mucho.</summary>
    private const int MaxBandColumns = 160;

    /// <summary>Diferencia media (0-255) a partir de la cual la franja ha cambiado.</summary>
    public const double ChangedThreshold = 6;

    private List<TrackedEdge> _edges = [];
    private int _nextId = 1;

    /// <summary>Escaneos seguidos sin cambios necesarios para confirmar un borde.</summary>
    public int ConfirmScans { get; init; } = 2;

    public IReadOnlyList<TrackedEdge> Edges => _edges;

    public IEnumerable<TrackedEdge> Confirmed => _edges.Where(e => e.Hits >= ConfirmScans);

    public TrackedEdge? Find(int id) => _edges.Find(e => e.Id == id);

    /// <summary>
    /// Incorpora un escaneo nuevo. <paramref name="offsetX"/> y <paramref name="offsetY"/> sitúan el píxel
    /// (0, 0) de la imagen en coordenadas locales de la ventana. Los bordes que ya no aparecen se olvidan.
    /// </summary>
    /// <param name="unknown">Zonas que no se deben comparar (el propio lander), en coordenadas locales.</param>
    public void Update(GrayImage image, int offsetX, int offsetY, IReadOnlyList<ContentEdge> detected, IReadOnlyList<PixelRect>? unknown = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(detected);

        var used = new bool[_edges.Count];
        var matches = new TrackedEdge?[detected.Count];

        // Primero, los que siguen en su sitio (±1 fila).
        for (var d = 0; d < detected.Count; d++)
        {
            var found = detected[d];
            var y = found.Y + offsetY;
            var left = found.Left + offsetX;
            var right = found.Right + offsetX;

            var match = -1;
            var bestOverlap = 0;
            for (var i = 0; i < _edges.Count; i++)
            {
                var old = _edges[i];
                if (used[i] || old.Rising != found.Rising || Math.Abs(old.Y - y) > 1)
                {
                    continue;
                }

                var overlap = Math.Min(old.Right, right) - Math.Max(old.Left, left);
                if (overlap > bestOverlap && overlap >= 0.6 * Math.Min(old.Length, right - left))
                {
                    bestOverlap = overlap;
                    match = i;
                }
            }

            if (match >= 0)
            {
                used[match] = true;
                if (BandDifference(_edges[match], image, offsetX, offsetY, y - _edges[match].Y, unknown, maxRow: IdentityRows) <= ChangedThreshold)
                {
                    matches[d] = _edges[match];
                }
            }
        }

        // Después, los que se han desplazado (scroll entre dos escaneos): su franja entera debe encajar. Entre
        // los que encajan, el más cercano: en una página repetitiva (filas de una tabla) encajan varios y lo
        // prudente es suponer el desplazamiento más pequeño.
        for (var d = 0; d < detected.Count; d++)
        {
            if (matches[d] is not null)
            {
                continue;
            }

            var found = detected[d];
            var y = found.Y + offsetY;
            var left = found.Left + offsetX;
            var right = found.Right + offsetX;
            var match = -1;
            var bestDistance = int.MaxValue;
            var bestDifference = double.MaxValue;
            for (var i = 0; i < _edges.Count; i++)
            {
                var old = _edges[i];
                var overlap = Math.Min(old.Right, right) - Math.Max(old.Left, left);
                var distance = Math.Abs(old.Y - y);
                if (used[i] || old.Rising != found.Rising || distance > MaxRematchShift || distance > bestDistance
                    || overlap < 0.6 * Math.Min(old.Length, right - left))
                {
                    continue;
                }

                var difference = BandDifference(old, image, offsetX, offsetY, y - old.Y, unknown);
                if (difference <= ChangedThreshold && (distance < bestDistance || difference < bestDifference))
                {
                    bestDistance = distance;
                    bestDifference = difference;
                    match = i;
                }
            }

            if (match >= 0)
            {
                used[match] = true;
                matches[d] = _edges[match];
            }
        }

        var next = new List<TrackedEdge>(detected.Count);
        for (var d = 0; d < detected.Count; d++)
        {
            var found = detected[d];
            var y = found.Y + offsetY;
            var left = found.Left + offsetX;
            var right = found.Right + offsetX;
            var edge = matches[d];
            if (edge is not null)
            {
                edge.Y = y;
                edge.Left = left;
                edge.Right = right;
                edge.Contrast = found.Contrast;
                edge.Hits++;
            }
            else
            {
                edge = new TrackedEdge(_nextId++, y, left, right, found.Rising, found.Contrast) { Hits = 1 };
            }

            CaptureBand(edge, image, offsetX, offsetY, unknown);
            next.Add(edge);
        }

        _edges = next;
    }

    /// <summary>Desplaza todos los bordes <paramref name="dy"/> píxeles (scroll medido).</summary>
    public void Shift(int dy)
    {
        foreach (var edge in _edges)
        {
            Shift(edge, dy);
        }
    }

    /// <summary>Desplaza un borde <paramref name="dy"/> píxeles.</summary>
    public static void Shift(TrackedEdge edge, int dy)
    {
        ArgumentNullException.ThrowIfNull(edge);
        edge.Y += dy;
        edge.BandTop += dy;
    }

    /// <summary>
    /// Vuelve a tomar la franja de un borde de una imagen nueva (tras medir el scroll), para que la plantilla
    /// no se quede atrás si el contenido de alrededor cambia poco a poco.
    /// </summary>
    public static void Refresh(TrackedEdge edge, GrayImage image, int offsetX, int offsetY, IReadOnlyList<PixelRect>? unknown = null)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(image);
        CaptureBand(edge, image, offsetX, offsetY, unknown);
    }

    /// <summary>
    /// Un escaneo en el que se sabe (por otra vía) que la imagen no ha cambiado: cuenta como haber visto otra
    /// vez todos los bordes, sin leer ni analizar nada.
    /// </summary>
    public void ConfirmUnchanged()
    {
        foreach (var edge in _edges)
        {
            edge.Hits++;
        }
    }

    /// <summary>Vuelve a dejar el borde sin confirmar (lo que hay alrededor no para de cambiar).</summary>
    public void Demote(int id)
    {
        if (Find(id) is { } edge)
        {
            edge.Hits = Math.Min(edge.Hits, 1);
        }
    }

    public void Remove(int id) => _edges.RemoveAll(e => e.Id == id);

    public void Clear() => _edges.Clear();

    /// <summary>
    /// Diferencia media entre la franja guardada de <paramref name="edge"/>, desplazada <paramref name="dy"/>
    /// filas, y la misma zona de <paramref name="image"/>. Infinito si no se solapan lo suficiente.
    /// </summary>
    /// <param name="unknown">Zonas de la imagen nueva que no se comparan, en coordenadas locales.</param>
    /// <param name="columnStride">Compara solo una de cada tantas columnas (búsquedas rápidas).</param>
    /// <param name="maxRow">
    /// Compara solo hasta esa fila relativa a la superficie (sin incluir); por defecto, la franja entera.
    /// </param>
    /// <param name="rowStride">Compara solo una de cada tantas filas (búsquedas rápidas).</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double BandDifference(
        TrackedEdge edge,
        GrayImage image,
        int offsetX,
        int offsetY,
        int dy,
        IReadOnlyList<PixelRect>? unknown = null,
        int columnStride = 1,
        int maxRow = BandBelow,
        int rowStride = 1)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(image);
        columnStride = Math.Max(1, columnStride);
        rowStride = Math.Max(1, rowStride);
        var rows = Math.Clamp(edge.Y + maxRow - edge.BandTop, 0, edge.BandRows);

        long sum = 0;
        var samples = 0;
        var pixels = image.Pixels;
        var width = image.Width;
        var hasUnknown = unknown is { Count: > 0 };
        var valid = edge.BandValid;
        var columns = edge.BandColumns;
        for (var row = 0; row < rows; row += rowStride)
        {
            var localY = edge.BandTop + row + dy;
            var imageY = localY - offsetY;
            if (imageY < 0 || imageY >= image.Height)
            {
                continue;
            }

            var line = pixels.Slice(imageY * width, width);
            var band = edge.Band.AsSpan(row * columns, columns);
            for (var col = 0; col < columns; col += columnStride)
            {
                var localX = edge.BandLeft + col * edge.BandStep;
                var imageX = localX - offsetX;
                if ((uint)imageX < (uint)width && valid[col] && !(hasUnknown && IsUnknown(unknown!, localX, localY)))
                {
                    sum += Math.Abs(line[imageX] - band[col]);
                    samples++;
                }
            }
        }

        var expected = ((rows + rowStride - 1) / rowStride) * ((columns + columnStride - 1) / columnStride);
        return samples * 2 < expected || samples == 0 ? double.PositiveInfinity : (double)sum / samples;
    }

    private static bool IsUnknown(IReadOnlyList<PixelRect> unknown, int x, int y)
    {
        for (var i = 0; i < unknown.Count; i++)
        {
            if (unknown[i].Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }

    private static void CaptureBand(TrackedEdge edge, GrayImage image, int offsetX, int offsetY, IReadOnlyList<PixelRect>? unknown)
    {
        var top = Math.Max(edge.Y - BandAbove, offsetY);
        var bottom = Math.Min(edge.Y + BandBelow, offsetY + image.Height);
        var left = Math.Max(edge.Left, offsetX);
        var right = Math.Min(edge.Right, offsetX + image.Width);
        var rows = Math.Max(0, bottom - top);
        var step = Math.Max(1, (right - left + MaxBandColumns - 1) / MaxBandColumns);
        var columns = right > left ? (right - left + step - 1) / step : 0;

        if (edge.Band.Length != rows * columns)
        {
            edge.Band = new byte[rows * columns];
        }

        if (edge.BandValid.Length != columns)
        {
            edge.BandValid = new bool[columns];
        }

        for (var col = 0; col < columns; col++)
        {
            var x = left + col * step;
            edge.BandValid[col] = unknown is null || !unknown.Any(r => x >= r.Left && x < r.Right && r.Top < bottom && r.Bottom > top);
        }

        var pixels = image.Pixels;
        for (var row = 0; row < rows; row++)
        {
            var line = pixels.Slice((top + row - offsetY) * image.Width, image.Width);
            for (var col = 0; col < columns; col++)
            {
                edge.Band[row * columns + col] = line[left + col * step - offsetX];
            }
        }

        edge.BandTop = top;
        edge.BandRows = rows;
        edge.BandLeft = left;
        edge.BandStep = step;
        edge.BandColumns = columns;
    }
}
