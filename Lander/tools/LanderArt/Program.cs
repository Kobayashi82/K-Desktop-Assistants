using System.IO;
using System.Windows.Media.Imaging;
using Lander.Assistant.Rendering;
using Lander.Core.Geometry;
using Lander.Core.Progression;

// Carpeta Package del asistente (o la que se pase).
var package = args.Length > 0 ? Path.GetFullPath(args[0]) : FindPackage(AppContext.BaseDirectory);

var thread = new Thread(() =>
{
    Save(ArtSheet.RenderLander(256, look: new Vec2(0.35, 0)), Path.Combine(package, "icon.png"));
    foreach (var unlock in Unlocks.OfKind(UnlockKind.Skin))
    {
        Save(ArtSheet.RenderLander(160, unlock.Id, new Vec2(0.4, 0)), Path.Combine(package, "assets", "skins", unlock.Id + ".png"));
    }

    foreach (var unlock in Unlocks.OfKind(UnlockKind.Trail))
    {
        Save(ArtSheet.RenderTrail(160, unlock.Id), Path.Combine(package, "assets", "trails", unlock.Id + ".png"));
    }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

static void Save(BitmapSource image, string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(image));
    using var stream = File.Create(path);
    encoder.Save(stream);
    Console.WriteLine(path);
}

// Sube desde la carpeta del ejecutable hasta la de Lander (la que tiene src/Lander.Assistant).
static string FindPackage(string from)
{
    for (var dir = new DirectoryInfo(from); dir is not null; dir = dir.Parent)
    {
        var package = Path.Combine(dir.FullName, "src", "Lander.Assistant", "Package");
        if (Directory.Exists(package))
        {
            return package;
        }
    }

    throw new DirectoryNotFoundException("No encuentro src/Lander.Assistant/Package; pásame la carpeta.");
}
