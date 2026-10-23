using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Tests;

/// <summary>Lo que hace paso a paso: la mariposa, la pelota, comer y beber, y las reacciones al PC.</summary>
public sealed class PlayTests
{
    private static readonly MonitorInfo Monitor = new(0, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1, true);

    private static World Desk() => TerrainBuilder.Build([Monitor], []);

    private static (KobiiGame Game, Sensors Sensors) Start(World world, int seed = 7, int hour = 12)
    {
        var game = new KobiiGame(new GameOptions(), new GameRandom(seed));
        game.Initialize(world, 0.5);
        var sensors = new Sensors { LocalTime = new DateTime(2026, 10, 23, hour, 0, 0), UserIdleSeconds = 1 };
        Run(game, world, sensors, 0.5);
        return (game, sensors);
    }

    /// <summary>Avanza hasta que deje de hacer lo que hacía (o pase el tiempo); devuelve los sucesos.</summary>
    private static List<GameEvent> RunWhilePlaying(KobiiGame game, World world, Sensors sensors, double seconds)
    {
        var events = new List<GameEvent>();
        for (var t = 0.0; t < seconds && game.Activity == Activity.Play; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
            game.DrainEvents(events);
            Assert.InRange(game.Body.Feet.X, 0, 1920);
            Assert.InRange(game.Body.Feet.Y, 0, 1040.5);
        }

        return events;
    }

    private static void Run(KobiiGame game, World world, Sensors sensors, double seconds)
    {
        for (var t = 0.0; t < seconds; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
        }
    }

    [Theory]
    [InlineData(ButterflyEnding.Catch)]
    [InlineData(ButterflyEnding.Perch)]
    [InlineData(ButterflyEnding.Escape)]
    public void Plays_with_a_butterfly_until_it_flies_away(ButterflyEnding ending)
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        Assert.True(game.ChaseButterfly(ending, misses: 1));
        Assert.NotNull(game.Butterfly);
        var events = RunWhilePlaying(game, world, sensors, 60);
        Assert.NotEqual(Activity.Play, game.Activity);
        Assert.Contains(events, e => e.Kind == GameEventKind.Clapped);
        if (ending == ButterflyEnding.Catch)
        {
            Assert.Contains(events, e => e.Kind == GameEventKind.Clapped && e.Text == "caught");
        }

        // Al acabar, la mariposa se va del todo.
        Run(game, world, sensors, 6);
        Assert.Null(game.Butterfly);
    }

    [Fact]
    public void Takes_out_the_ball_kicks_it_around_and_puts_it_away()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        Assert.True(game.PlayWithBall());
        var events = RunWhilePlaying(game, world, sensors, 60);
        Assert.Contains(events, e => e.Kind == GameEventKind.Pulled && e.Text == "out");
        Assert.True(events.Count(e => e.Kind == GameEventKind.Kicked) >= 2);
        Assert.Contains(events, e => e.Kind == GameEventKind.Pulled && e.Text == "in");
        Assert.Empty(game.Playground.Toys);
    }

    [Fact]
    public void A_ball_left_lying_around_after_an_interruption_disappears()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        game.PlayWithBall();
        Run(game, world, sensors, 2.5);
        Assert.NotEmpty(game.Playground.Toys);

        // Le hablas: lo deja (la pelota se queda tirada) y al rato ya no está.
        game.SetAttention(Attention.Speaking);
        Run(game, world, sensors, 1);
        game.SetAttention(Attention.None);
        Run(game, world, sensors, 25);
        Assert.Empty(game.Playground.Toys);
    }

    [Theory]
    [InlineData(FoodKind.Apple)]
    [InlineData(FoodKind.Juice)]
    public void Takes_out_a_snack_and_eats_it_up(FoodKind kind)
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        Assert.True(game.Eat(own: kind));
        var events = RunWhilePlaying(game, world, sensors, 20);
        Assert.Contains(events, e => e.Kind == GameEventKind.Pulled && e.Text == "out");
        Assert.Contains(events, e => e.Kind == (kind.IsDrink() ? GameEventKind.Gulp : GameEventKind.Chomp));
        Assert.Contains(events, e => e.Kind == GameEventKind.Ate && e.Text == kind.Id());
        Assert.Empty(game.FoodItems);
    }

    [Fact]
    public void Goes_for_the_food_you_give_it()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        game.Give(FoodKind.Cookie, world);
        var events = new List<GameEvent>();
        for (var t = 0.0; t < 25 && game.FoodItems.Count > 0; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
            game.DrainEvents(events);
        }

        Assert.Contains(events, e => e.Kind == GameEventKind.Ate && e.Text == "cookie");
    }

    [Fact]
    public void Sweeps_when_you_empty_the_recycle_bin()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        Assert.True(game.RecycleBinEmptied());
        var events = RunWhilePlaying(game, world, sensors, 15);
        Assert.True(events.Count(e => e.Kind == GameEventKind.Swept) >= 3);
    }

    [Fact]
    public void Points_at_a_notification_but_not_at_every_one_of_a_burst()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        Assert.True(game.NoticeNotification(new Vec2(1800, 900)));
        RunWhilePlaying(game, world, sensors, 6);
        Assert.False(game.NoticeNotification(new Vec2(1800, 900)));
    }

    [Fact]
    public void Dances_while_music_plays_and_stops_when_it_stops()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        sensors.Music = new MusicState(true, 120, 0);
        Run(game, world, sensors, 4);
        Assert.Equal(PlayKind.Dance, game.Playing);
        sensors.Music = default;
        Run(game, world, sensors, 2);
        Assert.NotEqual(PlayKind.Dance, game.Playing);
    }

    [Fact]
    public void Puts_on_its_pajamas_at_night_and_takes_them_off_in_the_morning()
    {
        var world = Desk();
        var (game, sensors) = Start(world, hour: 21);
        Assert.False(game.InPajamas);
        sensors.LocalTime = new DateTime(2026, 10, 23, 22, 0, 0);
        var events = new List<GameEvent>();
        for (var t = 0.0; t < 8; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
            game.DrainEvents(events);
        }

        Assert.True(game.InPajamas);
        Assert.Equal(1, game.Visual.Pajamas, 3);
        Assert.Contains(events, e => e.Kind == GameEventKind.Dressed && e.Text == "on");

        sensors.LocalTime = new DateTime(2026, 10, 24, 8, 0, 0);
        Run(game, world, sensors, 8);
        Assert.False(game.InPajamas);
        Assert.Equal(0, game.Visual.Pajamas, 3);
    }

    [Fact]
    public void Celebrates_when_you_plug_the_charger_in()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        sensors.OnBattery = true;
        sensors.BatteryLow = true;
        Run(game, world, sensors, 3);
        sensors.OnBattery = false;
        var celebrated = false;
        for (var t = 0.0; t < 1; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
            celebrated |= game.Activity == Activity.Celebrate;
        }

        Assert.True(celebrated);
    }

    [Fact]
    public void Talking_to_it_interrupts_what_it_was_doing()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        game.ChaseButterfly();
        Run(game, world, sensors, 2);
        game.SetAttention(Attention.Listening);
        Run(game, world, sensors, 0.5);
        Assert.Equal(Activity.Attend, game.Activity);
    }
}
