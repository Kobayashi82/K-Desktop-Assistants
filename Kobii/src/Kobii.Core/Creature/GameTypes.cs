using Kobii.Core.Geometry;

namespace Kobii.Core.Creature;

/// <summary>Lo que está haciendo.</summary>
public enum Activity
{
    Idle,

    /// <summary>Andando (o corriendo) hasta un punto de su plataforma.</summary>
    Walk,

    /// <summary>Haciendo animaciones por su cuenta (o una reacción).</summary>
    Perform,

    /// <summary>Atento a ti: te escucha, piensa la respuesta o te habla.</summary>
    Attend,

    /// <summary>Cogido con el ratón.</summary>
    Held,

    Fall,

    /// <summary>Un saltito de alegría.</summary>
    Celebrate,

    /// <summary>Dormido (llevas mucho rato sin tocar el PC).</summary>
    Sleep,

    /// <summary>De camino a otro sitio: andando, saltando entre ventanas, trepando o descolgándose.</summary>
    Travel,
}

/// <summary>Lo que hace el módulo Asistente (escuchar, pensar, hablar…), dicho sin depender del SDK.</summary>
public enum Attention
{
    None,
    Listening,
    Thinking,
    Speaking,

    /// <summary>Espera a que contestes sí o no.</summary>
    Waiting,

    /// <summary>Haciendo una orden (los gestos lo mueven).</summary>
    Busy,
}

/// <summary>Cómo reacciona a una orden (las del SDK: <c>Reaction</c>).</summary>
public enum ReactionKind
{
    Success,
    Warning,
    Failure,
    Celebration,
}

/// <summary>Lo que el juego sabe del usuario y del PC en cada paso.</summary>
public sealed class Sensors
{
    public Vec2? Cursor { get; set; }

    /// <summary>Segundos que lleva el cursor quieto.</summary>
    public double CursorStillSeconds { get; set; }

    public double UserIdleSeconds { get; set; }

    public DateTime LocalTime { get; set; } = DateTime.Now;

    /// <summary>«No molestar»: quieto y callado.</summary>
    public bool Quiet { get; set; }
}

public sealed record GameOptions
{
    /// <summary>Tamaño elegido (1 = el normal).</summary>
    public double Size { get; init; } = 1;

    /// <summary>Lo movido que es (1 normal; menos, se queda más rato quieto).</summary>
    public double Liveliness { get; init; } = 1;

    /// <summary>Se acerca a curiosear el cursor si lo dejas quieto cerca.</summary>
    public bool FollowCursor { get; init; } = true;
}

public enum GameEventKind
{
    /// <summary>Una caricia (clic).</summary>
    Petted,

    /// <summary>Una pasada al frotarle.</summary>
    Rubbed,

    Grabbed,
    Thrown,

    /// <summary>Ha tocado suelo (la magnitud es la velocidad en DIP/s).</summary>
    Landed,

    /// <summary>Un paso al andar.</summary>
    Step,

    /// <summary>Un salto suyo.</summary>
    Hop,

    FellAsleep,
    WokeUp,

    /// <summary>Te saluda al volver.</summary>
    Greeted,

    /// <summary>Ha hecho la animación de una reacción (el texto es cuál).</summary>
    Reacted,

    /// <summary>Ha empezado una animación por su cuenta (el texto es su id).</summary>
    Performed,

    /// <summary>Se ha encaramado a lo alto de una ventana.</summary>
    Climbed,

    /// <summary>Ha llegado adonde iba (el texto: «window» o «ground»).</summary>
    Arrived,

    /// <summary>No ha encontrado forma de llegar adonde iba.</summary>
    Lost,
}

public readonly record struct GameEvent(GameEventKind Kind, Vec2 Position, double Magnitude = 0, string? Text = null);
