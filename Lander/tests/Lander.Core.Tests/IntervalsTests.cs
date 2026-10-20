using Lander.Core.Geometry;

namespace Lander.Core.Tests;

public class IntervalsTests
{
    [Fact]
    public void Subtract_middle_splits_in_two()
    {
        var result = Intervals.Subtract([new Interval(0, 100)], new Interval(40, 60));

        Assert.Equal([new Interval(0, 40), new Interval(60, 100)], result);
    }

    [Fact]
    public void Subtract_covering_cut_removes_everything()
    {
        var result = Intervals.Subtract([new Interval(10, 20)], new Interval(0, 100));

        Assert.Empty(result);
    }

    [Fact]
    public void Subtract_disjoint_cut_keeps_interval()
    {
        var result = Intervals.Subtract(new Interval(0, 10), [new Interval(20, 30), new Interval(-5, -1)]);

        Assert.Equal([new Interval(0, 10)], result);
    }

    [Fact]
    public void Intersect_with_mask_clips_and_merges()
    {
        var result = Intervals.Intersect([new Interval(-50, 250)], [new Interval(0, 100), new Interval(100, 200)]);

        Assert.Equal([new Interval(0, 200)], result);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(Math.PI * 3, Math.PI)]
    [InlineData(-Math.PI * 3, Math.PI)]
    [InlineData(Math.PI / 2 + Math.Tau, Math.PI / 2)]
    public void WrapAngle_normalizes_to_half_open_range(double input, double expected)
    {
        Assert.Equal(expected, MathUtil.WrapAngle(input), 9);
    }
}
