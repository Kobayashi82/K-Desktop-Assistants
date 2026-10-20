using Lander.Core.Autopilot;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Progression;
using Lander.Core.Terrain;
using static Lander.Core.Tests.TestWorlds;

namespace Lander.Core.Tests;

/// <summary>
/// Las técnicas de vuelo una a una (salto balístico, saltitos, rasante, precisión, picado y ondulante), la
/// predicción de trayectorias y los límites de seguridad entre monitores.
/// </summary>
public class FlightStyleTests
{
    /// <summary>Un escritorio alto con ventanas a distintas alturas y bien separadas.</summary>
    private static World TallDesktop() => TerrainBuilder.Build(
        [Monitor(height: 1440)],
        [
            Window(1, 100, 260, 600, 500),
            Window(2, 1200, 200, 600, 400),
            Window(3, 700, 820, 500, 300),
            Window(4, 1500, 900, 380, 300),
        ]);

    /// <summary>Dos monitores de distinto tamaño y a distinta altura: hay paredes y techos entre ellos.</summary>
    private static World TwoMonitors() => TerrainBuilder.Build(
        [Monitor(), Monitor(1, left: -1600, top: 300, width: 1600, height: 900, primary: false)],
        [
            Window(1, 200, 250, 700, 500),
            Window(2, 1100, 120, 600, 400),
            Window(3, -1400, 520, 600, 400),
            Window(4, -700, 760, 500, 300),
        ]);

    private static LanderGame NewGame(World world, int seed, FlightStyle? style = null, double acrobatics = 0.5)
    {
        var game = new LanderGame(new GameOptions { Activity = 4, Acrobatics = acrobatics }, new PilotProgress(), new GameRandom(seed));
        game.Initialize(world);
        game.Autopilot.ForcedStyle = style;
        return game;
    }

    /// <summary>Vuela un rato y cuenta los aterrizajes de cada estilo, comprobando que todos son perfectos.</summary>
    private static Dictionary<FlightStyle, int> Fly(LanderGame game, World world, double minutes, List<GameEvent>? all = null)
    {
        var landings = new Dictionary<FlightStyle, int>();
        var events = new List<GameEvent>();
        var style = FlightStyle.Direct;
        for (var i = 0; i < minutes * 60 * 60; i++)
        {
            game.Step(world, new Sensors { UserIdleSeconds = 1 }, default);
            if (game.Body.IsFlying)
            {
                style = game.Autopilot.Style;
            }

            game.DrainEvents(events);
            foreach (var e in events)
            {
                Assert.False(e.Kind is GameEventKind.Crashed or GameEventKind.HitCeiling or GameEventKind.FuelEmpty, $"{e.Kind} volando con {style}");
                if (e.Kind == GameEventKind.Touchdown)
                {
                    Assert.Equal(LandingQuality.Perfect, e.Landing!.Quality);
                    landings[style] = landings.GetValueOrDefault(style) + 1;
                }
            }

            all?.AddRange(events);
            events.Clear();
        }

        return landings;
    }

    [Theory]
    [InlineData(FlightStyle.Ballistic)]
    [InlineData(FlightStyle.Contour)]
    [InlineData(FlightStyle.Precision)]
    [InlineData(FlightStyle.Swoop)]
    [InlineData(FlightStyle.Dolphin)]
    [InlineData(FlightStyle.Hopper)]
    public void Each_new_flight_style_lands_perfectly(FlightStyle style)
    {
        var landings = 0;
        for (var seed = 1; seed <= 4; seed++)
        {
            // Los saltitos son por la misma plataforma: sin ventanas, por la barra de tareas.
            var world = style == FlightStyle.Hopper ? Desktop() : TallDesktop();
            landings += Fly(NewGame(world, seed, style, acrobatics: 1), world, 10).GetValueOrDefault(style);
        }

        Assert.True(landings >= 2, $"Solo {landings} aterrizajes con {style}.");
    }

