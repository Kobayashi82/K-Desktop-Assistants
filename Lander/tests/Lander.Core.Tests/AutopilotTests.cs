using Lander.Core.Autopilot;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Progression;
using Lander.Core.Terrain;
using static Lander.Core.Tests.TestWorlds;

namespace Lander.Core.Tests;

public class AutopilotTests
{
    private static LanderGame NewGame(World world, int seed, GameOptions? options = null)
    {
        var game = new LanderGame(options ?? new GameOptions(), new PilotProgress(), new GameRandom(seed));
        game.Initialize(world);
        return game;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void First_flight_goes_to_a_window_and_lands_perfectly(int seed)
    {
        var world = Desktop(Window(10, 200 + seed * 150, 250 + seed * 60, 500, 400));
        var game = NewGame(world, seed);
        var sensors = new Sensors { UserIdleSeconds = 1 };

        var events = Sim.RunGame(game, world, sensors, 60, g => g.Body.Support?.OwnerId == 10 && !g.Autopilot.IsFlightActive);

        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.Crashed);
        var touchdown = Assert.Single(events, e => e.Kind == GameEventKind.Touchdown);
        Assert.Equal(LandingQuality.Perfect, touchdown.Landing!.Quality);
        Assert.Equal(10, touchdown.Platform!.OwnerId);
        Assert.Equal(AutopilotState.Resting, game.Autopilot.State);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void Ten_minutes_of_autonomous_life_without_accidents(int seed)
    {
        var world = Desktop(
            Window(1, 80, 120, 700, 500),
            Window(2, 600, 380, 900, 450),
            Window(3, 1300, 160, 520, 600, maximized: false),
            Window(4, 300, 700, 420, 250));
        var game = NewGame(world, seed, new GameOptions { Activity = 3 });
        var sensors = new Sensors { UserIdleSeconds = 1 };

        var events = Sim.RunGame(game, world, sensors, 600);

        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.Crashed);
        Assert.True(events.Count(e => e.Kind == GameEventKind.Touchdown) >= 5);
        Assert.All(events.Where(e => e.Kind == GameEventKind.Touchdown), e => Assert.NotEqual(LandingQuality.Crash, e.Landing!.Quality));
        Assert.NotNull(world.MonitorAt(game.Body.Position));
    }

