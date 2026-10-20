using Lander.Core.Terrain;

namespace Lander.Core.Physics;

public enum PhysicsEventKind
{
    TookOff,
    Touchdown,
    Crashed,

    /// <summary>La plataforma desapareció (ventana cerrada, minimizada o tapada) o el lander se salió de ella.</summary>
    SupportLost,

    /// <summary>La plataforma frenó en seco al subir y lanzó el lander hacia arriba.</summary>
    Launched,

    /// <summary>La plataforma se agitó tanto que el lander volcó.</summary>
    Toppled,

    /// <summary>La plataforma aceleró más de lo que agarran las patas.</summary>
    Slipped,

    HitCeiling,
    HitWall,
    Boosted,
    LegsDeployed,
    LegsRetracted,
    FuelEmpty,

    /// <summary>El lander estaba fuera de todos los monitores y se ha recolocado.</summary>
    OutOfBounds,
}

public readonly record struct PhysicsEvent(
    PhysicsEventKind Kind,
    double Magnitude = 0,
    Platform? Platform = null,
    LandingResult? Landing = null);
