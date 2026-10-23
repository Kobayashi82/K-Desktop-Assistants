using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Sdk;
using Lander.Assistant.Audio;
using Lander.Assistant.Rendering;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Terrain;

namespace Lander.Assistant;

/// <summary>
/// Entrar y salir según el motivo (SDK 1.9). Al arrancar K-Desktop, una de sus entradas: bajar con el haz, caer del cielo
/// frenando con el motor o aparecer de un salto entre destellos (con <see cref="Variety{T}"/>, sin repetir la de la última
/// vez). Cuando lo pones tú, lo mismo pero junto al cursor, y saluda al llegar. Al recargarse (otra versión, otro idioma, el
/// proceso que vuelve), reaparece donde estaba, sin espectáculo. Al esconderlo se despide a su manera (haz, despegar y
/// perderse por arriba, o saludar e irse); al quitarlo, un adiós más largo; al cerrar K-Desktop, deprisa y sin ruido.
/// </summary>
public sealed partial class LanderAssistant
{
    private enum Arrival { Beam, Sky, Warp }

    private enum Departure { Beam, TakeOff, Wave }

    private const string MomentsFile = "moments.json";

    /// <summary>Cada cuánto, como mucho, se apunta dónde está (para reaparecer ahí tras una recarga o una caída).</summary>
    private const double SpotSaveInterval = 10;

    private readonly Variety<Arrival> _arrivals = new Variety<Arrival>()
        .Add(Arrival.Beam)
        .Add(Arrival.Sky)
        .Add(Arrival.Warp, 0.7);

    private readonly Variety<Departure> _departures = new Variety<Departure>()
        .Add(Departure.Beam)
        .Add(Departure.TakeOff)
        .Add(Departure.Wave, 0.8);

    private double _fadeStart = double.NaN;
    private double _fadeSeconds = 0.6;

    /// <summary>Saluda cuando llegue (lo has puesto tú): al posarse, o enseguida si ya está posado.</summary>
    private double _greetAt = double.NaN;

    private Vec2 _savedSpot = new(double.NaN, double.NaN);
    private double _nextSpotSave;

    public void Enter(EntranceKind how)
    {
        _pendingEntrance = how;
        PublishPanel();
        TryStart();
        _host.RequestTick();
    }

