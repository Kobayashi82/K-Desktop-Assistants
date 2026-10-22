using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Creature;

public enum BodyState
{
    /// <summary>De pie sobre una plataforma (barra de tareas o ventana).</summary>
    Grounded,

    /// <summary>Cayendo o lanzado por el aire.</summary>
    Airborne,

    /// <summary>Cogido con el ratón.</summary>
    Held,

    /// <summary>Agarrado al lado de una ventana, trepando: lo mueve el comportamiento, sin gravedad.</summary>
    Climbing,
}

/// <summary>Cómo ha tocado el suelo, para la animación y los efectos.</summary>
/// <param name="Bounced">Ha rebotado (una pelota): sigue en el aire.</param>
public readonly record struct Landing(Vec2 Point, double Speed, Platform Platform, bool Bounced = false);

/// <summary>
/// Cuerpo físico de Kobii (o de cualquier objeto que cae, como la comida). La posición es la de los pies
/// (centro de la base), en píxeles físicos. Sobre una ventana la sigue cuando se mueve; si la ventana
/// desaparece, se cae. Las magnitudes están en DIP y se multiplican por <see cref="Scale"/>.
/// </summary>
public sealed class CreatureBody
{
    public const double GravityDip = 1900;
    private const double MaxFallSpeedDip = 1800;
    public const double AirDrag = 0.35;
    private const double SlideFrictionDip = 1600;
    private const double WallBounce = 0.45;
    private const double TeleportDip = 250;

    private Vec2 _supportOrigin;

    public CreatureBody(double heightDip, double scale)
    {
        HeightDip = heightDip;
        Scale = scale;
    }

    public Vec2 Feet { get; set; }

    public Vec2 Velocity { get; set; }

    public BodyState State { get; private set; } = BodyState.Airborne;

    /// <summary>Dueño de la plataforma en la que está (HWND o identificador del suelo).</summary>
    public long SupportId { get; private set; }

    public Platform? Support { get; private set; }

    /// <summary>Altura a tamaño normal; la real es <see cref="Height"/>.</summary>
    public double HeightDip { get; set; }

    /// <summary>Escala total: tamaño elegido × DPI del monitor.</summary>
    public double Scale { get; set; }

    /// <summary>Lo que cambia la escala por segundo al pasar a un monitor con otro escalado (del 100 al 150 %, en 0,2 s).</summary>
    public const double ScaleRate = 2.5;

    /// <summary>
    /// Va hacia la escala que toca donde está (la del monitor): al cruzar a otro con distinto escalado crece o
    /// encoge en un momento, desde los pies, en vez de cambiar de golpe. Con <paramref name="instant"/> (acaba de
    /// aparecer ahí), al momento.
    /// </summary>
    public void ScaleToward(double target, double dt, bool instant = false) =>
        Scale = instant || Scale <= 0 ? target : MathUtil.MoveTowards(Scale, target, ScaleRate * dt);

    public double Height => HeightDip * Scale;

    /// <summary>Media anchura respecto a la altura (0,5: tan ancho como alto).</summary>
    public double WidthFactor { get; init; } = 0.5;

    /// <summary>Lo que rebota al caer (0 nada; una pelota, más de 0,5).</summary>
    public double Restitution { get; init; }

    /// <summary>Velocidad mínima (DIP/s) para rebotar en vez de posarse.</summary>
    public double MinBounceDip { get; init; } = 140;

    /// <summary>Frenada al deslizarse o rodar por el suelo (DIP/s²).</summary>
    public double FrictionDip { get; init; } = SlideFrictionDip;

    /// <summary>Lo que rebota contra los bordes de la pantalla.</summary>
    public double WallBounciness { get; init; } = WallBounce;

    public double HalfWidth => Height * WidthFactor;

    public Vec2 Center => new(Feet.X, Feet.Y - Height / 2);

    public bool IsGrounded => State == BodyState.Grounded;

    public bool IsHeld => State == BodyState.Held;

    public bool IsClimbing => State == BodyState.Climbing;

    /// <summary>Velocidad horizontal propia al caminar (la pone el comportamiento).</summary>
    public double WalkVelocity { get; set; }

