using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Progression;
using Lander.Core.Terrain;
using static Lander.Core.Tests.TestWorlds;

namespace Lander.Core.Tests;

public class LanderGameTests
{
    private static LanderGame NewGame(World world, PilotProgress? progress = null, GameOptions? options = null)
    {
        var game = new LanderGame(options ?? new GameOptions(), progress ?? new PilotProgress(), new GameRandom(42));
        game.Initialize(world);
        return game;
    }

    /// <summary>Piloto humano sencillo: mantiene el descenso por debajo de 35 px/s.</summary>
    private static ManualControls GentlePilot(LanderGame game) =>
        new(Up: game.Body.Velocity.Y > 35, Down: false, Left: false, Right: false, Boost: false);

    [Fact]
    public void Starts_on_the_primary_taskbar()
    {
        var world = Desktop();
        var game = NewGame(world);

        Assert.True(game.Body.IsOnBase);
        Assert.Equal(GroundY - game.Body.HalfHeight, game.Body.Position.Y, 6);
        Assert.Equal(ControlMode.Auto, game.Mode);
    }

    [Fact]
    public void Manual_perfect_landing_on_a_window_scores_points()
    {
        var world = Desktop(Window(1, 700, 600, 300, 300));
        var game = NewGame(world);
        game.SetMode(ControlMode.Manual, ModeChangeReason.User);
        game.Body.Position = new Vec2(850, 400);
        game.Body.Fly(Vec2.Zero);

        var events = Sim.RunGame(game, world, new Sensors(), 20, g => g.Body.IsLanded, GentlePilot);

        var touchdown = Assert.Single(events, e => e.Kind == GameEventKind.Touchdown);
        Assert.True(touchdown.Manual);
        Assert.Equal(LandingQuality.Perfect, touchdown.Landing!.Quality);
        Assert.True(touchdown.Points > Scoring.BasePoints(LandingQuality.Perfect));
        Assert.Equal(touchdown.Points, game.Progress.TotalPoints);
        Assert.Equal(1, game.Progress.PerfectLandings);
        Assert.Equal(1, game.Progress.CurrentStreak);
        Assert.NotEmpty(game.Visual.Popups);
    }

    [Fact]
    public void Touch_and_go_hops_do_not_score()
    {
        var world = Desktop();
        var game = NewGame(world);
        game.SetMode(ControlMode.Manual, ModeChangeReason.User);

        // Pulsaciones cortísimas del motor: el lander apenas despega y vuelve a posarse una y otra vez.
        var events = Sim.RunGame(game, world, new Sensors(), 6,
            controls: g => new ManualControls(g.Time % 0.5 < 0.05, false, false, false, false));

        Assert.Contains(events, e => e.Kind == GameEventKind.Touchdown);
        Assert.Equal(0, game.Progress.TotalPoints);
        Assert.Equal(0, game.Progress.CurrentStreak);
        Assert.Empty(game.Visual.Popups);
    }

    [Fact]
    public void Manual_control_times_out_back_to_auto()
    {
        var world = Desktop();
        var game = NewGame(world, options: new GameOptions { ManualTimeoutSeconds = 5 });
        game.SetMode(ControlMode.Manual, ModeChangeReason.User);

        var events = Sim.RunGame(game, world, new Sensors(), 6);

        Assert.Equal(ControlMode.Auto, game.Mode);
        Assert.Contains(events, e => e.Kind == GameEventKind.ModeChanged && e.Reason == ModeChangeReason.Timeout);
    }

    [Fact]
    public void Pressing_keys_keeps_manual_mode_alive()
    {
        var world = Desktop();
        var game = NewGame(world, options: new GameOptions { ManualTimeoutSeconds = 5 });
        game.SetMode(ControlMode.Manual, ModeChangeReason.User);

        Sim.RunGame(game, world, new Sensors(), 8, controls: g => new ManualControls(false, g.Time % 2 < 0.1, false, false, false));

        Assert.Equal(ControlMode.Manual, game.Mode);
    }

    [Fact]
    public void Crash_respawns_at_base_after_a_moment()
    {
        var world = Desktop();
        var game = NewGame(world);
        game.SetMode(ControlMode.Manual, ModeChangeReason.User);
        game.Body.Position = new Vec2(900, 100);
        game.Body.Fly(Vec2.Zero);

        var events = Sim.RunGame(game, world, new Sensors(), 10, g => g.Body.IsLanded);

        Assert.Contains(events, e => e.Kind == GameEventKind.Crashed);
        Assert.Contains(events, e => e.Kind == GameEventKind.Respawned);
        Assert.True(game.Body.IsOnBase);
        Assert.Equal(game.Body.FuelCapacity, game.Body.Fuel);
        Assert.Equal(1, game.Progress.Crashes);
        Assert.Equal(0, game.Progress.CurrentStreak);
    }

    [Fact]
    public void Crossing_a_level_threshold_raises_level_up_and_applies_upgrades()
    {
        var world = Desktop(Window(1, 700, 600, 300, 300));
        var progress = new PilotProgress { TotalPoints = Levels.PointsForLevel(3) - 10 };
        var game = NewGame(world, progress);
        var capacityBefore = game.Body.FuelCapacity;
        game.SetMode(ControlMode.Manual, ModeChangeReason.User);
        game.Body.Position = new Vec2(850, 400);
        game.Body.Fly(Vec2.Zero);

        var events = Sim.RunGame(game, world, new Sensors(), 20, g => g.Body.IsLanded, GentlePilot);

        var levelUp = Assert.Single(events, e => e.Kind == GameEventKind.LevelUp);
        Assert.Equal(3, levelUp.Level);
        Assert.Contains(levelUp.Unlocks, u => u.Id == Unlocks.TankUpgrade);
        Assert.True(game.Body.FuelCapacity > capacityBefore);
    }