    public async Task ExitAsync(ExitKind why, CancellationToken ct)
    {
        EndPress();
        ReleaseGameKeys();
        _greetAt = double.NaN;
        _exitStart = _now;
        _host.RequestTick();
        var shown = _game.IsInitialized && !_hidden && _game.Body.State != BodyState.Destroyed;
        if (_game.IsInitialized)
        {
            SaveMoments(force: true);
        }

        try
        {
            if (!shown)
            {
                return;
            }

            switch (why)
            {
                case ExitKind.Reloading:
                    // Vuelve enseguida donde está: sin espectáculo.
                    Fade(0.15);
                    await WaitAsync(0.15, ct);
                    break;

                case ExitKind.Shutdown:
                    // K-Desktop se cierra (o se apaga el módulo): deprisa y sin ruido.
                    _particles.Beam(_game.Body.Position, _game.Body.Scale);
                    Fade(0.35);
                    await WaitAsync(0.35, ct);
                    break;

                case ExitKind.Uninstalling:
                    await FarewellAsync(ct);
                    break;

                default:
                    var departure = _departures.Pick(d => d != Departure.TakeOff || CanTakeOff);
                    SaveMoments(force: true);
                    _host.Log.Info($"Se despide: {departure}.");
                    await DepartAsync(departure, ct);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // El host tiene prisa: se va tal cual.
        }
    }

    /// <summary>Puede irse volando por arriba: en automático, sin carrera y con sitio encima.</summary>
    private bool CanTakeOff => _game.CanRunErrands && !_dragging && _game.Body.Position.Y - TopOfMonitor() > _game.Body.Height * 4;

    private async Task DepartAsync(Departure departure, CancellationToken ct)
    {
        var body = _game.Body;
        switch (departure)
        {
            case Departure.TakeOff:
                await TakeOffAsync(ct);
                break;

            case Departure.Wave:
                _game.Visual.Flash(Expression.Happy, 2);
                _game.Visual.StartWave();
                _game.Visual.ShowBubble(new Bubble(BubbleIcon.Hello, 1.4));
                Play(Sound.Pop, 0.5);
                await WaitAsync(1.0, ct);
                BeamOut(0.6);
                await WaitAsync(0.6, ct);
                break;

            default:
                _particles.Beam(body.Position, body.Scale);
                Play(Sound.Respawn, 0.7);
                Fade(0.6);
                await WaitAsync(0.6, ct);
                break;
        }
    }

    /// <summary>Lo quitas: se pone triste, te dice adiós con un corazón y se va por arriba (o con el haz).</summary>
    private async Task FarewellAsync(CancellationToken ct)
    {
        _host.Log.Info("Se despide para siempre.");
        _game.Visual.Flash(Expression.Tired, 3);
        _game.Visual.StartWave();
        _game.Visual.ShowBubble(new Bubble(BubbleIcon.Heart, 1.6));
        _particles.Hearts(_game.Body.Position - new Vec2(0, _game.Body.Height * 0.6), _game.Body.Scale, 4);
        await WaitAsync(1.3, ct);
        if (CanTakeOff)
        {
            await TakeOffAsync(ct);
        }
        else
        {
            BeamOut(0.7);
            await WaitAsync(0.7, ct);
        }
    }

    /// <summary>Despega hacia arriba (hacia un lado, en diagonal) y se pierde desvaneciéndose.</summary>
    private async Task TakeOffAsync(CancellationToken ct)
    {
        var body = _game.Body;
        var side = _sensors.Cursor is { } cursor && cursor.X > body.Position.X ? -1 : 1;
        _game.BeginErrand(new Vec2(body.Position.X + side * body.Height * 3, TopOfMonitor()), 1.5);
        Play(Sound.Boost, 0.6);
        await WaitAsync(0.8, ct);
        Fade(0.7);
        Play(Sound.Whoosh, 0.5);
        await WaitAsync(0.7, ct);
    }

    private void BeamOut(double seconds)
    {
        _particles.Beam(_game.Body.Position, _game.Body.Scale);
        Play(Sound.Respawn, 0.7);
        Fade(seconds);
    }

    private void Fade(double seconds)
    {
        _fadeStart = _now;
        _fadeSeconds = Math.Max(0.05, seconds);
        _host.RequestTick();
    }

    /// <summary>Cuánto se ha desvanecido ya al salir (0 = nada, 1 = del todo).</summary>
    private double FadeAt(double now) => double.IsNaN(_fadeStart) ? 0 : Math.Clamp((now - _fadeStart) / _fadeSeconds, 0, 1);

    private static Task WaitAsync(double seconds, CancellationToken ct) => Task.Delay(TimeSpan.FromSeconds(seconds), ct);

    private double TopOfMonitor()
    {
        var position = _game.Body.Position;
        return (_world.MonitorAt(position) ?? _world.NearestMonitor(position))?.Bounds.Top ?? position.Y - _game.Body.Height * 8;
    }

    /// <summary>La entrada que toca (lo llama <see cref="TryStart"/> cuando el lander ya está en su base).</summary>
    private void PerformEntrance(EntranceKind how)
    {
        var body = _game.Body;
        if (how == EntranceKind.Reloaded)
        {
            if (!double.IsNaN(_savedSpot.X) && _world.PlatformBelow(_savedSpot.X, _savedSpot.Y) is { } spot && !OnFullscreenMonitor(spot))
            {
                _game.PlaceOn(spot, _savedSpot.X, visible: true);
            }
            else
            {
                _game.Visual.Opacity = 1;
            }

            DrainGameEvents();
            _previousPosition = body.Position;
            _forceRender = true;
            _host.Log.Info($"Vuelve donde estaba ({body.Position.X:0}, {body.Position.Y:0}).");
            return;
        }

        if (how == EntranceKind.Summoned && GroundNearCursor() is { } near)
        {
            _game.PlaceOn(near.Ground, near.X, visible: false);
            DrainGameEvents();
            _previousPosition = body.Position;
        }

        var arrival = _arrivals.Pick(a => a != Arrival.Sky || body.Position.Y - TopOfMonitor() > body.Height * 6);
        _host.Log.Info($"Entra ({how}): {arrival}.");
        var scale = body.Scale;
        switch (arrival)
        {
            case Arrival.Sky:
                // Aparece arriba (no más de 9 alturas: desde lo alto de un monitor grande tardaba 7 s en posarse) y baja: el
                // piloto frena con el motor y se posa en lo que tenga debajo.
                var top = Math.Max(TopOfMonitor() + body.Height * 1.4, body.Position.Y - body.Height * 9);
                _game.PlaceInAir(new Vec2(body.Position.X, top), new Vec2(0, 140 * scale));
                _game.Visual.Opacity = 0.2;
                _game.Visual.Flash(Expression.Happy, 2.5);
                _previousPosition = body.Position;
                Play(Sound.Whoosh, 0.6);
                break;

            case Arrival.Warp:
                // De un salto, entre destellos: aparece de golpe y se aplasta un poco al caer.
                _game.Visual.Opacity = 1;
                _game.Visual.Impact(1.4);
                _game.Visual.StartBounce();
                _particles.Sparkles(body.Position, 38 * scale, Gfx.Rgb(0xFFD23F), 22, scale);
                _particles.Ring(body.Position + new Vec2(0, body.HalfHeight), scale, 0.9);
                Play(Sound.Pop, 0.8);
                Play(Sound.Crystal, 0.4);
                break;

            default:
                _particles.Beam(body.Position, scale);
                _particles.Sparkles(body.Position, 30 * scale, Gfx.Rgb(0x7DE3FF), 16, scale);
                Play(Sound.Respawn, 0.7);
                break;
        }

        // Cuando lo pones tú, al llegar te saluda.
        _greetAt = how == EntranceKind.Summoned ? _now + (arrival == Arrival.Sky ? 0.6 : 0.9) : double.NaN;
        _forceRender = true;
        SaveMoments(force: true);
    }

    /// <summary>Si lo has puesto tú, saluda en cuanto esté posado y visible.</summary>
    private void UpdateGreeting(double now)
    {
        if (double.IsNaN(_greetAt) || now < _greetAt || !_game.Body.IsLanded || _game.Visual.Opacity < 0.9)
        {
            return;
        }

        _greetAt = double.NaN;
        _game.Visual.Flash(Expression.Happy, 2);
        _game.Visual.StartWave();
        _game.Visual.ShowBubble(new Bubble(BubbleIcon.Hello, 1.8));
        Play(Sound.Pop, 0.5);
    }

    /// <summary>Lo que hay debajo del cursor, en un monitor sin pantalla completa: ahí aparece cuando lo pones tú.</summary>
    private (Platform Ground, double X)? GroundNearCursor()
    {
        if (_sensors.Cursor is not { } cursor || _world.PlatformBelow(cursor.X, cursor.Y) is not { } ground || OnFullscreenMonitor(ground))
        {
            return null;
        }

        return (ground, cursor.X);
    }

    private bool OnFullscreenMonitor(Platform platform) => _sensors.FullscreenMonitors.Contains(platform.MonitorIndex);

    /// <summary>Lee la última entrada y salida (para no repetirlas) y dónde estaba.</summary>
    private void LoadMoments()
    {
        try
        {
            var path = Path.Combine(_host.DataDirectory, MomentsFile);
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject json)
            {
                return;
            }

            if (Enum.TryParse<Arrival>((string?)json["arrival"], out var arrival))
            {
                _arrivals.Use(arrival);
            }

            if (Enum.TryParse<Departure>((string?)json["departure"], out var departure))
            {
                _departures.Use(departure);
            }

            if (json["x"] is JsonValue x && json["y"] is JsonValue y)
            {
                _savedSpot = new Vec2(x.GetValue<double>(), y.GetValue<double>());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            _host.Log.Debug($"moments.json no se puede leer: {ex.Message}");
        }
    }

    /// <summary>
    /// Apunta la última entrada y salida y dónde está posado. Fuera de las salidas, como mucho cada
    /// <see cref="SpotSaveInterval"/> y solo si se ha movido: así, si el proceso se cae, vuelve también a su sitio.
    /// </summary>
    private void SaveMoments(bool force = false)
    {
        var body = _game.Body;
        if (!force && (_now < _nextSpotSave || !body.IsLanded || Vec2.Distance(body.Position, _savedSpot) < 4))
        {
            return;
        }

        _nextSpotSave = _now + SpotSaveInterval;
        if (body.State != BodyState.Destroyed)
        {
            _savedSpot = body.Position;
        }

        var json = new JsonObject
        {
            ["arrival"] = _arrivals.TryGetLast(out var arrival) ? arrival.ToString() : null,
            ["departure"] = _departures.TryGetLast(out var departure) ? departure.ToString() : null,
        };
        if (!double.IsNaN(_savedSpot.X))
        {
            json["x"] = Math.Round(_savedSpot.X, 1);
            json["y"] = Math.Round(_savedSpot.Y, 1);
        }

        try
        {
            Directory.CreateDirectory(_host.DataDirectory);
            File.WriteAllText(Path.Combine(_host.DataDirectory, MomentsFile), json.ToJsonString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _host.Log.Debug($"moments.json no se puede escribir: {ex.Message}");
        }
    }
}
