using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Sdk;
using Kobii.Assistant.Audio;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Assistant;

/// <summary>
/// Entrar y salir según el motivo (SDK 1.9). Al arrancar K-Desktop, una de sus entradas (con <see cref="Variety{T}"/>, sin
/// repetir la de la última vez): entra andando por un lado, cae desde arriba, aparece jugando al cucú-tras o de un salto con
/// los brazos arriba. Cuando lo pones tú, lo mismo pero junto al cursor, y te saluda. Al recargarse (otra versión, otro
/// idioma, el proceso que vuelve) reaparece donde estaba con un fundido. Al esconderlo se despide a su manera (saluda, te
/// lanza un beso o hace una reverencia); al quitarlo suspira y te dice adiós con las dos manos; al cerrar K-Desktop, deprisa.
/// </summary>
public sealed partial class KobiiAssistant
{
    private enum Arrival { Walk, Drop, PeekABoo, Pop }

    private const string MomentsFile = "moments.json";

    /// <summary>Cada cuánto, como mucho, se apunta dónde está (para reaparecer ahí tras una recarga o una caída).</summary>
    private const double SpotSaveInterval = 10;

    private readonly Variety<Arrival> _arrivals = new Variety<Arrival>()
        .Add(Arrival.Walk, 1.2)
        .Add(Arrival.Drop)
        .Add(Arrival.PeekABoo, 0.8)
        .Add(Arrival.Pop, 0.8);

    /// <summary>Las despedidas: la animación y lo que dura antes de desvanecerse.</summary>
    private readonly Variety<(string Clip, double Seconds)> _goodbyes = new Variety<(string Clip, double Seconds)>()
        .Add(("saludar", 1.5), 1.2)
        .Add(("beso", 1.4))
        .Add(("reverencia", 1.6), 0.8)
        .Add(("saludar-dos", 1.6), 0.8);

    private double _fadeOutStart = double.NaN;
    private double _fadeOutSeconds = 0.5;
    private Vec2 _savedSpot = new(double.NaN, double.NaN);
    private double _nextSpotSave;

    public void Enter(EntranceKind how)
    {
        _pendingEntrance = how;
        TryStart();
        _host.RequestTick();
    }

