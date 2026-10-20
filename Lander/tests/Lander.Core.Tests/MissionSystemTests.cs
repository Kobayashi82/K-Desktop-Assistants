using Lander.Core.Game;
using Lander.Core.Missions;
using Lander.Core.Physics;
using Lander.Core.Terrain;
using static Lander.Core.Tests.TestWorlds;

namespace Lander.Core.Tests;

public class MissionSystemTests
{
    private readonly MissionSystem _missions = new(new GameRandom(3));
    private readonly List<GameEvent> _events = [];

    private static LanderBody BodyOnGround(World world)
    {
        var body = new LanderBody();
        body.PlaceOn(world.PrimaryGround!, 900, 0);
        return body;
    }

    [Fact]
    public void Offer_is_accepted_by_taking_manual_control()
    {
        var world = Desktop();
        var body = BodyOnGround(world);

        Assert.True(_missions.OfferNow(world, body, ControlMode.Auto, _events, MissionKind.CollectCrystals));
        Assert.Equal(MissionStatus.Offered, _missions.Current!.Status);

        _missions.OnModeChanged(ControlMode.Manual, world, body, _events);

        Assert.Equal(MissionStatus.Active, _missions.Current!.Status);
        Assert.Contains(_events, e => e.Kind == GameEventKind.MissionStarted);
    }

    [Fact]
    public void Unaccepted_offer_expires()
    {
        var world = Desktop();
        var body = BodyOnGround(world);
        _missions.OfferNow(world, body, ControlMode.Auto, _events, MissionKind.CollectCrystals);

        for (var t = 0.0; t < MissionSystem.OfferDuration + 1; t += 0.5)
        {
            _missions.Update(world, body, ControlMode.Auto, new Sensors(), 0.5, _events);
        }

        Assert.Null(_missions.Current);
        Assert.Contains(_events, e => e.Kind == GameEventKind.MissionExpired);
    }

    [Fact]
    public void Collecting_all_crystals_completes_the_mission()
    {
        var world = Desktop();
        var body = BodyOnGround(world);
        _missions.OfferNow(world, body, ControlMode.Manual, _events, MissionKind.CollectCrystals);
        var mission = _missions.Current!;
        Assert.Equal(3, mission.Crystals.Count);
        Assert.All(mission.Crystals, c => Assert.NotNull(world.MonitorAt(c)));

        body.Fly(default);
        foreach (var crystal in mission.Crystals.ToList())
        {
            body.Position = crystal;
            _missions.Update(world, body, ControlMode.Manual, new Sensors(), 0.016, _events);
        }

        Assert.Equal(3, _events.Count(e => e.Kind == GameEventKind.CrystalCollected));
        var completed = Assert.Single(_events, e => e.Kind == GameEventKind.MissionCompleted);
        Assert.Equal(mission.Reward, completed.Points);
        Assert.Null(_missions.Current);
    }

    [Fact]
    public void Crystals_are_not_collected_by_the_autopilot()
    {
        var world = Desktop();
        var body = BodyOnGround(world);
        _missions.OfferNow(world, body, ControlMode.Manual, _events, MissionKind.CollectCrystals);
        body.Fly(default);
        body.Position = _missions.Current!.Crystals[0];

        _missions.Update(world, body, ControlMode.Auto, new Sensors(), 0.016, _events);

        Assert.DoesNotContain(_events, e => e.Kind == GameEventKind.CrystalCollected);
    }

    [Fact]
    public void Landing_on_the_requested_app_completes_the_mission()
    {
        var world = Desktop(Window(1, 300, 300, 600, 400, app: "explorer"));
        var body = BodyOnGround(world);
        _missions.OfferNow(world, body, ControlMode.Manual, _events, MissionKind.LandOnApp);
        Assert.Equal("explorer", _missions.Current!.AppId);
        Assert.Equal(1, _missions.Current!.AppHandle);

        _missions.OnTouchdown(world.PrimaryGround!, LandingQuality.Perfect, true, world, body, _events);
        Assert.NotNull(_missions.Current);

        _missions.OnTouchdown(world.SegmentsOf(1)[0], LandingQuality.Hard, true, world, body, _events);
        Assert.Contains(_events, e => e.Kind == GameEventKind.MissionCompleted);
    }

