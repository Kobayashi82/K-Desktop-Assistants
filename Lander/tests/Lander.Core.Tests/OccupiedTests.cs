using Lander.Core.Autopilot;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Progression;
using static Lander.Core.Tests.TestWorlds;

namespace Lander.Core.Tests;

/// <summary>K-Desktop 13.14: el lander no se posa encima de otro asistente que está quieto (pasar por delante sí).</summary>
public class OccupiedTests
{
    [Fact]
    public void La_base_queda_al_lado_de_otro_asistente()
    {
        var world = Desktop();
        var free = new LanderGame(new GameOptions(), new PilotProgress(), new GameRandom(1));
        free.Initialize(world);
        var usual = free.Body.Position.X;

        // Otro asistente (60 px de ancho) justo donde suele quedarse.
        var other = new PixelRect((int)usual - 30, GroundY - 80, (int)usual + 30, GroundY);
        var game = new LanderGame(new GameOptions(), new PilotProgress(), new GameRandom(1)) { Occupied = [other] };
        game.Initialize(world);

        Assert.False(DestinationPicker.IsTaken(game.Body, GroundY, game.Body.Position.X, [other]));
        Assert.True(Math.Abs(game.Body.Position.X - usual) < 30 + game.Body.HalfWidth * 3, $"{usual} → {game.Body.Position.X}");
    }

    [Fact]
    public void Ningun_destino_cae_encima_de_otro_asistente()
    {
        var a = Window(1, 300, 400, 500, 400);
        var b = Window(2, 1100, 300, 600, 500);
        var world = Desktop(a, b);
        var body = new LanderBody();
        body.PlaceOn(world.PrimaryGround!, 960, 0);
        var random = new GameRandom(5);
        // Uno quieto ocupa casi toda la ventana 1 y otro está en la barra de tareas.
        IReadOnlyList<PixelRect> occupied = [new PixelRect(320, 300, 760, 400), new PixelRect(1400, GroundY - 70, 1480, GroundY)];
        var query = new DestinationQuery { Occupied = occupied, BaseBias = 1 };

        for (var i = 0; i < 500; i++)
        {
            var pick = DestinationPicker.PickExploration(world, body, query, random);
            Assert.NotNull(pick);
            Assert.True(pick.TryResolve(world, body, out var center, out var segment));
            Assert.False(DestinationPicker.IsTaken(body, segment.Y, center.X, occupied), $"{pick}");
        }

        // A la base, cerca de la vertical pero nunca encima del de la barra.
        for (var i = 0; i < 100; i++)
        {
            body.Position = new Vec2(1440, 500);
            var home = DestinationPicker.PickBase(world, body, random, occupied)!;
            Assert.True(home.TryResolve(world, body, out var center, out var segment));
            Assert.False(DestinationPicker.IsTaken(body, segment.Y, center.X, occupied));
        }
    }

    [Fact]
    public void Sin_sitio_libre_en_una_plataforma_no_la_elige()
    {
        var a = Window(1, 300, 400, 120, 300);
        var world = Desktop(a);
        var body = new LanderBody();
        body.PlaceOn(world.PrimaryGround!, 1500, 0);
        IReadOnlyList<PixelRect> occupied = [new PixelRect(280, 300, 440, 400)];
        var platform = world.SegmentsOf(1).Single();

        Assert.Null(DestinationPicker.FreeX(body, platform, 360, occupied));
        Assert.Null(DestinationPicker.PickOnOwner(world, body, 1, new GameRandom(2), occupied));
        Assert.NotNull(DestinationPicker.PickOnOwner(world, body, 1, new GameRandom(2)));
    }
}
