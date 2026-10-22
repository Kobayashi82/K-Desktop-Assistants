using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Tests;

/// <summary>Kobii en un escritorio de mentira: anda, se cae, se le coge, te atiende, se duerme y te saluda.</summary>
public sealed class GameTests
{
    private static readonly MonitorInfo Monitor = new(0, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1, true);

    private static World Desk(params WindowInfo[] windows) => TerrainBuilder.Build([Monitor], windows);

    private static (KobiiGame Game, Sensors Sensors) Start(World world, int seed = 3)
    {
        var game = new KobiiGame(new GameOptions(), new GameRandom(seed));
        game.Initialize(world);
        return (game, new Sensors { LocalTime = new DateTime(2026, 10, 22, 12, 0, 0), UserIdleSeconds = 1 });
    }

    private static void Run(KobiiGame game, World world, Sensors sensors, double seconds)
    {
        for (var t = 0.0; t < seconds; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
        }
    }

    [Fact]
    public void Starts_standing_on_the_taskbar_of_the_main_monitor()
    {
        var world = Desk();
        var (game, _) = Start(world);
        Assert.True(game.Body.IsGrounded);
        Assert.Equal(1040, game.Body.Feet.Y);
        Assert.Equal(Species.HeightDip, game.Body.Height);
    }

    [Fact]
    public void Lives_on_its_own_for_a_long_while_without_leaving_the_screen()
    {
        var world = Desk(new WindowInfo(1, new PixelRect(300, 500, 1200, 1000)));
        var (game, sensors) = Start(world, seed: 11);
        var performed = new HashSet<string>();
        var events = new List<GameEvent>();
        for (var t = 0.0; t < 600; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
            game.DrainEvents(events);
            Assert.InRange(game.Body.Feet.X, 0, 1920);
            Assert.InRange(game.Body.Feet.Y, 0, 1040.5);
        }

        foreach (var e in events.Where(e => e.Kind == GameEventKind.Performed))
        {
            performed.Add(e.Text!);
        }

        // En diez minutos hace muchas cosas distintas.
        Assert.True(performed.Count >= 8, string.Join(", ", performed));
    }

    [Fact]
    public void Walking_moves_it_and_raises_steps()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        var x = game.Body.Feet.X;
        Assert.True(game.WalkTo(x - 300));
        Run(game, world, sensors, 1);
        var events = new List<GameEvent>();
        game.DrainEvents(events);
        Assert.True(game.Body.Feet.X < x - 40);
        Assert.Contains(events, e => e.Kind == GameEventKind.Step);
    }

    [Fact]
    public void Falls_when_the_window_it_stands_on_closes()
    {
        var window = new WindowInfo(1, new PixelRect(300, 500, 1200, 1000));
        var world = Desk(window);
        var (game, sensors) = Start(world);
        game.Relocate(world.SegmentsOf(1)[0], 700);
        Run(game, world, sensors, 0.2);
        Assert.Equal(500, game.Body.Feet.Y);

        var empty = Desk();
        Run(game, empty, sensors, 2);
        Assert.True(game.Body.IsGrounded);
        Assert.Equal(1040, game.Body.Feet.Y);
    }

    [Fact]
    public void Gets_off_a_window_after_a_while_on_it()
    {
        var world = Desk(new WindowInfo(1, new PixelRect(300, 500, 1200, 1000)));
        var (game, sensors) = Start(world, seed: 5);
        game.Relocate(world.SegmentsOf(1)[0], 700);
        Run(game, world, sensors, 260);
        Assert.Equal(1040, game.Body.Feet.Y);
    }

    [Fact]
    public void Thrown_it_flies_and_lands()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        var start = game.Body.Feet;
        game.BeginDrag(start);
        for (var i = 0; i < 20; i++)
        {
            game.Drag(start + new Vec2(-i * 30, -i * 30));
            game.Step(world, sensors);
        }

        Assert.Equal(Activity.Held, game.Activity);
        game.EndDrag();
        var events = new List<GameEvent>();
        Run(game, world, sensors, 3);
        game.DrainEvents(events);
        Assert.Contains(events, e => e.Kind == GameEventKind.Thrown);
        Assert.Contains(events, e => e.Kind == GameEventKind.Landed);
        Assert.True(game.Body.IsGrounded);
    }

    [Fact]
    public void Attends_you_while_the_module_listens_thinks_and_speaks()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        Run(game, world, sensors, 0.5);
        game.SetAttention(Attention.Listening);
        Run(game, world, sensors, 0.3);
        Assert.Equal(Activity.Attend, game.Activity);
        Assert.True(game.Visual.Antenna > 0.3);

        game.SetAttention(Attention.Thinking);
        Run(game, world, sensors, 0.3);
        Assert.Equal(Expression.Thinking, game.Visual.Expression);

        game.SetAttention(Attention.Speaking);
        game.SetVoiceLevel(0.9);
        Run(game, world, sensors, 0.2);
        Assert.True(game.Visual.Mouth > 0.5);

        game.SetAttention(Attention.None);
        Run(game, world, sensors, 0.5);
        Assert.NotEqual(Activity.Attend, game.Activity);
        Assert.True(game.Visual.Mouth < 0.3);
    }

    [Fact]
    public void Sleeps_when_you_are_away_and_greets_you_back()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        sensors.UserIdleSeconds = 2000;
        Run(game, world, sensors, 30);
        Assert.Equal(Activity.Sleep, game.Activity);

        sensors.UserIdleSeconds = 0;
        var events = new List<GameEvent>();
        Run(game, world, sensors, 20);
        game.DrainEvents(events);
        Assert.Contains(events, e => e.Kind == GameEventKind.WokeUp);
        Assert.Contains(events, e => e.Kind == GameEventKind.Greeted);
    }

    [Theory]
    [InlineData(ReactionKind.Success)]
    [InlineData(ReactionKind.Warning)]
    [InlineData(ReactionKind.Failure)]
    [InlineData(ReactionKind.Celebration)]
    public void Reacts_to_how_an_order_went(ReactionKind reaction)
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        Run(game, world, sensors, 0.5);
        game.React(reaction);
        Assert.Equal(Activity.Perform, game.Activity);
        var emotes = new List<Emote>();
        game.Visual.DrainEmotes(emotes);
        Assert.NotEmpty(emotes);
        Run(game, world, sensors, 4);
        Assert.True(game.Body.IsGrounded);
    }

    [Fact]
    public void Petting_cheers_it_up()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        for (var i = 0; i < 3; i++)
        {
            game.Click();
            Run(game, world, sensors, 0.5);
        }

        Assert.Equal(Mood.Happy, game.Mood);
    }

    [Fact]
    public void Is_sleepy_at_night_and_bored_if_ignored()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        sensors.LocalTime = new DateTime(2026, 10, 22, 1, 0, 0);
        Run(game, world, sensors, 0.1);
        Assert.Equal(Mood.Sleepy, game.Mood);

        sensors.LocalTime = new DateTime(2026, 10, 22, 12, 0, 0);
        Run(game, world, sensors, 1600);
        Assert.Equal(Mood.Bored, game.Mood);
        game.Click();
        Run(game, world, sensors, 0.1);
        Assert.NotEqual(Mood.Bored, game.Mood);
    }
}
