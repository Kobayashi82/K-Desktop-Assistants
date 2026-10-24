using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Sdk;
using Kumo.Assistant.Audio;
using Kumo.Core.Cloud;
using Kumo.Core.Geometry;

namespace Kumo.Assistant;

/// <summary>
/// Entrar y salir según el motivo (SDK 1.9). Al arrancar K-Desktop, una de sus entradas (con <see cref="Variety{T}"/>, sin
/// repetir la de la última vez): se condensa de la niebla en su sitio, llega flotando desde el borde de la pantalla o brota de
/// una nubecita que se hincha. Cuando lo pones tú, lo mismo junto al cursor, y te saluda. Al recargarse (otra versión, otro
/// idioma, el proceso que vuelve) reaparece donde estaba con un fundido. Al esconderlo se despide a su manera (saluda y se
/// difumina, sube y se va por arriba, o se hace gota y se deja caer); al quitarlo se pone gris, llovizna, dice adiós y se
/// deshace despacio; al cerrar K-Desktop, se difumina deprisa.
/// </summary>
public sealed partial class KumoAssistant
{
    private enum Arrival { Condense, Drift, Sprout }

    private enum Goodbye { Wave, Rise, Fall }

    private const string MomentsFile = "moments.json";

    /// <summary>Cada cuánto, como mucho, se apunta dónde está (para reaparecer ahí tras una recarga o una caída).</summary>
    private const double SpotSaveInterval = 10;

    private readonly Variety<Arrival> _arrivals = new Variety<Arrival>()
        .Add(Arrival.Condense, 1.2)
        .Add(Arrival.Drift)
        .Add(Arrival.Sprout, 0.9);

    private readonly Variety<Goodbye> _goodbyes = new Variety<Goodbye>()
        .Add(Goodbye.Wave, 1.2)
        .Add(Goodbye.Rise)
        .Add(Goodbye.Fall, 0.8);

    private EntranceKind? _pendingEntrance;
    private double _fadeOutStart = double.NaN;
    private double _fadeOutSeconds = 0.5;
    private Vec2 _savedSpot = new(double.NaN, double.NaN);
    private double _nextSpotSave;

    /// <summary>Lo que crece al brotar (multiplica su tamaño; 1 = el suyo).</summary>
    private double _sproutStart = double.NaN;

    public void Enter(EntranceKind how)
    {
        _pendingEntrance = how;
        if (_started)
        {
            _pendingEntrance = null;
            PerformEntrance(how);
        }

        _host.RequestTick();
    }

