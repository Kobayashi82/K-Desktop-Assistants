using System.Text.Json;
using KDesktop.Assistant.Sdk;
using Kumo.Assistant.Rendering;
using Kumo.Core;
using Kumo.Core.Cloud;
using Kumo.Core.Geometry;

namespace Kumo.Assistant;

/// <summary>
/// Kumo como asistente de K-Desktop: una nube ámbar que flota sobre las ventanas. La nube (forma, muelles, nubecitas) está en
/// Kumo.Core (<see cref="KumoCloud"/>); aquí se le da el mundo del host, se decide qué hace, se dibuja en vectorial
/// (<see cref="ISurface.Present"/>) y se atienden el ratón, los gestos y los estados del módulo.
/// </summary>
/// <remarks>
/// Todo ocurre en el hilo del motor del host. Kumo nunca está quieto del todo (su forma cambia sin parar), así que el ritmo es lo
/// que decide lo que gasta: a cada fotograma solo cuando se mueve de verdad o hace algo; en reposo, 30 fotogramas por segundo (con
/// 12, y con 4 sin tocar nada, se veía a saltos); si llevas un minuto sin tocar nada, 20 (puedes estar mirando), y solo cuando
/// llevas diez minutos fuera, 8.
/// </remarks>
public sealed partial class KumoAssistant : IAssistant
{
    /// <summary>Píxeles por unidad a escala 1 y 100 % de escalado (la nube mide unas 2,3 unidades de ancho).</summary>
    internal const double BaseUnit = 40;

    /// <summary>En reposo cambiando de forma (y con el cursor cerca, siguiéndolo con los ojos).</summary>
    private const double CalmInterval = 1.0 / 30;

    /// <summary>Solo se mueven las partículas: no hace falta el ritmo de la pantalla.</summary>
    private const double ParticleInterval = 1.0 / 30;

    /// <summary>Un minuto sin tocar nada: puede que estés mirando (leyendo, viendo algo).</summary>
    private const double StillInterval = 1.0 / 20;

    /// <summary>Diez minutos sin tocar nada: no hay nadie.</summary>
    private const double AwayInterval = 1.0 / 8;

    private IAssistantHost _host = null!;
    private KumoSettings _settings = new();
    private KumoCloud _cloud = null!;
    private double _now;

    /// <summary>Píxeles por unidad donde está (sin el rebote de brotar).</summary>
    private double _unit = BaseUnit;
    private bool _started;
    private bool _hidden;
    private double? _exitStart;

    // Cifras de diagnóstico (cada 30 s en el registro de depuración, o siempre con KUMO_STATS=1).
    private static readonly bool StatsAlways = Environment.GetEnvironmentVariable("KUMO_STATS") == "1";
    private double _statsStart;
    private int _statTicks;
    private int _statFrames;
    private double _statTickMs;
    private double _statFieldMs;
    private double _statDrawMs;

    /// <summary>Dónde está su cuerpo (SDK 1.11): lo que los otros asistentes evitan; sin nubecitas ni partículas.</summary>
    public KDesktop.Assistant.Sdk.PixelRect? BodyBounds
    {
        get
        {
            if (!_started || _hidden || _exitStart is not null)
            {
                return null;
            }

            var (l, t, r, b) = _cloud.BodyExtent();
            var u = _cloud.Unit;
            var at = _cloud.RenderPosition;
            return new KDesktop.Assistant.Sdk.PixelRect((int)(at.X + l * u), (int)(at.Y + t * u), (int)Math.Ceiling(at.X + r * u), (int)Math.Ceiling(at.Y + b * u));
        }
    }

    /// <summary>Encima de la nube (o del bocadillo).</summary>
    public System.Windows.Point? BubbleAnchor
    {
        get
        {
            if (!_started || _hidden || _exitStart is not null)
            {
                return null;
            }

            var (_, top, _, _) = _cloud.BodyExtent();
            var at = _cloud.RenderPosition;
            return new System.Windows.Point(at.X, at.Y + top * _cloud.Unit);
        }
    }

    /// <summary>
    /// Cada cuánto se dibuja cuando se mueve: unos 60 fotogramas por segundo, en un número entero de refrescos de la pantalla
    /// (como Kobii: a 144 Hz, uno de cada dos).
    /// </summary>
    private double FrameInterval => _host.World.System.PowerSaver ? Paced(1.0 / 30) : Paced(1.0 / 60);

    /// <summary>
    /// <paramref name="interval"/> redondeado a un número entero de refrescos de la pantalla: con otro ritmo, unos fotogramas
    /// duran un refresco más que otros y el movimiento lento da tirones.
    /// </summary>
    private double Paced(double interval)
    {
        var refresh = _host.Rendering.FrameInterval;
        return refresh <= 0 ? interval : refresh * Math.Max(1, Math.Round(interval / refresh));
    }

