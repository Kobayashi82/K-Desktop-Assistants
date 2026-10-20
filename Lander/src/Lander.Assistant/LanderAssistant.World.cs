using KDesktop.Assistant.Sdk;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Terrain;
using CoreMonitor = Lander.Core.Terrain.MonitorInfo;
using CoreRect = Lander.Core.Geometry.PixelRect;
using CoreWindow = Lander.Core.Terrain.WindowInfo;
using Sdk = KDesktop.Assistant.Sdk;
using SdkRect = KDesktop.Assistant.Sdk.PixelRect;

namespace Lander.Assistant;

/// <summary>
/// El escritorio que ve el lander: el <see cref="WorldSnapshot"/> del host convertido en el terreno de Lander.Core, los
/// sensores (cursor, inactividad, tema, batería…) y lo que hace con una aplicación a pantalla completa.
/// </summary>
public sealed partial class LanderAssistant
{
    private readonly HashSet<long> _windowHandles = [];
    private List<CoreMonitor> _monitors = [];
    private List<CoreWindow> _windows = [];
    private World _world = World.Empty;
    private WorldSnapshot? _snapshot;
    private bool _terrainDirty = true;
    private Vec2? _lastCursor;

    /// <summary>Extremos horizontales de los monitores (para el panorama del sonido).</summary>
    private Interval _span;

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
            _span = _monitors.Count == 0 ? default : new Interval(_monitors.Min(m => m.Bounds.Left), _monitors.Max(m => m.Bounds.Right));
            _content.OnMonitorsChanged();
            _forceRender = true;
        }

        _snapshot = snapshot;
        _windowHandles.Clear();
        _titles.Clear();
        _windows = new List<CoreWindow>(snapshot.Windows.Count);
        foreach (var window in snapshot.Windows)
        {
            _windowHandles.Add(window.Handle);
            _titles[window.Handle] = window.Title;
            _windows.Add(new CoreWindow(window.Handle, ToCore(window.Bounds), window.IsMaximized, window.AppId));
        }

        RebuildTerrain();
        _terrainDirty = false;
    }

    /// <summary>El terreno con las ventanas ya conocidas y los bordes de contenido confirmados.</summary>
    private void RebuildTerrain() =>
        _world = TerrainBuilder.Build(_monitors, _windows, new TerrainOptions { LanderHeightDip = _settings.LanderSize }, _now, _content.Surfaces);

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

        _sensors.CursorVelocity = new Vec2(system.CursorVelocity.X, system.CursorVelocity.Y);
        _sensors.UserIdleSeconds = system.UserIdleSeconds;
        _sensors.LocalTime = system.LocalTime == default ? DateTime.Now : system.LocalTime;
        _sensors.BatteryLow = system.BatteryLow;
        _sensors.PowerSaver = system.PowerSaver;
        _sensors.DarkMode = system.DarkMode;
        _sensors.QuietMode = system.QuietMode;
        _sensors.FullscreenMonitors = system.FullscreenMonitors;
        _sensors.ActiveWindow = _windowHandles.Contains(system.ActiveWindow) ? system.ActiveWindow : 0;
    }

    /// <summary>
    /// Con una aplicación a pantalla completa en su monitor, el lander se muda a otro monitor libre; si no hay ninguno,
    /// aterriza y se esconde. (Con un juego exclusivo o una presentación el host suelta además la GPU.)
    /// </summary>
    private bool UpdateFullscreen()
    {
        var position = _game.Body.Position;
        var monitor = (_world.MonitorAt(position) ?? _world.NearestMonitor(position))?.Index ?? -1;
        var hide = _sensors.FullscreenMonitors.Contains(monitor);
        if (hide && !_dragging && FindFreeGround() is { } ground)
        {
            EndPress();
            _game.Relocate(ground);
            DrainGameEvents();
            _particles.Clear();
            _previousPosition = _game.Body.Position;
            _forceRender = true;
            hide = false;
            _host.Log.Info($"Pantalla completa en el monitor {monitor}: el lander se muda al monitor {ground.MonitorIndex}.");
        }

        if (hide)
        {
            if (!_hidden)
            {
                _hidden = true;
                EndPress();
                _game.SnapToSurface();
                DrainGameEvents();
                HideLander();
                _markers.HideAll();
                _floors.HideAll();
                _particles.Clear();
                ReleaseGameKeys();
                _host.Log.Info("Pantalla completa: el lander se esconde.");
            }

            return true;
        }

        if (_hidden)
        {
            _hidden = false;
            _forceRender = true;
            _accumulator = 0;
            _previousPosition = _game.Body.Position;
            UpdateGameKeys();
        }

        return false;
    }

    /// <summary>Suelo de un monitor sin nada a pantalla completa (el principal primero).</summary>
    private Platform? FindFreeGround()
    {
        foreach (var monitor in _world.Monitors.OrderByDescending(m => m.IsPrimary))
        {
            if (!_sensors.FullscreenMonitors.Contains(monitor.Index) && _world.GroundOf(monitor.Index) is { } ground)
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

        if (e is Sdk.SystemEvent.WheelTurned wheel)
        {
            OnWheel(wheel);
            return;
        }

        // El mundo del host ya está al día cuando llega un suceso: el terreno también tiene que estarlo.
        UpdateTerrain();
        switch (e)
        {
            case Sdk.SystemEvent.WindowOpened opened when _windowHandles.Contains(opened.Window.Handle):
                _game.Notify(new WindowOpened(opened.Window.Handle));
                break;

            case Sdk.SystemEvent.WindowActivated activated when _windowHandles.Contains(activated.Window.Handle):
                _game.Notify(new WindowActivated(activated.Window.Handle));
                break;

            case Sdk.SystemEvent.NotificationShown when _settings.ReactToNotifications && !_sensors.QuietMode && _now >= _notificationCooldown:
                _notificationCooldown = _now + 4;
                _game.Notify(new NotificationArrived());
                break;

            case Sdk.SystemEvent.RecycleBinEmptied emptied:
                // El icono puede no estar (escritorio sin iconos o papelera oculta): entonces, la esquina del principal.
                var primary = _world.PrimaryMonitor;
                Vec2? target = emptied.Icon is { } icon ? new Vec2(icon.X, icon.Y)
                    : primary is null ? null : new Vec2(primary.WorkArea.Left + 50, primary.WorkArea.Top + 50);
                if (target is { } location)
                {
                    _host.Log.Info($"Papelera vaciada (icono en {location}).");
                    _game.Notify(new RecycleBinEmptied(location));
                }

                break;

            case Sdk.SystemEvent.FastScroll scroll:
                Gust(scroll);
                break;

            case Sdk.SystemEvent.ThemeChanged or Sdk.SystemEvent.MonitorsChanged:
                _terrainDirty = true;
                _forceRender = true;
                break;
        }

        DrainGameEvents();
        _host.RequestTick();
    }

    private static CoreRect ToCore(SdkRect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
