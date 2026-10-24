using KDesktop.Assistant.Sdk;
using Kumo.Assistant.Audio;
using Kumo.Assistant.Rendering;
using Kumo.Core.Cloud;
using Kumo.Core.Geometry;

namespace Kumo.Assistant;

/// <summary>
/// Lo que se le puede pedir (extras del menú, del panel y de los comandos) y lo que hace con ello por su cuenta: el arcoíris,
/// la llovizna (a veces con arcoíris al acabar), la gota, la siesta con almohada y bailar. Baila también con la música que
/// suena en el PC (SDK 1.7) si no lo has apagado en sus ajustes.
/// </summary>
public sealed partial class KumoAssistant
{
    internal const string RainbowExtra = "kumo.rainbow";
    internal const string DrizzleExtra = "kumo.drizzle";
    internal const string DropExtra = "kumo.drop";
    internal const string NapExtra = "kumo.nap";
    internal const string DanceExtra = "kumo.dance";

    /// <summary>Lo que dura el arcoíris, lo que tarda en salir entero y en apagarse.</summary>
    private const double RainbowSeconds = 6;
    private const double RainbowGrow = 0.9;
    private const double RainbowFade = 0.9;

    /// <summary>Lo que dura la siesta que le pides (si no lo despiertas antes).</summary>
    private const double NapSeconds = 40;

    /// <summary>Su ritmo cuando le pides bailar sin música.</summary>
    private const double OwnBpm = 112;

    private double _rainbowStart = double.NaN;
    private double _drizzleUntil = double.NaN;
    private bool _rainbowAfterRain;
    private double _napUntil = double.NaN;
    private double _danceStart;
    private double _danceUntil = double.NaN;
    private double _pillow;
    private bool _listening;

    public Task OnExtraAsync(string extraId, CancellationToken ct)
    {
        if (!_started || _hidden || _exitStart is not null)
        {
            return Task.CompletedTask;
        }

        _host.Log.Info($"Extra: {extraId}.");
        switch (extraId)
        {
            case RainbowExtra:
                WakeUp(quiet: true);
                Rainbow();
                break;
            case DrizzleExtra:
                WakeUp(quiet: true);
                Drizzle(6, rainbowAfter: true);
                break;
            case DropExtra:
                WakeUp(quiet: true);
                Act(CloudPose.Drop, 3);
                Play(Sound.Puff, 0.35);
                break;
            case NapExtra:
                Nap(NapSeconds);
                break;
            case DanceExtra:
                WakeUp(quiet: true);
                Dance(9);
                break;
            default:
                _host.Log.Warn($"Extra desconocido: {extraId}");
                break;
        }

        _host.RequestTick();
        return Task.CompletedTask;
    }

    private void Rainbow()
    {
        _rainbowStart = _now;
        Act(CloudPose.Happy, 1.3);
        Play(Sound.Sparkle, 0.4);
    }

    private void Drizzle(double seconds, bool rainbowAfter)
    {
        _drizzleUntil = _now + seconds;
        _rainbowAfterRain = rainbowAfter;
        Play(Sound.Drizzle, 0.3);
    }

    /// <summary>Se echa la siesta (lo duerme <see cref="Autonomy"/>; un clic, arrastrarlo o hablarle lo despiertan).</summary>
    private void Nap(double seconds)
    {
        _napUntil = _now + seconds;
        _host.Log.Debug("Kumo se echa la siesta.");
    }

    private void Dance(double seconds)
    {
        _danceStart = _now;
        _danceUntil = _now + seconds;
    }

    private bool Napping => !double.IsNaN(_napUntil) && _now < _napUntil;

    /// <summary>Cada ciclo: la llovizna, el arcoíris, la almohada y el baile.</summary>
    private void UpdateExtras(double elapsed)
    {
        var raining = !double.IsNaN(_drizzleUntil) && _now < _drizzleUntil;
        if (!raining && !double.IsNaN(_drizzleUntil))
        {
            _drizzleUntil = double.NaN;
            if (_rainbowAfterRain && _exitStart is null)
            {
                Rainbow();
            }
        }

        _cloud.Raining = raining;
        if (!double.IsNaN(_rainbowStart) && _now - _rainbowStart > RainbowSeconds)
        {
            _rainbowStart = double.NaN;
        }

        if (!double.IsNaN(_napUntil) && _now >= _napUntil)
        {
            _napUntil = double.NaN;
        }

        var pillow = _asleep && _cloud.Pose == CloudPose.Sleep ? 1.0 : 0.0;
        _pillow += (pillow - _pillow) * Math.Min(1, elapsed * 3);
        if (Math.Abs(_pillow - pillow) < 0.01)
        {
            _pillow = pillow;
        }

        _cloud.DanceBeat = DanceBeat();
    }

