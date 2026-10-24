using KDesktop.Assistant.Sdk;

namespace Kumo.Assistant.Audio;

/// <summary>Los sonidos de Kumo.</summary>
internal enum Sound
{
    /// <summary>Una nubecita que sale (aparecer, una sorpresa).</summary>
    Pop,

    /// <summary>Un soplo blando (condensarse, deshacerse).</summary>
    Puff,

    /// <summary>Echa a volar.</summary>
    Whoosh,

    /// <summary>Algo ha ido bien.</summary>
    Chime,

    /// <summary>Una risita (cosquillas).</summary>
    Giggle,

    /// <summary>El dedo de nube pulsa un botón.</summary>
    Boop,

    /// <summary>Destellos que suben (celebración, arcoíris).</summary>
    Sparkle,

    /// <summary>Un poco de lluvia (algo salió mal).</summary>
    Drizzle,

    /// <summary>Un suspiro.</summary>
    Sigh,

    /// <summary>¡Hola! (dos notas que suben).</summary>
    Hello,

    /// <summary>Adiós (dos notas que bajan).</summary>
    Bye,

    /// <summary>Se traga algo (un archivo que le das).</summary>
    Gulp,

    /// <summary>Una tecla.</summary>
    Tap,

    /// <summary>La foto al leer la pantalla.</summary>
    Shutter,

    /// <summary>Una campanilla para llamarte.</summary>
    Bell,
}

/// <summary>
/// Los sonidos de Kumo, hechos en código al cargarlo (cortos, así que no hacen falta archivos): blandos y aireados, a juego con
/// una nube. Suenan con el panorama de donde ocurren.
/// </summary>
internal sealed class KumoAudio
{
    private const int Rate = 44100;

    private readonly IAudio _audio;
    private readonly Dictionary<Sound, ISound> _sounds = [];

    public KumoAudio(IAudio audio)
    {
        _audio = audio;
        var random = new Random(23);
        _sounds[Sound.Pop] = Load(Mix(Sweep(0.08, 520, 1100, 0.32, decay: 34), Noise(random, 0.06, 1800, 900, 0.003, 0.15)));
        _sounds[Sound.Puff] = Load(Noise(random, 0.32, 500, 1400, 0.06, 0.32));
        _sounds[Sound.Whoosh] = Load(Noise(random, 0.34, 420, 2200, 0.1, 0.34));
        _sounds[Sound.Chime] = Load(Mix(Tone(0.35, 988, 0.26, decay: 9), Delay(Tone(0.42, 1480, 0.24, decay: 8), 0.09)));
        _sounds[Sound.Giggle] = Load(Mix(Mix(Sweep(0.07, 900, 1250, 0.18, decay: 16, vibrato: 26), Delay(Sweep(0.07, 960, 1330, 0.17, decay: 16, vibrato: 26), 0.1)), Delay(Sweep(0.08, 1020, 1400, 0.15, decay: 14, vibrato: 26), 0.2)));
        _sounds[Sound.Boop] = Load(Sweep(0.1, 620, 360, 0.36, decay: 26));
        _sounds[Sound.Sparkle] = Load(Mix(Mix(Tone(0.25, 1568, 0.16, decay: 12), Delay(Tone(0.25, 1976, 0.15, decay: 12), 0.07)), Delay(Tone(0.35, 2349, 0.14, decay: 9), 0.14)));
        _sounds[Sound.Drizzle] = Load(Mix(Noise(random, 0.5, 2600, 2000, 0.05, 0.12), Delay(Noise(random, 0.4, 3200, 2400, 0.03, 0.1), 0.12)));
        _sounds[Sound.Sigh] = Load(Mix(Sweep(0.45, 420, 230, 0.12, decay: 3), Noise(random, 0.45, 700, 400, 0.12, 0.18)));
        _sounds[Sound.Hello] = Load(Mix(Tone(0.18, 784, 0.22, decay: 10), Delay(Tone(0.3, 1175, 0.22, decay: 8), 0.13)));
        _sounds[Sound.Bye] = Load(Mix(Tone(0.2, 1175, 0.2, decay: 9), Delay(Tone(0.34, 784, 0.2, decay: 7), 0.15)));
        _sounds[Sound.Gulp] = Load(Sweep(0.13, 200, 460, 0.32, decay: 14));
        _sounds[Sound.Tap] = Load(Noise(random, 0.025, 3500, 3500, 0.001, 0.25));
        _sounds[Sound.Shutter] = Load(Mix(Noise(random, 0.04, 3000, 3000, 0.001, 0.32), Delay(Noise(random, 0.05, 2000, 2000, 0.001, 0.28), 0.07)));
        _sounds[Sound.Bell] = Load(Mix(Tone(0.5, 1319, 0.22, decay: 6), Delay(Tone(0.55, 1760, 0.18, decay: 6), 0.12)));
    }

