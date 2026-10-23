using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Tests;

/// <summary>K-Desktop 13.14: Kobii no se queda encima de otro asistente que está quieto (pasar por delante sí vale).</summary>
public sealed class RoomTests
{
    private static readonly MonitorInfo Monitor = new(0, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1, true);

    private static readonly Sensors Awake = new() { LocalTime = new DateTime(2026, 10, 23, 12, 0, 0), UserIdleSeconds = 1 };

    [Fact]
    public void Starts_next_to_another_assistant_rather_than_on_top()
    {
        var world = TerrainBuilder.Build([Monitor], []);
        var usual = new KobiiGame(new GameOptions(), new GameRandom(3));
        usual.Initialize(world);
        var x = usual.Body.Feet.X;

        var other = new PixelRect((int)x - 30, 960, (int)x + 30, 1040);
        var game = new KobiiGame(new GameOptions(), new GameRandom(3)) { Occupied = [other] };
        game.Initialize(world);
        Assert.False(game.IsTaken(game.Body.Feet.X, 1040));
        Assert.True(Math.Abs(game.Body.Feet.X - x) < 30 + game.Body.HalfWidth * 3);
    }

    [Fact]
    public void Steps_aside_when_another_one_stays_where_it_stands_and_never_strolls_back_onto_it()
    {
        var world = TerrainBuilder.Build([Monitor], []);
        var game = new KobiiGame(new GameOptions(), new GameRandom(7));
        game.Initialize(world);
        var x = game.Body.Feet.X;
        game.Occupied = [new PixelRect((int)x - 40, 960, (int)x + 40, 1040)];

        var events = new List<GameEvent>();
        var steppedAside = false;
        for (var t = 0.0; t < 300; t += KobiiGame.TimeStep)
        {
            game.Step(world, Awake);
            game.DrainEvents(events);
            steppedAside |= events.Any(e => e.Kind == GameEventKind.SteppedAside);
            events.Clear();
            // Once out of the way, whatever it does it never stands still on top of the other one again.
            if (steppedAside && game.Activity == Activity.Idle && game.Body.IsGrounded)
            {
                Assert.False(game.IsTaken(game.Body.Feet.X, game.Body.Feet.Y), $"{t:0.0} s: {game.Body.Feet.X}");
            }
        }

        Assert.True(steppedAside);
    }

    [Fact]
    public void A_place_is_free_beside_and_taken_on_top()
    {
        var world = TerrainBuilder.Build([Monitor], []);
        var game = new KobiiGame(new GameOptions(), new GameRandom(1));
        game.Initialize(world);
        game.Occupied = [new PixelRect(900, 960, 1000, 1040)];
        Assert.True(game.IsTaken(950, 1040));
        Assert.False(game.IsTaken(1200, 1040));
        // Higher up (on a window over it), it is not in the way.
        Assert.False(game.IsTaken(950, 600));
        var ground = world.PrimaryGround!;
        var free = game.FreeX(ground, 950)!.Value;
        Assert.False(game.IsTaken(free, 1040));
        Assert.True(Math.Abs(free - 950) < 50 + game.Body.HalfWidth * 2);
    }
}