    /// <summary>El golpe en el que va bailando, o null si ahora no baila (ocupado, dormido, volando, en tus manos…).</summary>
    private double? DanceBeat()
    {
        var free = !_asleep && !_dragging && !ManoeuvreActive && Carrying is null && _moduleState == AssistantState.Idle && _speech is null
                   && _cloud.Motion != Motion.Fly && _action is null or CloudPose.Happy;
        if (!free)
        {
            return null;
        }

        if (!double.IsNaN(_danceUntil) && _now < _danceUntil)
        {
            return (_now - _danceStart) * OwnBpm / 60;
        }

        var music = _host.World.Music;
        return _settings.Dance && music.Playing && music.Bpm > 0 ? music.BeatAt(_now) : null;
    }

    /// <summary>Escucha el ritmo de lo que suena solo si baila y se le ve (el medidor del host no gasta nada si nadie escucha).</summary>
    private void UpdateMusic()
    {
        var listen = _settings.Dance && !_hidden && !_host.World.System.QuietMode;
        if (listen != _listening)
        {
            _listening = listen;
            _host.World.ListenToMusic(listen);
        }
    }

    /// <summary>Cómo va el arcoíris (cuánto ha salido y lo que se ve); null si no hay.</summary>
    private (double Grown, double Opacity)? RainbowLook()
    {
        if (double.IsNaN(_rainbowStart))
        {
            return null;
        }

        var t = _now - _rainbowStart;
        var grown = KumoCloud.SmoothStep(0, RainbowGrow, t);
        var opacity = Math.Clamp((RainbowSeconds - t) / RainbowFade, 0, 1);
        return (grown, opacity);
    }

    /// <summary>Hay algo de esto que se mueve (para dibujar a cada fotograma).</summary>
    private bool ExtrasMoving => RainbowLook() is { } r && (r.Grown < 1 || r.Opacity < 1) || _pillow is > 0 and < 1 || _filesHeld is not null;

    /// <summary>Dónde va la almohada: justo debajo de su cuerpo.</summary>
    private Vec2 PillowAt()
    {
        var (_, _, _, bottom) = _cloud.BodyExtent();
        var at = _cloud.RenderPosition;
        return new Vec2(at.X, at.Y + (bottom - 0.12) * _cloud.Unit);
    }

    /// <summary>Lo que ocupan el arcoíris y la almohada (píxeles), para agrandar la superficie.</summary>
    private System.Windows.Rect? ExtrasBounds()
    {
        System.Windows.Rect? box = null;
        if (RainbowLook() is not null)
        {
            box = ExtrasPainter.RainbowBounds(_cloud.RenderPosition, _cloud.Unit);
        }

        if (_pillow > 0.01)
        {
            var pillow = ExtrasPainter.PillowBounds(PillowAt(), _cloud.Unit);
            box = box is { } b ? System.Windows.Rect.Union(b, pillow) : pillow;
        }

        if (CarriedPicture() is { } picture)
        {
            var rect = picture.Rect;
            rect.Inflate(6, 6);
            box = box is { } b ? System.Windows.Rect.Union(b, rect) : rect;
        }

        return box;
    }

    // ---------- lo suyo, a ratos ----------

    private enum Whim { Drop, Puffs, Wiggle, Wave, Drizzle, Rainbow }

    private readonly Variety<Whim> _whims = new Variety<Whim>()
        .Add(Whim.Drop, 1.0)
        .Add(Whim.Puffs, 0.8)
        .Add(Whim.Wiggle, 0.5, cooldown: 60)
        .Add(Whim.Wave, 0.4, cooldown: 120)
        .Add(Whim.Drizzle, 0.25, cooldown: 300)
        .Add(Whim.Rainbow, 0.2, cooldown: 420);

    /// <summary>Hace una de sus cosas (cuando no tiene nada que hacer, cada poco).</summary>
    private void DoWhim()
    {
        var whim = _whims.Pick(w => w != Whim.Wave || _cloud.Cursor is not null);
        _host.Log.Debug($"Kumo: {whim}.");
        switch (whim)
        {
            case Whim.Drop:
                Act(CloudPose.Drop, Random(2.4, 3.6));
                break;
            case Whim.Puffs:
                _cloud.PuffBurst(4, 0.9);
                break;
            case Whim.Wiggle:
                Act(CloudPose.Tickle, 0.9);
                break;
            case Whim.Wave when _cloud.Cursor is { } cursor:
                _cloud.Aim = cursor;
                Act(CloudPose.Wave, 1.3);
                break;
            case Whim.Drizzle:
                Drizzle(Random(3.5, 5), rainbowAfter: System.Random.Shared.NextDouble() < 0.5);
                break;
            case Whim.Rainbow:
                Rainbow();
                break;
        }
    }
}
