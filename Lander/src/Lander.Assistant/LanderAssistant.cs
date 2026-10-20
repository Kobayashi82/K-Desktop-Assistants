using System.Text.Json;
using KDesktop.Assistant.Sdk;
using Lander.Assistant.Audio;
using Lander.Assistant.Rendering;
using Lander.Assistant.Rendering.Skins;
using Lander.Core;
using Lander.Core.Autopilot;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Progression;
using Lander.Core.Settings;

namespace Lander.Assistant;

/// <summary>
/// El Lander como asistente de K-Desktop. Hace lo que hacía el motor del Lander independiente, pero con lo que le da
/// el host: el mundo y sus sucesos en lugar de los ganchos de Windows, superficies de la capa común en lugar de su
/// ventana y su GPU, una zona de clic, el teclado de K-Desktop y su mezclador. El juego sigue entero en Lander.Core:
/// aquí se le dan los sensores, se avanza la física con paso fijo, se dibuja y suena.
/// </summary>
/// <remarks>
/// Todo ocurre en el hilo del motor del host (ver <see cref="IAssistant"/>). Duerme casi del todo cuando el lander
/// está quieto: <see cref="Tick"/> pide el siguiente ciclo justo cuando hace falta (al parpadear, por ejemplo).
/// </remarks>
public sealed partial class LanderAssistant : IAssistant
{
    private const double IdleInterval = 0.25;
    private const double AttentiveInterval = 1.0 / 20;
    private const double AwayInterval = 0.5;
    private const int MaxStepsPerTick = 20;

    /// <summary>Lo que tarda en desvanecerse al salir (el host espera a <see cref="ExitAsync"/>).</summary>
    private const double ExitDuration = 0.6;

    private readonly List<GameEvent> _events = [];
    private readonly Sensors _sensors = new();
    private readonly LanderScene _scene = new();
    private readonly ParticleSystem _particles = new();

    private IAssistantHost _host = null!;
    private LanderAudio _audio = null!;
    private MarkerLayer _markers = null!;
    private LanderSettings _settings = new();
    private PilotProgress _progress = new();
    private LanderGame _game = null!;
    private LanderSkin _skin = SkinCatalog.Classic;

    private double _now;
    private double _accumulator;
    private Vec2 _previousPosition;
    private double _previousAngle;
    private bool _hidden;
    private EntranceKind? _pendingEntrance;
    private double? _exitStart;
    private (AutopilotState, BodyState, Core.Terrain.PlatformKind?) _pilotState;

    public System.Windows.Point? BubbleAnchor =>
        _game is { IsInitialized: true } game && !_hidden && game.Body.State != BodyState.Destroyed
            ? new System.Windows.Point(game.Body.Position.X, game.Body.Position.Y - LanderBody.BaseHalfHeight * game.Body.Scale)
            : null;

    private double FrameInterval => _sensors.PowerSaver ? 1.0 / 30 : Math.Min(1.0 / 60, _host.Rendering.FrameInterval);

    public void Attach(IAssistantHost host)
    {
        _host = host;
        Log.Target = host.Log;
        Texts.Language = host.Language;
        _settings = ReadSettings(host.Settings);
        _progress = LoadProgress();
        _audio = new LanderAudio(host.Audio);
        _markers = new MarkerLayer(host.Rendering);
        CreateContentVision();
        _surface = host.Rendering.CreateSurface();
        _hit = host.Pointer.CreateHitArea();
        _hit.Cursor = PointerCursor.Hand;
        _host.Keyboard.KeyChanged += OnKeyChanged;
        _host.World.Changed += OnWorldChanged;
        _game = new LanderGame(_settings.ToGameOptions(), _progress, new GameRandom());
        _skin = ResolveSkin(_settings.Skin);
        ApplyAudio();
        _host.Log.Info($"Lander {typeof(LanderAssistant).Assembly.GetName().Version?.ToString(3)}: nivel {_progress.Level}, {_progress.TotalPoints} puntos.");
    }

    public void Enter(EntranceKind how)
    {
        _pendingEntrance = how;
        PublishPanel();
        TryStart();
        _host.RequestTick();
    }

