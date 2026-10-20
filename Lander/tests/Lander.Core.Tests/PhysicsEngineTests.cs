using Lander.Core.Autopilot;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Terrain;
using static Lander.Core.Tests.TestWorlds;

namespace Lander.Core.Tests;

public class PhysicsEngineTests
{
    private readonly PhysicsEngine _engine = new(new PhysicsConfig());

    [Fact]
    public void Free_fall_from_high_crashes_on_the_ground()
    {
        var world = Desktop();
        var body = Sim.FlyingBody(500, 100);

        var events = Sim.RunPhysics(_engine, body, world, _ => ThrustInput.None, b => !b.IsFlying, 10);

        Assert.Equal(BodyState.Destroyed, body.State);
        var crash = Assert.Single(events, e => e.Kind == PhysicsEventKind.Crashed);
        Assert.Equal(LandingQuality.Crash, crash.Landing!.Quality);
        Assert.True(body.Damage > 0);
    }

    [Fact]
    public void Controlled_descent_lands_perfectly()
    {
        var world = Desktop();
        var body = Sim.FlyingBody(500, 400);
        var config = _engine.Config;

        var events = Sim.RunPhysics(_engine, body, world,
            b => FlightGuidance.TrackVelocity(b, config, new Vec2(0, 30)), b => !b.IsFlying, 30);

        Assert.True(body.IsLanded);
        Assert.True(body.IsOnBase);
        var touchdown = Assert.Single(events, e => e.Kind == PhysicsEventKind.Touchdown);
        Assert.Equal(LandingQuality.Perfect, touchdown.Landing!.Quality);
        Assert.Equal(GroundY - body.HalfHeight, body.Position.Y, 6);
    }

    [Fact]
    public void Without_fuel_it_falls_freely_and_crashes_from_high_up()
    {
        var world = Desktop();
        var body = Sim.FlyingBody(500, 300, vx: 80);
        body.Fuel = 0;
        body.Angle = 0.4;

        var events = Sim.RunPhysics(_engine, body, world, _ => new ThrustInput(Throttle: 1), b => !b.IsFlying, 60);

        // Caída libre de unos 700 px: llega a más de 400 px/s, muy por encima de lo que aguantan las patas.
        var crash = Assert.Single(events, e => e.Kind == PhysicsEventKind.Crashed);
        Assert.True(crash.Magnitude > 400);
        Assert.Equal(BodyState.Destroyed, body.State);
    }

    [Fact]
    public void Lands_on_a_monitor_without_taskbar()
    {
        var world = TerrainBuilder.Build([Monitor(taskbar: 0)], []);
        var body = Sim.FlyingBody(500, Height - 60, vy: 30);

        var events = Sim.RunPhysics(_engine, body, world, b => FlightGuidance.TrackVelocity(b, _engine.Config, new Vec2(0, 30)), b => !b.IsFlying, 10);

        Assert.True(body.IsOnBase);
        Assert.Contains(events, e => e.Kind == PhysicsEventKind.Touchdown);
    }

    [Fact]
    public void Without_fuel_the_gyros_still_keep_it_upright()
    {
        var world = Desktop();
        var body = Sim.FlyingBody(500, 300);
        body.Fuel = 0;
        body.Angle = 0.5;

        Sim.RunPhysics(_engine, body, world, _ => ThrustInput.None, _ => false, 1.5);

        Assert.InRange(Math.Abs(body.Angle), 0, 0.05);
    }

    [Fact]
    public void Full_throttle_on_the_ground_takes_off()
    {
        var world = Desktop();
        var body = new LanderBody();
        body.PlaceOn(world.PrimaryGround!, 600, world.Timestamp);
        var events = new List<PhysicsEvent>();

        _engine.Step(body, new ThrustInput(Throttle: 1), world, Sim.Dt, events);

        Assert.True(body.IsFlying);
        Assert.True(body.Velocity.Y < 0);
        Assert.Contains(events, e => e.Kind == PhysicsEventKind.TookOff);
    }

