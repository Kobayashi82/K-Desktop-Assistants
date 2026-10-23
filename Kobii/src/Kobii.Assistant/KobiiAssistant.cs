using System.IO;
using System.Text.Json;
using KDesktop.Assistant.Sdk;
using Kobii.Assistant.Audio;
using Kobii.Assistant.Rendering;
using Kobii.Core;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;

namespace Kobii.Assistant;

/// <summary>
/// Kobii como asistente de K-Desktop. El comportamiento está en Kobii.Core (<see cref="KobiiGame"/>): aquí se le da el
/// mundo y los sensores del host, se avanza con paso fijo, se dibuja en vectorial (<see cref="ISurface.Present"/>)
/// y se atienden el ratón, los gestos y los estados del módulo.
/// </summary>
/// <remarks>
/// Todo ocurre en el hilo del motor del host (ver <see cref="IAssistant"/>). Quieto y sin nadie mirando, duerme casi del
/// todo: <see cref="Tick"/> pide el siguiente ciclo justo cuando hace falta (al parpadear, por ejemplo).
/// </remarks>
public sealed partial class KobiiAssistant : IAssistant
{
    private const double IdleInterval = 0.25;
    private const double AttentiveInterval = 1.0 / 20;
    private const double AwayInterval = 0.5;

    /// <summary>Quieto pero respirando o asentándose: no hace falta dibujarlo a cada fotograma.</summary>
    private const double CalmInterval = 1.0 / 12;

    /// <summary>Solo se mueven las partículas (una «z» al dormir, una nota): no hace falta el ritmo de la pantalla.</summary>
    private const double ParticleInterval = 1.0 / 30;
    private const int MaxStepsPerTick = 20;

    /// <summary>Lo que tarda en desvanecerse al salir (el host espera a <see cref="ExitAsync"/>).</summary>
    private const double ExitDuration = 0.5;

    /// <summary>Lo que tarda en aparecer al volver (tras esconderse o con una pantalla completa).</summary>
    private const double FadeInDuration = 0.35;

    private readonly List<GameEvent> _events = [];
    private readonly List<Emote> _emotes = [];
    private readonly Sensors _sensors = new();
    private readonly ParticleSystem _particles = new();

    private IAssistantHost _host = null!;
    private KobiiSettings _settings = new();
    private KobiiGame _game = null!;

    private double _now;
    private double _accumulator;
    private Vec2 _previousFeet;
    private bool _hidden;
    private bool _listening;
    private PlayKind? _lastPlaying;
    private EntranceKind? _pendingEntrance;
    private double? _exitStart;
    private double _fadeStart = double.NegativeInfinity;

    // Cifras de diagnóstico (cada 30 s en el registro de depuración): ciclos, pasos de simulación y fotogramas.
    private double _statsStart;
    private int _statTicks;
    private int _statSteps;
    private int _statFrames;
    private double _statTickMs;
    private double _statPresentMs;
    private double _statDrawMs;

    public System.Windows.Point? BubbleAnchor =>
        _game is { IsInitialized: true } game && !_hidden && _exitStart is null
            ? new System.Windows.Point(game.HeadPoint.X, game.HeadPoint.Y - game.Body.Height * 0.1)
            : null;

    private double FrameInterval => _host.World.System.PowerSaver ? 1.0 / 30 : Math.Min(1.0 / 60, _host.Rendering.FrameInterval);

    public void Attach(IAssistantHost host)
    {
        _host = host;
        Log.Target = host.Log;
        _settings = KobiiSettings.Read(host.Settings);
        _game = new KobiiGame(_settings.ToGameOptions(), new GameRandom());
        _surface = host.Rendering.CreateSurface();
        _effects = host.Rendering.CreateSurface(SurfaceLayer.Front);
        _layer = new ManoeuvreLayer(host.Rendering);
        _audio = new KobiiAudio(host.Audio);
        _hit = host.Pointer.CreateHitArea();
        _hit.Cursor = PointerCursor.Hand;
        _host.World.Changed += OnWorldChanged;
        _host.Log.Info($"Kobii {typeof(KobiiAssistant).Assembly.GetName().Version?.ToString(3)}: color {_settings.Color}.");
    }

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
        if (_game.IsInitialized && !_hidden)
        {
            _game.PlayClip("saludar");
        }

