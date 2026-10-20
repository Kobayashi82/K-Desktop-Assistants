using Lander.Core.Physics;
using Lander.Core.Terrain;

namespace Lander.Core.Progression;

public static class Scoring
{
    public const int MaxStreakBonusSteps = 4;

    public static int BasePoints(LandingQuality quality) => quality switch
    {
        LandingQuality.Perfect => 100,
        LandingQuality.Hard => 40,
        _ => 0,
    };

    /// <summary>Un borde de contenido puntúa como mínimo como una ventana estrecha (unas cuatro veces el lander).</summary>
    public const double ContentMinDifficulty = 3;

    /// <summary>
    /// Dificultad de la plataforma: la barra de tareas vale ×1; una ventana estrecha (poco más ancha que
    /// el lander) puede llegar a ×4. Los bordes del contenido, que apenas se distinguen y pueden moverse con
    /// el scroll, valen siempre como una ventana estrecha.
    /// </summary>
    public static double Difficulty(Platform platform, double landerWidth)
    {
        if (platform.Kind == PlatformKind.Ground || landerWidth <= 0)
        {
            return 1;
        }

        var ratio = platform.Width / landerWidth;
        var difficulty = Math.Clamp(1 + (8 - ratio) / 2, 1, 4);
        return platform.Kind == PlatformKind.Content ? Math.Max(difficulty, ContentMinDifficulty) : difficulty;
    }

    /// <summary>Multiplicador por racha de aterrizajes perfectos: ×1, ×1,5, ×2, ×2,5, ×3.</summary>
    public static double StreakMultiplier(int streak) => 1 + 0.5 * Math.Clamp(streak - 1, 0, MaxStreakBonusSteps);

    public static int Points(LandingQuality quality, Platform platform, double landerWidth, int streak)
    {
        var points = BasePoints(quality) * Difficulty(platform, landerWidth);
        if (quality == LandingQuality.Perfect)
        {
            points *= StreakMultiplier(streak);
        }

        return (int)Math.Round(points / 5) * 5;
    }
}
