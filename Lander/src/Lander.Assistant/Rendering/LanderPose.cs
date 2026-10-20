using Lander.Core.Game;
using Lander.Core.Geometry;

namespace Lander.Assistant.Rendering;

/// <summary>Todo lo que un aspecto necesita para dibujar el lander en un instante.</summary>
internal readonly record struct LanderPose
{
    public double LegsDeploy { get; init; } = 1;

    public Expression Expression { get; init; }

    /// <summary>Mirada, cada componente entre -1 y 1.</summary>
    public Vec2 Look { get; init; }

    /// <summary>Párpado: 0 abierto, 1 cerrado (parpadeo).</summary>
    public double Blink { get; init; }

    public bool Night { get; init; }

    public double Time { get; init; }

    public double Damage { get; init; }

    public double MainFlame { get; init; }

    public double LeftJet { get; init; }

    public double RightJet { get; init; }

    public double LowerLeftJet { get; init; }

    public double LowerRightJet { get; init; }

    public double BoostFlash { get; init; }

    public bool CarryingPackage { get; init; }

    /// <summary>
    /// Hundimiento de la suspensión en unidades base: el cuerpo baja y las patas se doblan sin que los pies
    /// se muevan. Negativo, se estira.
    /// </summary>
    public double Compress { get; init; }

    /// <summary>Bostezo, de 0 a 1.</summary>
    public double Yawn { get; init; }

    /// <summary>Inclinación de la antena por la inercia (radianes).</summary>
    public double AntennaSway { get; init; }

    public LanderPose()
    {
    }
}
