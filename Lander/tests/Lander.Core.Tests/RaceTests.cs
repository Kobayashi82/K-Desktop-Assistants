using Lander.Core.Game;
using Lander.Core.Physics;
using Lander.Core.Progression;
using Lander.Core.Racing;
using Lander.Core.Terrain;
using static Lander.Core.Tests.TestWorlds;

namespace Lander.Core.Tests;

public class RaceTests
{
    private readonly RaceSystem _races = new(new GameRandom(5));
    private readonly List<GameEvent> _events = [];

    private static World ThreeWindows() => Desktop(
        Window(1, 150, 500, 420, 300, app: "notepad"),
        Window(2, 760, 300, 420, 300, app: "explorer"),
        Window(3, 1380, 620, 420, 250, app: "calc"));

    private static LanderBody BodyOnGround(World world)
    {
        var body = new LanderBody();
        body.PlaceOn(world.PrimaryGround!, 900, 0);
        return body;
    }

    private static Platform PlatformOf(World world, RaceCheckpoint checkpoint) =>
        checkpoint.IsBase ? world.PrimaryGround! : Race.TargetPlatform(world, checkpoint)!;

    [Fact]
    public void Needs_at_least_two_windows()
    {
        var world = Desktop(Window(1, 700, 500, 400, 300));

        Assert.Equal(RaceStartResult.NoCircuit, _races.Start(world, BodyOnGround(world), false, _events));
        Assert.Null(_races.Current);
    }

    [Fact]
    public void Circuit_visits_distinct_windows_and_ends_at_the_base()
    {
        var world = ThreeWindows();

        Assert.Equal(RaceStartResult.Started, _races.Start(world, BodyOnGround(world), false, _events));

        var race = _races.Current!;
        Assert.Equal(RaceStatus.Ready, race.Status);
        Assert.Equal(4, race.Checkpoints.Count);
        Assert.True(race.Checkpoints[^1].IsBase);
        Assert.Equal(3, race.Checkpoints.Take(3).Select(c => c.OwnerId).Distinct().Count());
        Assert.True(race.ParSeconds > 5);
        Assert.Contains(_events, e => e.Kind == GameEventKind.RaceStarted);
    }

    [Fact]
    public void Clock_starts_on_takeoff()
    {
        var world = ThreeWindows();
        var body = BodyOnGround(world);
        _races.Start(world, body, false, _events);

        _races.Update(world, body, 3, _events);
        Assert.Equal(0, _races.Current!.Elapsed);

        _races.OnTakeoff();
        _races.Update(world, body, 2.5, _events);
        Assert.Equal(RaceStatus.Running, _races.Current.Status);
        Assert.Equal(2.5, _races.Current.Elapsed, 6);
    }

    [Fact]
    public void Checkpoints_count_only_in_order()
    {
        var world = ThreeWindows();
        var body = BodyOnGround(world);
        _races.Start(world, body, false, _events);
        var race = _races.Current!;
        _races.OnTakeoff();

        // Posarse en la segunda baliza o en la base antes de tiempo no cuenta.
        _races.OnTouchdown(PlatformOf(world, race.Checkpoints[1]), LandingQuality.Perfect, body, _events);
        _races.OnTouchdown(world.PrimaryGround!, LandingQuality.Perfect, body, _events);
        Assert.Equal(0, race.Next);

        _races.OnTouchdown(PlatformOf(world, race.Checkpoints[0]), LandingQuality.Perfect, body, _events);
        Assert.Equal(1, race.Next);
        Assert.Contains(_events, e => e.Kind == GameEventKind.RaceCheckpoint);
    }

    [Fact]
    public void Hard_landing_on_a_checkpoint_adds_a_penalty()
    {
        var world = ThreeWindows();
        var body = BodyOnGround(world);
        _races.Start(world, body, false, _events);
        var race = _races.Current!;
        _races.OnTakeoff();

        _races.OnTouchdown(PlatformOf(world, race.Checkpoints[0]), LandingQuality.Hard, body, _events);

        Assert.Equal(Race.HardLandingPenalty, race.Penalties);
        Assert.Equal(Race.HardLandingPenalty, race.Elapsed, 6);
        Assert.True(race.LastLegPenalized);
    }

