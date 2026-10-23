using Kobii.Core.Animation;

namespace Kobii.Core.Creature;

/// <summary>Cómo se lleva a cabo una decisión.</summary>
public enum ChoiceKind
{
    /// <summary>Pasear hasta un punto de su plataforma.</summary>
    Wander,

    /// <summary>Quedarse quieto un momento, mirando.</summary>
    Linger,

    /// <summary>Acercarse al cursor.</summary>
    ApproachCursor,

    /// <summary>Ir al borde de la plataforma y hacer allí los clips.</summary>
    ToEdge,

    /// <summary>Hacer los clips aquí mismo.</summary>
    Perform,

    /// <summary>Algo que se hace paso a paso (la mariposa, la pelota, merendar): el id dice qué.</summary>
    Play,
}

/// <summary>Cómo está de ánimo (sale del trato y de la hora; no hay hambre, ni sueño de verdad, ni enfermedades).</summary>
public enum Mood
{
    /// <summary>Contento: le has hecho caso hace poco.</summary>
    Happy,

    Content,

    /// <summary>Aburrido: lleva mucho rato sin que le hagas caso (estando tú delante).</summary>
    Bored,

    /// <summary>De noche: va más despacio y bosteza.</summary>
    Sleepy,
}

/// <summary>Lo que sabe de sí mismo y de lo que le rodea al decidir qué hacer.</summary>
public sealed class AutonomyContext
{
    public Mood Mood { get; init; } = Mood.Content;

    /// <summary>Ganas de moverse (0–1): menos de noche.</summary>
    public double Energy { get; init; } = 0.8;

    public int Hour { get; init; } = 12;

    /// <summary>El cursor está quieto cerca de él.</summary>
    public bool CursorNear { get; init; }

    public bool FollowCursor { get; init; } = true;

    /// <summary>El usuario acaba de volver tras un buen rato sin tocar el PC.</summary>
    public bool UserReturned { get; init; }

    /// <summary>Está sobre una ventana (tiene bordes a los que asomarse).</summary>
    public bool OnWindow { get; init; }

    public double SecondsSinceWake { get; init; } = double.PositiveInfinity;

    /// <summary>Su plataforma es lo bastante ancha para rodar o correr.</summary>
    public bool RoomToRun { get; init; }

    /// <summary>Pasa una mariposa (no son de cada rato).</summary>
    public bool ButterflyPassing { get; init; }

    /// <summary>Tiene a mano la pelota que le has dado.</summary>
    public bool BallNear { get; init; }

    /// <summary>Tiene a mano su peluche.</summary>
    public bool PlushNear { get; init; }

    /// <summary>Tiene a mano su cama (y no está ya dentro).</summary>
    public bool BedNear { get; init; }

    /// <summary>Hora de comer algo (media mañana, comida o merienda).</summary>
    public bool SnackTime => Hour is 11 or 14 or 17;

    public bool Cheerful => Mood == Mood.Happy;

    public bool Down => Mood == Mood.Bored;

    public bool Night => Hour is >= 22 or < 7;
}

/// <summary>Una de las cosas que puede decidir hacer por su cuenta.</summary>
/// <param name="Id">Identificador estable (para no repetirse).</param>
/// <param name="Name">Nombre para el registro.</param>
/// <param name="Kind">Cómo se lleva a cabo.</param>
/// <param name="Score">Ganas de hacerlo ahora (0 = nada).</param>
/// <param name="Clips">Animaciones que encadena, en orden.</param>
public sealed record AutonomyAction(string Id, string Name, ChoiceKind Kind, Func<AutonomyContext, double> Score, params string[] Clips)
{
    /// <summary>Gasta energía: se hace menos de noche.</summary>
    public bool Lively => Kind is ChoiceKind.Wander or ChoiceKind.ApproachCursor
        || Clips.Any(id => AnimationCatalog.Get(id).Motion != ClipMotion.None || AnimationCatalog.Get(id).Pose is Pose.Dance);
}

/// <summary>
/// Comportamiento autónomo por utilidad (el de Kobii AI sin necesidades, amigos ni edades): puntúa todo lo que podría
/// hacer según su ánimo, la hora y lo que pasa en el escritorio, y elige entre lo mejor con algo de azar. Recuerda lo
/// que ha hecho hace poco para no repetirse.
/// </summary>
public sealed class Autonomy
{
    /// <summary>Lo que tarda en olvidar que acaba de hacer algo (constante de tiempo, en segundos).</summary>
    private const double NoveltySeconds = 300;

    private readonly Dictionary<string, double> _lastDone = [];

    public string? Last { get; private set; }

    public static IReadOnlyList<AutonomyAction> Actions { get; } = BuildActions();

    public static AutonomyAction Get(string id) => Actions.First(a => a.Id == id);

