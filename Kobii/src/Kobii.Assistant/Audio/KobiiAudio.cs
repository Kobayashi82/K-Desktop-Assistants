using KDesktop.Assistant.Sdk;

namespace Kobii.Assistant.Audio;

/// <summary>Los sonidos de Kobii.</summary>
internal enum Sound
{
    /// <summary>Saca algo (la pelota, las gafas).</summary>
    Pop,

    /// <summary>Lanza algo.</summary>
    Whoosh,

    /// <summary>La pelota da en el botón.</summary>
    Bonk,

    /// <summary>Un saltito.</summary>
    Boing,

    /// <summary>Llama a una ventana silbando.</summary>
    Whistle,

    /// <summary>Una tecla.</summary>
    Tap,

    /// <summary>La foto al leer la pantalla.</summary>
    Shutter,

    /// <summary>Cae de pie (de un salto o al descolgarse).</summary>
    Land,

    /// <summary>Llega arriba de una ventana, o algo ha ido bien.</summary>
    Chime,

    /// <summary>Un tirón al empezar a arrastrar una ventana.</summary>
    Tug,

    /// <summary>La ventana que arrastra se desliza.</summary>
    Slide,

    /// <summary>Un mordisco crujiente.</summary>
    Munch,

    /// <summary>Un trago («glup»).</summary>
    Gulp,

    /// <summary>Una patada a la pelota.</summary>
    Kick,

    /// <summary>La pelota bota.</summary>
    Bounce,

    /// <summary>Una palmada con las manos ahuecadas.</summary>
    Clap,

    /// <summary>¡Achís!</summary>
    Sneeze,

    /// <summary>Una pasada de la escoba.</summary>
    Swish,

    /// <summary>El peluche al apretujarlo.</summary>
    Squeak,
}

/// <summary>
/// Los sonidos de Kobii, hechos en código al cargarlo (ninguno pasa de medio segundo, así que no hacen falta archivos):
/// suaves y cortos, a juego con un bichito blando. Suenan con el panorama de donde ocurren.
/// </summary>
internal sealed class KobiiAudio
{
    private const int Rate = 44100;

    private readonly IAudio _audio;
    private readonly Dictionary<Sound, ISound> _sounds = [];

    public KobiiAudio(IAudio audio)
    {
        _audio = audio;
        var random = new Random(82);
        _sounds[Sound.Pop] = Load(Sweep(0.07, 700, 1500, 0.5, decay: 40));
        _sounds[Sound.Whoosh] = Load(Noise(random, 0.26, from: 600, to: 2600, attack: 0.08, gain: 0.45));
        _sounds[Sound.Bonk] = Load(Mix(Sweep(0.16, 320, 120, 0.7, decay: 22), Noise(random, 0.03, 2500, 2500, 0.002, 0.35)));
        _sounds[Sound.Boing] = Load(Sweep(0.2, 260, 560, 0.35, decay: 12, vibrato: 18));
        _sounds[Sound.Whistle] = Load(Whistle());
        _sounds[Sound.Tap] = Load(Noise(random, 0.025, 3500, 3500, 0.001, 0.3));
        _sounds[Sound.Shutter] = Load(Mix(Noise(random, 0.04, 3000, 3000, 0.001, 0.4), Delay(Noise(random, 0.05, 2000, 2000, 0.001, 0.35), 0.07)));
        _sounds[Sound.Land] = Load(Sweep(0.09, 150, 80, 0.6, decay: 35));
        _sounds[Sound.Tug] = Load(Mix(Sweep(0.12, 210, 130, 0.55, decay: 20), Noise(random, 0.05, 900, 500, 0.003, 0.25)));
        _sounds[Sound.Slide] = Load(Noise(random, 0.45, 350, 1100, 0.12, 0.3));
        _sounds[Sound.Munch] = Load(Mix(Noise(random, 0.05, 2800, 1800, 0.002, 0.4), Delay(Noise(random, 0.04, 2200, 1500, 0.002, 0.3), 0.06)));
        _sounds[Sound.Gulp] = Load(Sweep(0.12, 180, 420, 0.4, decay: 14));
        _sounds[Sound.Kick] = Load(Mix(Sweep(0.1, 260, 110, 0.6, decay: 28), Noise(random, 0.03, 1800, 900, 0.002, 0.3)));
        _sounds[Sound.Bounce] = Load(Sweep(0.08, 340, 200, 0.45, decay: 30));
        _sounds[Sound.Clap] = Load(Noise(random, 0.06, 1600, 1100, 0.001, 0.55));
        _sounds[Sound.Sneeze] = Load(Mix(Sweep(0.18, 520, 900, 0.12, decay: 6), Delay(Noise(random, 0.22, 4200, 1600, 0.004, 0.5), 0.16)));
        _sounds[Sound.Swish] = Load(Noise(random, 0.18, 900, 2600, 0.05, 0.25));
        _sounds[Sound.Squeak] = Load(Mix(Sweep(0.14, 900, 1350, 0.22, decay: 10, vibrato: 30), Delay(Sweep(0.12, 1250, 980, 0.16, decay: 14), 0.1)));
        _sounds[Sound.Chime] = Load(Mix(Tone(0.35, 1046, 0.32, decay: 9), Delay(Tone(0.4, 1568, 0.3, decay: 8), 0.09)));
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

    /// <summary>Un silbido de dos notas («¡fiu-fiuu!»).</summary>
    private static float[] Whistle()
    {
        const double seconds = 0.5;
        var samples = new float[(int)(seconds * Rate)];
        var phase = 0.0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (double)i / Rate;
            var first = t < 0.16;
            var local = first ? t / 0.16 : (t - 0.2) / 0.3;
            var freq = first ? 1300 + 500 * local : 1250 + 650 * Math.Sin(Math.PI * Math.Clamp(local, 0, 1) * 0.6);
            phase += Math.Tau * freq * (1 + 0.006 * Math.Sin(Math.Tau * 6 * t)) / Rate;
            var gate = first ? Math.Sin(Math.PI * local) : t < 0.2 ? 0 : Math.Sin(Math.PI * Math.Clamp(local, 0, 1));
            samples[i] = (float)(0.3 * gate * Math.Sin(phase));
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