    [Fact]
    public void Finishing_awards_a_medal_points_and_a_record()
    {
        var world = ThreeWindows();
        var body = BodyOnGround(world);
        _races.Start(world, body, false, _events);
        var race = _races.Current!;
        _races.OnTakeoff();
        _races.Update(world, body, race.ParSeconds * 0.9, _events);

        foreach (var checkpoint in race.Checkpoints)
        {
            _races.OnTouchdown(PlatformOf(world, checkpoint), LandingQuality.Perfect, body, _events);
        }

        Assert.Equal(RaceStatus.Finished, race.Status);
        Assert.Equal(RaceMedal.Gold, race.Medal);
        Assert.True(race.IsNewRecord);
        var finished = Assert.Single(_events, e => e.Kind == GameEventKind.RaceFinished);
        Assert.Equal(RaceSystem.Points(RaceMedal.Gold, 3), finished.Points);
        Assert.Equal(race.Elapsed, _races.LastCircuitRecord);
        Assert.False(_races.IsActive);

        // El resultado se queda a la vista un momento y luego desaparece.
        _races.Update(world, body, RaceSystem.ResultDuration + 0.1, _events);
        Assert.Null(_races.Current);
    }

    [Fact]
    public void Repeating_a_circuit_keeps_its_checkpoints_and_record()
    {
        var world = ThreeWindows();
        var body = BodyOnGround(world);
        _races.Start(world, body, false, _events);
        var first = _races.Current!;
        _races.OnTakeoff();
        _races.Update(world, body, 40, _events);
        foreach (var checkpoint in first.Checkpoints)
        {
            _races.OnTouchdown(PlatformOf(world, checkpoint), LandingQuality.Perfect, body, _events);
        }

        Assert.True(_races.CanRepeat);
        Assert.Equal(RaceStartResult.Started, _races.Start(world, body, true, _events));

        var second = _races.Current!;
        Assert.Equal(first.CircuitKey, second.CircuitKey);
        Assert.Equal(40, second.PreviousRecord!.Value, 6);

        // Más lento que el récord: no lo mejora.
        _races.OnTakeoff();
        _races.Update(world, body, 50, _events);
        foreach (var checkpoint in second.Checkpoints)
        {
            _races.OnTouchdown(PlatformOf(world, checkpoint), LandingQuality.Perfect, body, _events);
        }

        Assert.False(second.IsNewRecord);
        Assert.Equal(40, _races.LastCircuitRecord!.Value, 6);
    }

    [Fact]
    public void Repeat_fails_when_a_window_is_gone()
    {
        var world = ThreeWindows();
        var body = BodyOnGround(world);
        _races.Start(world, body, false, _events);
        var gone = _races.Current!.Checkpoints[0].OwnerId;
        _races.Abandon(body, _events);

        var windows = new[]
        {
            Window(1, 150, 500, 420, 300, app: "notepad"),
            Window(2, 760, 300, 420, 300, app: "explorer"),
            Window(3, 1380, 620, 420, 250, app: "calc"),
        }.Where(w => w.Handle != gone).ToArray();

        Assert.Equal(RaceStartResult.CircuitGone, _races.Start(Desktop(windows), body, true, _events));
    }

    [Fact]
    public void Crashing_or_leaving_manual_mode_loses_the_race()
    {
        var world = ThreeWindows();
        var body = BodyOnGround(world);

        _races.Start(world, body, false, _events);
        _races.OnCrash(body, _events);
        Assert.Equal(RaceFailure.Crashed, _races.Current!.Failure);

        _races.Start(world, body, false, _events);
        _races.OnModeChanged(ControlMode.Auto, body, _events);
        Assert.Equal(RaceFailure.Abandoned, _races.Current!.Failure);
        Assert.Equal(2, _events.Count(e => e.Kind == GameEventKind.RaceFailed));
    }

