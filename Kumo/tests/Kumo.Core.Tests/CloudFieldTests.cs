using Kumo.Core.Cloud;
using Kumo.Core.Geometry;

namespace Kumo.Core.Tests;

/// <summary>El campo de bolas y sus contornos (marching squares): la forma que se dibuja sale de aquí.</summary>
public sealed class CloudFieldTests
{
    [Fact]
    public void A_lone_ball_gives_its_circle_at_level_one()
    {
        var field = new CloudField();
        field.Build([new FieldBlob(0, 0, 1)], 1.34, 0.04);
        var loops = new List<Vec2[]>();
        field.Contours(1, 0.02, loops);
        var loop = Assert.Single(loops);
        foreach (var p in loop)
        {
            Assert.InRange(p.Length, 0.97, 1.03);
        }

        Assert.InRange(Math.Abs(CloudField.Area(loop)), Math.PI * 0.94, Math.PI * 1.06);
    }

    [Fact]
    public void Two_balls_far_apart_are_two_loops_and_close_together_one()
    {
        var field = new CloudField();
        var loops = new List<Vec2[]>();
        field.Build([new FieldBlob(-2, 0, 0.5), new FieldBlob(2, 0, 0.5)], 1.34, 0.04);
        field.Contours(1, 0.03, loops);
        Assert.Equal(2, loops.Count);

        loops.Clear();
        field.Build([new FieldBlob(-0.45, 0, 0.5), new FieldBlob(0.45, 0, 0.5)], 1.34, 0.04);
        field.Contours(1, 0.03, loops);
        Assert.Single(loops);
    }

    [Fact]
    public void A_box_ball_is_a_rounded_rectangle()
    {
        var field = new CloudField();
        field.Build([new FieldBlob(0, 0, 0.3, 1.2, 0.4)], 1.34, 0.04);
        var loops = new List<Vec2[]>();
        field.Contours(1, 0.03, loops);
        var loop = Assert.Single(loops);
        Assert.InRange(loop.Max(p => p.X), 1.45, 1.55);
        Assert.InRange(loop.Max(p => p.Y), 0.65, 0.75);
    }

    [Fact]
    public void The_cloud_keeps_one_body_while_it_changes_shape()
    {
        var cloud = new KumoCloud(new GameRandom(5), 44);
        cloud.Place(new Vec2(500, 500));
        var field = new CloudField();
        var blobs = new FieldBlob[40];
        var loops = new List<Vec2[]>();
        for (var second = 0; second < 40; second++)
        {
            cloud.Step(1);
            var n = cloud.Blobs(blobs);
            field.Build(blobs.AsSpan(0, n), cloud.FieldReach, 0.055);
            loops.Clear();
            field.Contours(1.1, 0.07, loops);

            // El cuerpo (el bucle más grande) siempre está y mide lo de una nube (unas 2,3 × 1,5 unidades).
            var body = loops.OrderByDescending(l => Math.Abs(CloudField.Area(l))).First();
            Assert.InRange(Math.Abs(CloudField.Area(body)), 1.6, 4.5);
        }
    }
}
