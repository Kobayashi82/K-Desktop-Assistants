using Kumo.Assistant.Audio;

namespace Kumo.Assistant;

/// <summary>Los sonidos de Kumo, con el panorama de donde ocurren.</summary>
public sealed partial class KumoAssistant
{
    private KumoAudio? _audio;

    private void AttachAudio() => _audio = new KumoAudio(_host.Audio);

    /// <summary>Suena <paramref name="sound"/>; a la izquierda o a la derecha según <paramref name="x"/> (píxeles; sin él, donde está).</summary>
    private void Play(Sound sound, double gain = 1, double? x = null)
    {
        var at = x ?? _cloud.Position.X;
        var monitor = _host.World.Current.MonitorAt(new System.Windows.Point(at, _cloud.Position.Y)) ?? Nearest(_cloud.Position);
        var pan = monitor is null ? 0 : (at - monitor.Bounds.Center.X) / Math.Max(1, monitor.Bounds.Width / 2.0) * 0.6;
        _audio?.Play(sound, gain, pan);
    }
}
