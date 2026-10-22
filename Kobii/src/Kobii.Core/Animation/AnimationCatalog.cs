using Kobii.Core.Creature;
using Kobii.Core.Geometry;

namespace Kobii.Core.Animation;

/// <summary>Movimiento del cuerpo que acompaña a un clip (lo hace el juego, no el esqueleto).</summary>
public enum ClipMotion
{
    None,

    /// <summary>Rueda por el suelo hacia donde mira.</summary>
    Roll,

    /// <summary>Un salto al empezar.</summary>
    Hop,

    /// <summary>Saltitos seguidos.</summary>
    Hops,

    /// <summary>Un salto alto (voltereta).</summary>
    BigHop,
}

/// <summary>De dónde salen los efectos de un clip.</summary>
public enum EmoteSource
{
    Head,
    Hand,
    Mouth,
    Feet,
}

/// <summary>
/// Una animación descrita como datos: una postura, un gesto encima, un objeto en la mano, la cara y los
/// efectos. La cara, si no se indica, sale de su estado de ánimo: el mismo clip se ve distinto contento
/// que aburrido.
/// </summary>
/// <param name="Id">Identificador estable.</param>
/// <param name="Name">Nombre para las herramientas de desarrollo.</param>
/// <param name="Seconds">Duración (las posturas de una sola vez, como estirarse, la usan entera).</param>
/// <param name="Face">Expresión mientras dura (null: la de su ánimo).</param>
/// <param name="Emote">Efecto que suelta de vez en cuando.</param>
/// <param name="EmoteEvery">Cada cuántos segundos (0: una sola vez al empezar).</param>
/// <param name="Look">Hacia dónde mira mientras dura (null: lo de siempre).</param>
public sealed record AnimationClip(
    string Id,
    string Name,
    Pose Pose,
    Gesture Gesture = Gesture.None,
    Prop Prop = Prop.None,
    double Seconds = 4,
    Expression? Face = null,
    EmoteKind? Emote = null,
    double EmoteEvery = 0,
    EmoteSource EmoteFrom = EmoteSource.Head,
    Wear Wear = Wear.None,
    ClipMotion Motion = ClipMotion.None,
    Vec2? Look = null)
{
    /// <summary>Se mueve todo el rato (hay que dibujarlo a la frecuencia alta).</summary>
    public bool Lively => Poses.Info(Pose).Lively || (Gesture != Gesture.None && Gesture is not (Gesture.Hold or Gesture.HoldBoth or Gesture.HoldUp or Gesture.Think or Gesture.CoverEyes or Gesture.Hug or Gesture.HoldBelly or Gesture.Back))
        || Motion != ClipMotion.None || Prop is Prop.Ball or Prop.Balloon;
}

/// <summary>
/// Las animaciones de Kobii. Cada una se arma con piezas (postura, gesto, objeto, cara y efectos) y la
/// cara cambia con su ánimo, así que de unas pocas piezas salen cientos de animaciones distintas.
/// </summary>
public static class AnimationCatalog
{
    private static readonly Vec2 Down = new(0, 0.55);
    private static readonly Vec2 Up = new(0.3, -0.8);

