using System.IO;
using Kobii.Assistant.Rendering;
using Kobii.Core.Creature;

var thread = new Thread(() =>
{
    using var art = new ArtSheet();
    if (args is ["bench", ..])
    {
        // Lo que cuesta dibujarlo en la GPU de verdad: CPU del proceso frente a tiempo.
        Bench(args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 300);
        return;
    }

    if (args is ["sheet", var output, ..])
    {
        // Una hoja con lo que hace (para revisar el dibujo sin K-Desktop).
        var color = args.Length > 2 ? args[2] : "mint";
        ArtSheet.Save(art.Strip(220, color, Scenes()), output);
        Console.WriteLine(output);
        return;
    }

    var package = args.Length > 0 ? Path.GetFullPath(args[0]) : FindPackage(AppContext.BaseDirectory);
    var icon = Path.Combine(package, "icon.png");
    ArtSheet.Save(art.Portrait(256, face: Expression.Happy), icon);
    Console.WriteLine(icon);
    foreach (var palette in Species.Palettes)
    {
        var path = Path.Combine(package, "assets", "colors", palette.Id + ".png");
        ArtSheet.Save(art.Portrait(160, palette.Id, face: Expression.Happy), path);
        Console.WriteLine(path);
    }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

static void Bench(int size)
{
    var shader = CreatureRenderer.Compile(Path.Combine(Path.GetTempPath(), "K-Desktop", "Kobii", "shaders"));
    using var target = new Offscreen(size, size, shader, hardware: true);
    var world = Kobii.Core.Terrain.TerrainBuilder.Build([new Kobii.Core.Terrain.MonitorInfo(0, new Kobii.Core.Geometry.PixelRect(0, 0, 2000, 1500), new Kobii.Core.Geometry.PixelRect(0, 0, 2000, 1500), 1, true)], []);
    var game = new KobiiGame(new GameOptions(), new Kobii.Core.GameRandom(1));
    game.Initialize(world, 0.5);
    var frame = new KobiiFrame();
    var sensors = new Sensors { UserIdleSeconds = 1 };
    var process = System.Diagnostics.Process.GetCurrentProcess();
    for (var pass = 0; pass < 3; pass++)
    {
        process.Refresh();
        var cpu = process.TotalProcessorTime;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 600; i++)
        {
            game.Step(world, sensors);
            frame.Update(game, game.Body.Feet, game.Time, 0, game.Body.Feet.Y, 1, 1);
            target.Draw(frame, new Kobii.Core.Geometry.Vec2(game.Body.Feet.X - size / 2, game.Body.Feet.Y - size * 0.8));
            target.Finish();
            Thread.Sleep(16);
        }

        process.Refresh();
        var used = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        Console.WriteLine(FormattableString.Invariant($"{size}x{size}: {used / 600:0.00} ms de CPU por fotograma, {(clock.Elapsed.TotalMilliseconds - 600 * 16) / 600:0.00} ms de espera, hilos {process.Threads.Count}"));
    }
}

static List<(string, Action<KobiiGame>, double)> Scenes() =>
[
    ("De pie", _ => { }, 0.5),
    ("Andando", g => g.WalkTo(g.Body.Feet.X + 2000), 0.6),
    ("Corriendo", g => g.WalkTo(g.Body.Feet.X + 2000, run: true), 0.6),
    ("Sentado", g => g.PlayClip("sentarse"), 1),
    ("Saludando", g => g.PlayClip("saludar"), 0.7),
    ("Pensando", g => g.SetAttention(Attention.Thinking), 1.2),
    ("Escuchando", g => g.SetAttention(Attention.Listening), 1),
    ("Hablando", g => { g.SetAttention(Attention.Speaking); g.SetVoiceLevel(0.8); }, 0.5),
    ("¡Bien!", g => g.React(ReactionKind.Celebration), 0.35),
    ("Asiente", g => g.React(ReactionKind.Success), 0.6),
    ("¿Eh?", g => g.React(ReactionKind.Failure), 0.7),
    ("Señala", g => g.React(ReactionKind.Warning), 0.7),
    ("Leyendo", g => g.PlayClip("leer"), 1.2),
    ("Pintando", g => g.PlayClip("pintar"), 1.2),
    ("Globo", g => g.PlayClip("globo"), 1.2),
    ("Té", g => g.PlayClip("te"), 1.5),
    ("Tumbado", g => g.PlayClip("tumbarse"), 1.2),
    ("Bailando", g => g.PlayClip("bailar"), 1),
];

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
