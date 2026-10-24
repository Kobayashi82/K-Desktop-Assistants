using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Progression;
using Lander.Core.Terrain;
using static Lander.Core.Tests.TestWorlds;

namespace Lander.Core.Tests;

/// <summary>La entrada desde lo alto del monitor: caída con el motor apagado y frenada a tope en el último instante.</summary>
public class SkyDropTests
{
    private static readonly Sensors Awake = new() { UserIdleSeconds = 1 };

    [Theory]
    [InlineData(1, 1440)]
    [InlineData(2, 1440)]
    [InlineData(3, 1080)]
    [InlineData(4, 2160)]
    public void It_falls_with_the_engine_off_and_brakes_at_the_last_moment(int seed, int monitorHeight)
    {
        var world = TerrainBuilder.Build([Monitor(height: monitorHeight)], [], null, 0);
        var game = new LanderGame(new GameOptions(), new PilotProgress(), new GameRandom(seed));
        game.Initialize(world);
        for (var i = 0; i < 60; i++)
        {
            game.Step(world, Awake, default);
        }

        var ground = game.Body.Position.Y;
        var top = game.Body.Height * 0.8;
        var fuel = game.Body.Fuel;
        game.DropFromSky(new Vec2(game.Body.Position.X, top), new Vec2(0, 60 * game.Body.Scale));

        double? ignitedAt = null;
        var landedAfter = -1.0;
        var fastest = 0.0;
        var weakest = 1.0;
        var trace = new System.Text.StringBuilder();
        for (var i = 0; i < 12 * 60; i++)
        {
            game.Step(world, Awake, default);
            fastest = Math.Max(fastest, game.Body.Velocity.Y);
            if (i % 10 == 0)
            {
                trace.Append(System.FormattableString.Invariant($"[{game.Body.Position.Y:0} v{game.Body.Velocity.Y:0} t{game.LastInput.Throttle:0.00} a{game.Body.Angle:0.00} d{game.Body.Damage:0.00}]"));
            }
            if (ignitedAt is null && game.LastInput.Throttle > 0.05)
            {
                ignitedAt = game.Body.Position.Y;
                Assert.True(game.LastInput.Throttle > 0.85, $"Enciende al {game.LastInput.Throttle:P0}, no a tope");
            }

            if (ignitedAt is not null && game.Body.Velocity.Y > 150 * game.Body.Scale)
            {
                weakest = Math.Min(weakest, game.LastInput.Throttle);
            }

            if (game.Body.IsLanded)
            {
                landedAfter = i / 60.0;
                break;
            }
        }

        Assert.True(landedAfter > 0, $"No se ha posado: está en {game.Body.Position}");
        Assert.NotEqual(BodyState.Destroyed, game.Body.State);
        Assert.True(game.Body.Damage < 0.01, $"Daño {game.Body.Damage:0.00} {trace}");
        Assert.True(Math.Abs(game.Body.Position.Y - ground) < 2, $"Se ha posado en {game.Body.Position.Y:0}, no en el suelo ({ground:0})");
        Assert.Equal(fuel, game.Body.Fuel, 3);

        // Cae libre más de la mitad del camino (con empuje 360 y gravedad 150, la frenada justa empieza hacia la mitad).
        Assert.NotNull(ignitedAt);
        var fallen = (ignitedAt.Value - top) / (ground - top);
        Assert.True(fallen > 0.45, $"Enciende tras caer solo el {fallen:P0}");
        Assert.True(fastest > 300 * game.Body.Scale, $"Velocidad máxima {fastest:0} px/s: no ha caído de verdad");
        Assert.True(weakest > 0.85, $"Mientras frena deprisa, el motor baja al {weakest:P0} {trace}");
        Assert.True(landedAfter < 8, $"Ha tardado {landedAfter:0.0} s");
        game.Step(world, Awake, default);
        game.Step(world, Awake, default);
        Assert.False(game.Autopilot.InSkyDrop);
    }
}
