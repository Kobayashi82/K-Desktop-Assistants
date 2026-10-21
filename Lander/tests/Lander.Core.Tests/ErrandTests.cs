using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Progression;
using Lander.Core.Terrain;
using static Lander.Core.Tests.TestWorlds;

namespace Lander.Core.Tests;

/// <summary>Encargos del asistente (las maniobras de K-Desktop): llegar deprisa a un punto, esperar y posarse.</summary>
public class ErrandTests
{
    private static World Desktop() => TerrainBuilder.Build(
        [Monitor(height: 1440)],
        [
            Window(1, 100, 300, 700, 500),
            Window(2, 1300, 200, 800, 600),
        ],
        null,
        0);

    private static LanderGame NewGame(World world, int seed)
    {
        var game = new LanderGame(new GameOptions(), new PilotProgress(), new GameRandom(seed));
        game.Initialize(world);
        for (var i = 0; i < 120; i++)
        {
            game.Step(world, Awake, default);
        }

        return game;
    }

    private static readonly Sensors Awake = new() { UserIdleSeconds = 1 };

    /// <summary>Pasos hasta llegar al punto (o -1 si no llega en <paramref name="limit"/> segundos).</summary>
    private static int FlyTo(LanderGame game, World world, Vec2 point, double limit)
    {
        game.BeginErrand(point, 1.25);
        for (var i = 0; i < limit * 60; i++)
        {
            game.Step(world, Awake, default);
            if (game.Autopilot.ErrandArrived)
            {
                return i;
            }
        }

        return -1;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void It_reaches_a_point_across_the_screen_quickly_and_waits_there(int seed)
    {
        var world = Desktop();
        var game = NewGame(world, seed);
        var point = new Vec2(1500, 420);
        var steps = FlyTo(game, world, point, 7);
        Assert.True(steps > 0, $"No ha llegado: está en {game.Body.Position}");
        Assert.True(steps < 5.5 * 60, $"Ha tardado {steps / 60.0:0.0} s");

        // Espera allí, quieto, mientras dure el encargo.
        for (var i = 0; i < 120; i++)
        {
            game.Step(world, Awake, default);
        }

        Assert.True(game.Autopilot.OnErrand);
        Assert.True(Vec2.Distance(game.Body.Position, point) < game.Body.Height);
    }

    [Fact]
    public void A_near_point_is_reached_in_under_three_seconds()
    {
        var world = Desktop();
        var game = NewGame(world, 4);
        var steps = FlyTo(game, world, game.Body.Position + new Vec2(220, -260), 3);
        Assert.InRange(steps, 1, 3 * 60);
    }

    [Fact]
    public void Ending_it_lands_on_the_window_asked_for_without_trouble()
    {
        var world = Desktop();
        var game = NewGame(world, 5);
        Assert.True(FlyTo(game, world, new Vec2(1700, 120), 6) > 0);
        game.EndErrand(perchOn: 2);
        var events = new List<GameEvent>();
        for (var i = 0; i < 20 * 60 && !game.Body.IsLanded; i++)
        {
            game.Step(world, Awake, default);
            game.DrainEvents(events);
        }

        Assert.True(game.Body.IsLanded);
        Assert.Equal(2, game.Body.Support?.OwnerId);
        Assert.False(game.Autopilot.OnErrand);
        Assert.DoesNotContain(events, e => e.Kind is GameEventKind.Crashed or GameEventKind.HitCeiling);
    }

    [Fact]
    public void Recoil_and_a_stall_do_not_end_in_a_crash()
    {
        var world = Desktop();
        var game = NewGame(world, 6);
        Assert.True(FlyTo(game, world, new Vec2(900, 700), 6) > 0);
        game.Recoil(new Vec2(-110, 0));
        game.Autopilot.Stall();
        var events = new List<GameEvent>();
        for (var i = 0; i < 3 * 60; i++)
        {
            game.Step(world, Awake, default);
            game.DrainEvents(events);
        }

        game.EndErrand();
        for (var i = 0; i < 20 * 60 && !game.Body.IsLanded; i++)
        {
            game.Step(world, Awake, default);
            game.DrainEvents(events);
        }

        Assert.True(game.Body.IsLanded);
        Assert.DoesNotContain(events, e => e.Kind is GameEventKind.Crashed or GameEventKind.HitCeiling);
    }

    [Fact]
    public void An_abandoned_errand_ends_by_itself()
    {
        var world = Desktop();
        var game = NewGame(world, 7);
        game.BeginErrand(new Vec2(700, 600), 1);
        Assert.True(game.Autopilot.OnErrand);
        for (var i = 0; i < 45 * 60; i++)
        {
            game.Step(world, Awake, default);
        }

        Assert.False(game.Autopilot.OnErrand);
    }

    [Fact]
    public void No_errands_in_manual_mode_or_paused()
    {
        var world = Desktop();
        var game = NewGame(world, 8);
        game.SetPaused(true);
        Assert.False(game.CanRunErrands);
        game.BeginErrand(new Vec2(700, 600), 1);
        Assert.False(game.Autopilot.OnErrand);
        game.SetPaused(false);
        game.SetMode(ControlMode.Manual, ModeChangeReason.User);
        Assert.False(game.CanRunErrands);
    }
}
