using Lander.Core.Autopilot;
using Lander.Core.Game;
using Lander.Core.Physics;
using Lander.Core.Progression;
using Lander.Core.Settings;
using Lander.Core.Terrain;
using Lander.Core.Vision;
using static Lander.Core.Tests.TestWorlds;

namespace Lander.Core.Tests;

/// <summary>Bordes de contenido convertidos en plataformas, puntuación, privacidad y ajustes.</summary>
public class ContentTerrainTests
{
    private static World Build(IReadOnlyList<WindowInfo> windows, params ContentSurface[] contents) =>
        TerrainBuilder.Build([Monitor()], windows, null, 0, contents);

    [Fact]
    public void Content_edge_becomes_a_platform_relative_to_its_window()
    {
        var world = Build([Window(1, 200, 100, 1200, 800, app: "msedge")], new ContentSurface(1, 7, 100, 600, 300));

        var platform = Assert.Single(world.Platforms, p => p.Kind == PlatformKind.Content);
        Assert.Equal(400, platform.Y);
        Assert.Equal(300, platform.Left);
        Assert.Equal(800, platform.Right);
        Assert.Equal(Platform.ContentOwnerId(1, 7), platform.OwnerId);
        Assert.Equal(1, platform.WindowHandle);
        Assert.Equal(new Geometry.Vec2(200, 400), platform.OwnerOrigin);
        Assert.Equal("msedge", platform.AppId);
    }

    [Fact]
    public void Maximized_window_content_counts_although_its_title_bar_does_not()
    {
        var world = Build([Window(1, 0, 0, Width, GroundY, maximized: true)], new ContentSurface(1, 1, 300, 900, 500));

        Assert.False(world.HasOwner(1));
        Assert.Single(world.SegmentsOf(Platform.ContentOwnerId(1, 1)));
    }

    [Fact]
    public void Window_in_front_of_the_space_above_cuts_the_platform()
    {
        // Borde a y = 400 de x = 200 a 1000; la ventana de delante tapa la zona libre entre 500 y 700.
        var front = Window(2, 500, 380, 200, 220);
        var behind = Window(1, 200, 100, 1200, 800);

        var world = Build([front, behind], new ContentSurface(1, 1, 0, 800, 300));

        var pieces = world.SegmentsOf(Platform.ContentOwnerId(1, 1)).OrderBy(p => p.Left).ToList();
        Assert.Equal(2, pieces.Count);
        Assert.Equal((200d, 500d), (pieces[0].Left, pieces[0].Right));
        Assert.Equal((700d, 1000d), (pieces[1].Left, pieces[1].Right));
    }

    [Fact]
    public void Windows_entirely_above_or_below_the_free_space_do_not_cut_it()
    {
        var above = Window(2, 300, 120, 400, 150);
        var below = Window(3, 300, 420, 400, 150);
        var behind = Window(1, 200, 100, 1200, 800);

        var world = Build([above, below, behind], new ContentSurface(1, 1, 0, 800, 300));

        var platform = Assert.Single(world.SegmentsOf(Platform.ContentOwnerId(1, 1)));
        Assert.Equal(800, platform.Width);
    }

    [Fact]
    public void Content_needs_the_landers_height_free_inside_its_window_and_above_the_taskbar()
    {
        var window = Window(1, 200, 100, 1200, 1000);

        var world = Build([window],
            new ContentSurface(1, 1, 0, 800, 20),
            new ContentSurface(1, 2, 0, 800, GroundY - 100 + 10),
            new ContentSurface(1, 3, 0, 800, 200));

        Assert.False(world.HasOwner(Platform.ContentOwnerId(1, 1)));
        Assert.False(world.HasOwner(Platform.ContentOwnerId(1, 2)));
        Assert.True(world.HasOwner(Platform.ContentOwnerId(1, 3)));
    }

    [Fact]
    public void Content_pieces_shorter_than_three_lander_widths_are_dropped()
    {
        var front = Window(2, 300, 350, 600, 300);
        var behind = Window(1, 200, 100, 1200, 800);

        var world = Build([front, behind], new ContentSurface(1, 1, 0, 800, 300));

        // Quedan 100 px a la izquierda (menos de 108) y 100 px a la derecha.
        Assert.False(world.HasOwner(Platform.ContentOwnerId(1, 1)));
    }