    /// <summary>Coloca el cuerpo de pie sobre una plataforma.</summary>
    public void PlaceOn(Platform platform, double x)
    {
        Feet = new Vec2(Math.Clamp(x, platform.Left + HalfWidth, Math.Max(platform.Left + HalfWidth, platform.Right - HalfWidth)), platform.Y);
        Velocity = Vec2.Zero;
        Attach(platform);
    }

    public void Grab()
    {
        State = BodyState.Held;
        Support = null;
        SupportId = 0;
        Velocity = Vec2.Zero;
        WalkVelocity = 0;
    }

    /// <summary>Mientras está cogido, los pies siguen al punto indicado.</summary>
    public void MoveHeld(Vec2 feet, double dt)
    {
        if (dt > 0)
        {
            Velocity = MathUtil.Damp(Velocity, (feet - Feet) / dt, 18, dt);
        }

        Feet = feet;
    }

    /// <summary>Suelta el cuerpo con la velocidad que llevaba el ratón (limitada).</summary>
    public void Release()
    {
        if (State != BodyState.Held)
        {
            return;
        }

        State = BodyState.Airborne;
        Velocity = Velocity.ClampLength(2600 * Scale);
    }

    /// <summary>Lo suelta en el aire con la velocidad indicada (se le cae algo de la mano, una patada).</summary>
    public void Launch(Vec2 velocity)
    {
        State = BodyState.Airborne;
        Support = null;
        SupportId = 0;
        WalkVelocity = 0;
        Velocity = velocity;
    }

    /// <summary>
    /// Se agarra al lado de la ventana <paramref name="ownerId"/> para trepar: deja de caer y lo mueve el
    /// comportamiento con <see cref="ClimbTo"/>. Si la ventana desaparece, se cae.
    /// </summary>
    public void Cling(long ownerId)
    {
        State = BodyState.Climbing;
        Support = null;
        SupportId = ownerId;
        WalkVelocity = 0;
        Velocity = Vec2.Zero;
    }

    /// <summary>Mientras trepa, los pies van a <paramref name="feet"/> (la velocidad sale del movimiento).</summary>
    public void ClimbTo(Vec2 feet, double dt)
    {
        if (State != BodyState.Climbing)
        {
            return;
        }

        if (dt > 0)
        {
            Velocity = (feet - Feet) / dt;
        }

        Feet = feet;
    }

    /// <summary>Empuja hacia arriba (saltar de alegría).</summary>
    public void Hop(double speedDip)
    {
        if (State != BodyState.Grounded)
        {
            return;
        }

        State = BodyState.Airborne;
        Velocity = new Vec2(Velocity.X, -speedDip * Scale);
        Support = null;
        SupportId = 0;
    }

    /// <summary>Un paso de simulación. Devuelve el aterrizaje si ha tocado suelo en este paso.</summary>
    public Landing? Step(World world, double dt)
    {
        switch (State)
        {
            case BodyState.Held:
                return null;

            case BodyState.Climbing:
                // Lo coloca quien trepa; si la ventana a la que se agarra ya no está, se cae.
                if (!world.HasOwner(SupportId))
                {
                    Detach();
                }

                return null;

            case BodyState.Grounded:
                StepGrounded(world, dt);
                return null;

            default:
                return StepAirborne(world, dt);
        }
    }

    private void StepGrounded(World world, double dt)
    {
        // Seguir a la ventana si se mueve.
        if (!world.TryGetOwnerOrigin(SupportId, out var origin))
        {
            Detach();
            return;
        }

        var shift = origin - _supportOrigin;
        _supportOrigin = origin;
        if (shift.Length > TeleportDip * Scale)
        {
            // Maximizar, restaurar o saltar de monitor: no la sigue, se cae desde donde estaba.
            Detach();
            return;
        }

        // Deslizamiento tras aterrizar con velocidad horizontal.
        var slide = Velocity.X;
        if (Math.Abs(slide) > 0)
        {
            slide = MathUtil.MoveTowards(slide, 0, FrictionDip * Scale * dt);
            Velocity = new Vec2(slide, 0);
        }

        var x = Feet.X + shift.X + (slide + WalkVelocity) * dt;
        var segment = world.FindSegment(SupportId, x);
        if (segment is null)
        {
            // Se ha salido por el borde o la parte de la ventana donde estaba ha quedado tapada.
            Feet = new Vec2(x, Feet.Y + shift.Y);
            Velocity = new Vec2(slide + WalkVelocity, 0);
            Detach();
            return;
        }

        Support = segment;
        Feet = new Vec2(x, segment.Y);
    }

