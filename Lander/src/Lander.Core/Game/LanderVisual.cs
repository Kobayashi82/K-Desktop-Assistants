using Lander.Core.Geometry;
using Lander.Core.Physics;

namespace Lander.Core.Game;

public enum Expression
{
    Neutral,
    Happy,
    Tired,
    Asleep,
    Scared,
    Curious,
    Focused,
}

/// <summary>Iconos de los bocadillos: el lander se expresa con iconos, no con texto.</summary>
public enum BubbleIcon
{
    /// <summary>Flecha hacia el destino elegido.</summary>
    Arrow,
    Sleep,
    Exclamation,
    Question,
    Heart,
    Star,
    Fuel,
    Battery,
    Home,
    Wrench,
    Crystal,
    Package,
    RoundTrip,

    /// <summary>Icono de una aplicación (misión de aterrizar sobre ella).</summary>
    App,
    Trophy,

    /// <summary>Bandera a cuadros: carrera.</summary>
    Flag,
    MedalGold,
    MedalSilver,
    MedalBronze,

    /// <summary>Reloj: la hora en punto.</summary>
    Clock,

    /// <summary>Mano que saluda.</summary>
    Hello,

    // Lo que hace el asistente mientras atiende una orden (estados de K-Desktop con detalle).

    /// <summary>Internet: buscar o abrir una web.</summary>
    Globe,

    /// <summary>Una página o un documento que lee.</summary>
    Page,

    Folder,

    /// <summary>Una nota con su lápiz.</summary>
    Note,

    /// <summary>Sol con nube: el tiempo.</summary>
    Weather,

    /// <summary>Corchea: música.</summary>
    Music,

    /// <summary>Ojo: mirar la pantalla o vigilar algo.</summary>
    Eye,

    /// <summary>Lápiz: escribir o dictar.</summary>
    Pencil,

    /// <summary>Cuentas: calcular, convertir, fechas.</summary>
    Sums,

    /// <summary>Engranajes que giran: el PC, programas, pensar a fondo.</summary>
    Gears,

    /// <summary>Puntos que giran: cargando.</summary>
    Loading,
}

/// <param name="Icon">Icono principal.</param>
/// <param name="Duration">Segundos que permanece visible.</param>
/// <param name="Direction">Ángulo de la flecha en radianes (0 = derecha, sentido horario).</param>
/// <param name="AppHandle">Ventana de la que tomar el icono cuando <paramref name="Icon"/> es <see cref="BubbleIcon.App"/>.</param>
public sealed record Bubble(BubbleIcon Icon, double Duration, double Direction = 0, long AppHandle = 0);

public enum PopupStyle
{
    Points,
    Perfect,
    Bonus,

    /// <summary>Penalización (por ejemplo, segundos añadidos en una carrera).</summary>
    Penalty,
}

public sealed class ScorePopup(string text, Vec2 position, PopupStyle style)
{
    public const double Lifetime = 1.6;

    public string Text { get; } = text;

    public Vec2 Position { get; } = position;

    public PopupStyle Style { get; } = style;

    public double Age { get; set; }
}

/// <summary>
/// Estado puramente visual del lander: expresión, mirada, bocadillos y llamas suavizadas.
/// La simulación lo actualiza; el renderizador solo lo lee.
/// </summary>
public sealed class LanderVisual
{
    private const int MaxPopups = 6;

    private double _expressionTimer;
    private Expression _baseExpression = Expression.Neutral;

    public Expression Expression { get; private set; } = Expression.Neutral;

    /// <summary>Dirección de la mirada, cada componente entre -1 y 1.</summary>
    public Vec2 Look { get; private set; }

    /// <summary>Punto del mundo al que mirar, o null para mirar al frente.</summary>
    public Vec2? LookTarget { get; set; }

    public Bubble? Bubble { get; private set; }

    public double BubbleAge { get; private set; }

    /// <summary>Intensidad visual del motor principal (0–1).</summary>
    public double MainFlame { get; private set; }

    /// <summary>Propulsor superior izquierdo, que empuja hacia la derecha (0–1).</summary>
    public double LeftJet { get; private set; }

    /// <summary>Propulsor superior derecho, que empuja hacia la izquierda (0–1).</summary>
    public double RightJet { get; private set; }

