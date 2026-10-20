using Lander.Core.Geometry;
using Lander.Core.Terrain;

namespace Lander.Core.Physics;

public enum BodyState
{
    Flying,
    Landed,
    Held,
    Destroyed,
}

/// <summary>Plataforma sobre la que está posado el lander y cómo se está moviendo.</summary>
public sealed class Support(Platform platform, double localX, double sampleTime)
{
    public long OwnerId { get; } = platform.OwnerId;

    public PlatformKind Kind { get; } = platform.Kind;

    public Platform Platform { get; set; } = platform;

    /// <summary>Posición del lander relativa al origen del dueño.</summary>
    public double LocalX { get; set; } = localX;

    public Vec2 Origin { get; set; } = platform.OwnerOrigin;

    /// <summary>Velocidad suavizada de la plataforma (px/s).</summary>
    public Vec2 Velocity { get; set; }

    /// <summary>Aceleración de la plataforma en la última muestra (px/s²).</summary>
    public Vec2 Acceleration { get; set; }

    public double SampleTime { get; set; } = sampleTime;
}

/// <summary>Estado físico del módulo lunar.</summary>
public sealed class LanderBody
{
    /// <summary>Medidas del sprite a escala 1 (48 px de alto). El origen es el centro de masas.</summary>
    public const double BaseHeight = 48;
    public const double BaseHalfHeight = 24;
    public const double BaseHalfWidth = 18;
    public const double BaseFootOffset = 18;

    public Vec2 Position { get; set; }

    public Vec2 Velocity { get; set; }

    /// <summary>Inclinación en radianes, positiva en sentido horario.</summary>
    public double Angle { get; set; }

    public double AngularVelocity { get; set; }

    /// <summary>Escala actual: tamaño elegido × escalado DPI del monitor.</summary>
    public double Scale { get; set; } = 1;

    public double Fuel { get; set; } = 100;

    public double FuelCapacity { get; set; } = 100;

    /// <summary>Daño acumulado de 0 a 1.</summary>
    public double Damage { get; set; }

    /// <summary>Despliegue de las patas de 0 (recogidas) a 1 (desplegadas).</summary>
    public double LegsDeploy { get; set; } = 1;

    /// <summary>Aceleración externa temporal, como una ráfaga de viento (px/s²).</summary>
    public Vec2 ExternalAcceleration { get; set; }

    public double BoostCooldown { get; set; }

    public BodyState State { get; private set; } = BodyState.Flying;

    /// <summary>Segundos en el estado actual.</summary>
    public double StateTime { get; set; }

    public Support? Support { get; private set; }

    /// <summary>Velocidad suavizada mientras se arrastra con el ratón.</summary>
    internal Vec2 HeldVelocity { get; set; }

    public double HalfWidth => BaseHalfWidth * Scale;

    public double HalfHeight => BaseHalfHeight * Scale;

    public double FootOffset => BaseFootOffset * Scale;

    public double Height => BaseHeight * Scale;

    public double FeetY => Position.Y + HalfHeight;

    public Vec2 Up => Vec2.UpFromAngle(Angle);

    public double FuelFraction => FuelCapacity > 0 ? Math.Clamp(Fuel / FuelCapacity, 0, 1) : 0;

    public bool IsLanded => State == BodyState.Landed;

    public bool IsFlying => State == BodyState.Flying;

    public bool IsOnBase => State == BodyState.Landed && Support?.Kind == PlatformKind.Ground;

    public void Fly(Vec2 velocity)
    {
        State = BodyState.Flying;
        Support = null;
        Velocity = velocity;
        StateTime = 0;
    }

    /// <summary>Posa el lander en <paramref name="platform"/> a la altura de su superficie.</summary>
    public void Land(Platform platform, double sampleTime)
    {
        State = BodyState.Landed;
        Position = new Vec2(Position.X, platform.Y - HalfHeight);
        Support = new Support(platform, Position.X - platform.OwnerOrigin.X, sampleTime);
        Velocity = Vec2.Zero;
        AngularVelocity = 0;
        StateTime = 0;
    }

    /// <summary>Coloca el lander directamente sobre una plataforma (aparición o recolocación).</summary>
    public void PlaceOn(Platform platform, double x, double sampleTime)
    {
        Position = new Vec2(Math.Clamp(x, platform.Left, platform.Right), platform.Y - HalfHeight);
        Angle = 0;
        LegsDeploy = 1;
        Land(platform, sampleTime);
    }

    public void Hold()
    {
        State = BodyState.Held;
        Support = null;
        HeldVelocity = Vec2.Zero;
        Velocity = Vec2.Zero;
        StateTime = 0;
    }

    public void Destroy()
    {
        State = BodyState.Destroyed;
        Support = null;
        Velocity = Vec2.Zero;
        AngularVelocity = 0;
        StateTime = 0;
    }
}
