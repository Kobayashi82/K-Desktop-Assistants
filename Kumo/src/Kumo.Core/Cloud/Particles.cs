namespace Kumo.Core.Cloud;

public enum ParticleKind
{
    /// <summary>Estrellita de cuatro puntas que salta y cae.</summary>
    Star,

    /// <summary>Anillo que se abre (el dedo que pulsa, una llamada).</summary>
    Ring,

    /// <summary>Media onda a un lado (escuchando).</summary>
    Wave,

    /// <summary>Una «z» que sube (dormido).</summary>
    Z,

    /// <summary>Gota de lluvia.</summary>
    Rain,

    /// <summary>Niebla blanda (deshacerse, condensarse).</summary>
    Mist,

    /// <summary>Destello pequeño.</summary>
    Sparkle,

    /// <summary>Corchea.</summary>
    Note,

    /// <summary>Corazón.</summary>
    Heart,

    /// <summary>Interrogación.</summary>
    Question,

    /// <summary>Exclamación.</summary>
    Exclamation,
}

/// <summary>Una partícula (píxeles de pantalla).</summary>
public sealed class Particle
{
    public ParticleKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double VX { get; set; }
    public double VY { get; set; }
    public double T { get; set; }
    public double Life { get; set; }
    public double Size { get; set; }

    /// <summary>A qué lado (la onda de escuchar) o cuánto gira.</summary>
    public double Side { get; set; }

    /// <summary>Opacidad de partida.</summary>
    public double Alpha { get; set; } = 1;

    /// <summary>Con la niebla, gris (algo salió mal) en vez del color de la nube.</summary>
    public bool Grey { get; set; }

    public double Fraction => Life <= 0 ? 1 : Math.Clamp(T / Life, 0, 1);
}

/// <summary>Las partículas de Kumo: se mueven solas y se van al acabar su vida.</summary>
public sealed class ParticleSystem
{
    private const int Max = 240;
    private readonly List<Particle> _items = [];

    public IReadOnlyList<Particle> Items => _items;

    public int Count => _items.Count;

    public void Add(Particle p)
    {
        if (_items.Count >= Max)
        {
            _items.RemoveAt(0);
        }

        _items.Add(p);
    }

    public void Clear() => _items.Clear();

    public void Update(double dt, double unit)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var p = _items[i];
            p.T += dt;
            if (p.T >= p.Life)
            {
                _items.RemoveAt(i);
                continue;
            }

            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            switch (p.Kind)
            {
                case ParticleKind.Star or ParticleKind.Sparkle:
                    p.VX *= 1 - 1.6 * dt;
                    p.VY *= 1 - 1.6 * dt;
                    p.VY += 1.4 * unit * dt;
                    break;
                case ParticleKind.Rain:
                    p.VY += 6 * unit * dt;
                    break;
                case ParticleKind.Z or ParticleKind.Note or ParticleKind.Heart:
                    p.X += Math.Sin(p.T * 3 + p.Side) * 0.2 * unit * dt;
                    break;
                case ParticleKind.Mist:
                    p.Size *= 1 + dt * 0.4;
                    p.VX *= 1 - 0.8 * dt;
                    p.VY *= 1 - 0.8 * dt;
                    break;
            }
        }
    }

    /// <summary>Lo que ocupan (píxeles), o null si no hay ninguna.</summary>
    public (double Left, double Top, double Right, double Bottom)? Bounds()
    {
        if (_items.Count == 0)
        {
            return null;
        }

        double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
        foreach (var p in _items)
        {
            var s = p.Kind switch
            {
                ParticleKind.Ring => p.Size * 1.3,
                ParticleKind.Wave => p.Size * 2.2,
                ParticleKind.Rain => p.Size * 1.2,
                _ => p.Size * 1.2,
            } + 3;
            l = Math.Min(l, p.X - s);
            t = Math.Min(t, p.Y - s);
            r = Math.Max(r, p.X + s);
            b = Math.Max(b, p.Y + s);
        }

        return (l, t, r, b);
    }
}