    private Landing? StepAirborne(World world, double dt)
    {
        var scale = Scale;
        var velocity = Velocity;
        velocity = new Vec2(velocity.X * Math.Exp(-AirDrag * dt), Math.Min(velocity.Y + GravityDip * scale * dt, MaxFallSpeedDip * scale));
        var previous = Feet;
        var next = previous + velocity * dt;

        // Paredes y techos de los monitores.
        foreach (var barrier in world.Barriers)
        {
            switch (barrier.Kind)
            {
                case BarrierKind.LeftWall when previous.X - HalfWidth >= barrier.Position - 0.5 && next.X - HalfWidth < barrier.Position && barrier.Spans(next.Y - Height / 2):
                    next = next.WithX(barrier.Position + HalfWidth);
                    velocity = velocity.WithX(Math.Abs(velocity.X) * WallBounciness);
                    break;
                case BarrierKind.RightWall when previous.X + HalfWidth <= barrier.Position + 0.5 && next.X + HalfWidth > barrier.Position && barrier.Spans(next.Y - Height / 2):
                    next = next.WithX(barrier.Position - HalfWidth);
                    velocity = velocity.WithX(-Math.Abs(velocity.X) * WallBounciness);
                    break;
                case BarrierKind.Ceiling when previous.Y - Height >= barrier.Position - 0.5 && next.Y - Height < barrier.Position && barrier.Spans(next.X):
                    next = next.WithY(barrier.Position + Height);
                    velocity = velocity.WithY(Math.Abs(velocity.Y) * 0.2);
                    break;
            }
        }

        // Aterrizaje: la plataforma más alta que cruza en este paso.
        if (velocity.Y >= 0)
        {
            Platform? hit = null;
            foreach (var platform in world.Platforms)
            {
                if (platform.Y >= previous.Y - 0.5 && platform.Y <= next.Y && platform.ContainsX(next.X) && (hit is null || platform.Y < hit.Y))
                {
                    hit = platform;
                }
            }

            if (hit is not null)
            {
                var speed = velocity.Y;
                if (Restitution > 0 && speed > MinBounceDip * scale)
                {
                    // Rebota: sigue en el aire, más despacio.
                    Feet = new Vec2(next.X, hit.Y);
                    Velocity = new Vec2(velocity.X * 0.92, -speed * Restitution);
                    return new Landing(Feet, speed, hit, Bounced: true);
                }

                Feet = new Vec2(next.X, hit.Y);
                Velocity = new Vec2(velocity.X * 0.5, 0);
                Attach(hit);
                return new Landing(Feet, speed, hit);
            }
        }

        Feet = next;
        Velocity = velocity;

        // Red de seguridad: si se ha salido de todos los monitores, reaparece en el suelo principal.
        if (world.MonitorAt(Feet) is null && world.MonitorAt(Feet - new Vec2(0, Height)) is null && world.PrimaryGround is { } ground)
        {
            var monitor = world.NearestMonitor(Feet);
            if (monitor is null || Feet.Y > monitor.Bounds.Bottom + Height * 2 || Math.Abs(Feet.X - monitor.Bounds.Center.X) > monitor.Bounds.Width)
            {
                PlaceOn(ground, ground.CenterX);
                return new Landing(Feet, 0, ground);
            }
        }

        return null;
    }

    private void Attach(Platform platform)
    {
        State = BodyState.Grounded;
        Support = platform;
        SupportId = platform.OwnerId;
        _supportOrigin = platform.OwnerOrigin;
    }

    private void Detach()
    {
        State = BodyState.Airborne;
        Support = null;
        SupportId = 0;
        WalkVelocity = 0;
    }
}