    public void Attach(IAssistantHost host)
    {
        _host = host;
        Log.Target = host.Log;
        _settings = KumoSettings.Read(host.Settings, host.Common);
        _cloud = new KumoCloud(new GameRandom());
        AttachRendering();
        AttachAudio();
        _host.World.Changed += OnWorldChanged;
        LoadMoments();
        _host.Log.Info($"Kumo {typeof(KumoAssistant).Assembly.GetName().Version?.ToString(3)}.");
    }

    public double Tick(TickInfo tick)
    {
        var clock = System.Diagnostics.Stopwatch.GetTimestamp();
        _now = tick.Now;
        ReleaseFrames();
        if (!_started && !TryStart())
        {
            return 0.25;
        }

        UpdateOthers();
        UpdateSensors();
        if (UpdateFullscreen())
        {
            return 0.5;
        }

        UpdateManoeuvres();
        Behave(tick.Elapsed);
        UpdateExtras(tick.Elapsed);
        _unit = UnitHere();
        _cloud.Unit = _unit * SproutScale();
        _cloud.Step(tick.Elapsed);
        KeepOnScreen();
        Render();
        UpdateHitArea();
        SaveMoments();
        _statTickMs += System.Diagnostics.Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
        Stats();

        if (_exitStart is not null || IsActive())
        {
            return FrameInterval;
        }

        if (_cloud.Particles.Count > 0)
        {
            return Math.Max(FrameInterval, Paced(ParticleInterval));
        }

        var idle = _pressed || _dragging ? 0 : _host.World.System.UserIdleSeconds;
        var interval = Paced(idle > 600 ? AwayInterval : idle > 60 ? StillInterval : CalmInterval);

        // El parpadeo se ve entero: se despierta justo cuando empieza.
        var blink = _cloud.NextBlink - _cloud.Time;
        return blink <= 0 ? FrameInterval : Math.Clamp(blink + 0.002, 0.002, interval);
    }

    /// <summary>Hay algo que se ve moverse: a cada fotograma.</summary>
    private bool IsActive() =>
        _cloud.IsLively || _badge.IsAnimating(_now) || ExtrasMoving || _dragging || _pressed || ManoeuvreActive || _cloud.Lid > 0 || _now - _fadeStart < FadeInDuration || !double.IsNaN(_fadeOutStart);

    /// <summary>Lo pone en su sitio en cuanto hay monitores (al cargarlo el mundo puede estar aún vacío) y hace su entrada.</summary>
    private bool TryStart()
    {
        if (_host.World.Current.Monitors.Count == 0)
        {
            return false;
        }

        _started = true;
        _unit = _cloud.Unit = UnitAt(DefaultSpot());
        _cloud.Place(DefaultSpot());
        _host.Log.Info($"Kumo en marcha: {_host.World.Current.Monitors.Count} monitor(es).");
        if (_pendingEntrance is { } how)
        {
            _pendingEntrance = null;
            PerformEntrance(how);
        }

        return true;
    }

    private void Stats()
    {
        _statTicks++;
        if (_now - _statsStart < 30)
        {
            return;
        }

        var seconds = _now - _statsStart;
        if (_statsStart > 0)
        {
            var line = FormattableString.Invariant($"Kumo: {_statTicks / seconds:0.0} ciclos/s, {_statFrames / seconds:0.0} fotogramas/s, {_statTickMs / Math.Max(1, _statTicks):0.00} ms por ciclo, campo {_statFieldMs / Math.Max(1, _statFrames):0.00} ms y dibujo {_statDrawMs / Math.Max(1, _statFrames):0.00} ms por fotograma ({_cloud.Pose}, {_cloud.Motion}; nubecitas {_cloud.Puffs.Count}, partículas {_cloud.Particles.Count}; en {_cloud.Position.X:0},{_cloud.Position.Y:0}, unidad {_cloud.Unit:0.#})");
            if (StatsAlways)
            {
                _host.Log.Info(line);
            }
            else
            {
                _host.Log.Debug(line);
            }
        }

        _statsStart = _now;
        _statTicks = _statFrames = 0;
        _statTickMs = _statFieldMs = _statDrawMs = 0;
    }

    public void OnSettingsChanged(JsonElement settings)
    {
        _settings = KumoSettings.Read(settings, _host.Common);
        _host.RequestTick();
    }

    public void Dispose()
    {
        _host.World.Changed -= OnWorldChanged;
        DisposeRendering();
    }
}
