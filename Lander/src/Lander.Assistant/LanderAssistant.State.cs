using System.IO;
using System.Text.Json;
using KDesktop.Assistant.Sdk;
using Lander.Assistant.Rendering.Skins;
using Lander.Core.Progression;
using Lander.Core.Settings;

namespace Lander.Assistant;

/// <summary>
/// Ajustes y progreso. Los ajustes llegan de K-Desktop (Opciones → Asistentes) con los mismos nombres que el
/// settings.json del Lander independiente, así que se leen con su mismo modelo. El progreso (nivel, puntos, logros…) se
/// guarda en la carpeta de datos del asistente, que K-Desktop conserva al actualizarlo e incluye en sus copias.
/// </summary>
public sealed partial class LanderAssistant
{
    private const double ProgressSaveInterval = 5;

    private JsonStore<PilotProgress> _progressStore = null!;
    private Task _saving = Task.CompletedTask;
    private int _pushedVersion = -1;
    private int _pushedFlightBucket;
    private double _nextProgressSave;
    private int _panelVersion = -1;
    private int _panelFlightBucket;

    /// <summary>Altura del lander a tamaño 100 % (en DIP).</summary>
    internal const double BaseSize = 48;

    /// <summary>Volumen de sus sonidos a 100 %: el volumen propio lo aplica el host encima (ajustes comunes de K-Desktop).</summary>
    private const double BaseVolume = 0.5;

    internal static LanderSettings ReadSettings(JsonElement json) => ReadSettings(json, CommonSettings.Default);

    /// <summary>
    /// Sus ajustes, con lo que K-Desktop pone igual para todos los asistentes (SDK 1.9, pestaña «General» de su ficha): el
    /// tamaño en %, lo movido que es y cogerlo con el ratón; el volumen lo aplica el host y el sonido tiene su interruptor
    /// general en Opciones → Módulo Asistente.
    /// </summary>
    internal static LanderSettings ReadSettings(JsonElement json, CommonSettings common)
    {
        LanderSettings? settings = null;
        if (json.ValueKind == JsonValueKind.Object)
        {
            try
            {
                settings = JsonSerializer.Deserialize(json, LanderJsonContext.Default.LanderSettings);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                // Un valor de otro tipo: los de por defecto.
            }
        }

        settings ??= new LanderSettings();
        settings.LanderSize = BaseSize * common.Size;
        settings.Activity = Math.Max(0.25, common.Liveliness);
        settings.AllowDragging = common.Dragging;
        settings.SoundEnabled = true;
        settings.Volume = BaseVolume;
        return settings.Normalize();
    }

    private void ApplySettings(LanderSettings settings)
    {
        var previous = _settings;
        _settings = settings;
        _game.ApplyOptions(settings.ToGameOptions());
        ApplyContentSettings(previous, settings);
        if (Math.Abs(previous.LanderSize - settings.LanderSize) > 0.01)
        {
            _terrainDirty = true;
        }

        // Solo cuando cambia el ajuste: tomar el control lo saca de la pausa (el Lander independiente apagaba entonces su
        // ajuste; aquí lo guarda K-Desktop), y cambiar otro ajuste no debe volver a pausarlo.
        if (_game.IsInitialized && settings.Paused != previous.Paused && settings.Paused != _game.IsPaused)
        {
            _game.SetPaused(settings.Paused);
            DrainGameEvents();
        }

        _skin = ResolveSkin(settings.Skin);
        ApplyAudio();
        _forceRender = true;
        _host.RequestTick();
    }

    /// <summary>Un aspecto aún bloqueado (progreso a cero, ajustes importados) se queda en el clásico.</summary>
    private LanderSkin ResolveSkin(string id) =>
        Unlocks.IsUnlocked(id, _progress.Level) ? SkinCatalog.Get(id) : SkinCatalog.Classic;

    private void ApplyAudio() => _audio.Configure(_settings.SoundEnabled, _settings.Volume);

    private PilotProgress LoadProgress()
    {
        _progressStore = new JsonStore<PilotProgress>(Path.Combine(_host.DataDirectory, "progress.json"), LanderJsonContext.Default.PilotProgress);
        return _progressStore.Load() ?? new PilotProgress();
    }

    /// <summary>
    /// Guarda el progreso cuando cambia (como mucho cada pocos segundos, y por el tiempo de vuelo cada 30 s), fuera del
    /// hilo del motor y en orden.
    /// </summary>
    private void SaveProgress(double now, bool force = false)
    {
        var flightBucket = (int)(_progress.FlightSeconds / 30);

        // El panel, en cuanto cambia (al aterrizar, al terminar una carrera, con un logro); el disco, como mucho cada pocos segundos.
        if (_game.ProgressVersion != _panelVersion || flightBucket != _panelFlightBucket)
        {
            _panelVersion = _game.ProgressVersion;
            _panelFlightBucket = flightBucket;
            PublishPanel();
        }

        var changed = _game.ProgressVersion != _pushedVersion || flightBucket != _pushedFlightBucket;
        if (!force && (!changed || now < _nextProgressSave))
        {
            return;
        }

        _pushedVersion = _game.ProgressVersion;
        _pushedFlightBucket = flightBucket;
        _nextProgressSave = now + ProgressSaveInterval;
        var snapshot = _progress.Clone();
        var store = _progressStore;
        var log = _host.Log;
        _saving = _saving.ContinueWith(_ =>
        {
            try
            {
                store.Save(snapshot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Warn("No se ha podido guardar el progreso", ex);
            }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>Al soltarlo: espera a lo que se esté guardando y guarda lo último.</summary>
    private void FlushProgress()
    {
        if (!_game.IsInitialized)
        {
            return;
        }

        SaveProgress(_now, force: true);
        if (!_saving.Wait(TimeSpan.FromSeconds(2)))
        {
            _host.Log.Warn("El progreso tarda en guardarse; se deja a medias.");
        }
    }
}