    /// <summary>Propulsor inferior izquierdo, que empuja hacia la derecha (0–1).</summary>
    public double LowerLeftJet { get; private set; }

    /// <summary>Propulsor inferior derecho, que empuja hacia la izquierda (0–1).</summary>
    public double LowerRightJet { get; private set; }

    private static double Jet(double current, double target, double dt)
    {
        var value = MathUtil.Damp(current, Math.Min(1, target), 20, dt);
        return value < 0.005 ? 0 : value;
    }

    public double BoostFlash { get; set; }

    public double CelebrateTime { get; set; }

    /// <summary>Opacidad del lander (aparece poco a poco al reaparecer).</summary>
    public double Opacity { get; set; } = 1;

    public bool ShowHud { get; set; }

    public bool CarryingPackage { get; set; }

    public List<ScorePopup> Popups { get; } = [];

    public const double WaveDuration = 1.6;
    public const double YawnDuration = 2.4;
    public const double StretchDuration = 1.7;
    public const double WiggleDuration = 1.1;
    public const double BounceDuration = 0.75;

    /// <summary>
    /// Compresión de la suspensión al posarse: positiva hundida, negativa estirada. Oscila como un muelle
    /// amortiguado y vuelve a 0.
    /// </summary>
    public double Squash { get; private set; }

    /// <summary>Segundos que le quedan al saludo con la nave (balanceo de lado a lado).</summary>
    public double WaveTime { get; private set; }

    public double YawnTime { get; private set; }

    public double StretchTime { get; private set; }

    public double WiggleTime { get; private set; }

    public double BounceTime { get; private set; }

    /// <summary>
    /// Petición de agacharse en este paso (1 = del todo), como antes de saltar: el piloto la renueva en cada
    /// paso mientras se prepara para despegar y se pone a 0 sola.
    /// </summary>
    public double Crouch { get; set; }

    /// <summary>Cuánto está agachado ahora mismo (0–1), suavizado.</summary>
    public double CrouchLevel { get; private set; }

    private double _squashVelocity;

    /// <summary>Hay animaciones en curso que requieren refrescar a menudo.</summary>
    public bool IsAnimating =>
        MainFlame > 0.01 || LeftJet > 0.01 || RightJet > 0.01 || LowerLeftJet > 0.01 || LowerRightJet > 0.01
        || BoostFlash > 0 || CelebrateTime > 0
        || Opacity < 1 || Popups.Count > 0 || (Bubble is not null && BubbleAge < 0.4)
        || (Bubble is not null && BubbleAge > Bubble.Duration - 0.4)
        || Math.Abs(Squash) > 0.003 || Math.Abs(_squashVelocity) > 0.02
        || WaveTime > 0 || YawnTime > 0 || StretchTime > 0 || WiggleTime > 0 || BounceTime > 0 || CrouchLevel > 0.01 || Crouch > 0
        || _lookSettling;

    /// <summary>La mirada aún se está moviendo hacia su objetivo.</summary>
    private bool _lookSettling;

    /// <summary>Golpe de las patas contra la plataforma; 1 equivale al límite de un aterrizaje perfecto.</summary>
    public void Impact(double strength) => _squashVelocity += Math.Clamp(strength, 0.2, 2.5) * 7;

    /// <summary>Impulso hacia arriba al despegar: el cuerpo se estira un instante sobre las patas.</summary>
    public void PushOff(double strength) => _squashVelocity -= Math.Clamp(strength, 0, 2) * 5;

    public void StartWave() => WaveTime = WaveDuration;

    public void StartYawn() => YawnTime = YawnDuration;

    public void StartStretch() => StretchTime = StretchDuration;

    public void StartWiggle() => WiggleTime = WiggleDuration;

    public void StartBounce() => BounceTime = BounceDuration;

    public void ShowBubble(Bubble bubble)
    {
        Bubble = bubble;
        BubbleAge = 0;
    }

    public void ClearBubble() => Bubble = null;

    /// <summary>Expresión de fondo, la que se mantiene mientras no haya otra temporal.</summary>
    public void SetBaseExpression(Expression expression)
    {
        _baseExpression = expression;
        if (_expressionTimer <= 0)
        {
            Expression = expression;
        }
    }

    /// <summary>Termina la expresión temporal que hubiera y vuelve a la de fondo.</summary>
    public void ClearFlash()
    {
        _expressionTimer = 0;
        Expression = _baseExpression;
    }

