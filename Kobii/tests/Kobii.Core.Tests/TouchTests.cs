using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Tests;

/// <summary>Los juegos con el cursor: cosquillas, cepillarle, chocar los cinco y darse la mano.</summary>
public sealed class TouchTests
{
    private static readonly MonitorInfo Monitor = new(0, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1, true);

    private static World Desk() => TerrainBuilder.Build([Monitor], []);

    private static (KobiiGame Game, Sensors Sensors) Start(World world)
    {
        var game = new KobiiGame(new GameOptions(), new GameRandom(5));
        game.Initialize(world, 0.5);
        var sensors = new Sensors { LocalTime = new DateTime(2026, 10, 23, 12, 0, 0), UserIdleSeconds = 1 };
        Step(game, world, sensors, 0.5);
        return (game, sensors);
    }

    private static List<GameEvent> Step(KobiiGame game, World world, Sensors sensors, double seconds, Func<double, Vec2?>? cursor = null)
    {
        var events = new List<GameEvent>();
        for (var t = 0.0; t < seconds; t += KobiiGame.TimeStep)
        {
            if (cursor is not null)
            {
                var previous = sensors.Cursor;
                sensors.Cursor = cursor(t);
                sensors.CursorVelocity = previous is { } p && sensors.Cursor is { } c ? (c - p) / KobiiGame.TimeStep : Vec2.Zero;
            }

            game.Step(world, sensors);
            game.DrainEvents(events);
        }

        return events;
    }

    /// <summary>El cursor va y viene deprisa por la barriga.</summary>
    private static Func<double, Vec2?> Rubbing(KobiiGame game, double height = 0.3, double speed = 14)
    {
        var center = game.Body.Feet - new Vec2(0, game.Body.Height * height);
        var reach = game.Body.HalfWidth * 0.6;
        return t => center + new Vec2(reach * Math.Sin(t * speed), 0);
    }

    [Fact]
    public void Rubbing_its_belly_fast_makes_it_laugh()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        var events = Step(game, world, sensors, 2.5, Rubbing(game));
        Assert.Contains(events, e => e.Kind == GameEventKind.Tickled);
        Assert.Contains(events, e => e.Kind == GameEventKind.Giggled);
        Assert.Equal(PlayKind.Tickles, game.Playing);

        // Cuando paras, recupera el aliento y lo deja.
        sensors.Cursor = null;
        Step(game, world, sensors, 5);
        Assert.NotEqual(PlayKind.Tickles, game.Playing);
    }

    [Fact]
    public void With_the_brush_rubbing_is_brushing_and_it_shakes_after()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        game.BrushMode = true;
        var events = Step(game, world, sensors, 2, Rubbing(game, 0.7, 8));
        Assert.Contains(events, e => e.Kind == GameEventKind.Brushed);
        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.Tickled);
        Assert.Equal(PlayKind.Brush, game.Playing);
        Assert.True(game.Visual.Shine > 0);
        sensors.Cursor = null;
        events = Step(game, world, sensors, 5);
        Assert.Contains(events, e => e.Kind == GameEventKind.Shook);
        Assert.NotEqual(PlayKind.Brush, game.Playing);
    }

    [Fact]
    public void Gives_you_a_high_five_when_you_touch_its_raised_hand()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        sensors.Cursor = game.Body.Feet + new Vec2(game.Body.Height * 3, -game.Body.Height * 2);
        Assert.True(game.OfferHighFive());
        Step(game, world, sensors, 0.8);
        var hand = game.HandPosition(1);
        var from = sensors.Cursor!.Value;
        var events = Step(game, world, sensors, 2, t => Vec2.Lerp(from, hand, Math.Min(1, t / 0.15)));
        Assert.Contains(events, e => e.Kind == GameEventKind.HighFive);
    }

    [Fact]
    public void Leaving_it_hanging_disappoints_it()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        sensors.Cursor = game.Body.Feet + new Vec2(game.Body.Height * 4, -game.Body.Height * 3);
        Assert.True(game.OfferHighFive());
        var events = Step(game, world, sensors, 7);
        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.HighFive);
        Assert.NotEqual(PlayKind.HighFive, game.Playing);
    }

    [Fact]
    public void Shakes_hands_when_you_take_its_hand_and_move_it_up_and_down()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        sensors.Cursor = game.Body.Feet + new Vec2(game.Body.Height * 3, -game.Body.Height * 0.5);
        Assert.True(game.OfferHandshake());
        Step(game, world, sensors, 0.7);
        var hand = game.HandPosition(1);
        var amplitude = game.Body.Height * 0.12;
        var events = Step(game, world, sensors, 4, t => hand + new Vec2(0, amplitude * Math.Sin(t * 14)));
        Assert.Contains(events, e => e.Kind == GameEventKind.Handshake);
    }
}
