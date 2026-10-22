using Kobii.Core.Geometry;

namespace Kobii.Core.Creature;

/// <summary>Cara de Kobii (el mismo índice que usa el shader).</summary>
public enum Expression
{
    Neutral,
    Happy,

    /// <summary>Alegría grande: ojos en arco (^ ^) y boca abierta.</summary>
    Joy,

    Love,
    Sad,
    Surprised,
    Curious,
    Sleepy,
    Sleeping,
    Eating,
    Angry,
    Confused,
    Thinking,
    Dizzy,

    /// <summary>Enfermo: ojos caídos y boca ondulada.</summary>
    Sick,

    /// <summary>Asco: un ojo apretado y la lengua fuera.</summary>
    Disgusted,

    /// <summary>Dolor: ojos apretados (&gt; &lt;) y dientes.</summary>
    Pain,

    /// <summary>Bostezo: ojos cerrados con fuerza y la boca muy abierta.</summary>
    Yawn,

    /// <summary>Guiño: un ojo cerrado y una sonrisa.</summary>
    Wink,
}

/// <summary>Pequeños efectos que salen de Kobii (los dibuja el asistente como partículas).</summary>
public enum EmoteKind
{
    Heart,
    Note,
    Sleep,
    Sparkle,
    Question,
    Exclamation,
    Crumbs,
    Warmth,
    Dust,
    Shell,

    /// <summary>Pompas de jabón.</summary>
    Bubble,

    /// <summary>Gotas de agua (al sacudirse o de sudor).</summary>
    Drop,

    /// <summary>Pelusas que suelta al cepillarle.</summary>
    Fluff,

    /// <summary>Ondas de mal olor.</summary>
    Stink,

    /// <summary>Una mosca que da vueltas.</summary>
    Fly,

    /// <summary>Estornudo.</summary>
    Sneeze,

    /// <summary>Una florecita.</summary>
    Flower,

    /// <summary>Gotas de pintura al pintar.</summary>
    Paint,

    /// <summary>Hojas de otoño que caen.</summary>
    Leaf,

    /// <summary>Un golpe con estrella de cómic (chocar los cinco).</summary>
    Burst,

    /// <summary>La vena de enfado de los cómics (💢): cuatro trazos rojos que laten junto a la cabeza.</summary>
    Anger,

    /// <summary>Confeti de colores que sale disparado y cae girando.</summary>
    Confetti,
}

public readonly record struct Emote(EmoteKind Kind, Vec2 Position, double Scale, int Count = 1);

/// <summary>
/// Una línea detrás de la que está escondido (en el mundo): vertical en <paramref name="Position"/> = x (el
/// borde de una ventana o de la pantalla) u horizontal en y (la barra de tareas o la barra de título en la que
/// está). <paramref name="Side"/> dice qué lado no se ve (−1 izquierda/arriba, 1 derecha/abajo).
/// </summary>
public readonly record struct ClipLine(double Position, bool Horizontal, int Side);

/// <summary>Lo que se interpola de un paso a otro al dibujar (el resto cambia despacio o va por saltos).</summary>
public readonly record struct VisualPose(double Squash, double Lean, double Facing, Vec2 Look)
{
    public static VisualPose Lerp(in VisualPose a, in VisualPose b, double t) => new(
        a.Squash + (b.Squash - a.Squash) * t,
        a.Lean + (b.Lean - a.Lean) * t,
        a.Facing + (b.Facing - a.Facing) * t,
        Vec2.Lerp(a.Look, b.Look, t));
}

/// <summary>
/// Estado visual de Kobii: expresión, mirada, rebote, boca, brote… Lo actualiza el juego en cada paso y lo lee el
/// dibujo. No afecta a la simulación. (Los bocadillos con texto los pone el host de K-Desktop.)
/// </summary>
public sealed class CreatureVisual
{
    private readonly List<Emote> _emotes = [];
    private double _expressionTimer;
    private double _squashVelocity;
    private double _talkTimer;

    public Expression Expression { get; private set; } = Expression.Neutral;

    /// <summary>Expresión cuando no hay ninguna temporal.</summary>
    public Expression BaseExpression { get; set; } = Expression.Neutral;

    /// <summary>Dirección de la mirada (-1..1 en cada eje).</summary>
    public Vec2 Look { get; set; }

    public Vec2 LookTarget { get; set; }

    /// <summary>1 = normal; menos de 1 = aplastado; más de 1 = estirado.</summary>
    public double Squash { get; private set; } = 1;

    /// <summary>Inclinación en radianes (positivo: hacia la derecha).</summary>
    public double Lean { get; set; }

    /// <summary>Hacia dónde mira el cuerpo (-1 izquierda, 0 de frente, 1 derecha).</summary>
    public double Facing { get; set; }

    public double FacingTarget { get; set; }

    public double WalkPhase { get; set; }

    /// <summary>Intensidad del paso (0 quieto, 1 caminando).</summary>
    public double Walk { get; set; }

    /// <summary>Apertura de la boca al hablar.</summary>
    public double Mouth { get; private set; }

    /// <summary>
    /// Lo fuerte que suena su voz ahora (0–1, lo da el host unas 30 veces por segundo mientras habla): la boca lo sigue.
    /// Negativo cuando no llega nivel (la boca se mueve sola).
    /// </summary>
    public double VoiceLevel { get; set; } = -1;