    public async Task ExitAsync(ExitKind why, CancellationToken ct)
    {
        EndPress();
        _exitStart = _now;
        var shown = _game.IsInitialized && !_hidden;
        if (_game.IsInitialized)
        {
            SaveMoments(force: true);
        }

        _host.RequestTick();
        try
        {
            if (!shown)
            {
                return;
            }

            switch (why)
            {
                case ExitKind.Reloading:
                    FadeOut(0.15);
                    await Task.Delay(TimeSpan.FromSeconds(0.15), ct).ConfigureAwait(true);
                    break;

                case ExitKind.Shutdown:
                    FadeOut(0.35);
                    await Task.Delay(TimeSpan.FromSeconds(0.35), ct).ConfigureAwait(true);
                    break;

                case ExitKind.Uninstalling:
                    // La última vez: un suspiro y adiós con las dos manos, entre corazones.
                    _host.Log.Info("Kobii se despide para siempre.");
                    _game.PlayClip("suspirar");
                    await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(true);
                    _game.PlayClip("saludar-dos");
                    _game.Visual.AddEmote(EmoteKind.Heart, _game.HeadPoint, _game.Body.Scale, 4);
                    DrainGameEvents();
                    await Task.Delay(TimeSpan.FromSeconds(1.3), ct).ConfigureAwait(true);
                    FadeOut(0.5);
                    await Task.Delay(TimeSpan.FromSeconds(0.5), ct).ConfigureAwait(true);
                    break;

                default:
                    var (clip, seconds) = _goodbyes.Pick();
                    _host.Log.Info($"Kobii se despide: {clip}.");
                    SaveMoments(force: true);
                    _game.PlayClip(clip);
                    DrainGameEvents();
                    await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(true);
                    FadeOut(0.5);
                    await Task.Delay(TimeSpan.FromSeconds(0.5), ct).ConfigureAwait(true);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // El host tiene prisa: se va tal cual.
        }
    }

    private void FadeOut(double seconds)
    {
        _fadeOutStart = _now;
        _fadeOutSeconds = Math.Max(0.05, seconds);
        _host.RequestTick();
    }

    /// <summary>Lo que queda de él al salir (1 = del todo, 0 = ya se ha ido).</summary>
    private double FadeOutAt(double now) => double.IsNaN(_fadeOutStart) ? 1 : 1 - Math.Clamp((now - _fadeOutStart) / _fadeOutSeconds, 0, 1);

    /// <summary>La entrada que toca (lo llama <see cref="TryStart"/> cuando Kobii ya está en el suelo).</summary>
    private void PerformEntrance(EntranceKind how)
    {
        var body = _game.Body;
        if (how == EntranceKind.Reloaded)
        {
            if (!double.IsNaN(_savedSpot.X) && _world.PlatformBelow(_savedSpot.X, _savedSpot.Y - 2) is { } spot && !OnFullscreenMonitor(spot))
            {
                _game.Relocate(spot, Math.Clamp(_savedSpot.X, spot.Left + body.HalfWidth, Math.Max(spot.Left + body.HalfWidth, spot.Right - body.HalfWidth)));
                _previousFeet = body.Feet;
            }

            _fadeStart = _now;
            _forceRender = true;
            _host.Log.Info($"Kobii vuelve donde estaba ({body.Feet.X:0}, {body.Feet.Y:0}).");
            return;
        }

        var summoned = how == EntranceKind.Summoned;
        if (summoned && GroundNearCursor() is { } near)
        {
            _game.Relocate(near.Ground, near.X);
            _previousFeet = body.Feet;
        }

        var top = (_world.MonitorAt(body.Center) ?? _world.NearestMonitor(body.Center))?.Bounds.Top ?? body.Feet.Y - body.Height * 6;
        var arrival = _arrivals.Pick(a => a != Arrival.Drop || body.Feet.Y - top > body.Height * 4);
        _host.Log.Info($"Kobii entra ({how}): {arrival}.");
        switch (arrival)
        {
            case Arrival.Drop:
                _game.DropIn(body.Feet.X, top + body.Height * 1.2, greet: summoned);
                _previousFeet = body.Feet;
                Play(Sound.Boing, 0.4, body.Feet.X);
                break;

            case Arrival.PeekABoo:
                _game.PlayClip("cucu-tras");
                if (summoned)
                {
                    _game.Greet();
                }

                break;

            case Arrival.Pop:
                _game.PlayClip("brazos-arriba");
                _game.Visual.AddEmote(EmoteKind.Sparkle, _game.HeadPoint, body.Scale, 8);
                Play(Sound.Chime, 0.4, body.Feet.X);
                if (summoned)
                {
                    _game.Greet();
                }

                break;

            default:
                _game.WalkIn(fromRight: body.Feet.X > (body.Support?.CenterX ?? 0));
                break;
        }

        DrainGameEvents();
        _fadeStart = _now;
        _forceRender = true;
        SaveMoments(force: true);
    }

    /// <summary>Lo que hay debajo del cursor, en un monitor sin pantalla completa: ahí aparece cuando lo pones tú.</summary>
    private (Platform Ground, double X)? GroundNearCursor()
    {
        if (_sensors.Cursor is not { } cursor || _world.PlatformBelow(cursor.X, cursor.Y) is not { } ground || OnFullscreenMonitor(ground))
        {
            return null;
        }

        var half = _game.Body.HalfWidth;
        return (ground, Math.Clamp(cursor.X, ground.Left + half, Math.Max(ground.Left + half, ground.Right - half)));
    }

    private bool OnFullscreenMonitor(Platform platform) =>
        _host.World.System.FullscreenMonitors.Contains(platform.MonitorIndex);

    /// <summary>Lee la última entrada (para no repetirla) y dónde estaba.</summary>
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
    /// Apunta la última entrada y dónde están sus pies. Fuera de las salidas, como mucho cada <see cref="SpotSaveInterval"/> y
    /// solo si se ha movido estando en el suelo: así, si el proceso se cae, también vuelve a su sitio.
    /// </summary>
    private void SaveMoments(bool force = false)
    {
        var body = _game.Body;
        if (!force && (_now < _nextSpotSave || !body.IsGrounded || Vec2.Distance(body.Feet, _savedSpot) < 4))
        {
            return;
        }

        _nextSpotSave = _now + SpotSaveInterval;
        if (body.IsGrounded)
        {
            _savedSpot = body.Feet;
        }

        var json = new JsonObject
        {
            ["arrival"] = _arrivals.TryGetLast(out var arrival) ? arrival.ToString() : null,
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