    [Fact]
    public void Content_of_a_closed_window_is_ignored()
    {
        var world = Build([Window(1, 200, 100, 1200, 800)], new ContentSurface(99, 1, 0, 800, 300));

        Assert.DoesNotContain(world.Platforms, p => p.Kind == PlatformKind.Content);
    }

    [Fact]
    public void Content_owner_ids_never_collide_with_windows_or_ground()
    {
        const long hwnd = 0x0002_0F3A;
        var id = Platform.ContentOwnerId(hwnd, 5);

        Assert.NotEqual(hwnd, id);
        Assert.NotEqual(Platform.ContentOwnerId(hwnd, 6), id);
        Assert.True(Platform.IsContentOwner(id));
        Assert.False(Platform.IsContentOwner(hwnd));
        Assert.False(Platform.IsContentOwner(Platform.GroundOwnerId(0, 0)));
        Assert.Equal(hwnd, Platform.ContentWindow(id));
        Assert.Equal(5, Platform.ContentEdgeId(id));
    }

    [Fact]
    public void Content_scores_at_least_like_a_narrow_window()
    {
        var wide = new Platform(Platform.ContentOwnerId(1, 1), PlatformKind.Content, 0, 1200, 300, 0, default);
        var narrow = wide with { Right = 72 };
        var wideWindow = wide with { Kind = PlatformKind.Window, OwnerId = 1 };

        Assert.Equal(1, Scoring.Difficulty(wideWindow, 36));
        Assert.Equal(Scoring.ContentMinDifficulty, Scoring.Difficulty(wide, 36));
        Assert.Equal(4, Scoring.Difficulty(narrow, 36));
    }

    [Theory]
    [InlineData("keepassxc", "Base de datos", true)]
    [InlineData("bitwarden", "", true)]
    [InlineData(null, "Lo que sea", true)]
    [InlineData("msedge", "Banco Santander - Microsoft Edge", true)]
    [InlineData("chrome", "Nueva pestaña de incógnito - Google Chrome", true)]
    [InlineData("firefox", "Iniciar sesión · GitHub", true)]
    [InlineData("msedge", "Módulo lunar - Wikipedia", false)]
    [InlineData("code", "Program.cs - Lander", false)]
    public void Privacy_excludes_sensitive_windows(string? app, string title, bool excluded) =>
        Assert.Equal(excluded, ContentPrivacy.IsExcluded(app, title));

    [Fact]
    public void Privacy_honours_the_users_list()
    {
        Assert.True(ContentPrivacy.IsExcluded("slack", "General", ["Slack.exe"]));
        Assert.False(ContentPrivacy.IsExcluded("slack", "General", ["outlook"]));
    }

    [Fact]
    public void Content_setting_is_off_by_default_and_its_list_is_normalized_and_copied()
    {
        var settings = new LanderSettings { ContentExcludedApps = [" Slack.EXE ", "", "slack", "Outlook"] }.Normalize();

        Assert.False(new LanderSettings().LandOnContent);
        Assert.Equal(["slack", "outlook"], settings.ContentExcludedApps);

        var clone = settings.Clone();
        clone.ContentExcludedApps.Add("teams");
        Assert.Equal(2, settings.ContentExcludedApps.Count);
    }

    [Fact]
    public void Game_asks_to_follow_the_content_it_is_landed_on_even_when_paused()
    {
        var world = Build([Window(1, 200, 100, 1200, 800)], new ContentSurface(1, 3, 100, 700, 300));
        var game = new LanderGame(new GameOptions(), new PilotProgress(), new GameRandom(1));
        game.Initialize(world);
        Assert.Equal(ContentNeed.None, game.ContentInterest.Need);

        var content = world.SegmentsOf(Platform.ContentOwnerId(1, 3))[0];
        game.Body.PlaceOn(content, content.CenterX, 0);
        Assert.Equal(new ContentInterest(ContentNeed.Landed, 1, content.OwnerId), game.ContentInterest);

        game.SetPaused(true);
        Assert.Equal(ContentNeed.Landed, game.ContentInterest.Need);
    }