    /// <summary>
    /// Suena <paramref name="sound"/> con el volumen relativo <paramref name="gain"/>, a la izquierda o a la derecha según
    /// <paramref name="pan"/> (−1…1).
    /// </summary>
    public void Play(Sound sound, double gain = 1, double pan = 0)
    {
        if (_audio.Enabled && _sounds.TryGetValue(sound, out var loaded))
        {
            _audio.Play(loaded, gain, Math.Clamp(pan, -1, 1));
        }
    }

    private ISound Load(float[] samples) => _audio.Load(samples, Rate);

    /// <summary>Un tono que se desliza de <paramref name="from"/> a <paramref name="to"/> Hz y se apaga.</summary>
    private static float[] Sweep(double seconds, double from, double to, double gain, double decay, double vibrato = 0)
    {
        var samples = new float[(int)(seconds * Rate)];
        var phase = 0.0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (double)i / Rate;
            var u = t / seconds;
            var freq = from * Math.Pow(to / from, u) * (1 + 0.04 * Math.Sin(Math.Tau * vibrato * t));
            phase += Math.Tau * freq / Rate;
            samples[i] = (float)(gain * Envelope(t, seconds, 0.004) * Math.Exp(-decay * t) * Math.Sin(phase));
        }

        return samples;
    }

    private static float[] Tone(double seconds, double freq, double gain, double decay)
    {
        var samples = new float[(int)(seconds * Rate)];
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (double)i / Rate;
            var wave = Math.Sin(Math.Tau * freq * t) + 0.25 * Math.Sin(Math.Tau * 2 * freq * t);
            samples[i] = (float)(gain * Envelope(t, seconds, 0.005) * Math.Exp(-decay * t) * wave);
        }

        return samples;
    }

    /// <summary>Ruido pasado por un filtro de banda que va de <paramref name="from"/> a <paramref name="to"/> Hz.</summary>
    private static float[] Noise(Random random, double seconds, double from, double to, double attack, double gain)
    {
        var samples = new float[(int)(seconds * Rate)];
        double low = 0, band = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (double)i / Rate;
            var u = t / seconds;
            var f = 2 * Math.Sin(Math.PI * (from + (to - from) * u) / Rate);
            var input = random.NextDouble() * 2 - 1;

            // Filtro de estado variable (paso banda).
            low += f * band;
            var high = input - low - 0.6 * band;
            band += f * high;
            var shape = Math.Sin(Math.PI * Math.Min(1, t / Math.Max(attack, 1e-4)) / 2) * (1 - u);
            samples[i] = (float)(gain * shape * band);
        }

        return samples;
    }

    private static float[] Mix(float[] a, float[] b)
    {
        var mixed = new float[Math.Max(a.Length, b.Length)];
        for (var i = 0; i < mixed.Length; i++)
        {
            mixed[i] = (i < a.Length ? a[i] : 0) + (i < b.Length ? b[i] : 0);
        }

        return mixed;
    }

    private static float[] Delay(float[] samples, double seconds)
    {
        var offset = (int)(seconds * Rate);
        var delayed = new float[samples.Length + offset];
        Array.Copy(samples, 0, delayed, offset, samples.Length);
        return delayed;
    }

    /// <summary>Entrada y salida suaves (sin chasquidos).</summary>
    private static double Envelope(double t, double seconds, double fade) =>
        Math.Min(1, Math.Min(t / fade, (seconds - t) / fade));
}
