using Lander.Core.Game;
using Lander.Core.Physics;
using Lander.Core.Progression;
using Lander.Core.Terrain;
using static Lander.Core.Tests.TestWorlds;

namespace Lander.Core.Tests;

/// <summary>El lander posado sobre un borde de contenido mientras la página se desplaza.</summary>
public class ContentScrollTests
{
    private const long Handle = 7;
    private const int EdgeId = 3;
    private static readonly WindowInfo Page = Window(Handle, 300, 100, 1200, 900, app: "msedge");
    private static readonly long Owner = Platform.ContentOwnerId(Handle, EdgeId);

    private static World At(double localY, double time) =>
        TerrainBuilder.Build([Monitor()], [Page], null, time, [new ContentSurface(Handle, EdgeId, 100, 900, (int)Math.Round(localY))]);

    private static LanderGame LandedOnContent(double localY)
    {
        var world = At(localY, 0);
        var game = new LanderGame(new GameOptions(), new PilotProgress(), new GameRandom(4));
        game.Initialize(world);
        var content = world.SegmentsOf(Owner)[0];
        game.Body.PlaceOn(content, content.CenterX, 0);
        return game;
    }

    [Fact]
    public void Rides_the_content_while_the_page_scrolls_down()
    {
        // Bajar por la página: el contenido sube a 400 px/s durante medio segundo, como con la rueda.
        var game = LandedOnContent(600);
        var sensors = new Sensors();
        var y = 600.0;
        for (var i = 0; i < 90; i++)
        {
            var t = (i + 1) * LanderGame.TimeStep;
            if (t < 0.5)
            {
                y -= 400 * LanderGame.TimeStep;
            }

            game.Step(At(y, t), sensors, default);
            Assert.True(game.Body.IsLanded, $"se ha soltado en el paso {i}");
            Assert.Equal(Owner, game.Body.Support!.OwnerId);
        }

        Assert.Equal(Page.Bounds.Top + Math.Round(y) - game.Body.HalfHeight, game.Body.Position.Y, 1);
    }

    [Fact]
    public void Content_that_jumps_up_and_stops_does_not_launch_it()
    {
        // Una medida tardía del scroll: 54 px de golpe y luego quieto. Una ventana arrastrada así lo lanzaría.
        var game = LandedOnContent(600);
        var sensors = new Sensors();
        for (var i = 0; i < 60; i++)
        {
            var t = (i + 1) * LanderGame.TimeStep;
            game.Step(At(i < 3 ? 600 : 546, t), sensors, default);
            Assert.True(game.Body.IsLanded, $"se ha soltado en el paso {i}");
        }

        Assert.Equal(Owner, game.Body.Support!.OwnerId);
    }

    [Fact]
    public void Scrolling_up_fast_drops_it_and_it_lands_again()
    {
        // Subir por la página deprisa: el contenido cae más rápido que la gravedad y el lander se separa.
        var game = LandedOnContent(300);
        var sensors = new Sensors();
        var y = 300.0;
        var separated = false;
        for (var i = 0; i < 60 * 8; i++)
        {
            var t = (i + 1) * LanderGame.TimeStep;
            if (t < 0.3)
            {
                y += 1200 * LanderGame.TimeStep;
            }

            game.Step(At(y, t), sensors, default);
            separated |= game.Body.IsFlying;
        }

        Assert.True(separated);
        Assert.True(game.Body.IsLanded);
        Assert.NotEqual(BodyState.Destroyed, game.Body.State);
    }

    [Fact]
    public void Edge_that_leaves_the_view_drops_the_lander_and_it_recovers()
    {
        var game = LandedOnContent(500);
        var sensors = new Sensors();
        var events = new List<GameEvent>();
        game.Step(At(500, LanderGame.TimeStep), sensors, default);

        // El borde sale por arriba: deja de estar en el terreno.
        var gone = TerrainBuilder.Build([Monitor()], [Page], null, 1);
        for (var i = 0; i < 60 * 10; i++)
        {
            game.Step(gone, sensors, default);
            game.DrainEvents(events);
        }

        Assert.Contains(events, e => e.Kind == GameEventKind.SupportLost);
        Assert.True(game.Body.IsLanded);
        Assert.NotEqual(Owner, game.Body.Support!.OwnerId);
        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.Crashed);
    }
}
