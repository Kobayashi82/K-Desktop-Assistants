namespace Lander.Core.Progression;

/// <summary>Curva de niveles del piloto: lenta y opcional.</summary>
public static class Levels
{
    public const int MaxLevel = 20;

    /// <summary>Puntos acumulados necesarios para alcanzar <paramref name="level"/>.</summary>
    public static long PointsForLevel(int level)
    {
        if (level <= 1)
        {
            return 0;
        }

        level = Math.Min(level, MaxLevel);
        return (long)Math.Round(300 * Math.Pow(level - 1, 1.6) / 10) * 10;
    }

    public static int LevelFor(long points)
    {
        var level = 1;
        while (level < MaxLevel && points >= PointsForLevel(level + 1))
        {
            level++;
        }

        return level;
    }

    /// <summary>Fracción (0–1) recorrida hacia el siguiente nivel.</summary>
    public static double ProgressToNext(long points)
    {
        var level = LevelFor(points);
        if (level >= MaxLevel)
        {
            return 1;
        }

        var start = PointsForLevel(level);
        var end = PointsForLevel(level + 1);
        return Math.Clamp((points - start) / (double)(end - start), 0, 1);
    }
}
