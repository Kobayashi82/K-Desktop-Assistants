using KDesktop.Assistant.Sdk;
using Kumo.Assistant.Audio;
using Kumo.Core.Cloud;
using Kumo.Core.Geometry;
using Sdk = KDesktop.Assistant.Sdk;

namespace Kumo.Assistant;

/// <summary>
/// Lo que hace Kumo con su forma en cada momento: lo que pide el módulo (escuchar, pensar, hablar…) manda; encima, un momento,
/// lo que pasa (una reacción, unas cosquillas, un saludo); y cuando nadie le pide nada, lo suyo: flota, de vez en cuando se
/// condensa en gota, viaja a otro sitio soltando nubecitas y, si llevas un rato sin tocar nada, se duerme.
/// </summary>
public sealed partial class KumoAssistant
{
    /// <summary>Lo que lleva el usuario sin tocar nada para que se duerma.</summary>
    private const double SleepAfter = 180;

    private CloudPose _basePose = CloudPose.Idle;
    private CloudPose? _action;
    private double _actionUntil;
    private double _greetAt = double.NaN;
    private double _nextWhim = double.NaN;
    private double _nextTrip = double.NaN;
    private bool _asleep;

    /// <summary>Lo que hace por su cuenta un momento (vuelve a lo de antes al acabar). Hecho bocadillo, sigue siéndolo.</summary>
    private void Act(CloudPose pose, double seconds)
    {
        _action = pose;
        _actionUntil = _now + seconds;
        _cloud.SetPose(_speech is not null ? CloudPose.Bubble : pose);
        _host.RequestTick();
    }

    private void EndAction()
    {
        _action = null;
        _cloud.SetPose(_speech is not null ? CloudPose.Bubble : _basePose);
    }

    /// <summary>Cada ciclo: la forma que toca y lo suyo cuando no tiene nada que hacer.</summary>
    private void Behave(double elapsed)
    {
        if (_exitStart is not null)
        {
            return;
        }

        if (_action is not null && _now >= _actionUntil)
        {
            EndAction();
        }

        if (!double.IsNaN(_greetAt) && _now >= _greetAt)
        {
            _greetAt = double.NaN;
            _cloud.Aim = _cloud.Cursor ?? _cloud.Position + new Vec2(-200, 0);
            Act(CloudPose.Wave, 1.4);
            Play(Sound.Hello, 0.45);
        }

        // En plena maniobra o mientras lo llevas, no hace nada por su cuenta.
        if (ManoeuvreActive || _dragging || Carrying is not null || !double.IsNaN(_returnAt))
        {
            return;
        }

        var shown = _speech is not null ? CloudPose.Bubble : _action ?? _basePose;
        if (_cloud.Pose != shown)
        {
            _cloud.SetPose(shown);
        }

        if (_moduleState != AssistantState.Idle || _speech is not null)
        {
            WakeUp(quiet: true);
            return;
        }

        Autonomy(elapsed);
    }

    /// <summary>Lo que hace cuando nadie le pide nada.</summary>
    private void Autonomy(double elapsed)
    {
        var lively = _settings.Liveliness;
        var idle = _host.World.System.UserIdleSeconds;
        if (idle > SleepAfter || Napping)
        {
            if (!_asleep)
            {
                _asleep = true;
                _basePose = CloudPose.Sleep;
                if (_action is null)
                {
                    _cloud.SetPose(CloudPose.Sleep);
                }

                _cloud.Hover();
                _host.Log.Debug("Kumo se duerme.");
            }

            return;
        }

        WakeUp(quiet: false);
        if (_action is not null || _cloud.Motion == Motion.Fly)
        {
            if (_cloud.Motion == Motion.Fly && _cloud.Arrived)
            {
                _cloud.Hover();
            }

            return;
        }

        // Lo suyo, a ratos (la gota, nubecitas, un saludo, a veces llovizna o un arcoíris). Bailando con la música, no.
        if (double.IsNaN(_nextWhim))
        {
            _nextWhim = _now + Random(14, 30) / lively;
        }
        else if (_now >= _nextWhim && _cloud.DanceBeat is null)
        {
            _nextWhim = _now + Random(20, 45) / lively;
            DoWhim();
            return;
        }

        // Un viaje a otro sitio, de vez en cuando.
        if (double.IsNaN(_nextTrip))
        {
            _nextTrip = _now + Random(30, 70) / lively;
        }
        else if (_now >= _nextTrip)
        {
            _nextTrip = _now + Random(35, 90) / lively;
            if (PickSpot() is { } spot)
            {
                Travel(spot);
            }
        }
    }

