using System.Windows;
using System.Windows.Media;
using Lander.Assistant.Rendering.Skins;
using Lander.Core.Geometry;

namespace Lander.Assistant.Rendering;

internal enum ParticleKind : byte
{
    Smoke,
    Dust,
    Spark,
    Debris,
    Confetti,
    Flash,
    Sparkle,
    Heart,
    Puff,
    Ring,

    /// <summary>Columna de luz del teletransporte (al reaparecer).</summary>
    Beam,
}

internal struct Particle
{
    public Vec2 Position;
    public Vec2 Velocity;
    public double Age;
    public double Life;
    public double Size;
    public double Growth;
    public double Rotation;
    public double Spin;
    public double Drag;
    public double Gravity;
    public Color Color;
    public ParticleKind Kind;

    public readonly bool InFront => Kind is ParticleKind.Spark or ParticleKind.Debris or ParticleKind.Confetti or ParticleKind.Sparkle or ParticleKind.Flash or ParticleKind.Heart or ParticleKind.Puff;
}

/// <summary>Colores de la estela del motor.</summary>
internal static class Trails
{
    public static Color SmokeColor(string trail, double time, double seed) => SmokeColorExact(trail, time, Math.Round(seed * 4) / 4);

    private static Color SmokeColorExact(string trail, double time, double seed) => trail switch
    {
        "blue" => Gfx.Lerp(Gfx.Rgb(0xB9DCFF), Gfx.Rgb(0x7DB7FF), seed),
        "rainbow" => FromHue(Math.Round(((time * 0.35 + seed * 0.25) % 1) * 24) / 24),
        "plasma" => Gfx.Lerp(Gfx.Rgb(0xD8A6FF), Gfx.Rgb(0x8B5CF6), seed),
        "gold" => Gfx.Lerp(Gfx.Rgb(0xFFE7A3), Gfx.Rgb(0xF5B942), seed),
        _ => Gfx.Lerp(Gfx.Rgb(0xF1F3F5), Gfx.Rgb(0xC9CED6), seed),
    };

    /// <summary>Color de muestra para el menú de ajustes.</summary>
    public static Color Swatch(string trail) => SmokeColor(trail, 0, 0.4);

    private static Color FromHue(double hue)
    {
        var h = hue * 6;
        var x = 1 - Math.Abs(h % 2 - 1);
        var (r, g, b) = (int)h switch
        {
            0 => (1.0, x, 0.0),
            1 => (x, 1.0, 0.0),
            2 => (0.0, 1.0, x),
            3 => (0.0, x, 1.0),
            4 => (x, 0.0, 1.0),
            _ => (1.0, 0.0, x),
        };

        static byte Channel(double v) => (byte)(255 * (0.35 + 0.65 * v));
        return Color.FromRgb(Channel(r), Channel(g), Channel(b));
    }
}

/// <summary>
/// Partículas en coordenadas del mundo. Se dibujan dentro de la ventana del lander y se desvanecen
/// cerca de sus bordes para que no se vea el recorte.
/// </summary>
internal sealed class ParticleSystem
{
    private const int Capacity = 600;
    private static readonly Color[] ConfettiColors =
        [Gfx.Rgb(0xFF5C8A), Gfx.Rgb(0xFFD23F), Gfx.Rgb(0x3DDC97), Gfx.Rgb(0x4CC9F0), Gfx.Rgb(0xB794F6), Gfx.Rgb(0xFF8C42)];

    private readonly Particle[] _items = new Particle[Capacity];
    private readonly Random _random = new();
    private double _exhaustAccumulator;
    private double _dustAccumulator;
    private double _wispAccumulator;
    private readonly double[] _rcsAccumulators = new double[4];

    public int Count { get; private set; }

    public void Clear() => Count = 0;

