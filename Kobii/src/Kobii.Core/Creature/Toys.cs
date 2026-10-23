using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Creature;

/// <summary>Los juguetes que se le pueden dar.</summary>
public enum ToyKind
{
    /// <summary>Una pelota de colores que bota y rueda: la patea, la persigue y te la trae.</summary>
    Ball,

    /// <summary>Su peluche: lo abraza, lo lleva de un lado a otro y duerme con él.</summary>
    Plush,

    /// <summary>Una camita: duerme mejor en ella y, si está contento, bota encima.</summary>
    Bed,
}

/// <summary>
/// Un juguete en el escritorio: un cuerpo con física (la pelota bota y rueda, el peluche cae blandito, la cama pesa) que
/// se puede coger y lanzar con el ratón y que Kobii coge, lleva y usa. Es el de Kobii AI con una sola mascota.
/// </summary>
public sealed class Toy
{
    public Toy(ToyKind kind, double scale)
    {
        Kind = kind;
        Body = kind switch
        {
            ToyKind.Ball => new CreatureBody(BallDiameterDip, scale) { Restitution = 0.62, MinBounceDip = 110, FrictionDip = 150, WallBounciness = 0.75 },
            ToyKind.Plush => new CreatureBody(PlushHeightDip, scale) { WidthFactor = 0.42, Restitution = 0.22, MinBounceDip = 260, FrictionDip = 1300 },
            _ => new CreatureBody(BedHeightDip, scale) { WidthFactor = BedWidthDip / BedHeightDip / 2, FrictionDip = 2600, WallBounciness = 0.2 },
        };
    }

    public const double BallDiameterDip = 22;
    public const double PlushHeightDip = 30;
    public const double BedHeightDip = 26;
    public const double BedWidthDip = 100;

    public ToyKind Kind { get; }

    public CreatureBody Body { get; }

    /// <summary>Giro (radianes): la pelota rueda y el peluche da volteretas en el aire.</summary>
    public double Spin { get; set; }

    /// <summary>Giro en el paso anterior (para dibujar entre dos pasos).</summary>
    public double PreviousSpin { get; set; }

    /// <summary>Pies en el paso anterior.</summary>
    public Vec2 PreviousFeet { get; set; }

    /// <summary>Aplastamiento al botar o al tirarse encima (1 normal); vuelve solo con un muelle.</summary>
    public double Squash { get; private set; } = 1;

    /// <summary>Lo tiene Kobii en las manos (o abrazado mientras duerme).</summary>
    public bool Held { get; set; }

    /// <summary>Lo ha sacado él para jugar un rato: si se queda tirado, desaparece (no es tuyo).</summary>
    public bool Ephemeral { get; init; }

    /// <summary>Lo tienes cogido con el ratón.</summary>
    public bool Grabbed => Body.IsHeld && !Held;

    /// <summary>Centro mientras una mascota lo tiene en las manos (en su espacio).</summary>
    public Vec2 HeldLocal { get; set; }

    public Vec2 PreviousHeldLocal { get; set; }

    /// <summary>Inclinación en las manos (radianes).</summary>
    public double HeldTilt { get; set; }

    /// <summary>Lo acabas de lanzar tú (para jugar a lanzar y traer).</summary>
    public bool ThrownByYou { get; set; }

    /// <summary>Dónde estaba tu cursor al lanzarlo (allí te lo devuelve).</summary>
    public Vec2 ThrowOrigin { get; set; }

    /// <summary>Segundos que lleva quieto en el suelo.</summary>
    public double RestSeconds { get; private set; }

    public double Radius => Body.Height / 2;

    public Vec2 Center => Body.Feet - new Vec2(0, Body.Height / 2);

    /// <summary>Libre en el suelo o cayendo (ni en unas manos ni cogido con el ratón).</summary>
    public bool Free => !Held && !Body.IsHeld;

    /// <summary>Altura del colchón de la cama sobre el suelo (en píxeles): ahí se tumba quien duerma en ella.</summary>
    public double BedLift => Body.Height * 0.35;

    private double _squashVelocity;

    /// <summary>Impulso al muelle del aplastamiento (negativo aplasta).</summary>
    public void Bounce(double impulse) => _squashVelocity += impulse;