        _host.RequestTick();
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(ExitDuration), ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // El host tiene prisa: se va tal cual.
        }
    }

    public double Tick(TickInfo tick)
    {
        var tickClock = System.Diagnostics.Stopwatch.GetTimestamp();
        var now = tick.Now;
        var elapsed = tick.Elapsed;
        _now = now;
        ReleaseFrames();
        if (!_game.IsInitialized && !TryStart())
        {
            return 0.25;
        }

        UpdateTerrain();
        UpdateSensors(elapsed);
        if (_game.Body.IsGrounded && _game.Body.Support is { IsBase: false })
        {
            // Encima de una ventana que puede moverse: que el mundo se relea a menudo.
            _host.World.WatchClosely(0.5);
        }

        if (UpdateFullscreen())
        {
            return IdleInterval;
        }

        StepSimulation(elapsed);
        _particles.Update(elapsed);
        UpdateManoeuvres(now);
        Render(now);
        UpdateHitArea();
        _statTickMs += System.Diagnostics.Stopwatch.GetElapsedTime(tickClock).TotalMilliseconds;
        Stats(now);

        if (_exitStart is not null)
        {
            return FrameInterval;
        }

        var interval = IsActive(now) ? FrameInterval
            : _particles.Count > 0 ? Math.Max(FrameInterval, ParticleInterval)
            : IsUserAway() ? AwayInterval
            : _game.NeedsHighFrequency ? CalmInterval
            : IsAttentive() ? AttentiveInterval : IdleInterval;

        // En reposo se despierta justo cuando parpadea, ni antes ni después.
        if (interval > FrameInterval && _game.Activity != Activity.Sleep)
        {
            interval = Math.Clamp(NextBlinkEdge(now) + 0.002 - now, 0.002, interval);
        }

        return interval;
    }

    /// <summary>
    /// Pone a Kobii en el suelo del monitor principal en cuanto hay monitores (al cargarlo el mundo puede estar aún
    /// vacío) y hace la entrada: al arrancar entra andando desde el borde; si solo vuelve, aparece donde estaba.
    /// </summary>
    private bool TryStart()
    {
        if (!_game.IsInitialized)
        {
            UpdateTerrain();
            if (_world.Monitors.Count == 0)
            {
                return false;
            }

            UpdateSensors(0);
            _game.Initialize(_world);

            // Solo para pruebas: empezar en otro monitor (KOBII_MONITOR=índice).
            if (int.TryParse(Environment.GetEnvironmentVariable("KOBII_MONITOR"), out var start) && _world.GroundOf(start) is { } startGround)
            {
                _game.Relocate(startGround, startGround.Left + startGround.Width * 0.72);
            }

            _previousFeet = _game.Body.Feet;
            _host.Log.Info($"Kobii en marcha: {_world.Monitors.Count} monitor(es), {_world.Platforms.Count} plataformas.");
            UpdateFullscreen();
        }

        if (_pendingEntrance is { } how && !_hidden)
        {
            _pendingEntrance = null;
            if (how is EntranceKind.Startup or EntranceKind.Switched)
            {
                _game.WalkIn(fromRight: _game.Body.Feet.X > (_game.Body.Support?.CenterX ?? 0));
            }

            _fadeStart = _now;
            _forceRender = true;
        }

        return true;
    }

    /// <summary>Nadie mira: el usuario lleva un rato sin tocar nada.</summary>
    private bool IsUserAway() => _sensors.UserIdleSeconds > 60 && !_pressed && !_dragging;

    private bool IsActive(double now) =>
        _game.NeedsFullRate || ManoeuvreActive || _pressed || _dragging || _exitStart is not null || now - _fadeStart < FadeInDuration;

    private bool IsAttentive() =>
        _sensors.Cursor is { } cursor && Vec2.Distance(cursor, _game.Body.Center) < _game.Body.Height * 7;

    private void StepSimulation(double elapsed)
    {
        _accumulator += elapsed;
        var steps = 0;
        while (_accumulator >= KobiiGame.TimeStep && steps < MaxStepsPerTick)
        {
            _previousFeet = _game.Body.Feet;
            if (_dragging && (_dragCursor ?? _sensors.Cursor) is { } cursor)
            {
                _game.Drag(cursor);
            }

            _game.Step(_world, _sensors);
            _accumulator -= KobiiGame.TimeStep;
            steps++;
        }

        _statSteps += steps;
        if (steps == MaxStepsPerTick)
        {
            _accumulator = 0;
        }

        DrainGameEvents();
    }

    /// <summary>Las cifras de diagnóstico van al registro también en Release (para medir lo que cuesta).</summary>
    private static readonly bool StatsAlways = Environment.GetEnvironmentVariable("KOBII_STATS") == "1";

    private void Stats(double now)
    {
        _statTicks++;
        if (now - _statsStart < 30)
        {
            return;
        }

        var seconds = now - _statsStart;
        if (_statsStart > 0)
        {
            var line = FormattableString.Invariant($"Kobii: {_statTicks / seconds:0.0} ciclos/s, {_statSteps / seconds:0.0} pasos/s, {_statFrames / seconds:0.0} fotogramas/s, {_statTickMs / Math.Max(1, _statTicks):0.00} ms por ciclo, {_statPresentMs / Math.Max(1, _statFrames):0.00} ms por fotograma (dibujo {_statDrawMs / Math.Max(1, _statFrames):0.00}) ({_game.Activity}; {_game.BusyReason}; partículas {_particles.Count}, cursor cerca {IsAttentive()})");
            if (StatsAlways)
            {
                _host.Log.Info(line);
            }
            else
            {
                _host.Log.Debug(line);
            }
        }

        _statsStart = now;
        _statTicks = _statSteps = _statFrames = 0;
        _statTickMs = _statPresentMs = _statDrawMs = 0;
    }

    private void DrainGameEvents()
    {
        _events.Clear();
        _game.DrainEvents(_events);
        foreach (var gameEvent in _events)
        {
            HandleGameEvent(gameEvent);
        }

        _emotes.Clear();
        _game.Visual.DrainEmotes(_emotes);
        var palette = Species.Palette(_settings.Color);
        foreach (var emote in _emotes)
        {
            _particles.Spawn(emote, palette.Shell);
        }

        if (_game.Playing != _lastPlaying)
        {
            _lastPlaying = _game.Playing;
            if (_lastPlaying is { } playing)
            {
                _host.Log.Debug($"Kobii: se pone con {playing}");
            }
        }
    }

    private void HandleGameEvent(GameEvent gameEvent)
    {
        switch (gameEvent.Kind)
        {
            case GameEventKind.Performed:
            case GameEventKind.FellAsleep:
            case GameEventKind.WokeUp:
            case GameEventKind.Greeted:
            case GameEventKind.Arrived:
            case GameEventKind.Lost:
                _host.Log.Debug($"Kobii: {gameEvent.Kind} {gameEvent.Text}");
                break;
            case GameEventKind.Hop:
                Play(Sound.Boing, 0.35, gameEvent.Position.X);
                break;
            case GameEventKind.Landed when gameEvent.Magnitude > 400:
                Play(Sound.Land, Math.Clamp(gameEvent.Magnitude / 1600, 0.25, 0.8), gameEvent.Position.X);
                break;
            case GameEventKind.Climbed:
                Play(Sound.Chime, 0.4, gameEvent.Position.X);
                _host.Log.Debug("Kobii: arriba de una ventana");
                break;
            case GameEventKind.Pulled:
                Play(Sound.Pop, 0.55, gameEvent.Position.X);
                break;
            case GameEventKind.Chomp:
                Play(Sound.Munch, 0.6, gameEvent.Position.X);
                break;
            case GameEventKind.Gulp:
                Play(Sound.Gulp, 0.45, gameEvent.Position.X);
                break;
            case GameEventKind.Ate:
                Play(Sound.Chime, 0.3, gameEvent.Position.X);
                _host.Log.Debug($"Kobii: se ha comido {gameEvent.Text}");
                break;
            case GameEventKind.Kicked:
                Play(Sound.Kick, 0.7, gameEvent.Position.X);
                break;
            case GameEventKind.BallBounced:
                Play(Sound.Bounce, Math.Clamp(gameEvent.Magnitude / 1200, 0.15, 0.6), gameEvent.Position.X);
                break;
            case GameEventKind.Clapped:
                Play(Sound.Clap, 0.6, gameEvent.Position.X);
                break;
            case GameEventKind.Sneezed:
                Play(Sound.Sneeze, 0.6, gameEvent.Position.X);
                break;
            case GameEventKind.Swept:
                Play(Sound.Swish, 0.35, gameEvent.Position.X);
                break;
            case GameEventKind.Dressed:
                Play(Sound.Chime, 0.35, gameEvent.Position.X);
                _host.Log.Debug($"Kobii: pijama {gameEvent.Text}");
                break;
        }
    }

    public void OnSettingsChanged(JsonElement settings)
    {
        _settings = KobiiSettings.Read(settings);
        _game.ApplyOptions(_settings.ToGameOptions());
        _forceRender = true;
        _host.RequestTick();
    }

    public void Dispose()
    {
        _host.World.Changed -= OnWorldChanged;
        _hit.Dispose();
        _layer.Dispose();
        _effects.Dispose();
        _surface.Dispose();
    }
}