    [Fact]
    public void Ballistic_hop_coasts_with_the_engine_off_and_brakes_at_the_end()
    {
        var world = TallDesktop();
        var coasting = 0;
        var burnsWhileCoasting = 0;
        var ignitions = 0;
        for (var seed = 1; seed <= 4 && ignitions == 0; seed++)
        {
            var game = NewGame(world, seed, FlightStyle.Ballistic, acrobatics: 1);
            var events = new List<GameEvent>();
            for (var i = 0; i < 10 * 60 * 60; i++)
            {
                game.Step(world, new Sensors { UserIdleSeconds = 1 }, default);
                if (game.Autopilot.Style == FlightStyle.Ballistic && game.Autopilot.IsFreeFalling)
                {
                    coasting++;
                    burnsWhileCoasting += game.LastInput.Throttle > 0 ? 1 : 0;
                }

                game.DrainEvents(events);
                ignitions += events.Count(e => e.Kind == GameEventKind.Ignition && game.Autopilot.Style == FlightStyle.Ballistic);
                events.Clear();
            }
        }

        Assert.True(coasting > 60, $"Solo {coasting} pasos con el motor apagado.");
        Assert.Equal(0, burnsWhileCoasting);
        Assert.True(ignitions > 0);
    }

    [Fact]
    public void Precision_flight_stays_level_and_slides_with_the_translation_jets()
    {
        var world = TallDesktop();
        var maxTilt = 0.0;
        var slides = 0;
        for (var seed = 1; seed <= 3; seed++)
        {
            var game = NewGame(world, seed, FlightStyle.Precision);
            for (var i = 0; i < 8 * 60 * 60; i++)
            {
                game.Step(world, new Sensors { UserIdleSeconds = 1 }, default);
                if (game.Body.IsFlying && game.Autopilot.Style == FlightStyle.Precision && game.Body.StateTime > 1)
                {
                    maxTilt = Math.Max(maxTilt, Math.Abs(game.Body.Angle));
                    slides += game.LastInput.Lateral != 0 ? 1 : 0;
                }

                game.DrainEvents([]);
            }
        }

        Assert.True(slides > 30);
        Assert.True(maxTilt < 0.12, $"Se inclinó {maxTilt:0.00} rad.");
    }

    [Fact]
    public void Hopper_lands_between_hops_on_the_same_platform()
    {
        var world = Desktop();
        var hops = 0;
        for (var seed = 1; seed <= 6 && hops == 0; seed++)
        {
            hops += Fly(NewGame(world, seed, FlightStyle.Hopper), world, 10).GetValueOrDefault(FlightStyle.Hopper);
        }

        Assert.True(hops >= 3, $"Solo {hops} saltitos.");
    }

    [Fact]
    public void Without_acrobatics_there_are_no_stunts()
    {
        var world = TallDesktop();
        var events = new List<GameEvent>();
        var styles = new HashSet<FlightStyle>();
        for (var seed = 1; seed <= 3; seed++)
        {
            var game = NewGame(world, seed, acrobatics: 0);
            for (var i = 0; i < 10 * 60 * 60; i++)
            {
                game.Step(world, new Sensors { UserIdleSeconds = 1 }, default);
                if (game.Body.IsFlying)
                {
                    styles.Add(game.Autopilot.Style);
                }

                game.DrainEvents(events);
            }
        }

        Assert.DoesNotContain(events, e => e.Kind is GameEventKind.Flipped or GameEventKind.EngineCut);
        Assert.DoesNotContain(FlightStyle.Ballistic, styles);
        Assert.DoesNotContain(FlightStyle.Dolphin, styles);
        Assert.DoesNotContain(FlightStyle.HoverSlam, styles);
    }

