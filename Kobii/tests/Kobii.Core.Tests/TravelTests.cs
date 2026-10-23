using Kobii.Core.Animation;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Tests;

/// <summary>Ir de un sitio a otro (trepar, saltar, bajar) y lo que necesitan las maniobras de las órdenes.</summary>
public sealed class TravelTests
{
    private static readonly MonitorInfo Monitor = new(0, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1, true);

    private static World Desk(params WindowInfo[] windows) => TerrainBuilder.Build([Monitor], windows);

    private static (KobiiGame Game, Sensors Sensors) Start(World world, double fraction = 0.72)
    {
        var game = new KobiiGame(new GameOptions(), new GameRandom(5));
        game.Initialize(world, fraction);
        return (game, new Sensors { LocalTime = new DateTime(2026, 10, 23, 12, 0, 0), UserIdleSeconds = 1 });
    }

    /// <summary>Avanza hasta que acabe el recorrido (o pase el tiempo) y devuelve cómo acabó.</summary>
    private static TravelOutcome RunTravel(KobiiGame game, World world, Sensors sensors, double seconds, List<GameEvent>? events = null)
    {
        for (var t = 0.0; t < seconds && game.Travel == TravelOutcome.Going; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
            events?.AddRange(Drain(game));
            Assert.InRange(game.Body.Feet.X, 0, 1920);
            Assert.InRange(game.Body.Feet.Y, 0, 1040.5);
        }

        return game.Travel;
    }

    private static List<GameEvent> Drain(KobiiGame game)
    {
        var events = new List<GameEvent>();
        game.DrainEvents(events);
        return events;
    }

    [Fact]
    public void Climbs_up_a_window_that_reaches_the_taskbar()
    {
        var window = new WindowInfo(1, new PixelRect(400, 520, 1100, 1040));
        var world = Desk(window);
        var (game, sensors) = Start(world, fraction: 0.8);
        game.BeginErrand();
        Assert.True(game.GoToWindow(1, errand: true));
        var events = new List<GameEvent>();
        Assert.Equal(TravelOutcome.Arrived, RunTravel(game, world, sensors, 25, events));
        Assert.True(game.Body.IsGrounded);
        Assert.Equal(1, game.Body.SupportId);
        Assert.Contains(events, e => e.Kind == GameEventKind.Climbed);
        Assert.Contains(events, e => e.Kind == GameEventKind.Arrived && e.Text == "window");
    }

    [Fact]
    public void Comes_down_from_a_window_back_to_the_taskbar()
    {
        var window = new WindowInfo(1, new PixelRect(400, 520, 1100, 1040));
        var world = Desk(window);
        var (game, sensors) = Start(world, fraction: 0.8);
        Assert.True(game.GoToWindow(1));
        Assert.Equal(TravelOutcome.Arrived, RunTravel(game, world, sensors, 25));
        for (var t = 0.0; t < 2.5; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
        }

        Assert.True(game.ComeDown());
        Assert.Equal(TravelOutcome.Arrived, RunTravel(game, world, sensors, 25));
        Assert.True(game.Body.Support!.IsBase);
    }

    [Fact]
    public void A_window_out_of_reach_is_not_a_route()
    {
        // Una ventana flotando muy alto, sin nada a lo que subirse: no hay forma de llegar.
        var window = new WindowInfo(1, new PixelRect(400, 60, 900, 200));
        var world = Desk(window);
        var (game, _) = Start(world, fraction: 0.8);
        Assert.False(game.GoToWindow(1, errand: true));
        Assert.Equal(Activity.Idle, game.Activity);
    }

    [Fact]
    public void Grabbing_it_while_it_climbs_lets_go_of_the_window()
    {
        var window = new WindowInfo(1, new PixelRect(400, 520, 1100, 1040));
        var world = Desk(window);
        var (game, sensors) = Start(world, fraction: 0.8);
        Assert.True(game.GoToWindow(1));
        for (var t = 0.0; t < 20 && !game.IsClimbing; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
        }

        Assert.True(game.IsClimbing);
        game.BeginDrag(game.Body.Center);
        game.EndDrag();
        Assert.Equal(TravelOutcome.Failed, game.Travel);
        for (var t = 0.0; t < 3; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
        }

        Assert.True(game.Body.IsGrounded);
    }

    [Fact]
    public void On_an_errand_it_does_nothing_on_its_own_and_holds_what_it_is_given()
    {
        var world = Desk();
        var (game, sensors) = Start(world);
        game.BeginErrand();
        game.HeldProp = Prop.Ball;
        game.LookAt(new Vec2(100, 300));
        var x = game.Body.Feet.X;
        for (var t = 0.0; t < 30; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
        }

        Assert.Equal(Activity.Idle, game.Activity);
        Assert.Equal(x, game.Body.Feet.X, 1);
        Assert.Equal(Prop.Ball, game.Animator.Prop);
        Assert.True(game.Visual.LookTarget.X < 0);

        // Lanzarla: el gesto la sigue enseñando en la mano hasta soltarla.
        game.PlayGesture(Gesture.Throw, Gestures.ThrowSeconds, -1);
        game.Step(world, sensors);
        Assert.Equal(Prop.Ball, game.Animator.Prop);
        game.HeldProp = Prop.None;
        game.EndErrand();
        Assert.False(game.OnErrand);
        Assert.Equal(Prop.None, game.HeldProp);
    }

    [Fact]
    public void Wanders_up_to_windows_on_its_own_now_and_then()
    {
        var world = Desk(new WindowInfo(1, new PixelRect(300, 560, 900, 1040)), new WindowInfo(2, new PixelRect(1000, 600, 1700, 1040)));
        var game = new KobiiGame(new GameOptions { Liveliness = 1.5 }, new GameRandom(21));
        game.Initialize(world);
        var sensors = new Sensors { LocalTime = new DateTime(2026, 10, 23, 12, 0, 0), UserIdleSeconds = 1 };
        var climbed = false;
        for (var t = 0.0; t < 900 && !climbed; t += KobiiGame.TimeStep)
        {
            game.Step(world, sensors);
            climbed = Drain(game).Any(e => e.Kind == GameEventKind.Climbed);
        }

        Assert.True(climbed);
    }
}
