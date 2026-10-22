using Kobii.Core;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;

namespace Kobii.Assistant.Rendering;

internal struct Particle
{
    public EmoteKind Kind;
    public Vec2 Position;
    public Vec2 Velocity;
    public double Age;
    public double Life;
    public double Size;
    public double Rotation;
    public double Spin;
    public double Gravity;
    public uint Color;

    /// <summary>Punto alrededor del que da vueltas (las moscas).</summary>
    public Vec2 Anchor;

    public readonly double T => Math.Clamp(Age / Life, 0, 1);

    /// <summary>Aparece rápido y se desvanece al final.</summary>
    public readonly double Alpha => Math.Min(1, Age / 0.12) * (1 - Math.Pow(T, 3));
}

/// <summary>Partículas en coordenadas del mundo (píxeles físicos). Se dibujan con Direct2D.</summary>
/// <param name="random">Con semilla en las vistas previas (la prueba visual necesita que salgan siempre igual).</param>
internal sealed class ParticleSystem(IRandom? random = null)
{
    private const int MaxParticles = 160;
    private static readonly uint[] PaintColors = [0xEF5350, 0x42A5F5, 0xFFCA28, 0x66BB6A, 0xAB47BC];
    private static readonly uint[] LeafColors = [0xE07B39, 0xD4A531, 0xB5532C];
    private static readonly uint[] ConfettiColors = [0xFF5C8A, 0xFFCA28, 0x42A5F5, 0x66BB6A, 0xAB47BC, 0xFF7043, 0x26C6DA];
    private readonly List<Particle> _particles = [];
    private readonly IRandom _random = random ?? new GameRandom();

    public IReadOnlyList<Particle> Items => _particles;

    public int Count => _particles.Count;

    public void Clear() => _particles.Clear();

    /// <summary>Lo que ocupan en el mundo (con su tamaño y un margen para los contornos), o null si no hay ninguna.</summary>
    public (double Left, double Top, double Right, double Bottom)? Bounds()
    {
        if (_particles.Count == 0)
        {
            return null;
        }

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var p in _particles)
        {
            var reach = p.Kind == EmoteKind.Fly ? p.Size * 3 : p.Size * 2.2 + 4;
            left = Math.Min(left, p.Position.X - reach);
            top = Math.Min(top, p.Position.Y - reach);
            right = Math.Max(right, p.Position.X + reach);
            bottom = Math.Max(bottom, p.Position.Y + reach);
        }

