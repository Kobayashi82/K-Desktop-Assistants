using Lander.Core.Autopilot;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Progression;
using Lander.Core.Terrain;
using static Lander.Core.Tests.TestWorlds;

namespace Lander.Core.Tests;

/// <summary>Vuelos del piloto automático: estilos, frenadas finales, techo, combustible y despistes.</summary>
public class FlightTests
{
    /// <summary>Un escritorio alto con ventanas a distintas alturas: sitio para caídas largas y volteretas.</summary>
    private static World TallDesktop(double timestamp = 0) => TerrainBuilder.Build(
        [Monitor(height: 1440)],
        [
            Window(1, 100, 200, 600, 500),
            Window(2, 1200, 160, 600, 400),
            Window(3, 700, 760, 500, 300),
        ],
        null,
        timestamp);

    private static LanderGame NewGame(World world, int seed, GameOptions? options = null)
    {
        var game = new LanderGame(options ?? new GameOptions { Activity = 4 }, new PilotProgress(), new GameRandom(seed));
        game.Initialize(world);
        return game;
    }

    [Theory]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    public void Fifteen_minutes_of_varied_flights_without_accidents_ceiling_hits_or_empty_tanks(int seed)
    {
        var world = TallDesktop();
        var game = NewGame(world, seed);
        var styles = new HashSet<FlightStyle>();
        var events = new List<GameEvent>();
        for (var i = 0; i < 15 * 60 * 60; i++)
        {
            game.Step(world, new Sensors { UserIdleSeconds = 1 }, default);
            game.DrainEvents(events);
            if (game.Body.IsFlying)
            {
                styles.Add(game.Autopilot.Style);
            }
        }

        Assert.DoesNotContain(events, e => e.Kind is GameEventKind.Crashed or GameEventKind.HitCeiling or GameEventKind.FuelEmpty);
        Assert.All(events.Where(e => e.Kind == GameEventKind.Touchdown), e => Assert.Equal(LandingQuality.Perfect, e.Landing!.Quality));
        Assert.True(styles.Count >= 4, $"Solo {styles.Count} estilos de vuelo: {string.Join(", ", styles)}");
    }

    [Fact]
    public void Suicide_burns_cut_the_engine_fall_and_brake_at_the_last_moment()
    {
        var engineCuts = 0;
        var ignitions = 0;
        for (var seed = 30; seed < 36; seed++)
        {
            var world = TallDesktop();
            var game = NewGame(world, seed);
            var events = Sim.RunGame(game, world, new Sensors { UserIdleSeconds = 1 }, 600);
            engineCuts += events.Count(e => e.Kind == GameEventKind.EngineCut);
            ignitions += events.Count(e => e.Kind == GameEventKind.Ignition);
            Assert.DoesNotContain(events, e => e.Kind is GameEventKind.Crashed or GameEventKind.HitCeiling);
        }

        Assert.True(engineCuts > 0);
        Assert.True(ignitions >= engineCuts);
    }

    [Fact]
    public void Free_fall_reaches_real_speed_before_the_burn()
    {
        var world = TallDesktop();
        for (var seed = 30; seed < 60; seed++)
        {
            var game = NewGame(world, seed);
            var maxFall = 0.0;
            var events = new List<GameEvent>();
            for (var i = 0; i < 600 * 60 && maxFall == 0; i++)
            {
                game.Step(world, new Sensors { UserIdleSeconds = 1 }, default);
                game.DrainEvents(events);
                if (events.Any(e => e.Kind == GameEventKind.Ignition) && game.Autopilot.Style == FlightStyle.HoverSlam)
                {
                    maxFall = game.Body.Velocity.Y;
                }

                events.Clear();
            }

            if (maxFall > 0)
            {
                // Mucho más deprisa de lo que bajaría con una aproximación prudente.
                Assert.True(maxFall > 250, $"Caía a {maxFall:0} px/s al encender");
                return;
            }
        }

        Assert.Fail("Ninguna semilla hizo una frenada final.");
    }

    [Fact]
    public void Window_dragged_up_to_the_top_edge_drops_the_lander_and_it_recovers()
    {
        var game = NewGame(Desktop(Window(1, 400, 500, 800, 400)), 3, new GameOptions());
        var start = Desktop(Window(1, 400, 500, 800, 400));
        game.Body.PlaceOn(start.SegmentsOf(1)[0], 800, 0);
        var sensors = new Sensors { UserIdleSeconds = 1 };
        var events = new List<GameEvent>();

        // El usuario sube la ventana hasta pegarla arriba en dos segundos.
        var steps = 120;
        for (var i = 0; i <= steps; i++)
        {
            var time = i * Sim.Dt;
            var top = (int)(500 - 500.0 * i / steps);
            var world = Desktop(time, Window(1, 400, top, 800, 400));
            game.Step(world, sensors, default);
            game.DrainEvents(events);
        }

        var final = Desktop(steps * Sim.Dt, Window(1, 400, 0, 800, 400));
        events.AddRange(Sim.RunGame(game, final, sensors, 30, g => g.Body.IsLanded && !g.Autopilot.IsFlightActive));

        Assert.Contains(events, e => e.Kind == GameEventKind.SupportLost);
        Assert.DoesNotContain(events, e => e.Kind is GameEventKind.Crashed or GameEventKind.HitCeiling);
        Assert.True(game.Body.IsOnBase);
    }