    [Fact]
    public void Pausing_while_flying_lands_and_then_stays_still()
    {
        var world = Desktop(Window(1, 400, 400, 900, 400));
        var game = NewGame(world);
        game.Body.Position = new Vec2(800, 150);
        game.Body.Fly(Vec2.Zero);

        game.SetPaused(true);
        var events = Sim.RunGame(game, world, new Sensors { UserIdleSeconds = 1 }, 30, g => g.Body.IsLanded);
        var landedAt = game.Body.Position;
        events.AddRange(Sim.RunGame(game, world, new Sensors { UserIdleSeconds = 1 }, 300));

        Assert.True(game.IsPaused);
        Assert.Equal(landedAt, game.Body.Position);
        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.Crashed);
        Assert.False(game.NeedsHighFrequency);
    }

    [Fact]
    public void Quiet_mode_keeps_the_lander_resting()
    {
        var world = Desktop(Window(1, 400, 400, 900, 400));
        var game = NewGame(world, options: new GameOptions { Activity = 3 });

        var events = Sim.RunGame(game, world, new Sensors { QuietMode = true, UserIdleSeconds = 1 }, 120);

        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.TookOff);
    }

    [Fact]
    public void Snap_to_surface_puts_a_flying_lander_down_instantly()
    {
        var world = Desktop(Window(1, 400, 500, 900, 400));
        var game = NewGame(world);
        game.SetMode(ControlMode.Manual, ModeChangeReason.User);
        game.Body.Position = new Vec2(800, 150);
        game.Body.Fly(new Vec2(100, 50));

        game.SnapToSurface();

        Assert.True(game.Body.IsLanded);
        Assert.Equal(1, game.Body.Support!.OwnerId);
        Assert.Equal(ControlMode.Auto, game.Mode);
    }

    [Fact]
    public void Wind_gust_pushes_a_flying_lander()
    {
        var world = Desktop();
        var game = NewGame(world);
        game.SetMode(ControlMode.Manual, ModeChangeReason.User);
        game.Body.Position = new Vec2(900, 400);
        game.Body.Fly(Vec2.Zero);

        game.Notify(new WindGust(new Vec2(600, 0), 0.5));
        Sim.RunGame(game, world, new Sensors(), 0.5, controls: _ => new ManualControls(true, false, false, false, false));

        Assert.True(game.Body.Velocity.X > 50);
    }

    [Fact]
    public void Lander_scales_with_size_setting_and_dpi()
    {
        var world = TerrainBuilder.Build([Monitor(dpi: 1.5)], []);
        var game = NewGame(world, options: new GameOptions { LanderSizeDip = 64 });

        Assert.Equal(64 / 48.0 * 1.5, game.Body.Scale, 6);
        Assert.Equal(64 * 1.5, game.Body.Height, 6);
    }

    /// <summary>Posado en una ventana, ↓ lo deja caer a través de ella; sin volver a pulsarla, se posa en lo de debajo.</summary>
    [Fact]
    public void Down_while_landed_drops_through_the_window()
    {
        var world = Desktop(Window(1, 700, 400, 500, 300));
        var game = NewGame(world);
        var window = world.SegmentsOf(1).MaxBy(p => p.Width)!;
        game.Body.PlaceOn(window, window.CenterX, world.Timestamp);
        game.SetMode(ControlMode.Manual, ModeChangeReason.User);

        // Sin soltarla: una sola pulsación atraviesa una sola plataforma.
        Sim.RunGame(game, world, new Sensors(), 20, g => g.Body.IsLanded && g.Time > 0.5, g => new ManualControls(Up: g.Body.Velocity.Y > 60, Down: true, Left: false, Right: false, Boost: false));

        Assert.True(game.Body.IsOnBase, $"Posado en {game.Body.Support?.Kind}, {game.Body.State} en {game.Body.Position}, t={game.Time:0.00}, ventana y={window.Y}");
        Assert.Equal(0, game.Body.PassThrough);
    }

    [Fact]
    public void Down_on_the_base_does_nothing()
    {
        var world = Desktop();
        var game = NewGame(world);
        game.SetMode(ControlMode.Manual, ModeChangeReason.User);
        var y = game.Body.Position.Y;

        Sim.RunGame(game, world, new Sensors(), 1, controls: g => new ManualControls(false, g.Time % 0.2 < 0.1, false, false, false));

        Assert.True(game.Body.IsOnBase);
        Assert.Equal(y, game.Body.Position.Y, 3);
    }

    /// <summary>Si sube otra vez por encima de la ventana que atravesaba, vuelve a poder posarse en ella.</summary>
    [Fact]
    public void Climbing_back_above_the_window_makes_it_solid_again()
    {
        var world = Desktop(Window(1, 700, 400, 500, 300));
        var game = NewGame(world);
        var window = world.SegmentsOf(1).MaxBy(p => p.Width)!;
        game.Body.PlaceOn(window, window.CenterX, world.Timestamp);
        game.SetMode(ControlMode.Manual, ModeChangeReason.User);

        Sim.RunGame(game, world, new Sensors(), 0.1, controls: _ => new ManualControls(false, true, false, false, false));
        Assert.Equal(1, game.Body.PassThrough);
        Sim.RunGame(game, world, new Sensors(), 3, g => g.Body.FeetY < window.Y - 30, _ => new ManualControls(true, false, false, false, false));
        Sim.RunGame(game, world, new Sensors(), 6, g => g.Body.IsLanded, g => new ManualControls(Up: g.Body.Velocity.Y > 35, Down: false, Left: false, Right: false, Boost: false));

        Assert.Equal(1, game.Body.Support?.OwnerId);
    }
}
