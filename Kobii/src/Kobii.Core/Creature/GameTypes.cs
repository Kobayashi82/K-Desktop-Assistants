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

    /// <summary>
    /// Jugando o haciendo algo paso a paso (una rutina): la mariposa, la pelota, comer, barrer, bailar, cambiarse de
    /// ropa… La rutina mueve a mano las partes del cuerpo que necesita.
    /// </summary>
    Play,
}

/// <summary>A qué está jugando (o qué está haciendo paso a paso).</summary>
public enum PlayKind
{
    Butterfly,
    Ball,

    /// <summary>Saca la pelota de detrás de la espalda (o la guarda).</summary>
    TakeOut,

    Meal,
    Notification,
    Dance,
    Sweep,
    Dress,

    /// <summary>Va a por la pelota que le has lanzado y te la trae.</summary>
    Fetch,

    /// <summary>Con su peluche (abrazarlo, pasearlo o llevárselo a dormir).</summary>
    Plush,

    /// <summary>En su cama (botar, sentarse o dormir).</summary>
    Bed,

    /// <summary>Le haces cosquillas en la barriga.</summary>
    Tickles,

    /// <summary>Le cepillas.</summary>
    Brush,

    /// <summary>Chocar los cinco.</summary>
    HighFive,

    /// <summary>Darse la mano.</summary>
    Handshake,
}

/// <summary>Cómo acaba la mariposa (para las pruebas y las vistas previas).</summary>
public enum ButterflyEnding
{
    /// <summary>La atrapa entre las manos, se asoma y la suelta.</summary>
    Catch,

    /// <summary>Se le posa en el brote.</summary>
    Perch,

    /// <summary>Se le escapa y se va.</summary>
    Escape,
}

/// <summary>La música que suena (lo que oye el host por la salida de audio).</summary>
/// <param name="Playing">Algo suena y tiene ritmo.</param>
/// <param name="Bpm">Pulsos por minuto (0 si aún no lo sabe).</param>
/// <param name="Beat">Pulso en el que va ahora (crece de uno en uno; la parte decimal es la fase).</param>
public readonly record struct MusicState(bool Playing, double Bpm, double Beat);

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

    /// <summary>Velocidad del cursor (píxeles por segundo, suavizada): uno que pasa deprisa asusta a la mariposa.</summary>
    public Vec2 CursorVelocity { get; set; }

    public MusicState Music { get; set; }

    /// <summary>Va con batería (desenchufado).</summary>
    public bool OnBattery { get; set; }

    public bool BatteryLow { get; set; }
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

    /// <summary>Se aparta: se había quedado encima de otro asistente de K-Desktop.</summary>
    SteppedAside,

    /// <summary>Saca algo de detrás de la espalda (o lo guarda): ¡puf! (el texto: «out» o «in»).</summary>
    Pulled,

    /// <summary>Un mordisco.</summary>
    Chomp,

    /// <summary>Un trago.</summary>
    Gulp,

    /// <summary>Se ha acabado lo que comía o bebía (el texto: qué era).</summary>
    Ate,

    /// <summary>Una patada a la pelota (la magnitud: lo fuerte que sale).</summary>
    Kicked,

    /// <summary>La pelota ha botado (la magnitud: lo deprisa que llegaba).</summary>
    BallBounced,

    /// <summary>Una palmada con las manos ahuecadas (la mariposa); el texto «caught» si la ha atrapado.</summary>
    Clapped,

    /// <summary>¡Achís!</summary>
    Sneezed,

    /// <summary>Una pasada de la escoba.</summary>
    Swept,

    /// <summary>Se ha puesto o quitado el pijama (el texto: «on» o «off»).</summary>
    Dressed,

    /// <summary>Te ha traído la pelota que le lanzaste.</summary>
    Fetched,

    /// <summary>Ha empezado a abrazar su peluche.</summary>
    Hugged,

    /// <summary>Ha caído en el colchón de su cama (la magnitud: lo fuerte, 0–1).</summary>
    BedBounced,

    /// <summary>Empiezan las cosquillas.</summary>
    Tickled,

    /// <summary>Una risita (la magnitud: 1 en pleno ataque de risa, por los suelos).</summary>
    Giggled,

    /// <summary>Una pasada del cepillo.</summary>
    Brushed,

    /// <summary>Se sacude después de que le cepillen.</summary>
    Shook,

    /// <summary>Ha chocado los cinco contigo.</summary>
    HighFive,

    /// <summary>Os habéis dado la mano.</summary>
    Handshake,
}

public readonly record struct GameEvent(GameEventKind Kind, Vec2 Position, double Magnitude = 0, string? Text = null);
