using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Terrain;

namespace Lander.Core.Tests;

/// <summary>Escritorios de prueba: un monitor 1920×1080 con barra de tareas de 48 px.</summary>
internal static class TestWorlds
{
    public const int Width = 1920;
    public const int Height = 1080;
    public const int Taskbar = 48;
    public const int GroundY = Height - Taskbar;

    public static MonitorInfo Monitor(
        int index = 0,
        int left = 0,
        int top = 0,
        int width = Width,
        int height = Height,
        int taskbar = Taskbar,
        double dpi = 1,
        bool primary = true) =>
        new(index, PixelRect.FromSize(left, top, width, height), new PixelRect(left, top, left + width, top + height - taskbar), dpi, primary);

    public static WindowInfo Window(long handle, int left, int top, int width, int height, bool maximized = false, string? app = null) =>
        new(handle, PixelRect.FromSize(left, top, width, height), maximized, app);

    public static World Desktop(params WindowInfo[] windows) => TerrainBuilder.Build([Monitor()], windows);

    public static World Desktop(double timestamp, params WindowInfo[] windows) =>
        TerrainBuilder.Build([Monitor()], windows, null, timestamp);
}

internal static class Sim
{
    public const double Dt = LanderGame.TimeStep;

    /// <summary>Avanza el motor físico hasta que se cumpla la condición o pase el tiempo máximo.</summary>
    public static List<PhysicsEvent> RunPhysics(
        PhysicsEngine engine,
        LanderBody body,
        World world,
        Func<LanderBody, ThrustInput> pilot,
        Func<LanderBody, bool> until,
        double maxSeconds)
    {
        var events = new List<PhysicsEvent>();
        for (var t = 0.0; t < maxSeconds && !until(body); t += Dt)
        {
            engine.Step(body, pilot(body), world, Dt, events);
        }

        return events;
    }

    /// <summary>Avanza el juego completo y devuelve todos los eventos generados.</summary>
    public static List<GameEvent> RunGame(
        LanderGame game,
        World world,
        Sensors sensors,
        double seconds,
        Func<LanderGame, bool>? until = null,
        Func<LanderGame, ManualControls>? controls = null)
    {
        var events = new List<GameEvent>();
        var steps = (int)Math.Ceiling(seconds / Dt);
        for (var i = 0; i < steps; i++)
        {
            game.Step(world, sensors, controls?.Invoke(game) ?? default);
            game.DrainEvents(events);
            if (until?.Invoke(game) == true)
            {
                break;
            }
        }

        return events;
    }

    public static LanderBody FlyingBody(double x, double y, double vx = 0, double vy = 0)
    {
        var body = new LanderBody { Position = new Vec2(x, y) };
        body.Fly(new Vec2(vx, vy));
        return body;
    }
}
