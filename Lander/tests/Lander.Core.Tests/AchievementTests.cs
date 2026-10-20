using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Progression;
using static Lander.Core.Tests.TestWorlds;

namespace Lander.Core.Tests;

public class AchievementTests
{
    [Fact]
    public void Ids_are_unique()
    {
        Assert.Equal(Achievements.All.Count, Achievements.All.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void Progress_is_a_fraction_of_the_target()
    {
        var achievement = Achievements.All.Single(a => a.Id == "steady-hand");
        var progress = new PilotProgress { BestStreak = 2 };

        Assert.Equal(0.4, achievement.ProgressOf(progress), 6);
        Assert.False(achievement.IsEarned(progress));

        progress.BestStreak = 7;
        Assert.Equal(1, achievement.ProgressOf(progress));
        Assert.True(achievement.IsEarned(progress));
    }

    [Fact]
    public void A_new_achievement_is_announced_once()
    {
        var world = Desktop(Window(1, 700, 600, 300, 300));
        var game = new LanderGame(new GameOptions(), new PilotProgress(), new GameRandom(1));
        game.Initialize(world);
        game.SetMode(ControlMode.Manual, ModeChangeReason.User);
        game.Body.Position = new Vec2(850, 400);
        game.Body.Fly(Vec2.Zero);

        var events = Sim.RunGame(game, world, new Sensors(), 25,
            controls: g => new ManualControls(Up: g.Body.Velocity.Y > 35, Down: false, Left: false, Right: false, Boost: false));

        Assert.Single(events, e => e.Kind == GameEventKind.AchievementUnlocked && e.Achievement!.Id == "first-perfect");
    }

    [Fact]
    public void Achievements_earned_before_are_not_announced_again()
    {
        var world = Desktop();
        var game = new LanderGame(new GameOptions(), new PilotProgress { PerfectLandings = 3, Crashes = 30 }, new GameRandom(1));
        game.Initialize(world);

        var events = Sim.RunGame(game, world, new Sensors(), 3);

        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.AchievementUnlocked);
    }
}
