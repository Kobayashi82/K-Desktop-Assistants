using KDesktop.Assistant.Sdk;
using Kobii.Core.Animation;
using Kobii.Core.Creature;

namespace Kobii.Assistant;

/// <summary>
/// Sus animaciones propias para el escaparate de K-Desktop (SDK 1.18, pestaña «Probar» de su ficha): lo que hace por su
/// cuenta (estirarse, pompas, un té, leer…), cómo se mueve (saltitos, rodar, subirse a una ventana), sus juegos (la
/// pelota, la mariposa, el globo, merendar), sus trucos (voltereta, bailar, cantar) y sus caras. Cada una es una animación
/// de su catálogo o una de sus rutinas; lo que pide K-Desktop a todos lo lanza el host.
/// </summary>
public sealed partial class KobiiAssistant
{
    /// <summary>Lo que hace cada elemento: una animación del catálogo (por su id) o una rutina.</summary>
    internal static readonly Dictionary<string, string> ShowcaseClips = new()
    {
        ["kobii.stretch"] = "estirarse",
        ["kobii.yawn"] = "bostezar",
        ["kobii.rubeyes"] = "frotarse-ojos",
        ["kobii.lookaround"] = "sentarse-mirar",
        ["kobii.bubbles"] = "pompas",
        ["kobii.tea"] = "te",
        ["kobii.read"] = "leer-gafas",
        ["kobii.flower"] = "oler-flor",
        ["kobii.doze"] = "cabezadas",
        ["kobii.hops"] = "saltitos",
        ["kobii.roll"] = "rodar",
        ["kobii.balloon"] = "globo",
        ["kobii.peekaboo"] = "cucu-tras",
        ["kobii.flip"] = "voltereta",
        ["kobii.sing"] = "cantar",
        ["kobii.wiggle"] = "menearse",
        ["kobii.bow"] = "reverencia",
        ["kobii.kiss"] = "beso",
        ["kobii.wink"] = "guino",
        ["kobii.sulk"] = "enfurrunarse",
        ["kobii.sigh"] = "suspirar",
        ["kobii.scare"] = "susto",
    };

    internal static readonly IReadOnlyList<ShowcaseItem> ShowcaseItems =
    [
        Item("kobii.stretch", ShowcaseGroup.Idle, "Estirarse", "Stretch", "S’étirer", "Sich strecken", "Stiracchiarsi", "Espreguiçar"),
        Item("kobii.yawn", ShowcaseGroup.Idle, "Bostezar", "Yawn", "Bâiller", "Gähnen", "Sbadigliare", "Bocejar"),
        Item("kobii.rubeyes", ShowcaseGroup.Idle, "Frotarse los ojos", "Rub its eyes", "Se frotter les yeux", "Augen reiben", "Stropicciarsi gli occhi", "Esfregar os olhos"),
        Item("kobii.lookaround", ShowcaseGroup.Idle, "Sentarse a mirar", "Sit and look around", "S’asseoir et regarder", "Sitzen und schauen", "Sedersi a guardare", "Sentar e olhar"),
        Item("kobii.bubbles", ShowcaseGroup.Idle, "Hacer pompas", "Blow bubbles", "Faire des bulles", "Seifenblasen", "Fare le bolle", "Fazer bolhas"),
        Item("kobii.tea", ShowcaseGroup.Idle, "Tomar un té", "Have a cup of tea", "Prendre un thé", "Tee trinken", "Bere un tè", "Tomar um chá"),
        Item("kobii.read", ShowcaseGroup.Idle, "Leer con gafas", "Read with glasses", "Lire avec des lunettes", "Mit Brille lesen", "Leggere con gli occhiali", "Ler de óculos"),
        Item("kobii.flower", ShowcaseGroup.Idle, "Oler una flor", "Smell a flower", "Sentir une fleur", "An einer Blume riechen", "Annusare un fiore", "Cheirar uma flor"),
        Item("kobii.doze", ShowcaseGroup.Idle, "Dar cabezadas", "Nod off", "Somnoler", "Einnicken", "Appisolarsi", "Cochilar"),
        Item("kobii.hops", ShowcaseGroup.Moving, "Saltitos", "Little hops", "Petits sauts", "Hüpfer", "Saltelli", "Pulinhos"),
        Item("kobii.roll", ShowcaseGroup.Moving, "Rodar", "Roll", "Rouler", "Rollen", "Rotolare", "Rolar"),
        Item("kobii.explore", ShowcaseGroup.Moving, "Subirse a una ventana", "Climb onto a window", "Grimper sur une fenêtre", "Auf ein Fenster klettern", "Salire su una finestra", "Subir numa janela"),
        Item("kobii.ball", ShowcaseGroup.Playing, "Jugar con la pelota", "Play with the ball", "Jouer avec la balle", "Mit dem Ball spielen", "Giocare con la palla", "Brincar com a bola"),
        Item("kobii.butterfly", ShowcaseGroup.Playing, "Perseguir una mariposa", "Chase a butterfly", "Chasser un papillon", "Einen Schmetterling jagen", "Inseguire una farfalla", "Correr atrás de uma borboleta"),
        Item("kobii.balloon", ShowcaseGroup.Playing, "Pasear el globo", "Walk the balloon", "Promener le ballon", "Mit dem Luftballon", "Passeggiare col palloncino", "Passear com o balão"),
        Item("kobii.peekaboo", ShowcaseGroup.Playing, "Cucú-tras", "Peekaboo", "Coucou", "Kuckuck", "Cucù", "Esconde-esconde"),
        Item("kobii.eat", ShowcaseGroup.Playing, "Merendar", "Have a snack", "Goûter", "Etwas naschen", "Fare merenda", "Lanchar"),
        Item("kobii.drink", ShowcaseGroup.Playing, "Beber zumo", "Drink some juice", "Boire du jus", "Saft trinken", "Bere un succo", "Tomar suco"),
        Item("kobii.flip", ShowcaseGroup.Tricks, "Voltereta", "Somersault", "Galipette", "Purzelbaum", "Capriola", "Cambalhota"),
        Item("kobii.dance", ShowcaseGroup.Tricks, "Bailar", "Dance", "Danser", "Tanzen", "Ballare", "Dançar"),
        Item("kobii.sing", ShowcaseGroup.Tricks, "Cantar", "Sing", "Chanter", "Singen", "Cantare", "Cantar"),
        Item("kobii.wiggle", ShowcaseGroup.Tricks, "Menear el culete", "Wiggle its bottom", "Remuer les fesses", "Mit dem Po wackeln", "Muovere il sederino", "Rebolar"),
        Item("kobii.bow", ShowcaseGroup.Tricks, "Reverencia", "Bow", "Révérence", "Verbeugung", "Inchino", "Reverência"),
        Item("kobii.kiss", ShowcaseGroup.Feelings, "Lanzar un beso", "Blow a kiss", "Envoyer un bisou", "Kusshand", "Mandare un bacio", "Mandar um beijo"),
        Item("kobii.wink", ShowcaseGroup.Feelings, "Guiño", "Wink", "Clin d’œil", "Zwinkern", "Occhiolino", "Piscadinha"),
        Item("kobii.sulk", ShowcaseGroup.Feelings, "Enfurruñarse", "Sulk", "Bouder", "Schmollen", "Tenere il broncio", "Emburrar"),
        Item("kobii.sigh", ShowcaseGroup.Feelings, "Suspirar", "Sigh", "Soupirer", "Seufzen", "Sospirare", "Suspirar"),
        Item("kobii.scare", ShowcaseGroup.Feelings, "Un susto", "A fright", "Une frayeur", "Ein Schreck", "Uno spavento", "Um susto"),
    ];

