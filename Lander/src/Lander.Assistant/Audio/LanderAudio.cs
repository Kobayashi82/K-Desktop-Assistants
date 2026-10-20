using KDesktop.Assistant.Sdk;
using Lander.Core.Geometry;

namespace Lander.Assistant.Audio;

/// <summary>
/// Los sonidos del lander sobre el mezclador del host. El host aplica el interruptor y el volumen de los asistentes
/// de K-Desktop; aquí se añade el volumen propio del lander (con la misma curva que el Lander independiente), el
/// zumbido del motor en bucle y la posición: lo que pasa a la izquierda de los monitores suena a la izquierda.
/// </summary>
internal sealed class LanderAudio(IAudio audio) : IDisposable
{
    /// <summary>Constante de tiempo con la que el motor sube y baja (la del mezclador del Lander independiente).</summary>
    private const double EngineResponse = 0.028;

    /// <summary>El panorama nunca va del todo a un lado: es una pista, no un efecto.</summary>
    private const double MaxPan = 0.6;

    private readonly Dictionary<Sound, ISound> _sounds = [];
    private ILoop? _engine;
    private double _engineGain;
    private double _master = Master(0.5);
    private bool _enabled = true;

    /// <summary>El zumbido del motor aún se oye o se está apagando: hay que seguir actualizándolo.</summary>
    public bool EngineAudible => _engineGain > 0.001;

    /// <summary>Ajustes propios del lander: sonido y volumen (0–1).</summary>
    public void Configure(bool enabled, double volume)
    {
        _enabled = enabled;
        _master = Master(volume);
        if (!enabled)
        {
            _engineGain = 0;
            if (_engine is not null)
            {
                _engine.Gain = 0;
            }
        }
    }

    /// <param name="x">Posición horizontal del sonido, para el panorama.</param>
    /// <param name="span">Extremos horizontales de los monitores.</param>
    public void Play(Sound sound, double gain, double x, Interval span)
    {
        if (!_enabled || !audio.Enabled)
        {
            return;
        }

        audio.Play(Get(sound), gain * _master, Pan(x, span));
    }

    /// <summary>Nivel del motor (0 apagado, 1 a fondo); se llama en cada ciclo, que es cuando se suaviza.</summary>
    public void SetEngine(double level, double x, Interval span, double elapsed)
    {
        var target = _enabled ? Math.Clamp(level, 0, 1) * 0.55 : 0;
        _engineGain = elapsed > 0 ? MathUtil.Damp(_engineGain, target, 1 / EngineResponse, elapsed) : target;
        if (_engineGain < 0.0005 && target == 0)
        {
            _engineGain = 0;
        }

        if (_engine is null)
        {
            if (_engineGain == 0 || !audio.Enabled)
            {
                return;
            }

            _engine = audio.Loop(Get(Sound.EngineLoop));
        }

        _engine.Gain = _engineGain * _master;
        _engine.Pitch = 0.94 + 0.12 * Math.Clamp(level, 0, 1);
        _engine.Pan = Pan(x, span);
    }

    public void Dispose()
    {
        _engine?.Dispose();
        _engine = null;
    }

    private ISound Get(Sound sound)
    {
        if (!_sounds.TryGetValue(sound, out var loaded))
        {
            loaded = audio.Load(SoundBank.Get(sound), SoundBank.SampleRate);
            _sounds[sound] = loaded;
        }

        return loaded;
    }

    private static double Pan(double x, Interval span) =>
        span.IsEmpty ? 0 : Math.Clamp((x - (span.Start + span.End) / 2) / (span.Length / 2), -1, 1) * MaxPan;

    private static double Master(double volume)
    {
        var v = Math.Clamp(double.IsFinite(volume) ? volume : 0.5, 0, 1);
        return v * v * 0.9;
    }
}