    public static IReadOnlyList<AnimationClip> All { get; } =
    [
        // Descansar.
        new("sentarse", "Sentarse", Pose.Sit, Seconds: 8),
        new("sentarse-mirar", "Sentarse a mirar", Pose.Sit, Gesture.LookAround, Seconds: 7, Face: Expression.Curious),
        new("tumbarse", "Tumbarse", Pose.Lie, Seconds: 10, Face: Expression.Happy),
        new("derretirse", "Derretirse", Pose.Flop, Seconds: 6, Face: Expression.Sleepy),
        new("acurrucarse", "Acurrucarse", Pose.Curl, Seconds: 8, Face: Expression.Sleepy),
        new("cabezadas", "Dar cabezadas", Pose.Doze, Seconds: 12, Face: Expression.Sleeping, Emote: EmoteKind.Sleep, EmoteEvery: 2.6),
        new("siesta", "Siesta", Pose.Lie, Seconds: 25, Face: Expression.Sleeping, Emote: EmoteKind.Sleep, EmoteEvery: 2.6),
        new("siesta-almohada", "Siesta con almohada", Pose.Curl, Gesture.Hug, Prop.Pillow, Seconds: 25, Face: Expression.Sleeping, Emote: EmoteKind.Sleep, EmoteEvery: 2.6),
        new("sentarse-borde", "Sentarse en el borde", Pose.SitEdge, Seconds: 12, Face: Expression.Happy),
        new("asomarse", "Asomarse por el borde", Pose.Peek, Seconds: 4, Face: Expression.Curious, Emote: EmoteKind.Exclamation, Look: new Vec2(0.6, 0.9)),

        // Despertar y estirarse.
        new("estirarse", "Estirarse", Pose.Stretch, Seconds: 2.4, Face: Expression.Joy),
        new("bostezar", "Bostezar", Pose.Stand, Gesture.Yawn, Seconds: 2.2, Face: Expression.Yawn),
        new("bostezar-sentado", "Bostezar sentado", Pose.Sit, Gesture.Yawn, Seconds: 2.2, Face: Expression.Yawn),
        new("frotarse-ojos", "Frotarse los ojos", Pose.Sit, Gesture.RubEyes, Seconds: 2, Face: Expression.Sleepy),
        new("puntillas", "De puntillas", Pose.Tiptoe, Gesture.LookAround, Seconds: 3, Face: Expression.Curious, Look: Up),

        // Alegría y juego.
        new("bailar", "Bailar", Pose.Dance, Seconds: 8, Face: Expression.Joy, Emote: EmoteKind.Note, EmoteEvery: 0.8),
        new("bailar-palmas", "Bailar con palmas", Pose.Sway, Gesture.Clap, Seconds: 6, Face: Expression.Happy, Emote: EmoteKind.Note, EmoteEvery: 1),
        new("aplaudir", "Aplaudir", Pose.Stand, Gesture.Clap, Seconds: 2.5, Face: Expression.Joy, Emote: EmoteKind.Sparkle, EmoteEvery: 0.8, EmoteFrom: EmoteSource.Hand),
        new("brazos-arriba", "¡Bien!", Pose.Hop, Gesture.ArmsUp, Seconds: 2, Face: Expression.Joy, Emote: EmoteKind.Sparkle, Motion: ClipMotion.Hop),
        new("voltereta", "Voltereta", Pose.Flip, Seconds: 1.1, Face: Expression.Joy, Emote: EmoteKind.Sparkle, Motion: ClipMotion.BigHop),
        new("rodar", "Rodar", Pose.Roll, Seconds: 2.2, Face: Expression.Joy, Motion: ClipMotion.Roll),
        new("menearse", "Menear el culete", Pose.Stand, Gesture.Wiggle, Seconds: 2.5, Face: Expression.Happy),
        new("saltitos", "Saltitos", Pose.Hop, Seconds: 2.4, Face: Expression.Joy, Motion: ClipMotion.Hops),
        new("pelota-botar", "Botar la pelota", Pose.Stand, Gesture.Dribble, Prop.Ball, Seconds: 6, Face: Expression.Happy),
        new("pelota-abrazar", "Abrazar la pelota", Pose.Sit, Gesture.HoldBoth, Prop.Ball, Seconds: 5, Face: Expression.Love, Emote: EmoteKind.Heart),
        new("pelota-arriba", "Levantar la pelota", Pose.Tiptoe, Gesture.HoldUp, Prop.Ball, Seconds: 2.5, Face: Expression.Joy),
        new("globo", "Pasear el globo", Pose.Stand, Gesture.HoldUp, Prop.Balloon, Seconds: 8, Face: Expression.Happy),
        new("globo-sentado", "Globo sentado", Pose.Sit, Gesture.HoldUp, Prop.Balloon, Seconds: 8, Face: Expression.Happy),
        new("pompas", "Hacer pompas", Pose.Stand, Gesture.Blow, Prop.Wand, Seconds: 6, Face: Expression.Happy, Emote: EmoteKind.Bubble, EmoteEvery: 0.45, EmoteFrom: EmoteSource.Hand),
        new("pompas-sentado", "Pompas sentado", Pose.Sit, Gesture.Blow, Prop.Wand, Seconds: 6, Face: Expression.Happy, Emote: EmoteKind.Bubble, EmoteEvery: 0.45, EmoteFrom: EmoteSource.Hand),
        new("cucu-tras", "Cucú-tras", Pose.Sit, Gesture.PeekABoo, Seconds: 4.4, Face: Expression.Joy),

        // Calma y aficiones.
        new("leer", "Leer", Pose.Sit, Gesture.HoldBoth, Prop.Book, Seconds: 12, Face: Expression.Neutral, Look: Down),
        new("leer-de-pie", "Leer de pie", Pose.Stand, Gesture.HoldBoth, Prop.Book, Seconds: 8, Face: Expression.Curious, Look: Down),
        new("leer-gafas", "Leer con gafas", Pose.Sit, Gesture.HoldBoth, Prop.Book, Seconds: 14, Face: Expression.Neutral, Wear: Wear.Glasses, Look: Down),
        new("pintar", "Pintar", Pose.Stand, Gesture.Paint, Prop.Brush, Seconds: 8, Face: Expression.Curious, Emote: EmoteKind.Paint, EmoteEvery: 0.9, EmoteFrom: EmoteSource.Hand),
        new("cantar", "Cantar", Pose.Sway, Gesture.Sing, Prop.Microphone, Seconds: 7, Face: Expression.Joy, Emote: EmoteKind.Note, EmoteEvery: 0.6, EmoteFrom: EmoteSource.Mouth),
        new("canturrear", "Canturrear", Pose.Sway, Gesture.Conduct, Seconds: 6, Face: Expression.Happy, Emote: EmoteKind.Note, EmoteEvery: 0.9, EmoteFrom: EmoteSource.Mouth),
        new("oler-flor", "Oler una flor", Pose.Stand, Gesture.Smell, Prop.Flower, Seconds: 5, Face: Expression.Love, Emote: EmoteKind.Heart, EmoteEvery: 2.5),
        new("flor-sentado", "Flor sentado", Pose.Sit, Gesture.Hold, Prop.Flower, Seconds: 6, Face: Expression.Happy),
        new("te", "Tomar un té", Pose.Sit, Gesture.Sip, Prop.Teacup, Seconds: 10, Face: Expression.Happy),
        new("hoja-paraguas", "Hoja como paraguas", Pose.Stand, Gesture.HoldUp, Prop.Leaf, Seconds: 6, Face: Expression.Happy),
        new("pensar", "Pensar", Pose.Stand, Gesture.Think, Seconds: 4, Face: Expression.Thinking, Look: Up),
        new("pensar-sentado", "Pensar sentado", Pose.Sit, Gesture.Think, Seconds: 6, Face: Expression.Thinking, Look: Up),
        new("mirar-alrededor", "Mirar alrededor", Pose.Stand, Gesture.LookAround, Seconds: 4, Face: Expression.Curious),
        new("olfatear", "Olfatear", Pose.Crouch, Gesture.Sniff, Seconds: 2.5, Face: Expression.Curious, Look: Down),

        // Contigo.
        new("saludar", "Saludar", Pose.Stand, Gesture.Wave, Seconds: 2.2, Face: Expression.Joy),
        new("saludar-dos", "Saludar con las dos manos", Pose.Tiptoe, Gesture.WaveBoth, Seconds: 2.4, Face: Expression.Joy),
        new("saludar-sentado", "Saludar sentado", Pose.Sit, Gesture.Wave, Seconds: 2.2, Face: Expression.Happy),
        new("llamar", "Llamarte", Pose.Stand, Gesture.Beckon, Seconds: 3, Face: Expression.Happy),
        new("beso", "Lanzar un beso", Pose.Stand, Gesture.Blow, Seconds: 1.8, Face: Expression.Love, Emote: EmoteKind.Heart, EmoteFrom: EmoteSource.Hand),
        new("guino", "Guiño", Pose.Stand, Gesture.Point, Seconds: 1.6, Face: Expression.Wink, Emote: EmoteKind.Sparkle, EmoteFrom: EmoteSource.Hand),
        new("reverencia", "Reverencia", Pose.Stand, Gesture.Bow, Seconds: 2, Face: Expression.Happy),
        new("senalar", "Señalar", Pose.Stand, Gesture.Point, Seconds: 2, Face: Expression.Surprised, Emote: EmoteKind.Exclamation),
        new("asentir", "Asentir", Pose.Stand, Gesture.Nod, Seconds: 1.6, Face: Expression.Happy),
        new("negar", "Negar con la cabeza", Pose.Stand, Gesture.ShakeHead, Seconds: 1.6, Face: Expression.Confused),

        // Emociones.
        new("encogerse", "Encogerse de hombros", Pose.Stand, Gesture.Shrug, Seconds: 2, Face: Expression.Confused, Emote: EmoteKind.Question),
        new("rascarse", "Rascarse la cabeza", Pose.Stand, Gesture.Scratch, Seconds: 2.4, Face: Expression.Confused, Emote: EmoteKind.Question),
        new("abrazarse", "Abrazarse", Pose.Sit, Gesture.Hug, Seconds: 6, Face: Expression.Sad),
        new("taparse", "Taparse los ojos", Pose.Crouch, Gesture.CoverEyes, Seconds: 3, Face: Expression.Sad),
        new("barriga", "Agarrarse la barriga", Pose.Crouch, Gesture.HoldBelly, Seconds: 3, Face: Expression.Pain),
        new("tamborilear", "Tamborilear la barriga", Pose.Sit, Gesture.Drum, Seconds: 3, Face: Expression.Happy),
        new("impaciente", "Impaciente", Pose.Stand, Gesture.TapFoot, Seconds: 3, Face: Expression.Angry),
        new("enfurrunarse", "Enfurruñarse", Pose.Sit, Gesture.Hug, Seconds: 5, Face: Expression.Angry),
        new("suspirar", "Suspirar", Pose.Flop, Seconds: 4, Face: Expression.Sad),
        new("balancearse", "Balancearse", Pose.Sway, Seconds: 5, Face: Expression.Happy),
        new("susto", "¡Un susto!", Pose.Tumble, Gesture.Flail, Seconds: 1.2, Face: Expression.Surprised, Emote: EmoteKind.Exclamation, Motion: ClipMotion.Hop),

        // De mayor.
        new("baston", "Descansar en el bastón", Pose.Hunched, Gesture.Hold, Prop.Cane, Seconds: 8),
        new("batallitas", "Acordarse de cuando era pequeño", Pose.Sit, Gesture.Think, Seconds: 10, Face: Expression.Happy, Look: Up),
        new("buscar-gafas", "Buscar las gafas", Pose.Stand, Gesture.LookAround, Seconds: 4, Face: Expression.Confused, Emote: EmoteKind.Question, Wear: Wear.GlassesUp),
        new("encontrar-gafas", "¡Aquí estaban!", Pose.Stand, Gesture.Scratch, Seconds: 1.8, Face: Expression.Joy, Emote: EmoteKind.Exclamation, Wear: Wear.Glasses),
        new("despiste", "¿A qué venía yo?", Pose.Stand, Gesture.Scratch, Seconds: 2.6, Face: Expression.Confused, Emote: EmoteKind.Question),
        new("cabezada-mayor", "Cabezadita", Pose.Doze, Seconds: 14, Face: Expression.Sleeping, Emote: EmoteKind.Sleep, EmoteEvery: 3),
        new("espalda", "¡Ay, la espalda!", Pose.Hunched, Gesture.Back, Seconds: 2.6, Face: Expression.Pain),
        new("despedirse", "Despedirse", Pose.Stand, Gesture.Wave, Seconds: 3.5, Face: Expression.Love, Emote: EmoteKind.Heart, EmoteEvery: 1.1),
    ];

    private static readonly Dictionary<string, AnimationClip> ById = All.ToDictionary(c => c.Id);

    public static AnimationClip Get(string id) => ById[id];

    public static AnimationClip? Find(string id) => ById.GetValueOrDefault(id);

    /// <summary>
    /// Cuántas combinaciones distintas de postura, gesto, objeto y gafas tienen sentido con las piezas que
    /// hay (sin contar las caras de cada estado de ánimo, que las multiplican).
    /// </summary>
    public static int CombinationCount()
    {
        var count = 0;
        foreach (var pose in Poses.All)
        {
            foreach (var gesture in Gestures.All)
            {
                if (gesture.UsesHands && !pose.Upright)
                {
                    continue;
                }

                var props = 1 + gesture.Props.Count;
                count += props * (pose.Upright && gesture.UsesHands ? 2 : 1);
            }
        }

        return count;
    }
}