    [Fact]
    public void Round_trip_requires_leaving_base_landing_far_and_coming_back()
    {
        var world = Desktop(Window(1, 100, 300, 500, 400));
        var body = BodyOnGround(world);
        _missions.OfferNow(world, body, ControlMode.Manual, _events, MissionKind.RoundTrip);
        var mission = _missions.Current!;

        _missions.OnTakeoff(world.PrimaryGround, body);
        Assert.Equal(1, mission.Stage);

        body.Position = body.Position with { X = 300 };
        _missions.OnTouchdown(world.SegmentsOf(1)[0], LandingQuality.Perfect, true, world, body, _events);
        Assert.Equal(2, mission.Stage);

        _missions.OnTouchdown(world.PrimaryGround!, LandingQuality.Perfect, true, world, body, _events);
        Assert.Contains(_events, e => e.Kind == GameEventKind.MissionCompleted);
    }

    [Fact]
    public void Delivery_needs_two_monitors_and_fails_on_crash()
    {
        var single = Desktop();
        Assert.False(_missions.OfferNow(single, BodyOnGround(single), ControlMode.Manual, _events, MissionKind.Delivery));

        var world = TerrainBuilder.Build([Monitor(0), Monitor(1, left: 1920, primary: false)], []);
        var body = BodyOnGround(world);
        Assert.True(_missions.OfferNow(world, body, ControlMode.Manual, _events, MissionKind.Delivery));
        Assert.Equal(1, _missions.Current!.TargetMonitor);

        _missions.OnCrash(body, _events);

        Assert.Null(_missions.Current);
        Assert.Contains(_events, e => e.Kind == GameEventKind.MissionExpired);
    }

    [Fact]
    public void Delivery_completes_on_the_target_monitor()
    {
        var world = TerrainBuilder.Build([Monitor(0), Monitor(1, left: 1920, primary: false)], []);
        var body = BodyOnGround(world);
        _missions.OfferNow(world, body, ControlMode.Manual, _events, MissionKind.Delivery);

        _missions.OnTouchdown(world.GroundOf(1)!, LandingQuality.Hard, true, world, body, _events);

        Assert.Contains(_events, e => e.Kind == GameEventKind.MissionCompleted);
    }

    [Fact]
    public void Missions_are_offered_periodically_when_the_user_is_active()
    {
        var world = Desktop(Window(1, 300, 300, 600, 400, app: "notepad"));
        var body = BodyOnGround(world);
        _missions.Frequency = MissionFrequency.High;

        for (var t = 0.0; t < 400 && _missions.Current is null; t += 1)
        {
            _missions.Update(world, body, ControlMode.Auto, new Sensors { UserIdleSeconds = 5 }, 1, _events);
        }

        Assert.NotNull(_missions.Current);
        Assert.Contains(_events, e => e.Kind == GameEventKind.MissionOffered);
    }

    [Fact]
    public void A_requested_mission_runs_even_when_missions_are_off()
    {
        var world = Desktop();
        var body = BodyOnGround(world);
        _missions.Frequency = MissionFrequency.Off;
        _missions.OfferNow(world, body, ControlMode.Manual, _events, MissionKind.CollectCrystals);
        var mission = _missions.Current!;

        body.Fly(Lander.Core.Geometry.Vec2.Zero);
        body.Position = mission.Crystals[0];
        _missions.Update(world, body, ControlMode.Manual, new Sensors(), Sim.Dt, _events);

        Assert.Equal(mission.TotalCrystals - 1, mission.Crystals.Count);
        Assert.True(mission.TimeLeft < mission.Duration);
    }

    [Fact]
    public void No_offers_when_missions_are_off()
    {
        var world = Desktop();
        var body = BodyOnGround(world);
        _missions.Frequency = MissionFrequency.Off;

        for (var t = 0.0; t < 3600; t += 10)
        {
            _missions.Update(world, body, ControlMode.Auto, new Sensors(), 10, _events);
        }

        Assert.Null(_missions.Current);
    }
}