    public async Task ExitAsync(ExitKind why, CancellationToken ct)
    {
        EndPress();
        var shown = _started && !_hidden;
        if (_started)
        {
            SaveMoments(force: true);
        }

        _exitStart = _now;
        _host.RequestTick();
        if (!shown)
        {
            return;
        }

        try
        {
            switch (why)
            {
                case ExitKind.Reloading:
                    FadeOut(0.15);
                    await DelayAsync(0.15, ct);
                    break;

                case ExitKind.Shutdown:
                    _cloud.SetPose(CloudPose.Dissolve);
                    FadeOut(0.4);
                    await DelayAsync(0.4, ct);
                    break;

                case ExitKind.Uninstalling:
                    // La última vez: gris y con llovizna, adiós con su brazo de nube y se deshace en niebla.
                    _host.Log.Info("Kumo se despide para siempre.");
                    _cloud.SetPose(CloudPose.Error);
                    Play(Sound.Sigh, 0.6);
                    await DelayAsync(1.1, ct);
                    _cloud.Aim = _cloud.Position + new Vec2(200, 0);
                    _cloud.SetPose(CloudPose.Wave);
                    Play(Sound.Bye, 0.5);
                    await DelayAsync(1.2, ct);
                    _cloud.SetPose(CloudPose.Dissolve);
                    Play(Sound.Puff, 0.4);
                    FadeOut(0.6);
                    await DelayAsync(0.6, ct);
                    break;

                default:
                    var goodbye = _goodbyes.Pick();
                    _host.Log.Info($"Kumo se despide: {goodbye}.");
                    SaveMoments(force: true);
                    await GoodbyeAsync(goodbye, ct);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // El host tiene prisa: se va tal cual.
        }
    }

    private async Task GoodbyeAsync(Goodbye goodbye, CancellationToken ct)
    {
        var u = _cloud.Unit;
        switch (goodbye)
        {
            case Goodbye.Rise:
            {
                // Sube y se va por arriba de la pantalla, soltando nubecitas.
                var top = (_host.World.Current.MonitorAt(new System.Windows.Point(_cloud.Position.X, _cloud.Position.Y)) ?? Nearest(_cloud.Position))?.Bounds.Top ?? 0;
                _cloud.FlyTo(new Vec2(_cloud.Position.X + u * 1.5, top - u * 3), 900 * u / BaseUnit);
                Play(Sound.Whoosh, 0.5);
                await DelayAsync(0.9, ct);
                FadeOut(0.4);
                await DelayAsync(0.4, ct);
                break;
            }

            case Goodbye.Fall:
                // Se condensa en una gota y se deja caer.
                _cloud.SetPose(CloudPose.Drop);
                await DelayAsync(0.8, ct);
                _cloud.FlyTo(_cloud.Position + new Vec2(0, u * 8), 1300 * u / BaseUnit);
                Play(Sound.Whoosh, 0.35);
                await DelayAsync(0.35, ct);
                FadeOut(0.3);
                await DelayAsync(0.3, ct);
                break;

            default:
                // Adiós con su brazo de nube y se difumina.
                _cloud.Aim = _cloud.Cursor ?? _cloud.Position + new Vec2(-200, 0);
                _cloud.SetPose(CloudPose.Wave);
                Play(Sound.Bye, 0.45);
                await DelayAsync(1.3, ct);
                _cloud.SetPose(CloudPose.Dissolve);
                FadeOut(0.5);
                await DelayAsync(0.5, ct);
                break;
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

    /// <summary>Lo que mide al brotar de una nubecita (crece con un rebote).</summary>
    private double SproutScale()
    {
        if (double.IsNaN(_sproutStart))
        {
            return 1;
        }

        var t = (_now - _sproutStart) / 0.7;
        if (t >= 1)
        {
            _sproutStart = double.NaN;
            return 1;
        }

        // Crece y se pasa un poco antes de quedarse en su tamaño (rebote amortiguado).
        return Math.Max(0.08, 1 - Math.Exp(-6 * t) * Math.Cos(9 * t) * 0.92);
    }

    /// <summary>La entrada que toca (en cuanto hay monitores).</summary>
    private void PerformEntrance(EntranceKind how)
    {
        _exitStart = null;
        _fadeOutStart = double.NaN;
        if (how == EntranceKind.Reloaded)
        {
            _cloud.Place(DefaultSpot());
            _fadeStart = _now;
            _host.Log.Info($"Kumo vuelve donde estaba ({_cloud.Position.X:0}, {_cloud.Position.Y:0}).");
            return;
        }

        var summoned = how == EntranceKind.Summoned;
        var spot = summoned && NearCursor() is { } near ? near : DefaultSpot();
        _unit = _cloud.Unit = UnitAt(spot);
        var arrival = _arrivals.Pick();
        _host.Log.Info($"Kumo entra ({how}): {arrival}.");
        _fadeStart = _now;
        switch (arrival)
        {
            case Arrival.Drift:
            {
                // Llega flotando desde el borde de la pantalla que tiene más cerca.
                var monitor = _host.World.Current.MonitorAt(new System.Windows.Point(spot.X, spot.Y)) ?? _host.World.Current.PrimaryMonitor!;
                var fromRight = spot.X > monitor.Bounds.Center.X;
                var start = new Vec2(fromRight ? monitor.Bounds.Right + _cloud.Unit * 2 : monitor.Bounds.Left - _cloud.Unit * 2, spot.Y - _cloud.Unit * 1.5);
                _cloud.Place(start);
                _cloud.FlyTo(spot, 520 * _cloud.Unit / BaseUnit);
                _fadeStart = double.NegativeInfinity;
                Play(Sound.Whoosh, 0.35, start.X);
                break;
            }

            case Arrival.Sprout:
                _cloud.Place(spot);
                _sproutStart = _now;
                _cloud.SetPose(CloudPose.Surprise);
                Play(Sound.Pop, 0.5, spot.X);
                break;

            default:
                _cloud.Place(spot);
                _cloud.Condense();
                Play(Sound.Puff, 0.45, spot.X);
                break;
        }

        if (summoned)
        {
            _greetAt = _now + (arrival == Arrival.Drift ? 1.6 : 0.8);
        }

        SaveMoments(force: true);
    }

    /// <summary>Junto al cursor, en un monitor sin pantalla completa: ahí aparece cuando lo pones tú.</summary>
    private Vec2? NearCursor()
    {
        if (_host.World.System.Cursor is not { } cursor || _host.World.Current.MonitorAt(cursor) is not { } monitor
            || _host.World.System.FullscreenMonitors.Contains(monitor.Index))
        {
            return null;
        }

        var u = BaseUnit * monitor.DpiScale * _settings.Size;
        var at = new Vec2(cursor.X + (cursor.X > monitor.Bounds.Center.X ? -2.4 : 2.4) * u, cursor.Y - 0.6 * u);
        return Clamp(at, monitor);
    }

    /// <summary>Lee la última entrada y despedida (para no repetirlas) y dónde estaba.</summary>
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

            if (Enum.TryParse<Goodbye>((string?)json["goodbye"], out var goodbye))
            {
                _goodbyes.Use(goodbye);
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
    /// Apunta la última entrada, la última despedida y dónde flota. Fuera de las salidas, como mucho cada
    /// <see cref="SpotSaveInterval"/> y solo si ha cambiado de sitio: así, si el proceso se cae, también vuelve a su sitio.
    /// </summary>
    private void SaveMoments(bool force = false)
    {
        var home = _cloud.Motion == Motion.Hover && !_dragging ? _cloud.Home : _savedSpot;
        if (!force && (_now < _nextSpotSave || double.IsNaN(home.X) || (!double.IsNaN(_savedSpot.X) && Vec2.Distance(home, _savedSpot) < 4)))
        {
            return;
        }

        _nextSpotSave = _now + SpotSaveInterval;
        if (!double.IsNaN(home.X) && Carrying is null)
        {
            _savedSpot = home;
        }

        var json = new JsonObject
        {
            ["arrival"] = _arrivals.TryGetLast(out var arrival) ? arrival.ToString() : null,
            ["goodbye"] = _goodbyes.TryGetLast(out var goodbye) ? goodbye.ToString() : null,
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