        return (left, top, right, bottom);
    }

    public void Spawn(Emote emote, uint shellColor)
    {
        var s = emote.Scale;
        for (var i = 0; i < Math.Max(1, emote.Count) && _particles.Count < MaxParticles; i++)
        {
            var p = new Particle { Kind = emote.Kind, Position = emote.Position, Size = 1, Color = 0xFFFFFF };
            switch (emote.Kind)
            {
                case EmoteKind.Heart:
                    p.Position += new Vec2(_random.Range(-10, 10) * s, 0);
                    p.Velocity = new Vec2(_random.Range(-12, 12) * s, -55 * s);
                    p.Life = 1.6;
                    p.Size = _random.Range(9, 13) * s;
                    p.Color = 0xFF5C8A;
                    break;
                case EmoteKind.Note:
                    p.Velocity = new Vec2(_random.Range(15, 30) * s * _random.Sign(), -45 * s);
                    p.Life = 1.8;
                    p.Size = 15 * s;
                    p.Color = 0x6C8CFF;
                    break;
                case EmoteKind.Sleep:
                    p.Velocity = new Vec2(18 * s, -26 * s);
                    p.Life = 2.6;
                    p.Size = 13 * s;
                    p.Color = 0x8FA3C8;
                    break;
                case EmoteKind.Sparkle:
                    var angle = _random.Range(0, Math.Tau);
                    var speed = _random.Range(40, 110) * s;
                    p.Velocity = new Vec2(Math.Cos(angle), Math.Sin(angle)) * speed;
                    p.Life = _random.Range(0.6, 1.1);
                    p.Size = _random.Range(5, 9) * s;
                    p.Spin = _random.Range(-4, 4);
                    p.Color = _random.Chance(0.5) ? 0xFFD54Fu : 0xFFFFFFu;
                    break;
                case EmoteKind.Question:
                case EmoteKind.Exclamation:
                    p.Velocity = new Vec2(0, -20 * s);
                    p.Life = 1.3;
                    p.Size = 20 * s;
                    p.Color = emote.Kind == EmoteKind.Question ? 0x6C8CFFu : 0xFF7043u;
                    break;
                case EmoteKind.Crumbs:
                    p.Velocity = new Vec2(_random.Range(-50, 50) * s, _random.Range(-90, -40) * s);
                    p.Gravity = 600 * s;
                    p.Life = 0.7;
                    p.Size = _random.Range(1.5, 3) * s;
                    p.Color = 0xD9A066;
                    break;
                case EmoteKind.Warmth:
                    p.Position += new Vec2(_random.Range(-18, 18) * s, _random.Range(-20, 10) * s);
                    p.Velocity = new Vec2(_random.Range(-8, 8) * s, -35 * s);
                    p.Life = 1.1;
                    p.Size = _random.Range(3, 6) * s;
                    p.Color = 0xFFA040;
                    break;
                case EmoteKind.Dust:
                    p.Velocity = new Vec2(_random.Range(-90, 90) * s, _random.Range(-30, -5) * s);
                    p.Life = 0.7;
                    p.Size = _random.Range(4, 8) * s;
                    p.Color = 0xB8B0A8;
                    break;
                case EmoteKind.Shell:
                    var a = _random.Range(-Math.PI * 0.9, -Math.PI * 0.1);
                    var v = _random.Range(120, 260) * s;
                    p.Velocity = new Vec2(Math.Cos(a) * v, Math.Sin(a) * v);
                    p.Gravity = 900 * s;
                    p.Life = 1.2;
                    p.Size = _random.Range(4, 8) * s;
                    p.Spin = _random.Range(-10, 10);
                    p.Color = shellColor;
                    break;
                case EmoteKind.Bubble:
                    p.Position += new Vec2(_random.Range(-8, 8) * s, _random.Range(-8, 8) * s);
                    p.Velocity = new Vec2(_random.Range(-15, 15) * s, _random.Range(-35, -12) * s);
                    p.Life = _random.Range(1.1, 1.8);
                    p.Size = _random.Range(3, 7) * s;
                    p.Color = 0xBFE6FF;
                    break;
                case EmoteKind.Drop:
                    var up = _random.Range(-Math.PI * 0.95, -Math.PI * 0.05);
                    var splash = emote.Count > 1 ? _random.Range(60, 170) * s : 0;
                    p.Velocity = new Vec2(Math.Cos(up) * splash, Math.Sin(up) * splash);
                    p.Gravity = 700 * s;
                    p.Life = emote.Count > 1 ? 0.8 : 1.0;
                    p.Size = _random.Range(2.5, 4) * s;
                    p.Color = 0x64B5F6;
                    break;
                case EmoteKind.Stink:
                    p.Velocity = new Vec2(_random.Range(-4, 4) * s, -20 * s);
                    p.Life = 2.0;
                    p.Size = 8 * s;
                    p.Color = 0x8BC34A;
                    break;
                case EmoteKind.Fly:
                    p.Anchor = emote.Position;
                    p.Rotation = _random.Range(0, Math.Tau);
                    p.Spin = _random.Range(4, 6) * _random.Sign();
                    p.Life = 5;
                    p.Size = 2.2 * s;
                    p.Color = 0x2B2A33;
                    break;
                case EmoteKind.Sneeze:
                    p.Velocity = new Vec2(_random.Range(70, 170) * s * _random.Sign(), _random.Range(-60, 10) * s);
                    p.Gravity = 300 * s;
                    p.Life = 0.5;
                    p.Size = _random.Range(1.5, 3) * s;
                    p.Color = 0xB3E5FC;
                    break;
                case EmoteKind.Flower:
                    p.Position += new Vec2(_random.Range(-16, 16) * s, 0);
                    p.Life = 6;
                    p.Size = 5 * s;
                    p.Rotation = _random.Range(0, Math.Tau);
                    p.Color = _random.Chance(0.5) ? 0xF48FB1u : 0xCE93D8u;
                    break;
                case EmoteKind.Paint:
                    p.Velocity = new Vec2(_random.Range(-40, 40) * s, _random.Range(-60, -10) * s);
                    p.Gravity = 500 * s;
                    p.Life = 0.8;
                    p.Size = _random.Range(2, 3.5) * s;
                    p.Color = _random.Pick(PaintColors);
                    break;
                case EmoteKind.Leaf:
                    // Hojas de otoño que caen meciéndose.
                    p.Position += new Vec2(_random.Range(-22, 22) * s, _random.Range(-12, 12) * s);
                    p.Velocity = new Vec2(_random.Range(-15, 15) * s, _random.Range(14, 26) * s);
                    p.Life = _random.Range(2.6, 3.6);
                    p.Size = _random.Range(4, 6) * s;
                    p.Rotation = _random.Range(0, Math.Tau);
                    p.Spin = _random.Range(-2, 2);
                    p.Color = _random.Pick(LeafColors);
                    break;
                case EmoteKind.Fluff:
                    // Pelusas del cepillo: caen despacio, meciéndose.
                    p.Position += new Vec2(_random.Range(-6, 6) * s, _random.Range(-4, 4) * s);
                    p.Velocity = new Vec2(_random.Range(-25, 25) * s, _random.Range(-20, 5) * s);
                    p.Gravity = 60 * s;
                    p.Life = _random.Range(0.9, 1.5);
                    p.Size = _random.Range(2, 3.5) * s;
                    p.Rotation = _random.Range(0, Math.Tau);
                    p.Color = shellColor;
                    break;
                case EmoteKind.Burst:
                    // Estrella de cómic en el punto del golpe: aparece de golpe y se apaga enseguida.
                    p.Life = 0.38;
                    p.Size = 16 * s;
                    p.Rotation = _random.Range(0, Math.Tau);
                    p.Color = 0xFFD54F;
                    break;
                case EmoteKind.Confetti:
                {
                    // Sale disparado hacia arriba en abanico y luego cae despacio, girando y meciéndose.
                    var fan = -Math.PI / 2 + _random.Range(-0.9, 0.9);
                    var burst = _random.Range(170, 360) * s;
                    p.Position += new Vec2(_random.Range(-10, 10) * s, _random.Range(-6, 6) * s);
                    p.Velocity = new Vec2(Math.Cos(fan) * burst, Math.Sin(fan) * burst);
                    p.Gravity = 420 * s;
                    p.Life = _random.Range(2.2, 3.2);
                    p.Size = _random.Range(3.2, 5) * s;
                    p.Rotation = _random.Range(0, Math.Tau);
                    p.Spin = _random.Range(-7, 7);
                    p.Anchor = new Vec2(_random.Range(6, 12), _random.Range(0, Math.Tau));
                    p.Color = _random.Pick(ConfettiColors);
                    break;
                }

                case EmoteKind.Anger:
                    // La vena de enfado, a un lado de la frente: se queda casi quieta mientras late.
                    p.Position += new Vec2(_random.Range(-4, 4) * s, _random.Range(-3, 3) * s);
                    p.Velocity = new Vec2(0, -6 * s);
                    p.Life = 1.1;
                    p.Size = 8 * s;
                    p.Rotation = _random.Range(-0.3, 0.3);
                    p.Color = 0xF4433B;
                    break;
            }

            _particles.Add(p);
        }
    }

    public void Update(double dt)
    {
        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _particles.RemoveAt(i);
                continue;
            }

            if (p.Kind == EmoteKind.Fly)
            {
                // Da vueltas alrededor de la mascota, con algún quiebro.
                p.Rotation += p.Spin * dt;
                var radius = p.Size * 9;
                p.Position = p.Anchor + new Vec2(Math.Cos(p.Rotation) * radius, Math.Sin(p.Rotation * 1.7) * radius * 0.45 + Math.Sin(p.Age * 13) * p.Size);
                _particles[i] = p;
                continue;
            }

            if (p.Kind == EmoteKind.Confetti)
            {
                // El aire lo frena mucho: tras el disparo cae a poca velocidad, de lado a lado (Anchor: ritmo y fase del vaivén).
                var fall = p.Size * 12;
                p.Velocity = new Vec2(p.Velocity.X * Math.Exp(-dt * 2.2), Math.Min(fall, p.Velocity.Y + p.Gravity * dt));
                p.Position += (p.Velocity + new Vec2(Math.Sin(p.Age * 3.2 + p.Anchor.Y) * p.Size * p.Anchor.X, 0)) * dt;
                p.Rotation += p.Spin * dt;
                _particles[i] = p;
                continue;
            }

            p.Velocity = new Vec2(p.Velocity.X * Math.Exp(-dt * (p.Kind == EmoteKind.Sparkle ? 3 : 0.6)), p.Velocity.Y + p.Gravity * dt);
            p.Position += p.Velocity * dt;
            p.Rotation += p.Spin * dt;
            if (p.Kind is EmoteKind.Heart or EmoteKind.Note or EmoteKind.Bubble or EmoteKind.Stink)
            {
                // Vaivén suave al subir.
                p.Position += new Vec2(Math.Sin(p.Age * 5 + p.Size) * 12 * dt * p.Size / 10, 0);
            }
            else if (p.Kind == EmoteKind.Leaf)
            {
                p.Position += new Vec2(Math.Sin(p.Age * 2.5 + p.Rotation) * p.Size * 7 * dt, 0);
            }

            _particles[i] = p;
        }
    }
}