    [Fact]
    public void Running_out_of_time_loses_the_race()
    {
        var world = ThreeWindows();
        var body = BodyOnGround(world);
        _races.Start(world, body, false, _events);
        _races.OnTakeoff();

        _races.Update(world, body, _races.Current!.TimeLimit + 1, _events);

        Assert.Equal(RaceFailure.TimeUp, _races.Current.Failure);
    }

    [Fact]
    public void A_closed_window_is_skipped_after_a_while()
    {
        var world = ThreeWindows();
        var body = BodyOnGround(world);
        _races.Start(world, body, false, _events);
        var race = _races.Current!;
        var first = race.Checkpoints[0].OwnerId;
        var without = Desktop(new[]
        {
            Window(1, 150, 500, 420, 300),
            Window(2, 760, 300, 420, 300),
            Window(3, 1380, 620, 420, 250),
        }.Where(w => w.Handle != first).ToArray());

        _races.Update(without, body, RaceSystem.LostCheckpointGrace / 2, _events);
        Assert.Equal(0, race.Next);

        _races.Update(without, body, RaceSystem.LostCheckpointGrace, _events);
        Assert.Equal(1, race.Next);
    }

    [Theory]
    [InlineData(29, RaceMedal.Gold)]
    [InlineData(40, RaceMedal.Silver)]
    [InlineData(60, RaceMedal.Bronze)]
    public void Medals_depend_on_the_reference_time(double seconds, RaceMedal expected) =>
        Assert.Equal(expected, Race.MedalFor(seconds, 30));

    [Fact]
    public void Times_are_formatted_with_tenths()
    {
        var comma = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        Assert.Equal($"9{comma}5", Race.FormatTime(9.54));
        Assert.Equal($"1:05{comma}0", Race.FormatTime(65));
    }

    /// <summary>Carrera completa con el piloto simulado: despega, sigue las balizas y vuelve a la base.</summary>
    [Fact]
    public void Demo_pilot_completes_a_race_in_a_reasonable_time()
    {
        var world = ThreeWindows();
        var game = new LanderGame(new GameOptions(), new PilotProgress(), new GameRandom(11));
        game.Initialize(world);
        var pilot = new DemoPilot(new GameRandom(2));

        Assert.Equal(RaceStartResult.Started, game.StartRace());
        Assert.Equal(ControlMode.Manual, game.Mode);
        var race = game.Races.Current!;

        var events = Sim.RunGame(game, world, new Sensors(), race.TimeLimit + 5, g => !g.Races.IsActive, g => pilot.Next(g, world));

        Assert.True(race.Status == RaceStatus.Finished, $"{race.Status} {race.Failure} en {race.Elapsed:0.0} s, baliza {race.Next}");
        Assert.Contains(events, e => e.Kind == GameEventKind.RaceFinished);
        Assert.Equal(1, game.Progress.RacesCompleted);
        Assert.True(game.Progress.TotalPoints >= RaceSystem.Points(RaceMedal.Bronze, 3));

        // El piloto simulado es prudente: no llega al oro, pero tampoco se queda muy lejos de la referencia.
        Assert.True(race.Elapsed < race.ParSeconds * Race.SilverFactor, $"{race.Elapsed:0.0} s con referencia {race.ParSeconds:0.0} s");

        // Terminar la primera carrera es un logro.
        Assert.Contains(events, e => e.Kind == GameEventKind.AchievementUnlocked && e.Achievement!.Id == "first-race");
    }

    [Fact]
    public void A_race_withdraws_the_current_mission()
    {
        var world = ThreeWindows();
        var game = new LanderGame(new GameOptions(), new PilotProgress(), new GameRandom(11));
        game.Initialize(world);
        Assert.True(game.OfferMission(Missions.MissionKind.CollectCrystals));

        game.StartRace();
        var events = new List<GameEvent>();
        game.DrainEvents(events);

        Assert.Null(game.Missions.Current);
        Assert.Contains(events, e => e.Kind == GameEventKind.MissionExpired);
        Assert.False(game.OfferMission());
    }
}