    /// <summary>Habla ahora (con o sin nivel de voz).</summary>
    public bool Talking { get; set; }

    /// <summary>Brillo del brote (0–1): escuchando, pensando.</summary>
    public double Antenna { get; private set; }

    /// <summary>Brillo que pide el juego para el brote (0 apagado).</summary>
    public double AntennaTarget { get; set; }

    /// <summary>El brillo del brote late (pensando) en vez de quedarse fijo (escuchando).</summary>
    public bool AntennaPulse { get; set; }

    /// <summary>Mojado (1 empapado; se seca solo).</summary>
    public double Wet { get; set; }

    /// <summary>Pelo brillante después de cepillarle (0–1; se apaga solo en unos minutos).</summary>
    public double Shine { get; set; }

    /// <summary>Ojos cerrados de gusto (0–1): mientras le cepillas.</summary>
    public double EyesClosed { get; set; }

    /// <summary>Lleva el pijama puesto (0–1).</summary>
    public double Pajamas { get; set; }

    /// <summary>Lleva el gorro de dormir (0–1; mientras se lo pone, baja desde arriba).</summary>
    public double Nightcap { get; set; }

    /// <summary>Escondido detrás de algo: lo que queda al otro lado de esta línea no se ve (null: nada).</summary>
    public ClipLine? Clip { get; set; }

    /// <summary>Lo interpolable, ahora.</summary>
    public VisualPose Pose => new(Squash, Lean, Facing, Look);

    /// <summary>Lo interpolable, al empezar el paso actual (para dibujar entre dos pasos).</summary>
    public VisualPose PreviousPose { get; private set; } = new(1, 0, 0, Vec2.Zero);

    /// <summary>Guarda cómo estaba antes de avanzar un paso.</summary>
    public void Remember() => PreviousPose = Pose;

    /// <summary>Hay algo animándose y conviene dibujar a la frecuencia alta.</summary>
    public bool IsAnimating =>
        Math.Abs(Squash - 1) > 0.004 || Math.Abs(_squashVelocity) > 0.01 || Walk > 0.01 || Mouth > 0.01 || Talking
        || Antenna > 0.01 || AntennaTarget > 0 || Wet > 0.01
        || (Look - LookTarget).LengthSquared > 1e-4 || Math.Abs(Facing - FacingTarget) > 0.01;

    /// <summary>El muelle del rebote se está moviendo (tras un salto, una caricia).</summary>
    public bool IsBouncing => Math.Abs(Squash - 1) > 0.01 || Math.Abs(_squashVelocity) > 0.05;

    /// <summary>Muestra una expresión durante un rato (0 = hasta que se cambie).</summary>
    public void SetExpression(Expression expression, double seconds)
    {
        Expression = expression;
        _expressionTimer = seconds;
    }

    public void ClearExpression() => _expressionTimer = 0.0001;

    /// <summary>Impulso al muelle del rebote (negativo aplasta, positivo estira).</summary>
    public void Bounce(double impulse) => _squashVelocity += impulse;

    /// <summary>Mueve la boca un rato como si dijera algo (sin voz).</summary>
    public void Babble(double seconds) => _talkTimer = Math.Max(_talkTimer, seconds);

    public void AddEmote(EmoteKind kind, Vec2 position, double scale, int count = 1) => _emotes.Add(new Emote(kind, position, scale, count));

    public void DrainEmotes(List<Emote> target)
    {
        target.AddRange(_emotes);
        _emotes.Clear();
    }

    /// <summary>Avanza las animaciones.</summary>
    public void Update(double dt, double time)
    {
        if (_expressionTimer > 0)
        {
            _expressionTimer -= dt;
            if (_expressionTimer <= 0)
            {
                _expressionTimer = 0;
                Expression = BaseExpression;
            }
        }
        else
        {
            Expression = BaseExpression;
        }

        // Muelle amortiguado para el rebote.
        const double stiffness = 260;
        const double damping = 14;
        _squashVelocity += (-(Squash - 1) * stiffness - _squashVelocity * damping) * dt;
        Squash = Math.Clamp(Squash + _squashVelocity * dt, 0.55, 1.5);

        Look = MathUtil.Damp(Look, LookTarget, 10, dt);
        Facing = MathUtil.Damp(Facing, FacingTarget, 7, dt);

        if (Talking && VoiceLevel >= 0)
        {
            // La boca sigue a la voz de verdad (un poco suavizada para que no tiemble).
            Mouth = MathUtil.Damp(Mouth, Math.Clamp(VoiceLevel * 1.6, 0, 1), 28, dt);
        }
        else if (Talking || _talkTimer > 0)
        {
            _talkTimer = Math.Max(0, _talkTimer - dt);
            Mouth = 0.5 + 0.5 * Math.Sin(time * 22);
        }
        else
        {
            Mouth = MathUtil.Damp(Mouth, 0, 18, dt);
        }

        var glow = AntennaTarget <= 0 ? 0 : AntennaPulse ? AntennaTarget * (0.6 + 0.4 * Math.Sin(time * 6)) : AntennaTarget;
        Antenna = MathUtil.Damp(Antenna, glow, 8, dt);
        if (Antenna < 0.003 && glow <= 0)
        {
            Antenna = 0;
        }

        Wet = Wet < 0.005 ? 0 : MathUtil.Damp(Wet, 0, 0.35, dt);
        Shine = Shine < 0.005 ? 0 : Math.Max(0, Shine - dt / 240);
    }
}
