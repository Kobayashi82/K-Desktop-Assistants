using Kobii.Core.Geometry;

namespace Kobii.Core.Creature;

/// <summary>Qué está haciendo la mariposa.</summary>
public enum ButterflyMode
{
    /// <summary>Revolotea en zigzag hacia donde quiere ir.</summary>
    Flutter,

    /// <summary>Posada (en su brote, en sus manos): las alas se abren y se cierran despacio.</summary>
    Perch,

    /// <summary>Entre sus manos, que la tapan (no se ve).</summary>
    Caught,

    /// <summary>Se va volando muy alto y desaparece.</summary>
    Leave,
}

/// <summary>
/// Una mariposa: revolotea a rachas, con un cabeceo a cada aletazo y cambios de rumbo de los de verdad, hacia
/// donde quiere ir (lo decide quien juega con ella). Se puede posar, quedar escondida entre unas manos y, al
/// final, se va volando muy alto. Las magnitudes están en DIP y se multiplican por <see cref="Scale"/>.
/// </summary>
public sealed class Butterfly
{
    /// <summary>De punta a punta de las alas abiertas.</summary>
    public const double WingspanDip = 25;

    /// <summary>Colores que hay (ver el pintor de la aplicación).</summary>
    public const int Palettes = 5;

    private const double CruiseDip = 90;
    private const double LeaveDip = 170;
    private const double StartleDip = 260;
    private const double WanderDip = 38;
    private const double BobDip = 5;
    private const double FadeInSeconds = 0.6;
    private const double LeaveSeconds = 4.5;

    private Vec2 _path;
    private double _wander;
    private double _bob;
    private double _burst;
    private double _age;
    private double _leaving;
    private double _landing = 1;

    public Butterfly(Vec2 position, double scale, int palette, double wander = 0)
    {
        _path = position;
        _wander = wander;
        Position = position;
        PreviousPosition = position;
        Target = position;
        Scale = scale;
        Palette = Math.Clamp(palette, 0, Palettes - 1);
    }

    /// <summary>Dónde se ve (en el mundo, píxeles físicos).</summary>
    public Vec2 Position { get; private set; }

    /// <summary>Dónde estaba en el paso anterior (para dibujar entre dos pasos).</summary>
    public Vec2 PreviousPosition { get; private set; }

    public Vec2 Velocity { get; private set; }

    /// <summary>Escala DIP → píxeles (la del monitor en el que está).</summary>
    public double Scale { get; set; }

    public int Palette { get; }

    public ButterflyMode Mode { get; private set; } = ButterflyMode.Flutter;

    /// <summary>Adonde quiere ir mientras revolotea.</summary>
    public Vec2 Target { get; set; }

    /// <summary>Se acerca para posarse: cuanto más cerca del destino, menos zigzag y menos cabeceo.</summary>
    public bool Settling { get; set; }

    /// <summary>Multiplica su velocidad de crucero (se da prisa para huir de unas manos).</summary>
    public double Hurry { get; set; } = 1;

    /// <summary>Fase del aleteo (radianes; no se reinicia): las alas se abren y se cierran.</summary>
    public double Flap { get; private set; }

    public double PreviousFlap { get; private set; }

    /// <summary>Inclinación del cuerpo (radianes): se ladea hacia donde va.</summary>
    public double Tilt { get; private set; }

    /// <summary>Hacia dónde va (−1 izquierda, 1 derecha), suavizado: el ala del lado al que gira se ve más.</summary>
    public double Facing { get; private set; }

    /// <summary>Opacidad: aparece poco a poco y, al irse, se desvanece a lo lejos.</summary>
    public double Opacity => Math.Min(Math.Clamp(_age / FadeInSeconds, 0, 1), Mode == ButterflyMode.Leave ? Math.Clamp((LeaveSeconds - _leaving) / 1.2, 0, 1) : 1);

    /// <summary>Se ve (no está escondida entre unas manos ni se ha ido del todo).</summary>
    public bool Visible => Mode != ButterflyMode.Caught && Opacity > 0.001;

    /// <summary>Se ha ido del todo.</summary>
    public bool Gone { get; private set; }

    /// <summary>Lleva un rato revoloteando con brío tras un susto (0–1).</summary>
    public double Burst => _burst;

    public double Wingspan => WingspanDip * Scale;

    /// <summary>Un paso.</summary>
    public void Step(double dt, IRandom random)
    {
        PreviousPosition = Position;
        PreviousFlap = Flap;
        _age += dt;
        _burst = MathUtil.MoveTowards(_burst, 0, dt * 1.2);
        switch (Mode)
        {
            case ButterflyMode.Perch:
                // Posada: las alas se abren y se cierran despacio (la posición la pone quien la sostiene).
                Velocity = Vec2.Zero;
                Flap += dt * Math.Tau * 0.55;
                Tilt = MathUtil.Damp(Tilt, 0, 6, dt);
                _landing = Math.Min(1, _landing + dt / 0.25);
                return;

            case ButterflyMode.Caught:
                Velocity = Vec2.Zero;
                return;

            case ButterflyMode.Leave:
                _leaving += dt;
                if (_leaving >= LeaveSeconds)
                {
                    Gone = true;
                }

                break;
        }

        Fly(dt, random);
    }