    [Fact]
    public void Ceiling_bounces_softly()
    {
        var world = Desktop();
        var body = Sim.FlyingBody(500, 60, vy: -400);

        var events = Sim.RunPhysics(_engine, body, world, _ => ThrustInput.None, b => b.Velocity.Y > 0, 1);

        Assert.Contains(events, e => e.Kind == PhysicsEventKind.HitCeiling);
        Assert.True(body.Position.Y - body.HalfHeight >= 0);
        Assert.Equal(0, body.Damage);
    }

    [Fact]
    public void Side_walls_keep_the_lander_on_screen()
    {
        var world = Desktop();
        var body = Sim.FlyingBody(60, 500, vx: -600);

        var events = Sim.RunPhysics(_engine, body, world, b => FlightGuidance.TrackVelocity(b, _engine.Config, new Vec2(b.Velocity.X, 0)), b => b.Velocity.X > 0, 1);

        Assert.Contains(events, e => e.Kind == PhysicsEventKind.HitWall);
        Assert.True(body.Position.X - body.HalfWidth >= 0);
    }

    [Fact]
    public void Lands_on_window_title_bar_from_above_only()
    {
        var world = Desktop(Window(1, 300, 600, 600, 300));

        var fromBelow = Sim.FlyingBody(600, 700, vy: -250);
        Sim.RunPhysics(_engine, fromBelow, world, _ => new ThrustInput(Throttle: 0.42), b => b.Position.Y < 400, 5);
        Assert.True(fromBelow.IsFlying);

        var fromAbove = Sim.FlyingBody(600, 450);
        var events = Sim.RunPhysics(_engine, fromAbove, world,
            b => FlightGuidance.TrackVelocity(b, _engine.Config, new Vec2(0, 30)), b => !b.IsFlying, 20);
        Assert.True(fromAbove.IsLanded);
        Assert.Equal(1, fromAbove.Support!.OwnerId);
        Assert.Contains(events, e => e.Kind == PhysicsEventKind.Touchdown && e.Landing!.Quality == LandingQuality.Perfect);
    }

    [Fact]
    public void Follows_a_window_that_moves_slowly()
    {
        var time = 0.0;
        var world = Desktop(time, Window(1, 300, 500, 600, 300));
        var body = new LanderBody();
        body.PlaceOn(world.SegmentsOf(1)[0], 500, time);
        var events = new List<PhysicsEvent>();

        for (var i = 1; i <= 60; i++)
        {
            time = i * Sim.Dt;
            world = Desktop(time, Window(1, 300 + i * 3, 500 - i, 600, 300));
            _engine.Step(body, ThrustInput.None, world, Sim.Dt, events);
        }

        Assert.True(body.IsLanded);
        Assert.Equal(680, body.Position.X, 3);
        Assert.Equal(440 - body.HalfHeight, body.Position.Y, 3);
        Assert.DoesNotContain(events, e => e.Kind == PhysicsEventKind.Slipped);
    }

    [Fact]
    public void Closing_the_window_drops_the_lander()
    {
        var world = Desktop(Window(1, 300, 500, 600, 300));
        var body = new LanderBody();
        body.PlaceOn(world.SegmentsOf(1)[0], 500, 0);
        var events = new List<PhysicsEvent>();

        _engine.Step(body, ThrustInput.None, Desktop(Sim.Dt), Sim.Dt, events);

        Assert.True(body.IsFlying);
        Assert.Contains(events, e => e.Kind == PhysicsEventKind.SupportLost);
    }

    [Fact]
    public void Covering_the_spot_with_another_window_drops_the_lander()
    {
        var back = Window(1, 300, 500, 600, 300);
        var world = Desktop(back);
        var body = new LanderBody();
        body.PlaceOn(world.SegmentsOf(1)[0], 500, 0);
        var events = new List<PhysicsEvent>();

        _engine.Step(body, ThrustInput.None, Desktop(Sim.Dt, Window(2, 400, 400, 300, 300), back), Sim.Dt, events);

        Assert.True(body.IsFlying);
        Assert.Contains(events, e => e.Kind == PhysicsEventKind.SupportLost);
    }