    /// <summary>
    /// Caja de todo lo que se ve de las partículas, recortada a <paramref name="window"/> (fuera del fotograma no se
    /// dibujan), o <see cref="Rect.Empty"/> si no hay ninguna.
    /// </summary>
    public Rect Bounds(Rect window)
    {
        var bounds = Rect.Empty;
        for (var i = 0; i < Count; i++)
        {
            ref readonly var p = ref _items[i];
            var reach = p.Kind switch
            {
                ParticleKind.Beam => new Rect(p.Position.X - p.Size * 1.6, p.Position.Y - p.Size * 6.1, p.Size * 3.2, p.Size * 7.4),
                ParticleKind.Spark => new Rect(p.Position.X - p.Size - Math.Abs(p.Velocity.X) * 0.03, p.Position.Y - p.Size - Math.Abs(p.Velocity.Y) * 0.03,
                    2 * (p.Size + Math.Abs(p.Velocity.X) * 0.03), 2 * (p.Size + Math.Abs(p.Velocity.Y) * 0.03)),
                ParticleKind.Ring => new Rect(p.Position.X - p.Size - 1, p.Position.Y - p.Size * 0.2 - 1, p.Size * 2 + 2, p.Size * 0.4 + 2),
                _ => new Rect(p.Position.X - p.Size * 2, p.Position.Y - p.Size * 2, p.Size * 4, p.Size * 4),
            };
            bounds.Union(reach);
        }

        if (!bounds.IsEmpty)
        {
            bounds.Intersect(window);
        }

        return bounds;
    }

    public void Update(double dt)
    {
        for (var i = 0; i < Count; i++)
        {
            ref var p = ref _items[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _items[i] = _items[--Count];
                i--;
                continue;
            }

            p.Velocity = (p.Velocity + new Vec2(0, p.Gravity * dt)) * Math.Max(0, 1 - p.Drag * dt);
            p.Position += p.Velocity * dt;
            p.Size += p.Growth * dt;
            p.Rotation += p.Spin * dt;
        }
    }

    /// <summary>Humo del motor principal.</summary>
    public void Exhaust(Vec2 nozzle, Vec2 direction, Vec2 bodyVelocity, double strength, double scale, string trail, double time, double dt)
    {
        _exhaustAccumulator += dt * 75 * strength;
        while (_exhaustAccumulator >= 1)
        {
            _exhaustAccumulator--;
            var spread = new Vec2(-direction.Y, direction.X) * Range(-25, 25) * scale;
            var seed = _random.NextDouble();
            Emit(new Particle
            {
                Position = nozzle + direction * Range(2, 6) * scale,
                Velocity = bodyVelocity * 0.25 + direction * Range(90, 150) * scale + spread,
                Life = Range(0.45, 0.85),
                Size = Range(2.2, 3.6) * scale,
                Growth = Range(7, 11) * scale,
                Drag = 2.2,
                Gravity = -12 * scale,
                Color = Trails.SmokeColor(trail, time, seed),
                Kind = ParticleKind.Smoke,
            });
        }
    }

    /// <summary>Polvo que levanta el motor cerca de una superficie.</summary>
    public void GroundDust(Vec2 at, double strength, double scale, double dt)
    {
        _dustAccumulator += dt * 45 * strength;
        while (_dustAccumulator >= 1)
        {
            _dustAccumulator--;
            var side = _random.NextDouble() < 0.5 ? -1 : 1;
            Emit(new Particle
            {
                Position = at + new Vec2(Range(-4, 4) * scale, -1),
                Velocity = new Vec2(side * Range(60, 140), Range(-30, -8)) * scale,
                Life = Range(0.4, 0.8),
                Size = Range(1.5, 2.6) * scale,
                Growth = 5 * scale,
                Drag = 2.5,
                Gravity = 10 * scale,
                Color = Gfx.Rgb(0xD9D2C3),
                Kind = ParticleKind.Dust,
            });
        }
    }

    public void DustBurst(Vec2 at, double intensity, double scale)
    {
        var count = (int)(6 + 18 * Math.Clamp(intensity, 0, 1));
        for (var i = 0; i < count; i++)
        {
            var side = i % 2 == 0 ? -1 : 1;
            Emit(new Particle
            {
                Position = at + new Vec2(side * Range(2, 12) * scale, -1),
                Velocity = new Vec2(side * Range(40, 130), Range(-45, -10)) * scale * (0.6 + intensity),
                Life = Range(0.5, 1.0),
                Size = Range(1.6, 3) * scale,
                Growth = 6 * scale,
                Drag = 2.8,
                Gravity = 18 * scale,
                Color = Gfx.Rgb(0xDCD5C6),
                Kind = ParticleKind.Dust,
            });
        }
    }

