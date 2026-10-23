using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Tests;

/// <summary>Los juguetes que le das: traer la pelota que le lanzas, el peluche, la cama y dormir con ellos.</summary>
public sealed class GiftTests
{
    private static readonly MonitorInfo Monitor = new(0, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1, true);

    private static World Desk() => TerrainBuilder.Build([Monitor], []);

    private static (KobiiGame Game, Sensors Sensors) Start(World world, int seed = 7)
    {
        var game = new KobiiGame(new GameOptions(), new GameRandom(seed));
        game.Initialize(world, 0.5);
        var sensors = new Sensors { LocalTime = new DateTime(2026, 10, 23, 12, 0, 0), UserIdleSeconds = 1 };
        Run(game, world, sensors, 0.5);
        return (game, sensors);
    }

    private static List<GameEvent> Run(KobiiGame game, World world, Sensors sensors, double seconds, Func<KobiiGame, bool>? until = null)
    {
        var events = new List<GameEvent>();
        for (var t = 0.0; t < seconds && until?.Invoke(game) != true; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
            game.DrainEvents(events);
            Assert.InRange(game.Body.Feet.X, 0, 1920);
        }

        return events;
    }

    [Fact]
    public void Brings_back_the_ball_you_throw()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        var ball = game.GiveToy(ToyKind.Ball, world);
        Run(game, world, sensors, 2);
        Assert.False(ball.Ephemeral);

        // La lanzas desde la izquierda hacia la derecha; tu cursor se queda donde la lanzaste.
        sensors.Cursor = new Vec2(game.Body.Feet.X - 200, game.Body.Feet.Y - 60);
        ball.Drop(new Vec2(game.Body.Feet.X - 150, game.Body.Feet.Y - 100), new Vec2(700, -500));
        ball.ThrownByYou = true;
        ball.ThrowOrigin = sensors.Cursor.Value;
        var events = Run(game, world, sensors, 25, g => g.Playing is null && !ball.ThrownByYou && ball.Free && ball.Body.IsGrounded && ball.RestSeconds > 0.5);
        Assert.Contains(events, e => e.Kind == GameEventKind.Fetched);

        // Te la ha dejado cerca del cursor, no donde cayó.
        Assert.True(Math.Abs(ball.Body.Feet.X - sensors.Cursor.Value.X) < game.Body.Height * 3, $"La pelota está en {ball.Body.Feet.X:0} y el cursor en {sensors.Cursor.Value.X:0}.");
    }

    [Fact]
    public void Hugs_its_plush_and_leaves_it_on_the_floor()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        var plush = game.GiveToy(ToyKind.Plush, world);
        Run(game, world, sensors, 2.5);
        Assert.True(game.PlayWith(ToyKind.Plush));
        var events = Run(game, world, sensors, 25, g => g.Playing is null);
        Assert.Contains(events, e => e.Kind == GameEventKind.Hugged);
        Assert.False(plush.Held);
        Assert.Contains(plush, game.Playground.Toys);
    }

    [Fact]
    public void Bounces_on_its_bed_and_hops_out()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        game.GiveToy(ToyKind.Bed, world);
        Run(game, world, sensors, 2.5);
        var bedToy = game.Playground.Find(ToyKind.Bed)!;
        Assert.True(game.PlayWith(ToyKind.Bed), $"{game.Activity} {game.Playing} bed grounded {bedToy.Body.IsGrounded} support {bedToy.Body.SupportId}/{game.Body.SupportId} x {bedToy.Body.Feet.X:0}/{game.Body.Feet.X:0}");
        var inBed = false;
        var events = Run(game, world, sensors, 25, g =>
        {
            inBed |= g.Bed is not null;
            return g.Playing is null;
        });

        Assert.True(inBed);
        Assert.True(events.Count(e => e.Kind == GameEventKind.BedBounced) >= 3);
        Assert.Null(game.Bed);
    }

    [Fact]
    public void Goes_to_sleep_in_its_bed_hugging_its_plush_and_hops_out_after_waking_up()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        var plush = game.GiveToy(ToyKind.Plush, world);
        game.GiveToy(ToyKind.Bed, world);
        Run(game, world, sensors, 2.5);

        // Te vas: le entra el sueño y se lleva el peluche a la cama.
        sensors.UserIdleSeconds = 2000;
        Run(game, world, sensors, 40, g => g.Activity == Activity.Sleep);
        Assert.Equal(Activity.Sleep, game.Activity);
        Assert.NotNull(game.Bed);
        Assert.True(plush.Held);

        // Vuelves: se despierta, deja el peluche a su lado y, cuando se va a otro sitio, sale de la cama de un saltito.
        sensors.UserIdleSeconds = 1;
        Run(game, world, sensors, 1);
        Assert.False(plush.Held);
        Run(game, world, sensors, 60, g => g.Bed is null);
        Assert.Null(game.Bed);
    }

    [Fact]
    public void Each_toy_you_give_it_lands_in_a_free_spot()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        var toys = new[] { ToyKind.Ball, ToyKind.Plush, ToyKind.Bed }.Select(k => game.GiveToy(k, world)).ToList();
        Run(game, world, sensors, 0.1);
        for (var i = 0; i < toys.Count; i++)
        {
            for (var j = i + 1; j < toys.Count; j++)
            {
                var gap = Math.Abs(toys[i].Body.Feet.X - toys[j].Body.Feet.X) - toys[i].Body.HalfWidth - toys[j].Body.HalfWidth;
                Assert.True(gap > 0, $"{toys[i].Kind} y {toys[j].Kind} se pisan ({gap:0}).");
            }
        }
    }

    [Fact]
    public void A_toy_that_ends_up_on_another_monitor_comes_back_to_it()
    {
        var second = new MonitorInfo(1, new PixelRect(1920, 0, 3840, 1080), new PixelRect(1920, 0, 3840, 1040), 1, false);
        var world = TerrainBuilder.Build([Monitor, second], []);
        var (game, sensors) = Start(world);
        var plush = game.GiveToy(ToyKind.Plush, world);
        Run(game, world, sensors, 2);
        plush.PlaceOn(world.GroundOf(1)!, 2500);
        Run(game, world, sensors, 10);
        Assert.True(plush.Body.Feet.X > 1920, "No vuelve enseguida.");
        Run(game, world, sensors, 15);
        Assert.InRange(plush.Body.Feet.X, 0, 1920);
    }

    [Fact]
    public void Putting_a_toy_away_takes_it_even_from_its_hands()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        var plush = game.GiveToy(ToyKind.Plush, world);
        Run(game, world, sensors, 2.5);
        game.PlayWith(ToyKind.Plush);
        Run(game, world, sensors, 15, _ => plush.Held);
        Assert.True(plush.Held);
        Assert.True(game.PutAwayToy(ToyKind.Plush));
        Assert.Empty(game.Playground.Toys);
        Run(game, world, sensors, 1);
        Assert.Null(game.Playing);
        Assert.False(game.PutAwayToy(ToyKind.Plush));
    }

    [Fact]
    public void The_ball_you_give_it_replaces_the_one_it_took_out_and_stays()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        game.PlayWithBall();
        Run(game, world, sensors, 3, g => g.Playground.Toys.Count > 0);
        Assert.True(game.Playground.Find(ToyKind.Ball)!.Ephemeral);
        game.GiveToy(ToyKind.Ball, world);
        Assert.False(game.Playground.Find(ToyKind.Ball)!.Ephemeral);
        Run(game, world, sensors, 40);
        Assert.NotNull(game.Playground.Find(ToyKind.Ball));
    }
}