    [Theory]
    [InlineData(61)]
    [InlineData(62)]
    [InlineData(63)]
    public void Flies_between_monitors_of_different_height_without_touching_walls_or_ceilings(int seed)
    {
        var world = TwoMonitors();
        var events = new List<GameEvent>();
        var landings = Fly(NewGame(world, seed, acrobatics: 1), world, 15, events);

        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.HitWall);
        Assert.True(landings.Values.Sum() >= 10);
    }

    [Fact]
    public void Recovering_below_a_window_pinned_to_the_top_edge_never_runs_out_of_fuel()
    {
        // La ventana está pegada arriba: no hay sitio para llegar a su barra de título desde arriba.
        var world = Desktop(Window(1, 600, 2, 900, 600));
        var game = NewGame(world, 3);
        game.Body.Position = new Vec2(1000, 70);
        game.Body.Fly(new Vec2(40, 0));

        var events = Sim.RunGame(game, world, new Sensors { UserIdleSeconds = 1 }, 60, g => g.Body.IsLanded && !g.Autopilot.IsFlightActive);

        Assert.DoesNotContain(events, e => e.Kind is GameEventKind.Crashed or GameEventKind.FuelEmpty);
        Assert.True(game.Body.IsLanded);
    }

    [Fact]
    public void Predictor_follows_the_same_physics_as_the_engine()
    {
        var engine = new PhysicsEngine(new PhysicsConfig());
        var world = Desktop();
        var body = Sim.FlyingBody(900, 300, vx: 120, vy: -80);
        var probe = FlightPredictor.CopyInto(new LanderBody(), body);
        var input = new ThrustInput(Throttle: 0.6, Rotate: 0.3, Lateral: -0.5);
        var events = new List<PhysicsEvent>();
        for (var i = 0; i < 90; i++)
        {
            engine.Step(body, input, world, Sim.Dt, events);
            FlightPredictor.Advance(probe, engine.Config, input, Sim.Dt);
        }

        Assert.Equal(body.Position.X, probe.Position.X, 3);
        Assert.Equal(body.Position.Y, probe.Position.Y, 3);
        Assert.Equal(body.Angle, probe.Angle, 6);
    }

    [Fact]
    public void Launch_velocity_draws_a_parabola_through_the_aim_point()
    {
        var config = new PhysicsConfig();
        var from = new Vec2(200, 900);
        var aim = new Vec2(1400, 700);
        var velocity = FlightPredictor.LaunchVelocity(from, aim, 400, config.Gravity, config);

        var (end, apex, _) = FlightPredictor.Coast(from, velocity, aim.Y, config.Gravity, config);

        Assert.Equal(aim.X, end.X, 0.02 * (aim.X - from.X));
        Assert.Equal(400, apex, 15.0);
    }

    [Fact]
    public void Ceiling_guard_looks_at_the_lower_top_edge_of_the_monitor_it_is_drifting_into()
    {
        // Sube deprisa junto al borde del monitor principal hacia el de la izquierda, que empieza más abajo.
        var world = TwoMonitors();
        var config = new PhysicsConfig();
        var body = Sim.FlyingBody(60, 420, vx: -260, vy: -180);

        var input = FlightEnvelope.Guard(world, body, config, new ThrustInput(Throttle: 1));

        Assert.Equal(0, input.Throttle);
        Assert.Equal(1, FlightEnvelope.Guard(world, Sim.FlyingBody(900, 420, vy: -180), config, new ThrustInput(Throttle: 1)).Throttle);
    }

    [Fact]
    public void Paths_that_leave_the_monitors_are_not_clear()
    {
        var world = TwoMonitors();
        var body = new LanderBody { Position = new Vec2(400, 600) };

        // Por encima del monitor de la izquierda (que empieza en y = 300) no hay pantalla.
        Assert.False(FlightEnvelope.PathIsClear(world, body, [new Vec2(200, 200), new Vec2(-200, 200)], 1100));
        Assert.True(FlightEnvelope.PathIsClear(world, body, [new Vec2(200, 600), new Vec2(-200, 600)], 1100));
    }
}
