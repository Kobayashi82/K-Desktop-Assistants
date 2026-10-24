namespace Kumo.Core;

/// <summary>Fuente de aleatoriedad inyectable para que la simulación sea reproducible en los tests.</summary>
public interface IRandom
{
    /// <summary>Número en [0, 1).</summary>
    double NextDouble();
}

public sealed class GameRandom(int? seed = null) : IRandom
{
    private readonly Random _random = seed is { } s ? new Random(s) : new Random();

    public double NextDouble() => _random.NextDouble();
}

public static class RandomExtensions
{
    public static double Range(this IRandom random, double min, double max) => min + (max - min) * random.NextDouble();

    public static int Next(this IRandom random, int maxExclusive) =>
        maxExclusive <= 0 ? 0 : Math.Min(maxExclusive - 1, (int)(random.NextDouble() * maxExclusive));

    public static bool Chance(this IRandom random, double probability) => random.NextDouble() < probability;

    public static double Sign(this IRandom random) => random.NextDouble() < 0.5 ? -1 : 1;

    public static T Pick<T>(this IRandom random, IReadOnlyList<T> items) => items[random.Next(items.Count)];

    /// <summary>Elige un índice con probabilidad proporcional a su peso. Devuelve -1 si no hay pesos positivos.</summary>
    public static int PickWeighted(this IRandom random, IReadOnlyList<double> weights)
    {
        var total = 0.0;
        foreach (var w in weights)
        {
            total += Math.Max(0, w);
        }

        if (total <= 0)
        {
            return -1;
        }

        var roll = random.NextDouble() * total;
        for (var i = 0; i < weights.Count; i++)
        {
            roll -= Math.Max(0, weights[i]);
            if (roll < 0)
            {
                return i;
            }
        }

        return weights.Count - 1;
    }
}
