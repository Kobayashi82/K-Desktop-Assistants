namespace Lander.Core.Geometry;

/// <summary>Intervalo semiabierto [Start, End) sobre un eje.</summary>
public readonly record struct Interval(double Start, double End)
{
    public double Length => End - Start;

    public bool IsEmpty => End <= Start;

    public bool Contains(double value) => value >= Start && value < End;

    public bool Overlaps(Interval other) => Start < other.End && other.Start < End;
}

/// <summary>Operaciones de conjuntos sobre listas ordenadas de intervalos disjuntos.</summary>
public static class Intervals
{
    /// <summary>Resta <paramref name="cut"/> de cada intervalo de <paramref name="source"/>.</summary>
    public static List<Interval> Subtract(IReadOnlyList<Interval> source, Interval cut)
    {
        var result = new List<Interval>(source.Count + 1);
        foreach (var piece in source)
        {
            if (cut.IsEmpty || !piece.Overlaps(cut))
            {
                result.Add(piece);
                continue;
            }

            if (cut.Start > piece.Start)
            {
                result.Add(new Interval(piece.Start, cut.Start));
            }

            if (cut.End < piece.End)
            {
                result.Add(new Interval(cut.End, piece.End));
            }
        }

        return result;
    }

    /// <summary>Resta todos los <paramref name="cuts"/> del intervalo <paramref name="source"/>.</summary>
    public static List<Interval> Subtract(Interval source, IEnumerable<Interval> cuts)
    {
        List<Interval> pieces = source.IsEmpty ? [] : [source];
        foreach (var cut in cuts)
        {
            if (pieces.Count == 0)
            {
                break;
            }

            pieces = Subtract(pieces, cut);
        }

        return pieces;
    }

    /// <summary>Intersección de <paramref name="source"/> con la unión de <paramref name="mask"/>.</summary>
    public static List<Interval> Intersect(IReadOnlyList<Interval> source, IReadOnlyList<Interval> mask)
    {
        var result = new List<Interval>();
        foreach (var piece in source)
        {
            foreach (var window in mask)
            {
                var start = Math.Max(piece.Start, window.Start);
                var end = Math.Min(piece.End, window.End);
                if (end > start)
                {
                    result.Add(new Interval(start, end));
                }
            }
        }

        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return Merge(result);
    }

    /// <summary>Une intervalos solapados o contiguos. La lista debe venir ordenada por inicio.</summary>
    public static List<Interval> Merge(List<Interval> sorted)
    {
        if (sorted.Count < 2)
        {
            return sorted;
        }

        var result = new List<Interval>(sorted.Count) { sorted[0] };
        for (var i = 1; i < sorted.Count; i++)
        {
            var last = result[^1];
            var current = sorted[i];
            if (current.Start <= last.End)
            {
                result[^1] = last with { End = Math.Max(last.End, current.End) };
            }
            else
            {
                result.Add(current);
            }
        }

        return result;
    }
}
