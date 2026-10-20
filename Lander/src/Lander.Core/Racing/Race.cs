using System.Globalization;
using Lander.Core.Terrain;

namespace Lander.Core.Racing;

public enum RaceStatus
{
    /// <summary>Circuito trazado; el cronómetro arranca al despegar.</summary>
    Ready,
    Running,
    Finished,
    Failed,
}

public enum RaceFailure
{
    None,
    Crashed,
    TimeUp,
    Abandoned,
}

public enum RaceMedal
{
    None,
    Bronze,
    Silver,
    Gold,
}

/// <summary>Una baliza del circuito: la ventana sobre la que hay que posarse o, al final, la base.</summary>
/// <param name="OwnerId">Ventana de la baliza; 0 para la base (cualquier suelo vale).</param>
/// <param name="AppId">Aplicación de la ventana, para mostrar su icono.</param>
public sealed record RaceCheckpoint(long OwnerId, string? AppId)
{
    public bool IsBase => OwnerId == 0;
}

/// <summary>Una carrera contrarreloj por un circuito de ventanas marcado con balizas.</summary>
public sealed class Race
{
    /// <summary>Segundos que se añaden por posarse de forma brusca en una baliza.</summary>
    public const double HardLandingPenalty = 2;

    /// <summary>Margen sobre el tiempo de referencia para la medalla de plata.</summary>
    public const double SilverFactor = 1.5;

    public required IReadOnlyList<RaceCheckpoint> Checkpoints { get; init; }

    /// <summary>Tiempo de referencia para la medalla de oro, según la longitud del circuito.</summary>
    public required double ParSeconds { get; init; }

    /// <summary>Tiempo máximo antes de darla por perdida.</summary>
    public required double TimeLimit { get; init; }

    /// <summary>Identificador del circuito (la secuencia de ventanas), para los récords.</summary>
    public required string CircuitKey { get; init; }

    public RaceStatus Status { get; internal set; } = RaceStatus.Ready;

    public RaceFailure Failure { get; internal set; }

    /// <summary>Índice de la próxima baliza.</summary>
    public int Next { get; internal set; }

    /// <summary>Tiempo transcurrido, penalizaciones incluidas.</summary>
    public double Elapsed { get; internal set; }

    public double Penalties { get; internal set; }

    /// <summary>La última baliza se alcanzó con un aterrizaje brusco (y sumó la penalización).</summary>
    public bool LastLegPenalized { get; internal set; }

    public RaceMedal Medal { get; internal set; }

    /// <summary>Mejor tiempo anterior en este mismo circuito, si lo hay.</summary>
    public double? PreviousRecord { get; init; }

    public bool IsNewRecord { get; internal set; }

    /// <summary>Segundos desde que terminó (para dejar el resultado a la vista un momento).</summary>
    public double ResultAge { get; internal set; }

    public bool IsActive => Status is RaceStatus.Ready or RaceStatus.Running;

    public RaceCheckpoint? NextCheckpoint => IsActive && Next < Checkpoints.Count ? Checkpoints[Next] : null;

    /// <summary>Progreso de 0 a 1.</summary>
    public double Progress => Checkpoints.Count == 0 ? 0 : Next / (double)Checkpoints.Count;

    /// <summary>Número de ventanas del circuito (sin contar la vuelta a la base).</summary>
    public int WindowCount => Checkpoints.Count(c => !c.IsBase);

    /// <summary>Una carrera de muestra en un estado concreto, para previsualizar el HUD.</summary>
    public static Race Sample(IReadOnlyList<RaceCheckpoint> checkpoints, RaceStatus status, int next, double elapsed, RaceMedal medal = RaceMedal.None) =>
        new()
        {
            Checkpoints = checkpoints,
            ParSeconds = 30,
            TimeLimit = 90,
            CircuitKey = KeyOf(checkpoints),
            Status = status,
            Next = next,
            Elapsed = elapsed,
            Medal = medal,
        };

    public static RaceMedal MedalFor(double seconds, double par) =>
        seconds <= par ? RaceMedal.Gold : seconds <= par * SilverFactor ? RaceMedal.Silver : RaceMedal.Bronze;

    /// <summary>«12,3» o «1:02,3».</summary>
    public static string FormatTime(double seconds)
    {
        seconds = Math.Max(0, seconds);
        var minutes = (int)(seconds / 60);
        var rest = seconds - minutes * 60;
        var tenths = rest.ToString("0.0", CultureInfo.CurrentCulture);
        if (minutes == 0)
        {
            return tenths;
        }

        return minutes.ToString(CultureInfo.CurrentCulture) + ":" + (rest < 10 ? "0" : string.Empty) + tenths;
    }

    internal static string KeyOf(IEnumerable<RaceCheckpoint> checkpoints) =>
        string.Join(",", checkpoints.Select(c => c.OwnerId.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Punto de la baliza: el centro del tramo visible más ancho de su ventana.</summary>
    public static Platform? TargetPlatform(World world, RaceCheckpoint checkpoint) =>
        checkpoint.IsBase ? null : world.SegmentsOf(checkpoint.OwnerId).MaxBy(p => p.Width);
}
