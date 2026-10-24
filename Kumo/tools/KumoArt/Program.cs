using System.IO;
using Kumo.Assistant.Rendering;
using Kumo.Core.Cloud;
using Kumo.Core.Geometry;

var thread = new Thread(() =>
{
    if (args is ["sheet", var path, ..])
    {
        // La hoja con el dibujo: formas, estados y caras. «dark» = fondo oscuro; «only=<texto>» = solo esos grupos.
        var dark = args.Contains("dark");
        var only = args.FirstOrDefault(a => a.StartsWith("only=", StringComparison.Ordinal))?[5..];
        var groups = Groups().Where(g => only is null || g.Item1.Contains(only, StringComparison.OrdinalIgnoreCase)).ToList();
        KumoSheet.Save(args.Contains("big") ? KumoSheet.Sheet(groups, cell: 420, columns: 3, unit: 110, dark: dark) : KumoSheet.Sheet(groups, dark: dark), path);
        Console.WriteLine(path);
        return;
    }

    if (args is ["bench", ..])
    {
        Bench();
        return;
    }

    var package = args.Length > 0 ? Path.GetFullPath(args[0]) : FindPackage(AppContext.BaseDirectory);
    var icon = Path.Combine(package, "icon.png");
    KumoSheet.Save(KumoSheet.Portrait(256, 0.33), icon);
    Console.WriteLine(icon);
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

// Lo que cuesta un fotograma: el paso de la simulación, el campo con sus contornos y grabar el dibujo (sin el host).
static void Bench()
{
    var cloud = KumoSheet.Cloud(55, new Vec2(500, 500));
    var frame = new KumoFrame();
    var painter = new KumoPainter();
    foreach (var (name, setup) in new (string, Action<KumoCloud>)[]
    {
        ("reposo", c => c.SetPose(CloudPose.Idle)),
        ("volando", c => c.FlyTo(new Vec2(1500, 500), 600)),
        ("gota", c => c.SetPose(CloudPose.Drop)),
        ("bocadillo", c => { c.Bubble = (2.2, 0.9); c.SetPose(CloudPose.Bubble); }),
    })
    {
        setup(cloud);
        double sim = 0, field = 0, paint = 0;
        const int frames = 600;
        for (var i = 0; i < frames; i++)
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            cloud.Step(1.0 / 60);
            var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            frame.Update(cloud, 1);
            var t2 = System.Diagnostics.Stopwatch.GetTimestamp();
            var group = new System.Windows.Media.DrawingGroup();
            using (var dc = group.Open())
            {
                painter.Draw(dc, frame);
            }

            var t3 = System.Diagnostics.Stopwatch.GetTimestamp();
            sim += System.Diagnostics.Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds;
            field += System.Diagnostics.Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds;
            paint += System.Diagnostics.Stopwatch.GetElapsedTime(t2, t3).TotalMilliseconds;
        }

        Console.WriteLine(FormattableString.Invariant($"{name}: simulación {sim / frames:0.000} ms, campo y contornos {field / frames:0.000} ms, dibujo {paint / frames:0.000} ms por fotograma"));
    }

    // El campo solo y cada nivel de contorno por separado (lo que más pesa del fotograma).
    var blobs = new FieldBlob[40];
    var n = cloud.Blobs(blobs);
    var f = new CloudField();
    var loops = new List<Vec2[]>();
    var clock = System.Diagnostics.Stopwatch.StartNew();
    for (var i = 0; i < 1000; i++)
    {
        f.Build(blobs.AsSpan(0, n), cloud.FieldReach, KumoFrame.Cell);
    }

    var build = clock.Elapsed.TotalMilliseconds / 1000;
    clock.Restart();
    for (var i = 0; i < 1000; i++)
    {
        loops.Clear();
        f.Contours(1.1, KumoFrame.Spacing, loops);
    }

    Console.WriteLine(FormattableString.Invariant($"campo {build:0.000} ms ({f.Columns}×{f.Rows}, {n} bolas), un contorno {clock.Elapsed.TotalMilliseconds / 1000:0.000} ms ({loops.Sum(l => l.Length)} puntos)"));
}

static List<(string, IReadOnlyList<SheetScene>)> Groups()
{
    static SheetScene S(string name, Action<KumoCloud> setup, double seconds) => new(name, setup, seconds);
    static Action<KumoCloud> Pose(CloudPose pose) => c => c.SetPose(pose);

    return
    [
        ("Cambiando de forma", [
            S("0 s", _ => { }, 0.1),
            S("3 s", _ => { }, 3),
            S("7 s", _ => { }, 7),
            S("12 s", _ => { }, 12),
            S("18 s", _ => { }, 18),
        ]),
        ("Estados", [
            S("Volando", c => c.FlyTo(c.Position + new Vec2(2000, -300), 600), 0.7),
            S("Gota", Pose(CloudPose.Drop), 2),
            S("Bocadillo", c => { c.Bubble = (1.9, 0.75); c.SetPose(CloudPose.Bubble); }, 1.5),
            S("Hablando", c => { c.VoiceLevel = 0.6; c.SetPose(CloudPose.Talk); }, 1),
            S("Pensando", Pose(CloudPose.Think), 1.3),
            S("Pensando a fondo", Pose(CloudPose.ThinkHard), 1.3),
            S("Escuchando", Pose(CloudPose.Listen), 1),
            S("Señalando", c => { c.Aim = c.Position + new Vec2(200, -120); c.SetPose(CloudPose.Point); }, 1),
            S("Pulsando", c => { c.Aim = c.Position + new Vec2(120, 80); c.Reach = 1; c.SetPose(CloudPose.Press); }, 1),
            S("Saludando", c => { c.Aim = c.Position + new Vec2(200, 0); c.SetPose(CloudPose.Wave); }, 1.1),
            S("Contento", Pose(CloudPose.Happy), 0.4),
            S("Sorpresa", Pose(CloudPose.Surprise), 0.15),
            S("Algo falló", Pose(CloudPose.Error), 1.2),
            S("Sin cerebro", Pose(CloudPose.Shrug), 1.2),
            S("Esperando", Pose(CloudPose.Wait), 1),
            S("Ocupado", Pose(CloudPose.Busy), 1),
            S("Durmiendo", Pose(CloudPose.Sleep), 2.5),
            S("Cosquillas", Pose(CloudPose.Tickle), 0.6),
            S("Deshaciéndose", Pose(CloudPose.Dissolve), 0.6),
            S("Condensándose", c => c.Condense(), 0.5),
        ]),
    ];
}

// Sube desde la carpeta del ejecutable hasta la de Kumo (la que tiene src/Kumo.Assistant).
static string FindPackage(string from)
{
    for (var dir = new DirectoryInfo(from); dir is not null; dir = dir.Parent)
    {
        var package = Path.Combine(dir.FullName, "src", "Kumo.Assistant", "Package");
        if (Directory.Exists(package))
        {
            return package;
        }
    }

    throw new DirectoryNotFoundException("No encuentro src/Kumo.Assistant/Package; pásame la carpeta.");
}