    [Fact]
    public void Game_asks_for_content_while_piloting_by_hand()
    {
        var game = new LanderGame(new GameOptions(), new PilotProgress(), new GameRandom(1));
        game.Initialize(Desktop());
        game.SetMode(ControlMode.Manual, ModeChangeReason.User);

        Assert.Equal(ContentNeed.Manual, game.ContentInterest.Need);
    }

    [Fact]
    public void Game_asks_for_content_shortly_before_choosing_a_destination()
    {
        var world = Desktop(Window(1, 200, 300, 900, 500));
        var game = new LanderGame(new GameOptions(), new PilotProgress(), new GameRandom(3));
        game.Initialize(world);
        var sensors = new Sensors();

        var asked = false;
        for (var i = 0; i < 60 * 20 && !game.Autopilot.IsFlightActive; i++)
        {
            asked |= game.ContentInterest.Need == ContentNeed.ChoosingSoon;
            game.Step(world, sensors, default);
        }

        Assert.True(game.Autopilot.IsFlightActive);
        Assert.True(asked);
    }

    [Fact]
    public void Autopilot_picks_content_now_and_then_preferring_the_active_window()
    {
        var a = Window(1, 100, 200, 800, 600);
        var b = Window(2, 1000, 200, 800, 600);
        var world = Build([a, b],
            new ContentSurface(1, 1, 50, 700, 250),
            new ContentSurface(1, 2, 50, 700, 400),
            new ContentSurface(2, 1, 50, 700, 250),
            new ContentSurface(2, 2, 50, 700, 400));
        var body = new LanderBody();
        body.PlaceOn(world.PrimaryGround!, 960, 0);
        var random = new GameRandom(11);
        var query = new DestinationQuery { ActiveWindow = 1, BaseBias = 0.6 };

        var picks = Enumerable.Range(0, 3000).Select(_ => DestinationPicker.PickExploration(world, body, query, random)!).ToList();

        var content = picks.Count(d => d.Kind == PlatformKind.Content);
        var active = picks.Count(d => d.Kind == PlatformKind.Content && Platform.ContentWindow(d.OwnerId) == 1);
        Assert.InRange(content, 3000 * 0.1, 3000 * 0.5);
        Assert.True(active > (content - active) * 1.4, $"activa {active}, otra {content - active}");
    }

    /// <summary>
    /// La rueda sobre la página en la que está posado: se eleva, espera en el aire a que pare y vuelve a posarse en el
    /// mismo borde, que entretanto se ha movido con el scroll.
    /// </summary>
    [Fact]
    public void Scrolling_the_page_it_stands_on_lifts_it_and_it_lands_back_on_the_same_edge()
    {
        var windows = new[] { Window(1, 200, 100, 1200, 800, app: "msedge") };
        var world = Build(windows, new ContentSurface(1, 7, 100, 900, 400));
        var game = new LanderGame(new GameOptions(), new PilotProgress(), new GameRandom(3));
        game.Initialize(world);
        var edge = world.SegmentsOf(Platform.ContentOwnerId(1, 7)).Single();
        game.Body.PlaceOn(edge, edge.CenterX, world.Timestamp);
        Sim.RunGame(game, world, new Sensors(), 0.2);

        game.OnContentScrolling();
        Sim.RunGame(game, world, new Sensors(), 0.4);
        Assert.True(game.Body.IsFlying);
        Assert.True(game.Body.FeetY < edge.Y - 5);

        // La página baja 60 px mientras espera (el borde sube en la ventana); más rueda alarga la espera.
        world = Build(windows, new ContentSurface(1, 7, 100, 900, 340));
        game.OnContentScrolling();
        var events = Sim.RunGame(game, world, new Sensors(), 8, g => g.Body.IsLanded);

        Assert.True(game.Body.IsLanded, $"{game.Body.State} en {game.Body.Position}, piloto {game.Autopilot.State} {game.Autopilot.Purpose} {game.Autopilot.Style}, destino {game.Autopilot.Destination}");
        Assert.Equal(Platform.ContentOwnerId(1, 7), game.Body.Support?.OwnerId);
        Assert.Equal(440, game.Body.FeetY, 1);
        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.Celebrated);
    }
}
