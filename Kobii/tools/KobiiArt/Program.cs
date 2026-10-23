using System.IO;
using Kobii.Assistant.Rendering;
using Kobii.Core.Creature;

var thread = new Thread(() =>
{
    if (args is ["vsheet", var vector, ..])
    {
        // La hoja con el dibujo vectorial: colores, caras y posturas.
        // «dark» = fondo oscuro; «only=<texto>» = solo los grupos cuyo título lo contiene (y más grandes).
        var dark = args.Contains("dark");
        var only = args.FirstOrDefault(a => a.StartsWith("only=", StringComparison.Ordinal))?[5..];
        var groups = VectorGroups().Where(g => only is null || g.Item1.Contains(only, StringComparison.OrdinalIgnoreCase)).ToList();
        VectorSheet.Save(only is null ? VectorSheet.Sheet(groups, dark: dark) : VectorSheet.Sheet(groups, cell: 300, columns: 3, creature: 0.42, dark: dark), vector);
        Console.WriteLine(vector);
        return;
    }

    if (args is ["vbench", ..])
    {
        // Lo que cuesta grabar un fotograma vectorial (lo que hace el asistente en cada Present), andando y hablando.
        VectorBench();
        return;
    }

    var package = args.Length > 0 ? Path.GetFullPath(args[0]) : FindPackage(AppContext.BaseDirectory);
    var icon = Path.Combine(package, "icon.png");
    VectorSheet.Save(VectorSheet.Portrait(256, "mint"), icon);
    Console.WriteLine(icon);
    foreach (var palette in Species.Palettes)
    {
        var path = Path.Combine(package, "assets", "colors", palette.Id + ".png");
        VectorSheet.Save(VectorSheet.Portrait(160, palette.Id), path);
        Console.WriteLine(path);
    }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

static void VectorBench()
{
    var world = Kobii.Core.Terrain.TerrainBuilder.Build([new Kobii.Core.Terrain.MonitorInfo(0, new Kobii.Core.Geometry.PixelRect(0, 0, 2000, 1500), new Kobii.Core.Geometry.PixelRect(0, 0, 2000, 1500), 1, true)], []);
    var game = new KobiiGame(new GameOptions { Size = 1.5 }, new Kobii.Core.GameRandom(1));
    game.Initialize(world, 0.2);
    var sensors = new Sensors { UserIdleSeconds = 1 };
    var frame = new KobiiFrame();
    var painter = new KobiiPainter();
    game.SetAttention(Attention.Speaking);
    for (var pass = 0; pass < 3; pass++)
    {
        game.WalkTo(pass % 2 == 0 ? 1800 : 200);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var paint = 0.0;
        for (var i = 0; i < 600; i++)
        {
            game.SetVoiceLevel(0.5 + 0.5 * Math.Sin(i * 0.3));
            game.Step(world, sensors);
            var t = System.Diagnostics.Stopwatch.GetTimestamp();
            frame.Update(game, game.Body.Feet, game.Time, 0, game.Body.Feet.Y, 1, 1);
            var group = new System.Windows.Media.DrawingGroup();
            using (var dc = group.Open())
            {
                painter.Draw(dc, frame);
            }

            paint += System.Diagnostics.Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        }

        Console.WriteLine(FormattableString.Invariant($"{paint / 600:0.000} ms por fotograma grabado ({clock.Elapsed.TotalMilliseconds / 600:0.000} ms con la simulación)"));
    }
}

static List<(string, IReadOnlyList<SheetScene>)> VectorGroups()
{
    static SheetScene S(string name, Action<KobiiGame> setup, double seconds, Expression? face = null, string? color = null, int hour = 12, ToyKind[]? toys = null) =>
        new(name, setup, seconds, face, color, hour, toys);

    var colors = Species.Palettes.Select(p => S(p.Id, _ => { }, 0.5, Expression.Happy, p.Id)).ToList();
    var faces = Enum.GetValues<Expression>().Select(e => S(e.ToString(), _ => { }, 0.5, e)).ToList();
    return
    [
        ("Colores", colors),
        ("Caras", faces),
        ("Posturas", [
            S("De pie", _ => { }, 0.5),
            S("Andando", g => g.WalkTo(g.Body.Feet.X + 2000), 0.6),
            S("Corriendo", g => g.WalkTo(g.Body.Feet.X + 2000, run: true), 0.6),
            S("Sentado", g => g.PlayClip("sentarse"), 1),
            S("Saludando", g => g.PlayClip("saludar"), 0.7),
            S("Pensando", g => g.SetAttention(Attention.Thinking), 1.2),
            S("Escuchando", g => g.SetAttention(Attention.Listening), 1),
            S("Hablando", g => { g.SetAttention(Attention.Speaking); g.SetVoiceLevel(0.8); }, 0.5),
            S("¡Bien!", g => g.React(ReactionKind.Celebration), 0.35),
            S("Asiente", g => g.React(ReactionKind.Success), 0.6),
            S("¿Eh?", g => g.React(ReactionKind.Failure), 0.7),
            S("Señala", g => g.React(ReactionKind.Warning), 0.7),
            S("Aplaudir", g => g.PlayClip("aplaudir"), 0.8),
            S("Bailando", g => g.PlayClip("bailar"), 1),
            S("Estirarse", g => g.PlayClip("estirarse"), 1.2),
            S("De puntillas", g => g.PlayClip("puntillas"), 1),
            S("Olfatear", g => g.PlayClip("olfatear"), 1),
            S("Abrazarse", g => g.PlayClip("abrazarse"), 1),
        ]),
        ("Brazos", [
            S("Pensando", g => g.SetAttention(Attention.Thinking), 1.2),
            S("Aplaudir", g => g.PlayClip("aplaudir"), 0.8),
            S("Abrazarse", g => g.PlayClip("abrazarse"), 1),
            S("Tumbado", g => g.PlayClip("tumbarse"), 1.5),
            S("Acurrucado", g => g.PlayClip("acurrucarse"), 1.5),
            S("Saludando", g => g.PlayClip("saludar"), 0.7),
        ]),
        ("Etapas 2 y 4", [
            S("Arrastrar", g => g.PlayGesture(Kobii.Core.Animation.Gesture.Haul, 3, 1), 0.35),
            S("Deslizándose", g => g.PlayGesture(Kobii.Core.Animation.Gesture.Haul, 3, 1), 1.2),
            S("Mariposa", g => g.ChaseButterfly(chase: false), 5.0),
            S("En el brote", g => g.ChaseButterfly(ButterflyEnding.Perch, 0, chase: false), 11),
            S("Saca la pelota", g => g.PlayWithBall(), 0.85),
            S("Patada", g => g.PlayWithBall(), 1.75),
            S("Manzana", g => g.Eat(own: FoodKind.Apple), 1.35),
            S("Mordisco", g => g.Eat(own: FoodKind.Apple), 3.2),
            S("Galleta", g => g.Eat(own: FoodKind.Cookie), 2.6),
            S("Bayas", g => g.Eat(own: FoodKind.Berries), 3.0),
            S("Zumo", g => g.Eat(own: FoodKind.Juice), 1.9),
            S("Leche", g => g.Eat(own: FoodKind.Milk), 2.3),
            S("Barrer", g => g.RecycleBinEmptied(), 1.6),
            S("Bailar", g => g.StartDance(), 1.3),
            S("Bailar (2)", g => g.StartDance(), 5.2),
            S("Notificación", g => g.NoticeNotification(new Kobii.Core.Geometry.Vec2(3800, 2800)), 1.0),
        ]),
        ("Etapa 5 y ropa", [
            S("Pijama", g => g.PlayClip("mirar-alrededor"), 1.2, hour: 23),
            S("Pijama sentado", g => g.PlayClip("sentarse"), 1.5, hour: 23),
            S("Siesta con gorro", g => g.PlayClip("siesta"), 1.5, hour: 23),
            S("Cama: sentado", g => g.PlayWith(ToyKind.Bed, alternative: true), 4.5, toys: [ToyKind.Bed]),
            S("Cama: botando", g => g.PlayWith(ToyKind.Bed), 3.6, toys: [ToyKind.Bed]),
            S("Osito", g => g.PlayWith(ToyKind.Plush), 4.2, toys: [ToyKind.Plush]),
            S("Osito de noche", g => g.PlayWith(ToyKind.Plush), 4.2, hour: 23, toys: [ToyKind.Plush]),
            S("Juguetes", _ => { }, 0.5, toys: [ToyKind.Ball, ToyKind.Plush, ToyKind.Bed]),
            S("Recién cepillado", g => g.Visual.Shine = 1, 0.62),
            S("Chocar los cinco", g => g.OfferHighFive(), 0.8),
            S("Darse la mano", g => g.OfferHandshake(), 0.8),
        ]),
        ("Descansar (nuevo)", [
            S("Tumbado", g => g.PlayClip("tumbarse"), 1.5),
            S("Siesta", g => g.PlayClip("siesta"), 1.5),
            S("Cabezadas", g => g.PlayClip("cabezadas"), 2.4),
            S("Acurrucado", g => g.PlayClip("acurrucarse"), 1.5),
        ]),
    ];
}

// Sube desde la carpeta del ejecutable hasta la de Kobii (la que tiene src/Kobii.Assistant).
static string FindPackage(string from)
{
    for (var dir = new DirectoryInfo(from); dir is not null; dir = dir.Parent)
    {
        var package = Path.Combine(dir.FullName, "src", "Kobii.Assistant", "Package");
        if (Directory.Exists(package))
        {
            return package;
        }
    }

    throw new DirectoryNotFoundException("No encuentro src/Kobii.Assistant/Package; pásame la carpeta.");
}