    [Fact]
    public void Lander_dropped_right_under_the_top_edge_never_hits_it()
    {
        var world = Desktop();
        var game = NewGame(world, 4, new GameOptions());
        var sensors = new Sensors { UserIdleSeconds = 1 };
        Sim.RunGame(game, world, sensors, 1);

        game.BeginDrag(game.Body.Position);
        for (var i = 0; i < 40; i++)
        {
            game.Drag(new Vec2(900, 30));
            Sim.RunGame(game, world, sensors, Sim.Dt);
        }

        game.EndDrag();
        var events = Sim.RunGame(game, world, sensors, 30, g => g.Body.IsLanded && !g.Autopilot.IsFlightActive);

        Assert.DoesNotContain(events, e => e.Kind is GameEventKind.Crashed or GameEventKind.HitCeiling);
        Assert.True(game.Body.IsLanded);
    }

    [Fact]
    public void Thrown_upwards_it_cuts_the_engine_instead_of_climbing_into_the_top_edge()
    {
        var world = Desktop();
        var game = NewGame(world, 5, new GameOptions());
        game.Body.Position = new Vec2(900, 300);
        game.Body.Fly(new Vec2(0, -200));
        var sensors = new Sensors { UserIdleSeconds = 1 };

        var events = Sim.RunGame(game, world, sensors, 30, g => g.Body.IsLanded && !g.Autopilot.IsFlightActive);

        Assert.DoesNotContain(events, e => e.Kind is GameEventKind.Crashed or GameEventKind.HitCeiling);
    }

    [Fact]
    public void Ceiling_guard_cuts_the_throttle_near_the_top()
    {
        var world = Desktop();
        var body = Sim.FlyingBody(900, 80, vy: -150);
        var config = new PhysicsConfig();

        var input = FlightEnvelope.Guard(world, body, config, new ThrustInput(Throttle: 1));

        Assert.Equal(0, input.Throttle);
    }

    [Fact]
    public void Clumsy_pilot_sometimes_lands_hard_or_crashes_and_a_careful_one_never_does()
    {
        var careful = 0;
        var clumsy = 0;
        for (var seed = 40; seed < 46; seed++)
        {
            foreach (var errors in new[] { 0.0, 1.0 })
            {
                var world = TallDesktop();
                var game = NewGame(world, seed, new GameOptions { Activity = 4, PilotErrors = errors });
                var events = Sim.RunGame(game, world, new Sensors { UserIdleSeconds = 1 }, 900);
                var mishaps = events.Count(e => e.Kind == GameEventKind.Crashed
                    || (e.Kind == GameEventKind.Touchdown && e.Landing!.Quality != LandingQuality.Perfect));
                if (errors == 0)
                {
                    careful += mishaps;
                }
                else
                {
                    clumsy += mishaps;
                }
            }
        }

        Assert.Equal(0, careful);
        Assert.True(clumsy > 0);
    }

    [Fact]
    public void Autopilot_crashes_do_not_count_against_the_player()
    {
        var world = Desktop();
        var game = NewGame(world, 6, new GameOptions());
        game.Progress.CurrentStreak = 4;
        game.Body.Position = new Vec2(900, 100);
        game.Body.Fly(new Vec2(0, 900));
        game.Body.Fuel = 0;

        var events = Sim.RunGame(game, world, new Sensors { UserIdleSeconds = 1 }, 5);

        Assert.Contains(events, e => e.Kind == GameEventKind.Crashed);
        Assert.Equal(0, game.Progress.Crashes);
        Assert.Equal(4, game.Progress.CurrentStreak);
    }

    [Fact]
    public void Fuel_estimate_covers_what_most_flights_really_burn()
    {
        // La estimación es prudente; los vuelos que se alargan (rodeos, volteretas) los vigila el control
        // de combustible en vuelo, que desvía a la base o aterriza antes de quedarse seco.
        var world = TallDesktop();
        var ratios = new List<double>();
        for (var seed = 50; seed < 56; seed++)
        {
            var game = NewGame(world, seed, new GameOptions { Activity = 4 });
            var events = new List<GameEvent>();
            double? estimate = null;
            var startFuel = 0.0;
            for (var i = 0; i < 300 * 60; i++)
            {
                game.Step(world, new Sensors { UserIdleSeconds = 1 }, default);
                game.DrainEvents(events);
                foreach (var e in events)
                {
                    if (e.Kind == GameEventKind.TookOff && game.Autopilot.Destination is { } destination
                        && destination.TryResolve(world, game.Body, out var target, out _))
                    {
                        estimate = FuelPlanner.Estimate(game.Body, game.Physics, game.Body.Position, target);
                        startFuel = game.Body.Fuel;
                    }
                    else if (e.Kind == GameEventKind.Touchdown && estimate is { } expected
                        && game.Autopilot.Purpose is FlightPurpose.Explore or FlightPurpose.ReturnToBase)
                    {
                        ratios.Add((startFuel - game.Body.Fuel) / expected);
                        estimate = null;
                    }
                }

                events.Clear();
            }
        }

        ratios.Sort();
        Assert.NotEmpty(ratios);
        var p75 = ratios[(int)(ratios.Count * 0.75)];
        Assert.True(p75 < 1, $"Más de una cuarta parte de los vuelos gasta más de lo estimado (p75 = {p75:0.00}).");
    }
}