    public async Task ExitAsync(ExitKind why, CancellationToken ct)
    {
        // Se desvanece con un haz, como al reaparecer; el progreso se guarda al soltarlo (Dispose).
        if (_game.IsInitialized && !_hidden && _game.Body.State != BodyState.Destroyed)
        {
            var body = _game.Body;
            _particles.Beam(body.Position, body.Scale);
            Play(Sound.Respawn, 0.7);
        }

        EndPress();
        ReleaseGameKeys();
        _exitStart = _now;
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
        var now = tick.Now;
        var elapsed = tick.Elapsed;
        _now = now;
        if (!_game.IsInitialized && !TryStart())
        {
            return 0.5;
        }

        UpdateTerrain();
        UpdateSensors(elapsed);
        _gustCooldown -= elapsed;
        if (_game.Body.Support is { } support && support.Velocity.LengthSquared > 1)
        {
            // Posado sobre una ventana que se mueve: el mundo se relee a cada fotograma mientras tanto.
            _host.World.WatchClosely(0.5);
        }

        if (UpdateFullscreen())
        {
            _audio.SetEngine(0, 0, _span, elapsed);
            UpdateWheelWatch();
            return IdleInterval;
        }

        if (UpdateContent(now))
        {
            RebuildTerrain();
        }

        StepSimulation(elapsed);
        UpdateEffects(now, elapsed);
        UpdateInteraction();
        UpdateWheelWatch();
        Render(now);
        UpdateMarkers(now);
        SaveProgress(now);

        if (_exitStart is not null)
        {
            return FrameInterval;
        }

        // Lo que se mueve manda: recién posado aún rebota la suspensión, se asienta el polvo y se apaga la llama, también
        // con el usuario ausente (es cuando más vuela solo, de vuelta a la base a dormir). Antes, el ritmo de «ausente»
        // iba primero y ese final se veía a 2 fotogramas por segundo.
        var interval = IsActive(now) ? FrameInterval
            : IsUserAway() && _game.Body.IsLanded ? AwayInterval
            : IsAttentive() ? AttentiveInterval : IdleInterval;

        // Mirando el contenido de las ventanas: recoger la lectura de la GPU o hacer el siguiente escaneo.
        var contentWake = _content.NextWake(now) - now;
        if (contentWake < interval)
        {
            interval = Math.Max(contentWake, 0.005);
        }

        // En reposo se despierta justo cuando parpadea, ni antes ni después.
        if (interval > FrameInterval && _game.Visual.Expression != Expression.Asleep && _game.Body.State != BodyState.Destroyed)
        {
            interval = Math.Clamp(NextBlinkEdge(now) + 0.002 - now, 0.002, interval);
        }

        return interval;
    }

    /// <summary>
    /// Pone el lander en su base en cuanto hay monitores (al cargarlo el mundo puede estar aún vacío) y hace la
    /// entrada: aparece con el haz de reaparecer.
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
            if (_settings.Paused)
            {
                _game.SetPaused(true);
            }

            _previousPosition = _game.Body.Position;
            _previousAngle = _game.Body.Angle;
            _pushedVersion = _game.ProgressVersion;
            DrainGameEvents();
            _host.Log.Info($"Lander en marcha: {_world.Monitors.Count} monitor(es), {_world.Platforms.Count} plataformas.");
        }

        if (_pendingEntrance is not null)
        {
            _pendingEntrance = null;
            var body = _game.Body;
            _particles.Beam(body.Position, body.Scale);
            _particles.Sparkles(body.Position, 30 * body.Scale, Gfx.Rgb(0x7DE3FF), 16, body.Scale);
            Play(Sound.Respawn, 0.7);
            _forceRender = true;
        }

        if (_pendingExtra is { } extra)
        {
            _pendingExtra = null;
            RunExtra(extra);
        }

        return true;
    }

    /// <summary>Nadie mira: el usuario lleva un rato sin tocar nada y no está pilotando.</summary>
    private bool IsUserAway() =>
        _sensors.UserIdleSeconds > 60 && _game.Mode == ControlMode.Auto && !_pressed && !_dragging;

    private bool IsActive(double now) =>
        _game.Mode == ControlMode.Manual || _game.NeedsHighFrequency || _particles.Count > 0 || AntennaMoving || LightFading
        || _pressed || _dragging || _markers.HasVisible || _audio.EngineAudible || _reaction is not null || _exitStart is not null;

    private bool IsAttentive() =>
        _game.Visual.Bubble is not null
        || (_sensors.Cursor is { } cursor && Vec2.Distance(cursor, _game.Body.Position) < _game.Body.Height * 6);

    private void StepSimulation(double elapsed)
    {
        if (_game.IsPaused && !_game.NeedsHighFrequency && !_dragging)
        {
            _accumulator = 0;
            return;
        }

        _accumulator += elapsed;
        var controls = _game.Mode == ControlMode.Manual ? ReadControls() : default;
        var steps = 0;
        while (_accumulator >= LanderGame.TimeStep && steps < MaxStepsPerTick)
        {
            _previousPosition = _game.Body.Position;
            _previousAngle = _game.Body.Angle;
            if (_dragging && (_dragCursor ?? _sensors.Cursor) is { } cursor)
            {
                _game.Drag(cursor);
            }

            _game.Step(_world, _sensors, controls);
            _accumulator -= LanderGame.TimeStep;
            steps++;
        }

        if (steps == MaxStepsPerTick)
        {
            _accumulator = 0;
        }

        DrainGameEvents();
        LogPilotState();
    }

    /// <summary>Cada cambio de estado del piloto automático, con su destino (registro de depuración).</summary>
    private void LogPilotState()
    {
        var autopilot = _game.Autopilot;
        var state = (autopilot.State, _game.Body.State, autopilot.Destination?.Kind);
        if (state != _pilotState)
        {
            _pilotState = state;
            Log.Stats($"Piloto: {autopilot.State}, cuerpo {_game.Body.State}, destino {autopilot.Destination?.Kind.ToString() ?? "ninguno"} ({autopilot.Purpose}).");
        }
    }

    private void DrainGameEvents()
    {
        _events.Clear();
        _game.DrainEvents(_events);
        foreach (var gameEvent in _events)
        {
            HandleGameEvent(gameEvent);
        }
    }

    public void OnSettingsChanged(JsonElement settings) => ApplySettings(ReadSettings(settings));

    public void Dispose()
    {
        _host.Keyboard.KeyChanged -= OnKeyChanged;
        _host.World.Changed -= OnWorldChanged;
        ReleaseGameKeys();
        _host.Pointer.WatchWheel = false;
        FlushProgress();
        _content.Dispose();
        _audio.Dispose();
        _markers.Dispose();
        _hit.Dispose();
        _surface.Dispose();
    }
}
