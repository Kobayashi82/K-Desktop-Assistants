using KDesktop.Assistant.Sdk;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;
using CoreMonitor = Kobii.Core.Terrain.MonitorInfo;
using CoreRect = Kobii.Core.Geometry.PixelRect;
using CoreWindow = Kobii.Core.Terrain.WindowInfo;
using Sdk = KDesktop.Assistant.Sdk;
using SdkRect = KDesktop.Assistant.Sdk.PixelRect;

namespace Kobii.Assistant;

/// <summary>
/// El escritorio que ve Kobii: el <see cref="WorldSnapshot"/> del host convertido en el terreno de Kobii.Core (el suelo
/// de cada monitor y lo alto de cada ventana), los sensores (cursor, inactividad, hora…) y lo que hace con una
/// aplicación a pantalla completa.
/// </summary>
public sealed partial class KobiiAssistant
{
    private List<CoreMonitor> _monitors = [];
    private List<CoreWindow> _windows = [];
    private World _world = World.Empty;
    private WorldSnapshot? _snapshot;
    private bool _terrainDirty = true;
    private Vec2? _lastCursor;

    private void OnWorldChanged(WorldSnapshot snapshot)
    {
        _terrainDirty = true;
        _host.RequestTick();
    }

    /// <summary>Vuelve a construir el terreno si el host ha hecho una foto nueva del escritorio.</summary>
    private void UpdateTerrain()
    {
        var snapshot = _host.World.Current;
        if (!_terrainDirty && ReferenceEquals(snapshot, _snapshot))
        {
            return;
        }

        if (_snapshot is null || !snapshot.Monitors.SequenceEqual(_snapshot.Monitors))
        {
            _monitors = snapshot.Monitors.Select(m => new CoreMonitor(m.Index, ToCore(m.Bounds), ToCore(m.WorkArea), m.DpiScale, m.IsPrimary)).ToList();
            _forceRender = true;
        }

        _snapshot = snapshot;
        _windows = snapshot.Windows.Select(w => new CoreWindow(w.Handle, ToCore(w.Bounds), w.IsMaximized, w.AppId)).ToList();
        _world = TerrainBuilder.Build(_monitors, _windows, new TerrainOptions { CreatureHeightDip = Species.HeightDip * _settings.Size }, _now);
        _terrainDirty = false;
    }

    private void UpdateSensors(double elapsed)
    {
        var system = _host.World.System;
        if (system.Cursor is { } point)
        {
            var cursor = new Vec2(point.X, point.Y);
            if (_lastCursor is { } last && elapsed > 0)
            {
                _sensors.CursorStillSeconds = (cursor - last).LengthSquared < 4 ? _sensors.CursorStillSeconds + elapsed : 0;
            }

            _lastCursor = cursor;
            _sensors.Cursor = cursor;
        }
        else
        {
            _sensors.Cursor = null;
        }

        _sensors.UserIdleSeconds = system.UserIdleSeconds;
        _sensors.LocalTime = system.LocalTime == default ? DateTime.Now : system.LocalTime;
        _sensors.Quiet = system.QuietMode;
    }

    /// <summary>
    /// Con una aplicación a pantalla completa en su monitor, Kobii se muda a otro monitor libre; si no hay ninguno, se
    /// esconde hasta que acabe. (Con un juego exclusivo o una presentación el host suelta además la GPU.)
    /// </summary>
    private bool UpdateFullscreen()
    {
        var feet = _game.Body.Feet;
        var monitor = (_world.MonitorAt(feet - new Vec2(0, 1)) ?? _world.NearestMonitor(feet))?.Index ?? -1;
        var fullscreen = _host.World.System.FullscreenMonitors;
        var hide = fullscreen.Contains(monitor);
        if (hide && !_dragging && FreeGround(fullscreen) is { } ground)
        {
            EndPress();
            _game.Relocate(ground, ground.Left + ground.Width * 0.72);
            _particles.Clear();
            _previousFeet = _game.Body.Feet;
            _fadeStart = _now;
            _forceRender = true;
            hide = false;
            _host.Log.Info($"Pantalla completa en el monitor {monitor}: Kobii se muda al monitor {ground.MonitorIndex}.");
        }

        if (hide)
        {
            if (!_hidden)
            {
                _hidden = true;
                EndPress();
                _particles.Clear();
                HideAll();
                _host.Log.Info("Pantalla completa: Kobii se esconde.");
            }

            return true;
        }

        if (_hidden)
        {
            _hidden = false;
            _fadeStart = _now;
            _forceRender = true;
            _accumulator = 0;
            _previousFeet = _game.Body.Feet;
        }

        return false;
    }

    /// <summary>Suelo de un monitor sin nada a pantalla completa (el principal primero).</summary>
    private Platform? FreeGround(IReadOnlySet<int> fullscreen)
    {
        foreach (var monitor in _world.Monitors.OrderByDescending(m => m.IsPrimary))
        {
            if (!fullscreen.Contains(monitor.Index) && _world.GroundOf(monitor.Index) is { } ground)
            {
                return ground;
            }
        }

        return null;
    }

    public void OnSystemEvent(Sdk.SystemEvent e)
    {
        if (!_game.IsInitialized)
        {
            return;
        }

        // El mundo del host ya está al día cuando llega un suceso: el terreno también tiene que estarlo.
        UpdateTerrain();
        switch (e)
        {
            case Sdk.SystemEvent.ThemeChanged or Sdk.SystemEvent.MonitorsChanged:
                _terrainDirty = true;
                _forceRender = true;
                break;
        }

        _host.RequestTick();
    }

    private static CoreRect ToCore(SdkRect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
