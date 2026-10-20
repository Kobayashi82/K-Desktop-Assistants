namespace Lander.Assistant.Audio;

internal enum Sound
{
    EngineLoop,
    LegClick,
    PerfectChime,
    HardThud,
    Explosion,
    Boost,
    Crystal,
    Fanfare,
    Puff,
    Scrape,
    Respawn,
    Pop,
    Whoosh,
}

/// <summary>
/// Sonidos sintetizados al vuelo: cortos, suaves y de volumen bajo. Nada que distraiga en una
/// videollamada.
/// </summary>
internal static class SoundBank
{
    public const int SampleRate = 44100;

    private static readonly Lazy<Dictionary<Sound, float[]>> Samples = new(Generate);

    public static float[] Get(Sound sound) => Samples.Value[sound];

    private static Dictionary<Sound, float[]> Generate()
    {
        var random = new Random(82);
        return new Dictionary<Sound, float[]>
        {
            [Sound.EngineLoop] = EngineLoop(random),
            [Sound.LegClick] = LegClick(random),
            [Sound.PerfectChime] = Chime([880, 1318.5], 0.12, 0.32),
            [Sound.HardThud] = Thud(random),
            [Sound.Explosion] = Explosion(random),
            [Sound.Boost] = Boost(random),
            [Sound.Crystal] = Chime([1318.5, 1661.2, 1975.5], 0.07, 0.28),
            [Sound.Fanfare] = Fanfare(),
            [Sound.Puff] = Puff(random),
            [Sound.Scrape] = Scrape(random),
            [Sound.Respawn] = Sweep(300, 900, 0.35, 0.22),
            [Sound.Pop] = Sweep(1100, 700, 0.06, 0.18),
            [Sound.Whoosh] = Whoosh(random),
        };
    }

    /// <summary>Zumbido grave del motor, en bucle sin costuras.</summary>
    private static float[] EngineLoop(Random random)
    {
        const double seconds = 1.6;
        const double fade = 0.25;
        var total = (int)((seconds + fade) * SampleRate);
        var raw = new float[total];
        double brown = 0, hiss = 0;
        for (var i = 0; i < total; i++)
        {
            var t = i / (double)SampleRate;
            var white = random.NextDouble() * 2 - 1;
            brown = (brown + 0.025 * white) / 1.025;
            hiss += 0.08 * (white - hiss);
            var hum = Math.Sin(Math.Tau * 52 * t) * (0.7 + 0.3 * Math.Sin(Math.Tau * 3 * t));
            raw[i] = (float)(brown * 9 + hum * 0.18 + hiss * 0.08);
        }

        // Fundido cruzado del final con el principio para que el bucle no haga clic.
        var length = (int)(seconds * SampleRate);
        var fadeLength = total - length;
        var loop = new float[length];
        Array.Copy(raw, loop, length);
        for (var i = 0; i < fadeLength; i++)
        {
            var w = i / (double)fadeLength;
            loop[i] = (float)(raw[i] * w + raw[length + i] * (1 - w));
        }

        return Normalize(loop, 0.45);
    }

    private static float[] LegClick(Random random)
    {
        var samples = Buffer(0.07);
        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / (double)SampleRate;
            var env = Math.Exp(-t * 70);
            var tone = Math.Sin(Math.Tau * 2100 * t) + 0.6 * Math.Sin(Math.Tau * 3400 * t) + 0.35 * Math.Sin(Math.Tau * 5200 * t);
            var noise = (random.NextDouble() * 2 - 1) * Math.Exp(-t * 300);
            samples[i] = (float)(env * tone * 0.5 + noise * 0.6);
        }

