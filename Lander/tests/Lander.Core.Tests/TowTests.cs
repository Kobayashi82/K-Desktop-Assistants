using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Progression;
using Lander.Core.Terrain;
using Lander.Core.Towing;
using static Lander.Core.Tests.TestWorlds;

namespace Lander.Core.Tests;

/// <summary>Fase 7: el disparo del gancho y el remolque de una ventana.</summary>
public class TowTests
{
    [Theory]
    [InlineData(0, 0, 400, -300)]
    [InlineData(0, 0, -900, -60)]
    [InlineData(0, 0, 150, 220)]
    [InlineData(500, 900, 520, 40)]
    [InlineData(0, 0, 1800, 0)]
    public void TheHookLandsWhereItIsAimedAtAnyDistanceAndHeight(double fx, double fy, double tx, double ty)
    {
        var from = new Vec2(fx, fy);
        var to = new Vec2(tx, ty);
        var shot = HookShot.Aim(from, to, scale: 1);
        Assert.InRange(shot.Duration, HookShot.MinDuration, HookShot.MaxDuration);
        Assert.True(Vec2.Distance(shot.At(shot.Duration), to) < 1e-6);
        Assert.True(Vec2.Distance(shot.At(0), from) < 1e-9);

        // Paso a paso con su velocidad y su gravedad llega al mismo sitio (es una parábola de verdad).
        var position = from;
        var velocity = shot.Velocity;
        const int steps = 2000;
        var dt = shot.Duration / steps;
        for (var i = 0; i < steps; i++)
        {
            velocity += new Vec2(0, shot.Gravity * dt);
            position += velocity * dt;
        }

        Assert.True(Vec2.Distance(position, to) < shot.Gravity * shot.Duration * dt + 0.5);
        Assert.True(shot.Hits(to + new Vec2(5, -5)));
        Assert.False(shot.Hits(to + new Vec2(40, 0)));
        Assert.False(shot.Hits(null));
    }

    /// <summary>Remolca con la nave yendo en línea recta de su sitio al final en <paramref name="seconds"/> s, y luego quieta.</summary>
    private static WindowTow Tow(TowRect start, TowRect destination, double seconds, double settle = 1.2)
    {
        var landerStart = new Vec2(start.Left + 100, start.Top - 150);
        var landerEnd = new Vec2(destination.Left + 100, destination.Top - 150);
        var tow = new WindowTow(start, destination, landerStart, landerEnd, titleOffset: 14);
        const double frame = 1.0 / 60;
        for (var t = 0.0; t < seconds + settle; t += frame)
        {
            var k = Math.Clamp(t / seconds, 0, 1);
            var eased = k * k * (3 - 2 * k);
            tow.Step(frame, Vec2.Lerp(landerStart, landerEnd, eased));
        }

        return tow;
    }

    [Fact]
    public void TheWindowFollowsTheLanderAndEndsInItsPlace()
    {
        var start = new TowRect(1400, 250, 900, 560);
        var destination = new TowRect(0, 0, 1720, 1400);
        var tow = Tow(start, destination, seconds: 2);
        Assert.True(tow.Arrived);
        Assert.True(Vec2.Distance(tow.Current.TopLeft, destination.TopLeft) < 1.5);
        Assert.True(Math.Abs(tow.Current.Width - 1720) < 0.5 && Math.Abs(tow.Current.Height - 1400) < 0.5);

        // Con su rebote, pero pequeño.
        var distance = Vec2.Distance(start.TopLeft, destination.TopLeft);
        Assert.True(tow.Overshoot < distance * 0.06, $"Se pasó {tow.Overshoot:0.0} px");

        tow.Finish();
        Assert.Equal(destination, tow.Current);
        Assert.Equal(new Vec2(0 + 260, 14), tow.Hook);
    }

    [Fact]
    public void ItNeverGoesBackAndLagsBehindTheLanderAtTheStart()
    {
        var start = new TowRect(100, 100, 600, 400);
        var destination = new TowRect(1300, 500, 600, 400);
        var landerStart = new Vec2(200, -50);
        var landerEnd = new Vec2(1400, 350);
        var tow = new WindowTow(start, destination, landerStart, landerEnd, 14);

        // La nave a medio camino de golpe: la ventana va detrás, no salta.
        tow.Step(1.0 / 60, Vec2.Lerp(landerStart, landerEnd, 0.5));
        Assert.Equal(0.5, tow.Progress, 6);
        Assert.True(tow.Current.Left < 300);

        // La nave vuelve un poco atrás: el avance no retrocede.
        tow.Step(1.0 / 60, Vec2.Lerp(landerStart, landerEnd, 0.3));
        Assert.Equal(0.5, tow.Progress, 6);
    }

    [Fact]
    public void TheLanderArrivesWithinItsRadius()
    {
        var tow = new WindowTow(new TowRect(0, 0, 400, 300), new TowRect(800, 0, 400, 300), default, new Vec2(800, 0), 14, arrival: 40);
        tow.Step(1.0 / 60, new Vec2(770, 10));
        Assert.Equal(1, tow.Progress, 6);
    }

    [Fact]
    public void BiggerWindowsAreHeavier()
    {
        var small = new WindowTow(new TowRect(0, 0, 400, 300), new TowRect(800, 0, 400, 300), default, new Vec2(800, 0), 14);
        var big = new WindowTow(new TowRect(0, 0, 1900, 1000), new TowRect(800, 0, 1900, 1000), default, new Vec2(800, 0), 14);
        Assert.True(small.Frequency > big.Frequency);

        // Tras un tirón igual, la pequeña ha avanzado más.
        small.Step(0.15, new Vec2(800, 0));
        big.Step(0.15, new Vec2(800, 0));
        Assert.True(small.Current.Left > big.Current.Left);
    }

    [Fact]
    public void TheBoostMakesTheSameFlightFaster()
    {
        double SecondsToArrive(double boost)
        {
            var world = TerrainBuilder.Build([Monitor(height: 1440)], [Window(1, 100, 300, 700, 500)], null, 0);
            var game = new LanderGame(new GameOptions(), new PilotProgress(), new GameRandom(3));
            game.Initialize(world);
            var awake = new Sensors { UserIdleSeconds = 1 };
            for (var i = 0; i < 120; i++)
            {
                game.Step(world, awake, default);
            }

            game.ErrandBoost = boost;
            game.BeginErrand(game.Body.Position + new Vec2(-900, -500), 1.25 * Math.Sqrt(boost));
            for (var i = 1; i < 20 * 60; i++)
            {
                game.Step(world, awake, default);
                if (game.Autopilot.ErrandArrived)
                {
                    game.ErrandBoost = 1;
                    return i / 60.0;
                }
            }

            return double.PositiveInfinity;
        }

        var normal = SecondsToArrive(1);
        var boosted = SecondsToArrive(4);
        Assert.True(double.IsFinite(normal) && double.IsFinite(boosted));
        Assert.True(boosted < normal * 0.65, $"Normal {normal:0.00} s, con empuje {boosted:0.00} s");
    }
}
