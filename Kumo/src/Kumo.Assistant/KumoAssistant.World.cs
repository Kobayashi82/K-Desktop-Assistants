using KDesktop.Assistant.Sdk;
using Kumo.Assistant.Audio;
using Kumo.Core.Cloud;
using Kumo.Core.Geometry;
using Sdk = KDesktop.Assistant.Sdk;
using SdkRect = KDesktop.Assistant.Sdk.PixelRect;

namespace Kumo.Assistant;

/// <summary>
/// El escritorio que ve Kumo: los monitores (su tamaño depende del escalado de cada uno), dónde puede flotar (dentro del área de
/// trabajo), los otros asistentes, el cursor y lo que hace con una aplicación a pantalla completa.
/// </summary>
public sealed partial class KumoAssistant
{
    private IReadOnlyList<OtherAssistant>? _othersOf;
    private List<SdkRect> _occupied = [];

    private void OnWorldChanged(WorldSnapshot snapshot)
    {
        if (!IsActive())
        {
            _host.RequestTick();
        }
    }

    /// <summary>Donde están quietos los otros asistentes (SDK 1.11): Kumo no se queda encima de ellos (pasar por delante sí).</summary>
    private void UpdateOthers()
    {
        var others = _host.World.Others;
        if (ReferenceEquals(others, _othersOf))
        {
            return;
        }

        _othersOf = others;
        _occupied = [.. others.Where(o => !o.Moving).Select(o => o.Body)];
    }

    /// <summary>Lo que ocuparía su cuerpo con el centro en <paramref name="at"/> (píxeles).</summary>
    private SdkRect BodyAt(Vec2 at, double margin = 0.1)
    {
        var u = UnitAt(at);
        var hw = (1.25 + margin) * u;
        var hh = (0.85 + margin) * u;
        return new SdkRect((int)(at.X - hw), (int)(at.Y - hh), (int)(at.X + hw), (int)(at.Y + hh));
    }

    /// <summary>Hay otro asistente quieto donde se pondría.</summary>
    private bool IsTaken(Vec2 at) => _occupied.Any(o => o.Intersects(BodyAt(at)));

    private void UpdateSensors()
    {
        var system = _host.World.System;
        _cloud.Cursor = _settings.FollowCursor && system.Cursor is { } c ? new Vec2(c.X, c.Y) : null;
        UpdateMusic();
    }

    /// <summary>Píxeles por unidad donde está ahora (sigue al escalado del monitor y al tamaño elegido).</summary>
    private double UnitHere()
    {
        var target = UnitAt(_cloud.Position);
        var current = _unit;

        // Al pasar a un monitor con otro escalado cambia de tamaño poco a poco.
        return Math.Abs(target - current) < 0.05 ? target : current + (target - current) * 0.15;
    }

    private double UnitAt(Vec2 at)
    {
        var world = _host.World.Current;
        var monitor = world.MonitorAt(new System.Windows.Point(at.X, at.Y)) ?? Nearest(at);
        return BaseUnit * (monitor?.DpiScale ?? 1) * _settings.Size;
    }

    private MonitorInfo? Nearest(Vec2 at)
    {
        MonitorInfo? best = null;
        var bestDistance = double.MaxValue;
        foreach (var m in _host.World.Current.Monitors)
        {
            var dx = Math.Max(Math.Max(m.Bounds.Left - at.X, 0), at.X - m.Bounds.Right);
            var dy = Math.Max(Math.Max(m.Bounds.Top - at.Y, 0), at.Y - m.Bounds.Bottom);
            var d = dx * dx + dy * dy;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = m;
            }
        }

