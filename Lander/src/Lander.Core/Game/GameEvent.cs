using Lander.Core.Geometry;
using Lander.Core.Missions;
using Lander.Core.Physics;
using Lander.Core.Progression;
using Lander.Core.Racing;
using Lander.Core.Terrain;

namespace Lander.Core.Game;

public enum GameEventKind
{
    TookOff,
    Touchdown,
    Crashed,
    Respawned,
    SupportLost,
    Launched,
    Toppled,
    Slipped,
    HitCeiling,
    HitWall,
    Boosted,
    LegsDeployed,
    LegsRetracted,
    FuelEmpty,
    ModeChanged,
    Startled,
    Celebrated,
    LevelUp,
    MissionOffered,
    MissionStarted,
    MissionCompleted,
    MissionExpired,
    CrystalCollected,
    ProgressChanged,
    RaceStarted,
    RaceCheckpoint,
    RaceFinished,
    RaceFailed,
    AchievementUnlocked,

    /// <summary>A medio vuelo ha cambiado de idea y va a otro sitio.</summary>
    ChangedMind,

    /// <summary>Ha terminado una voltereta en el aire.</summary>
    Flipped,

    /// <summary>Apaga el motor para caer antes de la frenada final.</summary>
    EngineCut,

    /// <summary>Enciende el motor a fondo para frenar en el último instante.</summary>
    Ignition,

    /// <summary>Le han acariciado con el cursor.</summary>
    Petted,

    /// <summary>Saluda al usuario que vuelve.</summary>
    Greeted,

    /// <summary>Hora en punto junto al reloj.</summary>
    HourChime,
}

/// <summary>Algo que ha pasado en la simulación y que la capa de presentación puede querer mostrar o sonar.</summary>
public sealed record GameEvent(GameEventKind Kind, Vec2 Position)
{
    public double Magnitude { get; init; }

    public Platform? Platform { get; init; }

    public LandingResult? Landing { get; init; }

    public bool Manual { get; init; }

    public int Points { get; init; }

    public int Streak { get; init; }

    public int Level { get; init; }

    public IReadOnlyList<Unlock> Unlocks { get; init; } = [];

    public ControlMode Mode { get; init; }

    public ModeChangeReason Reason { get; init; }

    public Mission? Mission { get; init; }

    public Race? Race { get; init; }

    public Achievement? Achievement { get; init; }
}
