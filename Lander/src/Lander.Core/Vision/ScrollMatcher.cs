using System.Runtime.CompilerServices;
using Lander.Core.Geometry;

namespace Lander.Core.Vision;

/// <summary>Resultado de buscar cuánto se ha desplazado un borde.</summary>
/// <param name="Shift">Filas que se ha movido (negativo: hacia arriba, como al bajar por una página).</param>
/// <param name="Difference">Diferencia media de la franja en ese desplazamiento (0-255).</param>
/// <param name="Found">Si la franja encaja de verdad en algún sitio del margen de búsqueda.</param>
public readonly record struct ScrollMatch(int Shift, double Difference, bool Found);

/// <summary>
/// Mide el scroll: busca, dentro de ±N filas, dónde está ahora la franja guardada de un borde (correlación
/// vertical por suma de diferencias). Primero prueba todos los desplazamientos con una de cada ocho columnas
/// y una de cada dos filas, y después afina los mejores con todas. Ante dos encajes parecidos (una tabla con filas iguales) prefiere el
/// más cercano al desplazamiento previsto por la velocidad del scroll.
/// </summary>
public static class ScrollMatcher
{
    /// <summary>Diferencia media máxima para dar el encaje por bueno.</summary>
    public const double MaxDifference = ContentEdgeTracker.ChangedThreshold * 1.5;

    /// <summary>Penalización por cada fila de distancia al desplazamiento previsto.</summary>
    private const double DistancePenalty = 0.02;

    private const int CoarseColumns = 8;
    private const int CoarseRows = 2;
    private const int Candidates = 4;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static ScrollMatch Find(
        TrackedEdge edge,
        GrayImage image,
        int offsetX,
        int offsetY,
        int maxShift,
        int predicted = 0,
        IReadOnlyList<PixelRect>? unknown = null)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(image);

        // Pasada gruesa: todos los desplazamientos, pocas columnas.
        Span<(int Shift, double Score)> best = stackalloc (int, double)[Candidates];
        best.Fill((0, double.PositiveInfinity));
        for (var dy = -maxShift; dy <= maxShift; dy++)
        {
            var score = ContentEdgeTracker.BandDifference(edge, image, offsetX, offsetY, dy, unknown, CoarseColumns, rowStride: CoarseRows)
                + DistancePenalty * Math.Abs(dy - predicted);
            if (score < best[^1].Score)
            {
                var i = Candidates - 1;
                while (i > 0 && best[i - 1].Score > score)
                {
                    best[i] = best[i - 1];
                    i--;
                }

                best[i] = (dy, score);
            }
        }

        // Pasada fina sobre los mejores candidatos y sus vecinos inmediatos.
        var result = new ScrollMatch(0, double.PositiveInfinity, false);
        var bestScore = double.PositiveInfinity;
        foreach (var (shift, score) in best)
        {
            if (double.IsPositiveInfinity(score))
            {
                continue;
            }

            for (var dy = shift - 1; dy <= shift + 1; dy++)
            {
                if (dy < -maxShift || dy > maxShift)
                {
                    continue;
                }

                var difference = ContentEdgeTracker.BandDifference(edge, image, offsetX, offsetY, dy, unknown);
                var total = difference + DistancePenalty * Math.Abs(dy - predicted);
                if (total < bestScore)
                {
                    bestScore = total;
                    result = new ScrollMatch(dy, difference, difference <= MaxDifference);
                }
            }
        }

        return result;
    }
}