    /// <summary>Expresión temporal durante <paramref name="seconds"/> segundos.</summary>
    public void Flash(Expression expression, double seconds)
    {
        Expression = expression;
        _expressionTimer = seconds;
    }

    public void AddPopup(string text, Vec2 position, PopupStyle style)
    {
        if (Popups.Count >= MaxPopups)
        {
            Popups.RemoveAt(0);
        }

        Popups.Add(new ScorePopup(text, position, style));
    }

    public void Update(double dt, LanderBody body, in ThrustInput input)
    {
        if (_expressionTimer > 0)
        {
            _expressionTimer -= dt;
            if (_expressionTimer <= 0)
            {
                Expression = _baseExpression;
            }
        }

        if (Bubble is not null)
        {
            BubbleAge += dt;
            if (BubbleAge >= Bubble.Duration)
            {
                Bubble = null;
            }
        }

        var target = Vec2.Zero;
        if (LookTarget is { } point)
        {
            var delta = point - body.Position;
            var range = body.Height * 2.5;
            target = new Vec2(Math.Clamp(delta.X / range, -1, 1), Math.Clamp(delta.Y / range, -1, 1));
        }

        Look = MathUtil.Damp(Look, target, 8, dt);
        _lookSettling = (Look - target).LengthSquared > 0.0004;

        // Suspensión: muelle amortiguado (unas dos oscilaciones visibles y quieto en medio segundo).
        _squashVelocity += (-260 * Squash - 15 * _squashVelocity) * dt;
        Squash = Math.Clamp(Squash + _squashVelocity * dt, -0.45, 1.2);
        if (Math.Abs(Squash) < 0.003 && Math.Abs(_squashVelocity) < 0.02)
        {
            Squash = 0;
            _squashVelocity = 0;
        }

        WaveTime = Math.Max(0, WaveTime - dt);
        YawnTime = Math.Max(0, YawnTime - dt);
        StretchTime = Math.Max(0, StretchTime - dt);
        WiggleTime = Math.Max(0, WiggleTime - dt);
        BounceTime = Math.Max(0, BounceTime - dt);
        CrouchLevel = MathUtil.Damp(CrouchLevel, Math.Clamp(Crouch, 0, 1), Crouch > CrouchLevel ? 9 : 22, dt);
        if (CrouchLevel < 0.005 && Crouch <= 0)
        {
            CrouchLevel = 0;
        }

        Crouch = 0;

        var flying = body.IsFlying && body.Fuel > 0;
        var throttle = flying ? Math.Clamp(input.Throttle, 0, 1) : 0;
        if (body.IsLanded && input.Throttle > 0 && body.Fuel > 0)
        {
            throttle = Math.Clamp(input.Throttle, 0, 1);
        }

        MainFlame = MathUtil.Damp(MainFlame, throttle, throttle > MainFlame ? 25 : 12, dt);
        // Cuatro propulsores: trasladarse enciende los dos de un lado; girar, dos opuestos en diagonal.
        var lateral = flying ? Math.Clamp(input.Lateral, -1, 1) : 0;
        var rotate = flying ? Math.Clamp(input.Rotate, -1, 1) : 0;
        LeftJet = Jet(LeftJet, Math.Max(0, lateral) + Math.Max(0, rotate), dt);
        RightJet = Jet(RightJet, Math.Max(0, -lateral) + Math.Max(0, -rotate), dt);
        LowerLeftJet = Jet(LowerLeftJet, Math.Max(0, lateral) + Math.Max(0, -rotate), dt);
        LowerRightJet = Jet(LowerRightJet, Math.Max(0, -lateral) + Math.Max(0, rotate), dt);

        if (MainFlame < 0.005)
        {
            MainFlame = 0;
        }

        BoostFlash = Math.Max(0, BoostFlash - dt);
        CelebrateTime = Math.Max(0, CelebrateTime - dt);
        Opacity = Math.Min(1, Opacity + dt * 1.5);

        for (var i = Popups.Count - 1; i >= 0; i--)
        {
            Popups[i].Age += dt;
            if (Popups[i].Age >= ScorePopup.Lifetime)
            {
                Popups.RemoveAt(i);
            }
        }
    }
}