    /// <summary>Ganas de hacer <paramref name="action"/> ahora, contando con no repetirse.</summary>
    public double Utility(AutonomyAction action, AutonomyContext c, double time)
    {
        var score = Math.Max(0, action.Score(c));
        if (score <= 0)
        {
            return 0;
        }

        if (action.Lively)
        {
            score *= (0.35 + 0.65 * Math.Clamp(c.Energy * 1.4, 0, 1)) * (c.Night ? 0.6 : 1);
        }

        if (_lastDone.TryGetValue(action.Id, out var last))
        {
            score *= 1 - 0.85 * Math.Exp(-Math.Max(0, time - last) / NoveltySeconds);
        }

        if (action.Id == Last)
        {
            score *= 0.1;
        }

        return score;
    }

    /// <summary>Elige qué hacer (casi siempre algo de lo que más le apetece, a veces otra cosa).</summary>
    public AutonomyAction Choose(AutonomyContext context, double time, IRandom random)
    {
        var weights = new double[Actions.Count];
        for (var i = 0; i < Actions.Count; i++)
        {
            var u = Utility(Actions[i], context, time);
            weights[i] = u * u;
        }

        var index = random.PickWeighted(weights);
        var action = index >= 0 ? Actions[index] : Get("quieto");
        Remember(action, time);
        return action;
    }

    public void Remember(AutonomyAction action, double time)
    {
        _lastDone[action.Id] = time;
        Last = action.Id;
    }