    [Fact]
    public void Yanking_the_window_sideways_makes_it_slip()
    {
        var time = 0.0;
        var world = Desktop(time, Window(1, 300, 500, 1200, 300));
        var body = new LanderBody();
        body.PlaceOn(world.SegmentsOf(1)[0], 900, time);
        var events = new List<PhysicsEvent>();

        for (var i = 1; i <= 6; i++)
        {
            time = i * Sim.Dt;
            world = Desktop(time, Window(1, 300 + i * 60, 500, 1200, 300));
            _engine.Step(body, ThrustInput.None, world, Sim.Dt, events);
        }

        Assert.Contains(events, e => e.Kind == PhysicsEventKind.Slipped);
        Assert.True(body.Position.X < 900 + 360);
    }

    [Fact]
    public void Base_refuels_and_repairs()
    {
        var world = Desktop();
        var body = new LanderBody { Fuel = 10, Damage = 0.5 };
        body.PlaceOn(world.PrimaryGround!, 600, 0);

        Sim.RunPhysics(_engine, body, world, _ => ThrustInput.None, _ => false, 1);

        Assert.InRange(body.Fuel, 39, 41);
        Assert.InRange(body.Damage, 0.43, 0.45);
    }

    [Fact]
    public void Boost_gives_an_instant_upward_kick_and_costs_fuel()
    {
        var world = Desktop();
        var body = Sim.FlyingBody(500, 500);
        var events = new List<PhysicsEvent>();

        _engine.Step(body, new ThrustInput(Boost: true), world, Sim.Dt, events);

        Assert.True(body.Velocity.Y < -200);
        Assert.Equal(100 - _engine.Config.BoostFuelCost, body.Fuel, 3);
        Assert.Contains(events, e => e.Kind == PhysicsEventKind.Boosted);
    }

    [Fact]
    public void Landing_with_one_leg_off_the_edge_is_never_perfect()
    {
        var world = Desktop(Window(1, 300, 600, 600, 300));
        var platform = world.SegmentsOf(1)[0];
        var body = Sim.FlyingBody(platform.Right - 5, 500);

        var events = Sim.RunPhysics(_engine, body, world,
            b => FlightGuidance.TrackVelocity(b, _engine.Config, new Vec2(0, 30)), b => !b.IsFlying, 20);

        var touchdown = Assert.Single(events, e => e.Kind == PhysicsEventKind.Touchdown);
        Assert.Equal(LandingQuality.Hard, touchdown.Landing!.Quality);
    }

    [Fact]
    public void Lander_outside_every_monitor_is_brought_back()
    {
        var world = Desktop();
        var body = Sim.FlyingBody(-300, 500);
        var events = new List<PhysicsEvent>();

        _engine.Step(body, ThrustInput.None, world, Sim.Dt, events);

        Assert.NotNull(world.MonitorAt(body.Position));
        Assert.Contains(events, e => e.Kind == PhysicsEventKind.OutOfBounds);
    }

    [Theory]
    [InlineData(30, 10, 0, LandingQuality.Perfect)]
    [InlineData(100, 10, 0, LandingQuality.Hard)]
    [InlineData(30, 80, 0, LandingQuality.Hard)]
    [InlineData(30, 10, 15, LandingQuality.Hard)]
    [InlineData(200, 0, 0, LandingQuality.Crash)]
    [InlineData(30, 150, 0, LandingQuality.Crash)]
    [InlineData(30, 0, 40, LandingQuality.Crash)]
    public void Landing_quality_thresholds(double vy, double vx, double degrees, LandingQuality expected)
    {
        var result = LandingEvaluator.Evaluate(vx, vy, MathUtil.DegreesToRadians(degrees), true, true, new PhysicsConfig(), 1);

        Assert.Equal(expected, result.Quality);
    }

    [Fact]
    public void Landing_without_legs_is_at_most_hard_and_fragile()
    {
        var config = new PhysicsConfig();

        Assert.Equal(LandingQuality.Hard, LandingEvaluator.Evaluate(0, 30, 0, false, true, config, 1).Quality);
        Assert.Equal(LandingQuality.Crash, LandingEvaluator.Evaluate(0, 130, 0, false, true, config, 1).Quality);
    }

    [Fact]
    public void Thresholds_scale_with_lander_size()
    {
        var config = new PhysicsConfig();

        Assert.Equal(LandingQuality.Crash, LandingEvaluator.Evaluate(0, 200, 0, true, true, config, 1).Quality);
        Assert.Equal(LandingQuality.Hard, LandingEvaluator.Evaluate(0, 200, 0, true, true, config, 2).Quality);
    }
}
