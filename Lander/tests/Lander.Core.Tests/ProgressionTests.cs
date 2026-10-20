using Lander.Core.Physics;
using Lander.Core.Progression;
using Lander.Core.Terrain;
using static Lander.Core.Tests.TestWorlds;

namespace Lander.Core.Tests;

public class ProgressionTests
{
    [Fact]
    public void Levels_grow_monotonically()
    {
        Assert.Equal(1, Levels.LevelFor(0));
        for (var level = 2; level <= Levels.MaxLevel; level++)
        {
            Assert.True(Levels.PointsForLevel(level) > Levels.PointsForLevel(level - 1));
            Assert.Equal(level, Levels.LevelFor(Levels.PointsForLevel(level)));
            Assert.Equal(level - 1, Levels.LevelFor(Levels.PointsForLevel(level) - 1));
        }

        Assert.Equal(Levels.MaxLevel, Levels.LevelFor(long.MaxValue / 2));
    }

    [Fact]
    public void Progress_to_next_level_is_a_fraction()
    {
        var start = Levels.PointsForLevel(4);
        var end = Levels.PointsForLevel(5);

        Assert.Equal(0, Levels.ProgressToNext(start), 6);
        Assert.Equal(0.5, Levels.ProgressToNext((start + end) / 2), 2);
    }

    [Fact]
    public void Narrow_windows_are_worth_more_than_the_taskbar()
    {
        var world = Desktop(Window(1, 100, 300, 120, 300), Window(2, 600, 300, 1200, 300));
        var narrow = world.SegmentsOf(1)[0];
        var wide = world.SegmentsOf(2)[0];
        var ground = world.PrimaryGround!;

        Assert.Equal(1, Scoring.Difficulty(ground, 36));
        Assert.Equal(1, Scoring.Difficulty(wide, 36));
        Assert.True(Scoring.Difficulty(narrow, 36) > 2.5);
        Assert.True(Scoring.Points(LandingQuality.Perfect, narrow, 36, 1) > Scoring.Points(LandingQuality.Perfect, ground, 36, 1));
    }

    [Fact]
    public void Perfect_streaks_multiply_points_up_to_a_cap()
    {
        var ground = Desktop().PrimaryGround!;

        Assert.Equal(100, Scoring.Points(LandingQuality.Perfect, ground, 36, 1));
        Assert.Equal(150, Scoring.Points(LandingQuality.Perfect, ground, 36, 2));
        Assert.Equal(300, Scoring.Points(LandingQuality.Perfect, ground, 36, 5));
        Assert.Equal(300, Scoring.Points(LandingQuality.Perfect, ground, 36, 50));
        Assert.Equal(40, Scoring.Points(LandingQuality.Hard, ground, 36, 5));
        Assert.Equal(0, Scoring.Points(LandingQuality.Crash, ground, 36, 5));
    }

    [Fact]
    public void Upgrades_apply_with_level()
    {
        var config = new PhysicsConfig();

        Assert.Equal(config.FuelCapacity, Unlocks.ApplyUpgrades(config, 1).FuelCapacity);
        Assert.Equal(config.FuelCapacity * 1.3, Unlocks.ApplyUpgrades(config, 3).FuelCapacity, 6);
        Assert.Equal(config.MainBurnRate * 0.8, Unlocks.ApplyUpgrades(config, 5).MainBurnRate, 6);
        Assert.Equal(config.Hard.VerticalSpeed * 1.3, Unlocks.ApplyUpgrades(config, 8).Hard.VerticalSpeed, 6);
    }

    [Fact]
    public void Unlock_catalog_is_consistent()
    {
        Assert.Equal(Unlocks.All.Count, Unlocks.All.Select(u => u.Id).Distinct().Count());
        Assert.True(Unlocks.IsUnlocked(Unlocks.DefaultSkin, 1));
        Assert.True(Unlocks.IsUnlocked(Unlocks.DefaultTrail, 1));
        Assert.False(Unlocks.IsUnlocked("pixel", 11));
        Assert.True(Unlocks.IsUnlocked("pixel", 12));
        Assert.Equal(5, Unlocks.OfKind(UnlockKind.Skin).Count());
        Assert.All(Unlocks.All, u => Assert.InRange(u.Level, 1, Levels.MaxLevel));
    }
}