    [Fact]
    public void Recovers_when_the_window_disappears()
    {
        var withWindow = Desktop(Window(1, 400, 300, 700, 400));
        var game = NewGame(withWindow, 7);
        game.Body.PlaceOn(withWindow.SegmentsOf(1)[0], 700, 0);
        var sensors = new Sensors { UserIdleSeconds = 1 };
        var events = Sim.RunGame(game, withWindow, sensors, 0.5);

        events.AddRange(Sim.RunGame(game, Desktop(), sensors, 30, g => g.Body.IsLanded && !g.Autopilot.IsFlightActive));

        Assert.Contains(events, e => e.Kind == GameEventKind.SupportLost);
        Assert.Contains(events, e => e.Kind == GameEventKind.Startled);
        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.Crashed);
        Assert.True(game.Body.IsOnBase);
    }

    [Fact]
    public void Returns_to_base_when_fuel_is_low()
    {
        var world = Desktop(Window(1, 400, 300, 700, 400));
        var game = NewGame(world, 3);
        game.Body.PlaceOn(world.SegmentsOf(1)[0], 700, 0);
        game.Body.Fuel = 15;
        var sensors = new Sensors { UserIdleSeconds = 1 };

        var events = Sim.RunGame(game, world, sensors, 40, g => g.Body.IsOnBase && !g.Autopilot.IsFlightActive);

        Assert.True(game.Body.IsOnBase);
        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.Crashed);
    }

    [Fact]
    public void Goes_home_and_sleeps_when_the_user_is_away()
    {
        var world = Desktop(Window(1, 400, 300, 700, 400));
        var game = NewGame(world, 5);
        game.Body.PlaceOn(world.SegmentsOf(1)[0], 700, 0);
        var sensors = new Sensors { UserIdleSeconds = Autopilot.Autopilot.IdleSleepSeconds + 1 };

        Sim.RunGame(game, world, sensors, 40, g => g.Autopilot.State == AutopilotState.Sleeping);

        Assert.Equal(AutopilotState.Sleeping, game.Autopilot.State);
        Assert.True(game.Body.IsOnBase);
        Assert.Equal(Expression.Asleep, game.Visual.Expression);

        sensors.UserIdleSeconds = 0;
        Sim.RunGame(game, world, sensors, 0.2);
        Assert.Equal(AutopilotState.Resting, game.Autopilot.State);
    }

    [Fact]
    public void Shy_lander_dodges_a_fast_cursor()
    {
        var world = Desktop();
        var game = NewGame(world, 9, new GameOptions { Shyness = 1 });
        var sensors = new Sensors { UserIdleSeconds = 1 };
        Sim.RunGame(game, world, sensors, 1);

        sensors.Cursor = game.Body.Position + new Vec2(20, 0);
        sensors.CursorVelocity = new Vec2(-400, 0);
        var start = game.Body.Position.X;
        var events = Sim.RunGame(game, world, sensors, 8, g => g.Body.IsLanded && !g.Autopilot.IsFlightActive && g.Time > 2);

        Assert.Contains(events, e => e.Kind == GameEventKind.TookOff);
        Assert.True(game.Body.Position.X < start - game.Body.Height);
        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.Crashed);
    }

    [Fact]
    public void Notification_makes_it_hop_in_place()
    {
        var world = Desktop();
        var game = NewGame(world, 2);
        var sensors = new Sensors { UserIdleSeconds = 1 };
        Sim.RunGame(game, world, sensors, 1);
        var start = game.Body.Position;

        game.Notify(new NotificationArrived());
        var events = Sim.RunGame(game, world, sensors, 10, g => g.Body.IsLanded && !g.Autopilot.IsFlightActive);

        Assert.Contains(events, e => e.Kind == GameEventKind.TookOff);
        Assert.Equal(start.X, game.Body.Position.X, 0);
        Assert.True(game.Body.IsOnBase);
    }

    [Fact]
    public void Recycle_bin_trip_celebrates_and_lands()
    {
        var world = Desktop();
        var game = NewGame(world, 4);
        var sensors = new Sensors { UserIdleSeconds = 1 };
        Sim.RunGame(game, world, sensors, 1);

        game.Notify(new RecycleBinEmptied(new Vec2(60, 60)));
        var events = Sim.RunGame(game, world, sensors, 40, g => g.Body.IsLanded && !g.Autopilot.IsFlightActive);

        Assert.Contains(events, e => e.Kind == GameEventKind.Celebrated);
        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.Crashed);
        Assert.True(game.Body.Position.X < 400);
    }

    [Fact]
    public void Thrown_lander_recovers_in_the_air()
    {
        var world = Desktop();
        var game = NewGame(world, 6);
        var sensors = new Sensors { UserIdleSeconds = 1 };
        Sim.RunGame(game, world, sensors, 1);

        game.BeginDrag(game.Body.Position);
        for (var i = 0; i < 20; i++)
        {
            game.Drag(game.Body.Position + new Vec2(25, -30));
            Sim.RunGame(game, world, sensors, Sim.Dt);
        }

        game.EndDrag();
        var events = Sim.RunGame(game, world, sensors, 30, g => g.Body.IsLanded && !g.Autopilot.IsFlightActive);

        Assert.True(game.Body.IsLanded);
        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.Crashed);
    }

    [Fact]
    public void Guidance_tracks_a_sideways_velocity_by_tilting()
    {
        var world = Desktop();
        var body = Sim.FlyingBody(400, 400);
        var engine = new PhysicsEngine(new PhysicsConfig());

        Sim.RunPhysics(engine, body, world, b => FlightGuidance.TrackVelocity(b, engine.Config, new Vec2(150, 0)), _ => false, 4);

        Assert.InRange(body.Velocity.X, 130, 170);
        Assert.InRange(body.Velocity.Y, -20, 20);
        Assert.InRange(body.Angle, 0.05, FlightGuidance.DefaultMaxTilt + 0.05);
    }

    /// <summary>
    /// A veces cambia de idea a medio vuelo y va a otro sitio, pero de tarde en tarde (no a cada rato), sin accidentes y
    /// posándose donde ha decidido al final.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Sometimes_changes_its_mind_in_flight_but_rarely(int seed)
    {
        var world = Desktop(
            Window(1, 100, 250, 450, 350, app: "a"),
            Window(2, 700, 500, 450, 300, app: "b"),
            Window(3, 1300, 200, 450, 400, app: "c"),
            Window(4, 450, 650, 400, 250, app: "d"));
        var game = NewGame(world, seed, new GameOptions { Acrobatics = 0 });
        game.Autopilot.Personality.Curiosity = 1;
        var sensors = new Sensors { UserIdleSeconds = 1 };
        var minutes = 40.0;

        var events = Sim.RunGame(game, world, sensors, minutes * 60);

        var changes = events.Count(e => e.Kind == GameEventKind.ChangedMind);
        var flights = events.Count(e => e.Kind == GameEventKind.TookOff);
        Assert.InRange(changes, 1, minutes * 60 / 150 + 1);
        Assert.True(changes * 3 < flights, $"{changes} cambios en {flights} vuelos");
        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.Crashed);
    }
}