    public void Sparks(Vec2 at, Vec2 direction, int count, double scale)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = Math.Atan2(direction.Y, direction.X) + Range(-1.1, 1.1);
            var speed = Range(120, 320) * scale;
            Emit(new Particle
            {
                Position = at,
                Velocity = new Vec2(Math.Cos(angle), Math.Sin(angle)) * speed,
                Life = Range(0.25, 0.55),
                Size = Range(0.8, 1.4) * scale,
                Drag = 1.5,
                Gravity = 420 * scale,
                Color = Gfx.Lerp(Gfx.Rgb(0xFFF3B0), Gfx.Rgb(0xFF9F1C), Math.Round(_random.NextDouble() * 3) / 3),
                Kind = ParticleKind.Spark,
            });
        }
    }

    public void Explosion(Vec2 at, double scale, Color[] debrisColors)
    {
        Emit(new Particle { Position = at, Life = 0.35, Size = 26 * scale, Growth = 50 * scale, Color = Gfx.Rgb(0xFFE29A), Kind = ParticleKind.Flash });
        Sparks(at, new Vec2(0, -1), 26, scale);
        for (var i = 0; i < 18; i++)
        {
            var angle = Range(0, Math.Tau);
            var speed = Range(60, 260) * scale;
            Emit(new Particle
            {
                Position = at + new Vec2(Range(-8, 8), Range(-8, 8)) * scale,
                Velocity = new Vec2(Math.Cos(angle) * speed, Math.Sin(angle) * speed - 120 * scale),
                Life = Range(0.8, 1.5),
                Size = Range(1.6, 3.4) * scale,
                Spin = Range(-12, 12),
                Drag = 0.6,
                Gravity = 380 * scale,
                Color = debrisColors[i % debrisColors.Length],
                Kind = ParticleKind.Debris,
            });
        }

        for (var i = 0; i < 14; i++)
        {
            var angle = Range(0, Math.Tau);
            Emit(new Particle
            {
                Position = at,
                Velocity = new Vec2(Math.Cos(angle), Math.Sin(angle) * 0.6) * Range(30, 110) * scale,
                Life = Range(0.9, 1.6),
                Size = Range(4, 7) * scale,
                Growth = 10 * scale,
                Drag = 1.8,
                Gravity = -20 * scale,
                Color = Gfx.Lerp(Gfx.Rgb(0x6B6B6B), Gfx.Rgb(0x3A3A3A), Math.Round(_random.NextDouble() * 3) / 3),
                Kind = ParticleKind.Smoke,
            });
        }
    }

    public void Confetti(Vec2 at, double scale, int count = 26)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = -Math.PI / 2 + Range(-1.2, 1.2);
            var speed = Range(110, 240) * scale;
            Emit(new Particle
            {
                Position = at,
                Velocity = new Vec2(Math.Cos(angle) * speed, Math.Sin(angle) * speed),
                Life = Range(1.0, 1.7),
                Size = Range(1.4, 2.4) * scale,
                Rotation = Range(0, Math.Tau),
                Spin = Range(-10, 10),
                Drag = 1.6,
                Gravity = 160 * scale,
                Color = ConfettiColors[i % ConfettiColors.Length],
                Kind = ParticleKind.Confetti,
            });
        }
    }

    public void Sparkles(Vec2 at, double radius, Color color, int count, double scale)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = Range(0, Math.Tau);
            var distance = Range(0.2, 1) * radius;
            Emit(new Particle
            {
                Position = at + new Vec2(Math.Cos(angle), Math.Sin(angle)) * distance,
                Velocity = new Vec2(Math.Cos(angle), Math.Sin(angle)) * Range(10, 40) * scale,
                Life = Range(0.5, 1.0),
                Size = Range(2, 3.5) * scale,
                Drag = 1,
                Gravity = -8 * scale,
                Color = color,
                Kind = ParticleKind.Sparkle,
            });
        }
    }

    /// <summary>Gas frío de un propulsor de giro o traslación: un chorrito blanco que se disipa enseguida.</summary>
    public void Rcs(int jet, Vec2 at, Vec2 outward, Vec2 bodyVelocity, double strength, double scale, double dt)
    {
        ref var accumulator = ref _rcsAccumulators[Math.Clamp(jet, 0, _rcsAccumulators.Length - 1)];
        accumulator += dt * 38 * Math.Clamp(strength, 0, 1);
        while (accumulator >= 1)
        {
            accumulator--;
            var spread = new Vec2(-outward.Y, outward.X) * Range(-18, 18) * scale;
            Emit(new Particle
            {
                Position = at,
                Velocity = bodyVelocity * 0.6 + outward * Range(70, 120) * scale + spread,
                Life = Range(0.22, 0.38),
                Size = Range(1.1, 1.7) * scale,
                Growth = 7 * scale,
                Drag = 5,
                Color = Gfx.Rgb(0xF4F7FB),
                Kind = ParticleKind.Puff,
            });
        }
    }

    /// <summary>Anillo de polvo que se abre sobre la superficie al tomar tierra.</summary>
    public void Ring(Vec2 at, double scale, double intensity)
    {
        intensity = Math.Clamp(intensity, 0, 1.5);
        Emit(new Particle
        {
            Position = at,
            Life = 0.45 + 0.2 * intensity,
            Size = 7 * scale,
            Growth = (55 + 90 * intensity) * scale,
            Color = Gfx.Rgb(0xDCD5C6),
            Kind = ParticleKind.Ring,
        });
    }

    /// <summary>Bocanada de humo en la tobera al apagar el motor de golpe (antes de una caída libre).</summary>
    public void EnginePuff(Vec2 nozzle, Vec2 direction, Vec2 bodyVelocity, double scale, string trail, double time)
    {
        for (var i = 0; i < 8; i++)
        {
            var spread = new Vec2(-direction.Y, direction.X) * Range(-34, 34) * scale;
            Emit(new Particle
            {
                Position = nozzle + direction * Range(1, 5) * scale,
                Velocity = bodyVelocity * 0.7 + direction * Range(20, 55) * scale + spread,
                Life = Range(0.55, 0.95),
                Size = Range(2.2, 3.4) * scale,
                Growth = Range(9, 13) * scale,
                Drag = 3,
                Gravity = -10 * scale,
                Color = Trails.SmokeColor(trail, time, _random.NextDouble()),
                Kind = ParticleKind.Smoke,
            });
        }
    }

    /// <summary>Fogonazo en la tobera al encender el motor a fondo de golpe.</summary>
    public void NozzleFlash(Vec2 nozzle, Vec2 direction, double scale)
    {
        Emit(new Particle
        {
            Position = nozzle + direction * 7 * scale,
            Life = 0.24,
            Size = 8 * scale,
            Growth = 46 * scale,
            Color = Gfx.Rgb(0xFFD58A),
            Kind = ParticleKind.Flash,
        });
    }

    /// <summary>Columna de luz al reaparecer, como un teletransporte.</summary>
    public void Beam(Vec2 at, double scale)
    {
        Emit(new Particle
        {
            Position = at,
            Life = 1.1,
            Size = 24 * scale,
            Color = Gfx.Rgb(0x7DE3FF),
            Kind = ParticleKind.Beam,
        });
    }

    /// <summary>Corazones que suben balanceándose (le han acariciado).</summary>
    public void Hearts(Vec2 at, double scale, int count = 5)
    {
        for (var i = 0; i < count; i++)
        {
            Emit(new Particle
            {
                Position = at + new Vec2(Range(-12, 12), Range(-4, 4)) * scale,
                Velocity = new Vec2(Range(-14, 14), Range(-46, -28)) * scale,
                Life = Range(1.0, 1.5),
                Size = Range(2.6, 3.8) * scale,
                Rotation = Range(0, Math.Tau),
                Spin = Range(2.5, 4),
                Drag = 0.6,
                Gravity = -6 * scale,
                Color = i % 3 == 0 ? Gfx.Rgb(0xFF8FB3) : Gfx.Rgb(0xFF5C8A),
                Kind = ParticleKind.Heart,
            });
        }
    }

    /// <summary>Humo de averías cuando el lander está muy dañado.</summary>
    public void DamageWisp(Vec2 at, double damage, double scale, double dt)
    {
        _wispAccumulator += dt * 8 * Math.Clamp((damage - 0.45) / 0.55, 0, 1);
        while (_wispAccumulator >= 1)
        {
            _wispAccumulator--;
            Emit(new Particle
            {
                Position = at + new Vec2(Range(-6, 6), Range(-4, 4)) * scale,
                Velocity = new Vec2(Range(-8, 8), Range(-30, -15)) * scale,
                Life = Range(0.8, 1.4),
                Size = Range(2, 3) * scale,
                Growth = 5 * scale,
                Drag = 0.8,
                Color = Gfx.Rgb(0x6B7280),
                Kind = ParticleKind.Smoke,
            });
        }
    }

    /// <summary>Dibuja las partículas de detrás (humo, polvo) o de delante (chispas, trozos...).</summary>
    public void Draw(DrawingContext dc, Rect window, bool front)
    {
        var center = new Vec2(window.X + window.Width / 2, window.Y + window.Height / 2);
        var radius = Math.Min(window.Width, window.Height) / 2;
        for (var i = 0; i < Count; i++)
        {
            ref readonly var p = ref _items[i];
            if (p.InFront != front)
            {
                continue;
            }

            var t = p.Age / p.Life;
            if (p.Kind == ParticleKind.Beam)
            {
                DrawBeam(dc, p, t);
                continue;
            }

            var edge = Math.Clamp((1 - Vec2.Distance(p.Position, center) / radius) / 0.22, 0, 1);
            if (edge <= 0)
            {
                continue;
            }

            var position = new Point(p.Position.X, p.Position.Y);
            switch (p.Kind)
            {
                case ParticleKind.Smoke:
                    dc.DrawEllipse(LanderParts.Glow(p.Color, Math.Pow(1 - t, 1.4) * 0.6 * edge), null, position, p.Size, p.Size);
                    break;

                case ParticleKind.Dust:
                    dc.DrawEllipse(LanderParts.Glow(p.Color, (1 - t) * 0.55 * edge), null, position, p.Size, p.Size * 0.8);
                    break;

                case ParticleKind.Spark:
                {
                    var tail = p.Velocity * -0.025;
                    dc.DrawLine(Gfx.Pen(p.Color.WithAlpha((1 - t) * edge), p.Size), position, new Point(position.X + tail.X, position.Y + tail.Y));
                    break;
                }

                case ParticleKind.Debris:
                    DrawRotatedRect(dc, position, p.Size * 1.4, p.Size, p.Rotation, p.Color.WithAlpha(Math.Min(1, (1 - t) * 3) * edge));
                    break;

                case ParticleKind.Confetti:
                    DrawRotatedRect(dc, position, p.Size * 1.8, p.Size * Math.Abs(Math.Cos(p.Rotation * 1.7)) + 0.3, p.Rotation, p.Color.WithAlpha(Math.Min(1, (1 - t) * 3) * edge));
                    break;

                case ParticleKind.Flash:
                    dc.DrawEllipse(LanderParts.Glow(p.Color, (1 - t) * 0.95 * edge), null, position, p.Size, p.Size);
                    break;

                case ParticleKind.Sparkle:
                    DrawSparkle(dc, position, p.Size * (1 - t * 0.5), p.Color.WithAlpha((1 - t) * edge));
                    break;

                case ParticleKind.Puff:
                    dc.DrawEllipse(LanderParts.Glow(p.Color, Math.Pow(1 - t, 1.5) * 0.7 * edge), null, position, p.Size, p.Size);
                    break;

                case ParticleKind.Ring:
                {
                    var alpha = (1 - t) * (1 - t) * 0.6 * edge;
                    dc.DrawEllipse(null, Gfx.Pen(p.Color.WithAlpha(alpha), Math.Max(0.6, p.Size * 0.06) * (1 - t * 0.5)), position, p.Size, p.Size * 0.18);
                    break;
                }

                case ParticleKind.Heart:
                {
                    // Aparece con un pequeño salto, se balancea y se desvanece al final.
                    var grow = Math.Min(1, t * 6);
                    var sway = Math.Sin(p.Rotation) * p.Size * 0.6;
                    var alpha = Math.Min(1, (1 - t) * 2.5) * edge;
                    DrawHeart(dc, new Point(position.X + sway, position.Y), p.Size * (0.6 + 0.4 * grow), p.Color.WithAlpha(alpha));
                    break;
                }
            }
        }
    }

    /// <summary>
    /// La columna de luz: un halo alargado y tenue con un núcleo estrecho y brillante que se enciende de golpe
    /// y se apaga estrechándose, más un destello en el suelo. Los halos usan el pincel radial compartido, así
    /// que se difuminan por todos los lados sin crear pinceles nuevos.
    /// </summary>
    private static void DrawBeam(DrawingContext dc, in Particle p, double t)
    {
        var alpha = Math.Min(1, t * 8) * Math.Pow(1 - t, 1.6);
        if (alpha < 0.01)
        {
            return;
        }

        var width = p.Size * (1 - 0.55 * t);
        var middle = new Point(p.Position.X, p.Position.Y - p.Size * 1.8);
        dc.DrawEllipse(LanderParts.Glow(p.Color, 0.55 * alpha), null, middle, width * 1.1, p.Size * 4.2);
        dc.DrawEllipse(LanderParts.Glow(Gfx.Rgb(0xE6FBFF), 0.9 * alpha), null, middle, width * 0.32, p.Size * 3.8);
        dc.DrawEllipse(LanderParts.Glow(p.Color, 0.7 * alpha), null, new Point(p.Position.X, p.Position.Y + p.Size * 0.95), width * 1.4, p.Size * 0.28);
    }

    private void Emit(in Particle particle)
    {
        if (Count < Capacity)
        {
            _items[Count++] = particle;
        }
    }

    private double Range(double min, double max) => min + (max - min) * _random.NextDouble();

    private static void DrawRotatedRect(DrawingContext dc, Point center, double width, double height, double rotation, Color color)
    {
        dc.PushTransform(new RotateTransform(rotation * 180 / Math.PI, center.X, center.Y));
        dc.DrawRectangle(Gfx.Brush(color), null, new Rect(center.X - width / 2, center.Y - height / 2, width, height));
        dc.Pop();
    }

    public static void DrawHeart(DrawingContext dc, Point center, double size, Color color)
    {
        var s = size;
        var heart = new StreamGeometry();
        using (var context = heart.Open())
        {
            context.BeginFigure(new Point(center.X, center.Y + s * 0.9), true, true);
            context.BezierTo(new Point(center.X - s * 1.3, center.Y), new Point(center.X - s * 0.9, center.Y - s * 1.1), new Point(center.X, center.Y - s * 0.35), true, true);
            context.BezierTo(new Point(center.X + s * 0.9, center.Y - s * 1.1), new Point(center.X + s * 1.3, center.Y), new Point(center.X, center.Y + s * 0.9), true, true);
        }

        heart.Freeze();
        dc.DrawGeometry(Gfx.Brush(color), null, heart);
    }

    public static void DrawSparkle(DrawingContext dc, Point center, double size, Color color)
    {
        var s = size;
        var star = Gfx.Polygon(
            center.X, center.Y - s,
            center.X + s * 0.22, center.Y - s * 0.22,
            center.X + s, center.Y,
            center.X + s * 0.22, center.Y + s * 0.22,
            center.X, center.Y + s,
            center.X - s * 0.22, center.Y + s * 0.22,
            center.X - s, center.Y,
            center.X - s * 0.22, center.Y - s * 0.22);
        dc.DrawGeometry(Gfx.Brush(color), null, star);
    }
}
