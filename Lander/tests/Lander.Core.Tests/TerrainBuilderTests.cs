using Lander.Core.Terrain;
using static Lander.Core.Tests.TestWorlds;

namespace Lander.Core.Tests;

public class TerrainBuilderTests
{
    [Fact]
    public void Ground_sits_on_top_of_the_taskbar()
    {
        var world = Desktop();

        var ground = Assert.Single(world.Platforms);
        Assert.Equal(PlatformKind.Ground, ground.Kind);
        Assert.Equal(GroundY, ground.Y);
        Assert.Equal(0, ground.Left);
        Assert.Equal(Width, ground.Right);
        Assert.Same(ground, world.PrimaryGround);
    }

    [Fact]
    public void Window_top_edge_becomes_platform_without_rounded_corners()
    {
        var world = Desktop(Window(1, 200, 300, 800, 500, app: "notepad"));

        var platform = Assert.Single(world.SegmentsOf(1));
        Assert.Equal(PlatformKind.Window, platform.Kind);
        Assert.Equal(300, platform.Y);
        Assert.Equal(208, platform.Left);
        Assert.Equal(992, platform.Right);
        Assert.Equal("notepad", platform.AppId);
    }

    [Fact]
    public void Window_above_in_z_order_hides_part_of_the_title_bar()
    {
        var front = Window(1, 400, 200, 300, 400);
        var back = Window(2, 100, 300, 1000, 400);

        var world = Desktop(front, back);

        var segments = world.SegmentsOf(2).OrderBy(p => p.Left).ToList();
        Assert.Equal(2, segments.Count);
        Assert.Equal(108, segments[0].Left);
        Assert.Equal(400, segments[0].Right);
        Assert.Equal(700, segments[1].Left);
        Assert.Equal(1092, segments[1].Right);
    }

    [Fact]
    public void Fully_covered_window_has_no_platform()
    {
        var world = Desktop(Window(1, 0, 0, 1920, 1032), Window(2, 300, 300, 400, 300));

        Assert.False(world.HasOwner(2));
    }

    [Fact]
    public void Maximized_window_offers_no_platform_and_hides_the_ones_behind()
    {
        var world = Desktop(Window(1, 0, 0, 1920, 1032, maximized: true), Window(2, 300, 300, 400, 300));

        Assert.False(world.HasOwner(1));
        Assert.False(world.HasOwner(2));
        Assert.Single(world.Platforms, p => p.Kind == PlatformKind.Ground);
    }

    [Fact]
    public void Window_without_room_above_offers_no_platform()
    {
        var world = Desktop(Window(1, 0, 0, 960, 1032), Window(2, 960, 30, 960, 600), Window(3, 960, 700, 960, 300));

        Assert.False(world.HasOwner(1));
        Assert.False(world.HasOwner(2));
        Assert.True(world.HasOwner(3));
    }

    [Fact]
    public void Window_at_the_top_of_a_monitor_with_another_above_keeps_its_platform()
    {
        var top = Monitor(0, taskbar: 0);
        var bottom = Monitor(1, top: Height, primary: false);

        var world = TerrainBuilder.Build([top, bottom], [Window(1, 200, Height, 800, 500)]);

        var platform = Assert.Single(world.SegmentsOf(1));
        Assert.Equal(Height, platform.Y);
    }

    [Fact]
    public void Narrow_or_hidden_windows_are_ignored()
    {
        var world = Desktop(
            Window(1, 100, 300, 60, 200),
            Window(2, 100, 1040, 400, 200),
            Window(3, 5000, 300, 400, 200));

        Assert.False(world.HasOwner(1));
        Assert.False(world.HasOwner(2));
        Assert.False(world.HasOwner(3));
    }

    [Fact]
    public void Platforms_scale_with_monitor_dpi()
    {
        var monitor = Monitor(dpi: 2);
        var world = TerrainBuilder.Build([monitor], [Window(1, 100, 400, 800, 300)]);

        var platform = Assert.Single(world.SegmentsOf(1));
        Assert.Equal(116, platform.Left);
        Assert.Equal(884, platform.Right);
    }

    [Fact]
    public void Side_by_side_monitors_open_the_shared_wall()
    {
        var left = Monitor(0);
        var right = Monitor(1, left: 1920, primary: false);

        var world = TerrainBuilder.Build([left, right], []);

        Assert.DoesNotContain(world.Barriers, b => b.Kind == BarrierKind.RightWall && b.Position == 1920);
        Assert.DoesNotContain(world.Barriers, b => b.Kind == BarrierKind.LeftWall && b.Position == 1920);
        Assert.Contains(world.Barriers, b => b.Kind == BarrierKind.LeftWall && b.Position == 0);
        Assert.Contains(world.Barriers, b => b.Kind == BarrierKind.RightWall && b.Position == 3840);
        Assert.Equal(2, world.Platforms.Count(p => p.Kind == PlatformKind.Ground));
    }

    [Fact]
    public void Monitors_with_different_heights_keep_partial_walls()
    {
        var left = Monitor(0);
        var right = Monitor(1, left: 1920, top: 200, primary: false);

        var world = TerrainBuilder.Build([left, right], []);

        var wall = Assert.Single(world.Barriers, b => b.Kind == BarrierKind.RightWall && b.Position == 1920);
        Assert.Equal(0, wall.From);
        Assert.Equal(200, wall.To);
    }

    [Fact]
    public void Stacked_monitor_without_taskbar_opens_the_floor()
    {
        var top = Monitor(0, taskbar: 0, primary: false);
        var bottom = Monitor(1, top: 1080);

        var world = TerrainBuilder.Build([top, bottom], []);

        Assert.Null(world.GroundOf(0));
        Assert.NotNull(world.GroundOf(1));
        Assert.DoesNotContain(world.Barriers, b => b.Kind == BarrierKind.Floor && b.Position == 1080);
    }

    [Fact]
    public void PlatformBelow_finds_the_highest_surface_under_a_point()
    {
        var world = Desktop(Window(1, 100, 500, 800, 300), Window(2, 300, 300, 300, 100));

        Assert.Equal(2, world.PlatformBelow(450, 100)!.OwnerId);
        Assert.Equal(1, world.PlatformBelow(200, 100)!.OwnerId);
        Assert.Equal(PlatformKind.Ground, world.PlatformBelow(1500, 100)!.Kind);
    }

    [Fact]
    public void Window_origin_is_tracked_for_movement()
    {
        var world = Desktop(Window(7, 250, 420, 600, 300));

        Assert.True(world.TryGetOwnerOrigin(7, out var origin));
        Assert.Equal(250, origin.X);
        Assert.Equal(420, origin.Y);
        Assert.NotNull(world.FindSegment(7, 500));
        Assert.Null(world.FindSegment(7, 100));
    }
}