    /// <summary>Se posa donde está ahora (en adelante, su sitio lo marca <see cref="HoldAt"/>).</summary>
    public void Land()
    {
        Mode = ButterflyMode.Perch;
        Settling = false;
        Velocity = Vec2.Zero;
        _landing = 0;
    }

    /// <summary>
    /// La sostiene algo que se mueve (el brote, unas manos): se coloca ahí en este paso. Recién posada, se
    /// acomoda en un momento en vez de saltar a su sitio.
    /// </summary>
    public void HoldAt(Vec2 point)
    {
        Position = Mode == ButterflyMode.Perch && _landing < 1 ? Vec2.Lerp(Position, point, 0.2 + 0.8 * _landing) : point;
        _path = Position;
    }

    /// <summary>Queda escondida entre unas manos.</summary>
    public void Hide(Vec2 point)
    {
        Mode = ButterflyMode.Caught;
        HoldAt(point);
    }

    /// <summary>Echa a volar desde donde está, con un aletazo hacia arriba.</summary>
    public void TakeOff(Vec2 target)
    {
        if (Mode is ButterflyMode.Perch or ButterflyMode.Caught)
        {
            Mode = ButterflyMode.Flutter;
            Velocity = new Vec2(0, -StartleDip * 0.6 * Scale);
            _burst = 0.6;
        }

        Target = target;
        Settling = false;
    }

    /// <summary>Un susto (unas manos que se cierran, el cursor que pasa deprisa): sale disparada hacia <paramref name="away"/>.</summary>
    public void Startle(Vec2 away)
    {
        if (Mode == ButterflyMode.Caught)
        {
            return;
        }

        if (Mode == ButterflyMode.Perch)
        {
            Mode = ButterflyMode.Flutter;
        }

        var direction = away.LengthSquared > 1e-9 ? away.Normalized() : new Vec2(0, -1);
        Velocity += direction * StartleDip * Scale;
        _burst = 1;
        Settling = false;
    }

    /// <summary>Se va: sube muy alto hacia un lado y desaparece.</summary>
    public void Leave(double side)
    {
        if (Mode == ButterflyMode.Leave)
        {
            return;
        }

        if (Mode is ButterflyMode.Perch or ButterflyMode.Caught)
        {
            Velocity = new Vec2(0, -StartleDip * 0.5 * Scale);
            _burst = Math.Max(_burst, 0.5);
        }

        Mode = ButterflyMode.Leave;
        Settling = false;
        _leaving = 0;
        Target = Position + new Vec2((side < 0 ? -1 : 1) * 700, -900) * Scale;
    }

    /// <summary>Revoloteo: va hacia el destino con zigzag y un cabeceo a cada aletazo.</summary>
    private void Fly(double dt, IRandom random)
    {
        var s = Scale;
        var to = Target - _path;
        var distance = to.Length;

        // Al posarse, el zigzag y el cabeceo se apagan al llegar (si no, aterrizaría a tirones).
        var calm = Settling ? Math.Clamp(distance / (Wingspan * 2.5), 0.08, 1) : 1;
        var cruise = (Mode == ButterflyMode.Leave ? LeaveDip : CruiseDip * Hurry) * s * (1 + 0.8 * _burst);
        var desired = distance > 1e-6 ? to / distance * Math.Min(cruise, distance * (Settling ? 4 : 2.2)) : Vec2.Zero;

        // Zigzag: el rumbo da bandazos (un paseo aleatorio del ángulo), más en horizontal que en vertical.
        _wander += random.Range(-1, 1) * 9 * dt;
        desired += new Vec2(Math.Cos(_wander), 0.55 * Math.Sin(_wander)) * WanderDip * s * calm;

        // Con brío tras un susto, gira menos (sigue el impulso un rato).
        var agility = 3.4 * (1 - 0.6 * _burst);
        Velocity += (desired - Velocity) * Math.Min(1, agility * dt);
        _path += Velocity * dt;

        // Cada aletazo la sube un poco y entre aletazos cae: un cabeceo de unas dos veces por segundo.
        _bob += dt * Math.Tau * (2.0 + 1.5 * _burst);
        var bob = (Math.Sin(_bob) + 0.35 * Math.Sin(_bob * 2.3 + 1)) * BobDip * s * calm;
        Position = _path + new Vec2(0, -bob);

        Flap += dt * Math.Tau * (Mode == ButterflyMode.Leave ? 8.5 : 7) * (1 + 0.5 * _burst);
        var heading = Math.Clamp(Velocity.X / (CruiseDip * s), -1.5, 1.5);
        Tilt = MathUtil.Damp(Tilt, 0.3 * heading - 0.15 * Math.Clamp(Velocity.Y / (CruiseDip * s), -1, 1) * Math.Sign(heading), 5, dt);
        if (Math.Abs(Velocity.X) > 12 * s)
        {
            Facing = MathUtil.Damp(Facing, Math.Sign(Velocity.X), 4, dt);
        }
    }
}