        return best;
    }

    /// <summary>Dónde puede estar su centro en ese monitor: dentro del área de trabajo, sin salirse por ningún lado.</summary>
    private (double Left, double Top, double Right, double Bottom) Room(MonitorInfo monitor)
    {
        var u = BaseUnit * monitor.DpiScale * _settings.Size;
        var w = monitor.WorkArea;
        var l = w.Left + 1.4 * u;
        var r = Math.Max(l, w.Right - 1.4 * u);
        var t = w.Top + 1.1 * u;
        var b = Math.Max(t, w.Bottom - 1.0 * u);
        return (l, t, r, b);
    }

    private Vec2 Clamp(Vec2 at, MonitorInfo monitor)
    {
        var (l, t, r, b) = Room(monitor);
        return new Vec2(Math.Clamp(at.X, l, r), Math.Clamp(at.Y, t, b));
    }

    /// <summary>Su sitio de siempre: abajo a la derecha del monitor principal, encima de la barra de tareas (o donde estaba).</summary>
    private Vec2 DefaultSpot()
    {
        var world = _host.World.Current;
        if (!double.IsNaN(_savedSpot.X) && world.MonitorAt(new System.Windows.Point(_savedSpot.X, _savedSpot.Y)) is { } saved
            && !_host.World.System.FullscreenMonitors.Contains(saved.Index))
        {
            return Clamp(_savedSpot, saved);
        }

        var monitor = FreeMonitor() ?? world.PrimaryMonitor!;
        var w = monitor.WorkArea;
        return Clamp(new Vec2(w.Right - w.Width * 0.12, w.Bottom - w.Height * 0.2), monitor);
    }

    /// <summary>Un monitor sin nada a pantalla completa (el principal primero).</summary>
    private MonitorInfo? FreeMonitor()
    {
        var fullscreen = _host.World.System.FullscreenMonitors;
        return _host.World.Current.Monitors.OrderByDescending(m => m.IsPrimary).FirstOrDefault(m => !fullscreen.Contains(m.Index));
    }

    /// <summary>Que no se salga de la pantalla (ni su sitio ni él), salvo mientras lo llevas tú.</summary>
    private void KeepOnScreen()
    {
        if (_dragging || _exitStart is not null || Carrying is not null)
        {
            return;
        }

        var world = _host.World.Current;
        var monitor = world.MonitorAt(new System.Windows.Point(_cloud.Home.X, _cloud.Home.Y)) ?? Nearest(_cloud.Home);
        if (monitor is null)
        {
            return;
        }

        // Lanzado: al llegar al borde de la pantalla se queda ahí (pasa de un monitor a otro si los hay seguidos).
        if (_cloud.Motion == Motion.Glide)
        {
            var inside = Clamp(_cloud.Position, monitor);
            if (Vec2.Distance(inside, _cloud.Position) > 0.5)
            {
                _cloud.StopAt(inside);
                Play(Sound.Puff, 0.25);
            }

            return;
        }

        var clamped = Clamp(_cloud.Home, monitor);
        if (Vec2.Distance(clamped, _cloud.Home) > 0.5)
        {
            _cloud.Home = clamped;
        }

        // Fuera de todos los monitores (se ha quitado uno): reaparece en su sitio.
        if (world.MonitorAt(new System.Windows.Point(_cloud.Position.X, _cloud.Position.Y)) is null && Vec2.Distance(_cloud.Position, clamped) > _cloud.Unit * 6)
        {
            _cloud.Place(clamped);
            _fadeStart = _now;
        }
    }

    /// <summary>
    /// Con una aplicación a pantalla completa en su monitor, Kumo se muda a otro monitor libre (condensándose allí); si no hay
    /// ninguno, se esconde hasta que acabe.
    /// </summary>
    private bool UpdateFullscreen()
    {
        var world = _host.World.Current;
        var monitor = world.MonitorAt(new System.Windows.Point(_cloud.Position.X, _cloud.Position.Y)) ?? Nearest(_cloud.Position);
        var fullscreen = _host.World.System.FullscreenMonitors;
        var hide = monitor is not null && fullscreen.Contains(monitor.Index);
        if (hide && !_dragging && FreeMonitor() is { } free)
        {
            EndPress();
            var w = free.WorkArea;
            var spot = Clamp(new Vec2(w.Right - w.Width * 0.12, w.Bottom - w.Height * 0.2), free);
            _cloud.Particles.Clear();
            _cloud.Puffs.Clear();
            _unit = _cloud.Unit = UnitAt(spot);
            _cloud.Place(spot);
            _cloud.Condense();
            _cloud.Hover();
            _fadeStart = _now;
            hide = false;
            _host.Log.Info($"Pantalla completa en el monitor {monitor!.Index}: Kumo se muda al monitor {free.Index}.");
        }

        if (hide)
        {
            if (!_hidden)
            {
                _hidden = true;
                EndPress();
                _cloud.Particles.Clear();
                HideAll();
                _host.Log.Info("Pantalla completa: Kumo se esconde.");
            }

            return true;
        }

        if (_hidden)
        {
            _hidden = false;
            _fadeStart = _now;
        }

        return false;
    }

    public void OnSystemEvent(Sdk.SystemEvent e)
    {
        if (!_started)
        {
            return;
        }

        OnSystemEventBehaviour(e);
        _host.RequestTick();
    }
}