        return Normalize(samples, 0.35);
    }

    private static float[] Chime(double[] notes, double spacing, double peak)
    {
        var samples = Buffer(spacing * notes.Length + 0.45);
        for (var n = 0; n < notes.Length; n++)
        {
            var start = (int)(n * spacing * SampleRate);
            for (var i = start; i < samples.Length; i++)
            {
                var t = (i - start) / (double)SampleRate;
                var env = Math.Min(1, t / 0.005) * Math.Exp(-t * 7);
                var f = notes[n];
                samples[i] += (float)(env * (Math.Sin(Math.Tau * f * t) + 0.25 * Math.Sin(Math.Tau * f * 2.76 * t) * Math.Exp(-t * 12)));
            }
        }

        return Normalize(samples, peak);
    }

    private static float[] Thud(Random random)
    {
        var samples = Buffer(0.25);
        double phase = 0, low = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / (double)SampleRate;
            var frequency = 45 + 30 * Math.Exp(-t * 20);
            phase += Math.Tau * frequency / SampleRate;
            low += 0.05 * ((random.NextDouble() * 2 - 1) - low);
            samples[i] = (float)(Math.Exp(-t * 16) * (Math.Sin(phase) + low * 2));
        }

        return Normalize(samples, 0.55);
    }

    private static float[] Explosion(Random random)
    {
        var samples = Buffer(0.9);
        double low = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / (double)SampleRate;
            var cutoff = 0.02 + 0.2 * Math.Exp(-t * 6);
            low += cutoff * ((random.NextDouble() * 2 - 1) - low);
            var crackle = random.NextDouble() < 0.002 * Math.Exp(-t * 3) ? random.NextDouble() - 0.5 : 0;
            samples[i] = (float)(Math.Exp(-t * 4.5) * (low * 3 + crackle));
        }

        return Normalize(samples, 0.5);
    }

    private static float[] Boost(Random random)
    {
        var samples = Buffer(0.35);
        double band = 0, phase = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / (double)SampleRate;
            var env = Math.Min(1, t / 0.02) * Math.Exp(-t * 8);
            band += (0.05 + 0.25 * t / 0.35) * ((random.NextDouble() * 2 - 1) - band);
            phase += Math.Tau * (180 + 500 * t / 0.35) / SampleRate;
            samples[i] = (float)(env * (band * 1.5 + Math.Sin(phase) * 0.25));
        }

        return Normalize(samples, 0.4);
    }

    private static float[] Fanfare()
    {
        double[] notes = [523.25, 659.25, 783.99, 1046.5];
        var samples = Buffer(0.11 * 3 + 0.5);
        for (var n = 0; n < notes.Length; n++)
        {
            var start = (int)(n * 0.11 * SampleRate);
            var hold = n == notes.Length - 1 ? 0.45 : 0.13;
            var end = Math.Min(samples.Length, start + (int)(hold * SampleRate));
            for (var i = start; i < end; i++)
            {
                var t = (i - start) / (double)SampleRate;
                var env = Math.Min(1, t / 0.01) * Math.Min(1, (hold - t) / 0.04);
                var phase = notes[n] * t % 1;
                var triangle = 4 * Math.Abs(phase - 0.5) - 1;
                samples[i] += (float)(env * triangle);
            }
        }

        return Normalize(samples, 0.28);
    }

    private static float[] Puff(Random random)
    {
        var samples = Buffer(0.08);
        double previous = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / (double)SampleRate;
            var white = random.NextDouble() * 2 - 1;
            var high = white - previous;
            previous = white;
            samples[i] = (float)(Math.Min(1, t / 0.005) * Math.Exp(-t * 40) * high);
        }

        return Normalize(samples, 0.12);
    }

    private static float[] Scrape(Random random)
    {
        var samples = Buffer(0.2);
        double band = 0, low = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / (double)SampleRate;
            var white = random.NextDouble() * 2 - 1;
            low += 0.3 * (white - low);
            band += 0.6 * (low - band);
            var tremolo = 0.6 + 0.4 * Math.Sin(Math.Tau * 35 * t);
            samples[i] = (float)(Math.Exp(-t * 14) * (low - band) * tremolo);
        }

        return Normalize(samples, 0.22);
    }

    /// <summary>Aire que pasa deprisa: ruido filtrado cuyo tono sube y baja (volteretas, picados).</summary>
    private static float[] Whoosh(Random random)
    {
        const double seconds = 0.5;
        var samples = Buffer(seconds);
        double low = 0, band = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / (double)SampleRate;
            var k = t / seconds;
            var sweep = Math.Sin(Math.PI * k);
            var cutoff = 0.02 + 0.16 * sweep;
            low += cutoff * ((random.NextDouble() * 2 - 1) - low);
            band += cutoff * 0.5 * (low - band);
            samples[i] = (float)(sweep * sweep * (low - band));
        }

        return Normalize(samples, 0.2);
    }

    private static float[] Sweep(double from, double to, double seconds, double peak)
    {
        var samples = Buffer(seconds);
        double phase = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / (double)SampleRate;
            var k = t / seconds;
            phase += Math.Tau * (from + (to - from) * k) / SampleRate;
            var env = Math.Min(1, t / 0.01) * Math.Min(1, (seconds - t) / 0.03);
            samples[i] = (float)(env * Math.Sin(phase));
        }

        return Normalize(samples, peak);
    }

    private static float[] Buffer(double seconds) => new float[(int)(seconds * SampleRate)];

    private static float[] Normalize(float[] samples, double peak)
    {
        var max = 0f;
        foreach (var s in samples)
        {
            max = Math.Max(max, Math.Abs(s));
        }

        if (max > 0)
        {
            var gain = (float)(peak / max);
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] *= gain;
            }
        }

        return samples;
    }
}