    private static List<AutonomyAction> BuildActions()
    {
        static double Hi(bool condition, double value) => condition ? value : 0;

        return
        [
            // Moverse y estar.
            new("pasear", "Pasear", ChoiceKind.Wander, c => 0.55 * (0.4 + c.Energy)),
            new("quieto", "Quedarse mirando", ChoiceKind.Linger, c => 0.36),
            new("curiosear-cursor", "Curiosear el cursor", ChoiceKind.ApproachCursor, c => Hi(c.CursorNear && c.FollowCursor, c.Down ? 0.9 : 0.5)),
            new("asomarse", "Asomarse por el borde", ChoiceKind.ToEdge, c => Hi(c.OnWindow, 0.28), "asomarse"),
            new("sentarse-borde", "Sentarse en el borde", ChoiceKind.ToEdge, c => Hi(c.OnWindow, 0.22), "sentarse-borde"),

            // Descansar.
            new("sentarse", "Sentarse", ChoiceKind.Perform, c => 0.18 + 0.3 * (1 - c.Energy), "sentarse"),
            new("sentarse-mirar", "Sentarse a mirar", ChoiceKind.Perform, c => 0.16, "sentarse-mirar"),
            new("tumbarse", "Tumbarse", ChoiceKind.Perform, c => 0.10 + 0.35 * (1 - c.Energy), "tumbarse"),
            new("acurrucarse", "Acurrucarse", ChoiceKind.Perform, c => 0.06 + Hi(c.Down, 0.2), "acurrucarse"),
            new("siesta", "Echar una siesta", ChoiceKind.Perform, c => Hi(c.Energy < 0.45, 0.35), "siesta"),
            new("siesta-almohada", "Siesta con almohada", ChoiceKind.Perform, c => Hi(c.Energy < 0.5, 0.3) + Hi(c.Night, 0.2), "siesta-almohada"),

            // Despertarse.
            new("despertar", "Desperezarse", ChoiceKind.Perform, c => Hi(c.SecondsSinceWake < 90, 1.2), "frotarse-ojos", "bostezar-sentado", "estirarse"),
            new("estirarse", "Estirarse", ChoiceKind.Perform, c => 0.10 + Hi(c.SecondsSinceWake < 600, 0.2), "estirarse"),
            new("bostezar", "Bostezar", ChoiceKind.Perform, c => Hi(c.Energy < 0.5, 0.45 * (1 - c.Energy)) + 0.03, "bostezar"),

            // Alegría y juego.
            new("bailar", "Bailar", ChoiceKind.Perform, c => Hi(c.Cheerful, 0.3), "bailar"),
            new("bailar-palmas", "Bailar con palmas", ChoiceKind.Perform, c => Hi(c.Cheerful, 0.2), "bailar-palmas"),
            new("aplaudir", "Aplaudir", ChoiceKind.Perform, c => Hi(c.Cheerful, 0.12), "aplaudir"),
            new("celebrar", "Saltar de alegría", ChoiceKind.Perform, c => Hi(c.Cheerful, 0.15), "brazos-arriba"),
            new("voltereta", "Dar una voltereta", ChoiceKind.Perform, c => Hi(c.Cheerful && c.Energy > 0.5, 0.2), "voltereta"),
            new("rodar", "Rodar", ChoiceKind.Perform, c => Hi(c.RoomToRun, 0.12), "rodar"),
            new("menearse", "Menear el culete", ChoiceKind.Perform, c => 0.10, "menearse"),
            new("saltitos", "Saltitos", ChoiceKind.Perform, c => Hi(c.Cheerful, 0.1), "saltitos"),
            new("globo", "Pasear el globo", ChoiceKind.Perform, c => 0.14, "globo"),
            new("globo-sentado", "Globo sentado", ChoiceKind.Perform, c => 0.08, "globo-sentado"),
            new("pompas", "Hacer pompas", ChoiceKind.Perform, c => 0.12 + Hi(c.Down, 0.1), "pompas"),
            new("pompas-sentado", "Pompas sentado", ChoiceKind.Perform, c => 0.08, "pompas-sentado"),
            new("mariposa", "Perseguir una mariposa", ChoiceKind.Play, c => Hi(c.ButterflyPassing && c.RoomToRun, 0.9)),
            new("pelota", "Jugar con la pelota", ChoiceKind.Play, c => Hi(c.RoomToRun, 0.16 + Hi(c.Cheerful, 0.1) + Hi(c.BallNear, 0.14))),
            new("peluche-abrazar", "Abrazar el peluche", ChoiceKind.Play, c => Hi(c.PlushNear, 0.22 + Hi(c.Down, 0.25))),
            new("peluche-pasear", "Pasear el peluche", ChoiceKind.Play, c => Hi(c.PlushNear && c.RoomToRun, 0.12)),
            new("cama-botar", "Botar en la cama", ChoiceKind.Play, c => Hi(c.BedNear, 0.14 + Hi(c.Cheerful, 0.15))),
            new("cama-sentarse", "Sentarse en la cama", ChoiceKind.Play, c => Hi(c.BedNear, 0.12 + 0.2 * (1 - c.Energy))),

            // Aficiones y calma.
            new("leer", "Leer", ChoiceKind.Perform, c => Hi(!c.Down, 0.2), "leer"),
            new("leer-de-pie", "Leer de pie", ChoiceKind.Perform, c => 0.08, "leer-de-pie"),
            new("leer-gafas", "Leer con gafas", ChoiceKind.Perform, c => 0.06, "leer-gafas"),
            new("cantar", "Cantar", ChoiceKind.Perform, c => Hi(c.Cheerful, 0.2), "cantar"),
            new("canturrear", "Canturrear", ChoiceKind.Perform, c => Hi(!c.Down, 0.14), "canturrear"),
            new("oler-flor", "Oler una flor", ChoiceKind.Perform, c => 0.14, "oler-flor"),
            new("flor-sentado", "Flor sentado", ChoiceKind.Perform, c => 0.08, "flor-sentado"),
            new("te", "La hora del té", ChoiceKind.Perform, c => 0.12 + Hi(c.Hour is >= 16 and < 19, 0.2), "te"),
            new("merendar", "Comer algo", ChoiceKind.Play, c => 0.08 + Hi(c.SnackTime, 0.3)),
            new("beber", "Beber algo", ChoiceKind.Play, c => 0.09),
            new("pensar", "Pensar", ChoiceKind.Perform, c => 0.10, "pensar"),
            new("pensar-sentado", "Pensar sentado", ChoiceKind.Perform, c => 0.07, "pensar-sentado"),
            new("mirar-alrededor", "Mirar alrededor", ChoiceKind.Perform, c => 0.16, "mirar-alrededor"),
            new("olfatear", "Olfatear", ChoiceKind.Perform, c => 0.1, "olfatear"),
            new("puntillas", "Mirar de puntillas", ChoiceKind.Perform, c => 0.08, "puntillas"),
            new("balancearse", "Balancearse", ChoiceKind.Perform, c => Hi(!c.Down, 0.1), "balancearse"),

            // Contigo.
            new("saludar", "Saludarte", ChoiceKind.Perform, c => Hi(c.UserReturned, 2.5) + Hi(c.CursorNear, 0.12), "saludar"),
            new("saludar-dos", "Saludarte con las dos manos", ChoiceKind.Perform, c => Hi(c.UserReturned, 1.5) + Hi(c.CursorNear, 0.06), "saludar-dos"),
            new("llamar", "Llamarte", ChoiceKind.Perform, c => Hi(c.Down, 0.45) + Hi(c.CursorNear, 0.06), "llamar"),
            new("beso", "Lanzarte un beso", ChoiceKind.Perform, c => Hi(c.CursorNear && c.Cheerful, 0.16), "beso"),
            new("guino", "Guiñarte un ojo", ChoiceKind.Perform, c => Hi(c.CursorNear && c.Cheerful, 0.08), "guino"),
            new("reverencia", "Hacer una reverencia", ChoiceKind.Perform, c => Hi(c.CursorNear, 0.05), "reverencia"),

            // Cómo se siente.
            new("rascarse", "Rascarse la cabeza", ChoiceKind.Perform, c => 0.06, "rascarse"),
            new("encogerse", "Encogerse de hombros", ChoiceKind.Perform, c => Hi(c.Down, 0.12) + 0.03, "encogerse"),
            new("abrazarse", "Abrazarse", ChoiceKind.Perform, c => Hi(c.Down, 0.2), "abrazarse"),
            new("suspirar", "Suspirar", ChoiceKind.Perform, c => Hi(c.Down, 0.25), "suspirar"),
        ];
    }
}