    public IReadOnlyList<ShowcaseItem> Showcase => ShowcaseItems;

    public async Task<bool> PlayShowcaseAsync(string id, CancellationToken ct)
    {
        if (!_game.IsInitialized || _hidden || _exitStart is not null || _dragging || _game.Body.IsHeld || ShowcaseItems.All(i => i.Id != id))
        {
            return false;
        }

        _host.Log.Info($"Escaparate: {id}.");
        double seconds;
        bool started;
        if (ShowcaseClips.TryGetValue(id, out var clip))
        {
            started = _game.PlayClip(clip);
            seconds = (AnimationCatalog.Find(clip)?.Seconds ?? 3) + 0.4;
        }
        else
        {
            started = id switch
            {
                "kobii.explore" => _game.Explore(),
                "kobii.ball" => _game.PlayWithBall(),
                "kobii.butterfly" => _game.ChaseButterfly(),
                "kobii.eat" => _game.Eat(own: FoodKind.Cookie),
                "kobii.drink" => _game.Eat(own: FoodKind.Juice),
                "kobii.dance" => _game.StartDance(),
                _ => false,
            };
            seconds = 25;
        }

        DrainGameEvents();
        _host.RequestTick();
        if (!started)
        {
            _host.Log.Debug($"Escaparate: {id} no se puede ahora ({_game.Activity}).");
            return false;
        }

        try
        {
            // Hasta que acabe lo suyo (una animación dura lo que dice el catálogo; una rutina, hasta que vuelve a estar quieto).
            var until = _now + seconds;
            await DelayAsync(0.3, ct);
            while (_now < until && _game.Activity is Activity.Perform or Activity.Play or Activity.Travel or Activity.Walk or Activity.Fall or Activity.Celebrate)
            {
                await DelayAsync(0.1, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Lo han parado o han pedido otra: lo deja.
            _game.StopTravel();
            if (clip is not null)
            {
                _game.StopClip(clip);
            }
        }

        return true;
    }

    private static ShowcaseItem Item(string id, ShowcaseGroup group, string es, string en, string fr, string de, string it, string pt) =>
        new(id, new Dictionary<string, string> { ["es"] = es, ["en"] = en, ["fr"] = fr, ["de"] = de, ["it"] = it, ["pt"] = pt }, group);
}