    /// <summary>Un paso de física (si nadie lo tiene en las manos).</summary>
    public Landing? Step(World world, double dt)
    {
        PreviousFeet = Body.Feet;
        PreviousSpin = Spin;
        PreviousHeldLocal = HeldLocal;
        _squashVelocity += (-(Squash - 1) * 300 - _squashVelocity * 13) * dt;
        Squash = Math.Clamp(Squash + _squashVelocity * dt, 0.5, 1.5);
        if (Held)
        {
            return null;
        }

        var landing = Body.Step(world, dt);
        var vx = Body.Velocity.X;
        RestSeconds = Body.IsGrounded && Math.Abs(vx) < 5 * Body.Scale ? RestSeconds + dt : 0;
        if (RestSeconds > 8)
        {
            // Si nadie ha ido a por ella, ya no cuenta como lanzada.
            ThrownByYou = false;
        }
        switch (Kind)
        {
            case ToyKind.Ball:
                // Rueda sin resbalar (y en el aire sigue girando).
                Spin += vx * dt / Math.Max(1, Radius);
                break;
            case ToyKind.Plush when !Body.IsGrounded && !Body.IsHeld:
                // Da volteretas en el aire…
                Spin += vx * dt / Math.Max(1, Radius * 2.2);
                break;
            case ToyKind.Plush:
                // …y se queda sentado al posarse.
                Spin = MathUtil.Damp(MathUtil.WrapAngle(Spin), 0, 9, dt);
                break;
        }

        if (landing is { } land)
        {
            var speed = land.Speed / Math.Max(0.1, Body.Scale);
            Bounce(-Math.Clamp(speed / (Kind == ToyKind.Ball ? 900 : 500), 0.2, Kind == ToyKind.Bed ? 1.5 : 3));
        }

        return landing;
    }

    /// <summary>Lo suelta Kobii (o se le cae) en un punto, con algo de velocidad.</summary>
    public void Drop(Vec2 center, Vec2 velocity)
    {
        Held = false;
        HeldTilt = 0;
        Body.Feet = center + new Vec2(0, Body.Height / 2);
        PreviousFeet = Body.Feet;
        Body.Launch(velocity);
    }

    /// <summary>Lo deja quieto en una plataforma.</summary>
    public void PlaceOn(Platform platform, double x)
    {
        Held = false;
        HeldTilt = 0;
        Body.PlaceOn(platform, x);
        PreviousFeet = Body.Feet;
    }
}

/// <summary>Algo que les ha pasado a los juguetes (para los efectos).</summary>
public readonly record struct ToyEvent(Toy Toy, Landing Landing);

/// <summary>
/// Los juguetes que hay en el escritorio. Lleva su física: caen, botan, ruedan y siguen a las ventanas en las que están.
/// </summary>
public sealed class Playground
{
    private readonly List<Toy> _toys = [];
    private readonly List<ToyEvent> _events = [];

    public IReadOnlyList<Toy> Toys => _toys;

    /// <summary>Multiplicador del tamaño (el de Kobii).</summary>
    public double Size { get; set; } = 1;

    public Toy? Find(ToyKind kind) => _toys.FirstOrDefault(t => t.Kind == kind);

    /// <summary>Saca un juguete (cae desde un poco más arriba de <paramref name="near"/>). Si ya estaba, lo devuelve.</summary>
    public Toy Add(ToyKind kind, World world, Vec2 near)
    {
        if (Find(kind) is { } existing)
        {
            return existing;
        }

        var monitor = world.MonitorAt(near) ?? world.NearestMonitor(near);
        var toy = new Toy(kind, Size * (monitor?.DpiScale ?? 1));
        var top = monitor?.Bounds.Top ?? near.Y - 400;
        toy.Body.Feet = new Vec2(near.X, Math.Max(top + toy.Body.Height + 4, near.Y - 260 * toy.Body.Scale));
        toy.PreviousFeet = toy.Body.Feet;
        toy.Body.Velocity = new Vec2(0, -100 * toy.Body.Scale);
        _toys.Add(toy);
        return toy;
    }

    /// <summary>Pone en el escritorio un juguete que ya existe (el que saca Kobii de detrás de la espalda). Sustituye al del mismo tipo.</summary>
    public void Adopt(Toy toy)
    {
        _toys.RemoveAll(t => t.Kind == toy.Kind);
        _toys.Add(toy);
    }

    /// <summary>Guarda un juguete (si Kobii lo tenía en las manos, lo suelta).</summary>
    public bool Remove(ToyKind kind)
    {
        if (Find(kind) is not { } toy)
        {
            return false;
        }

        _toys.Remove(toy);
        return true;
    }

    /// <summary>Un paso de física de todos los juguetes.</summary>
    public void Step(World world, double dt)
    {
        foreach (var toy in _toys)
        {
            var monitor = world.MonitorAt(toy.Center) ?? world.NearestMonitor(toy.Center);
            toy.Body.ScaleToward(Size * (monitor?.DpiScale ?? 1), dt);
            if (toy.Step(world, dt) is { } landing)
            {
                _events.Add(new ToyEvent(toy, landing));
            }
        }
    }

    /// <summary>¿Se mueve algún juguete (hay que dibujar a la frecuencia alta)?</summary>
    public bool IsMoving => _toys.Any(t => !t.Body.IsGrounded || Math.Abs(t.Body.Velocity.X) > 1 || Math.Abs(t.Squash - 1) > 0.004);

    public void DrainEvents(List<ToyEvent> target)
    {
        target.AddRange(_events);
        _events.Clear();
    }
}