    private void WakeUp(bool quiet)
    {
        if (!_asleep)
        {
            return;
        }

        _asleep = false;
        _napUntil = double.NaN;
        _basePose = CloudPose.Idle;
        if (!quiet)
        {
            Act(CloudPose.Surprise, 0.5);
            Play(Sound.Pop, 0.35);
        }
        else if (_action is null)
        {
            _cloud.SetPose(CloudPose.Idle);
        }

        _host.Log.Debug("Kumo se despierta.");
    }

    /// <summary>Vuela a otro sitio soltando nubecitas.</summary>
    private void Travel(Vec2 spot, double speed = 360)
    {
        _cloud.FlyTo(spot, speed * _unit / BaseUnit);
        Play(Sound.Whoosh, 0.25);
        _host.Log.Debug($"Kumo viaja a {spot.X:0},{spot.Y:0}.");
        _host.RequestTick();
    }

    /// <summary>
    /// Un sitio para flotar en el mismo monitor: cerca de los bordes (no tapa el centro de lo que haces), lejos del cursor, sin
    /// otro asistente quieto y no demasiado cerca de donde está.
    /// </summary>
    private Vec2? PickSpot()
    {
        var world = _host.World.Current;
        var monitor = world.MonitorAt(new System.Windows.Point(_cloud.Position.X, _cloud.Position.Y)) ?? Nearest(_cloud.Position);
        if (monitor is null || _host.World.System.FullscreenMonitors.Contains(monitor.Index))
        {
            return null;
        }

        var (l, t, r, b) = Room(monitor);
        var band = Math.Min(r - l, b - t) * 0.28;
        Vec2? best = null;
        var bestScore = double.MinValue;
        for (var i = 0; i < 24; i++)
        {
            var at = new Vec2(Random(l, r), Random(t, b));

            // Hacia el borde más cercano, dentro de la franja.
            var dl = at.X - l;
            var dr = r - at.X;
            var dt = at.Y - t;
            var db = b - at.Y;
            var nearest = Math.Min(Math.Min(dl, dr), Math.Min(dt, db));
            if (nearest > band)
            {
                if (nearest == dl)
                {
                    at = at.WithX(l + Random(0, band));
                }
                else if (nearest == dr)
                {
                    at = at.WithX(r - Random(0, band));
                }
                else if (nearest == dt)
                {
                    at = at.WithY(t + Random(0, band));
                }
                else
                {
                    at = at.WithY(b - Random(0, band));
                }
            }

            if (IsTaken(at))
            {
                continue;
            }

            var distance = Vec2.Distance(at, _cloud.Position);
            var cursor = _cloud.Cursor is { } c ? Vec2.Distance(at, c) : double.MaxValue;
            var score = -Math.Abs(distance - _unit * 9) + Math.Min(cursor, _unit * 6) * 0.8;
            if (distance > _unit * 2.5 && score > bestScore)
            {
                bestScore = score;
                best = at;
            }
        }

        return best;
    }

    private static double Random(double min, double max) => min + (max - min) * System.Random.Shared.NextDouble();

    /// <summary>Lo que pasa en el PC.</summary>
    private void OnSystemEventBehaviour(Sdk.SystemEvent e)
    {
        var free = !ManoeuvreActive && !_dragging && _moduleState == AssistantState.Idle && _speech is null && !_hidden;
        switch (e)
        {
            case Sdk.SystemEvent.UserReturned when _asleep:
                WakeUp(quiet: false);
                break;
            case Sdk.SystemEvent.NotificationShown n when free && _settings.WatchNotifications && !_host.World.System.QuietMode:
            {
                // Se acerca un poco a ver el aviso.
                var target = new Vec2(n.Bounds.Center.X, n.Bounds.Center.Y);
                var toward = _cloud.Position + (target - _cloud.Position) * 0.35;
                if (_host.World.Current.MonitorAt(new System.Windows.Point(toward.X, toward.Y)) is { } monitor)
                {
                    Travel(Clamp(toward, monitor), 420);
                }

                _cloud.Burst(ParticleKind.Exclamation, new Vec2(_cloud.Position.X, _cloud.Position.Y - _unit));
                Act(CloudPose.Surprise, 0.6);
                break;
            }

            case Sdk.SystemEvent.RecycleBinEmptied when free:
                Act(CloudPose.Happy, 1.4);
                _cloud.Burst(ParticleKind.Sparkle, new Vec2(_cloud.Position.X, _cloud.Position.Y - _unit * 0.4), 8);
                Play(Sound.Sparkle, 0.4);
                break;
            case Sdk.SystemEvent.FastScroll s when free && Vec2.Distance(new Vec2(s.Position.X, s.Position.Y), _cloud.Position) < _unit * 10:
                // Como si el viento lo empujara un poco.
                _cloud.PuffBurst(3, 1.2);
                break;
            case Sdk.SystemEvent.PowerChanged { BatteryLow: true } when free:
                Act(CloudPose.Shrug, 1.6);
                break;
        }
    }
}
