namespace Lander.Core.Terrain;

public enum BarrierKind
{
    /// <summary>Borde superior de un monitor: impide subir.</summary>
    Ceiling,

    /// <summary>Borde inferior de un monitor: red de seguridad bajo el suelo.</summary>
    Floor,

    /// <summary>Borde izquierdo de un monitor: impide moverse a la izquierda.</summary>
    LeftWall,

    /// <summary>Borde derecho de un monitor: impide moverse a la derecha.</summary>
    RightWall,
}

/// <summary>
/// Límite sólido del mundo. Los techos y suelos son horizontales (<see cref="Position"/> es una Y y el
/// tramo va de <see cref="From"/> a <see cref="To"/> en X); las paredes son verticales.
/// </summary>
public readonly record struct Barrier(BarrierKind Kind, double Position, double From, double To)
{
    public bool IsHorizontal => Kind is BarrierKind.Ceiling or BarrierKind.Floor;

    public bool Spans(double value) => value >= From && value <= To;
}
