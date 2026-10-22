using Kobii.Core.Geometry;

namespace Kobii.Core.Animation;

/// <summary>Lo que el animador necesita saber de la criatura en cada paso.</summary>
/// <param name="Form">Forma (1 cría … 5 mayor).</param>
/// <param name="WalkPhase">Fase del paso.</param>
/// <param name="Walk">Intensidad del paso (0–1).</param>
/// <param name="Facing">Hacia dónde gira la cara (−1..1).</param>
/// <param name="Hunch">Lo encorvado que va de por sí (los mayores).</param>
/// <param name="Velocity">Velocidad del cuerpo en el mundo, en unidades de la criatura por segundo (para las sacudidas).</param>
public readonly record struct AnimationContext(double Form, double WalkPhase = 0, double Walk = 0, double Facing = 0, double Hunch = 0, Vec2 Velocity = default);

/// <summary>
/// Mezcla las capas de la animación: la postura de base (la pone el juego según lo que hace: caminar,
/// dormir, caer…), el clip que está representando (una postura con su gesto y su objeto), gestos sueltos
/// encima (saludar al verte, rascarse) y, por último, lo que el juego mueve a mano (comer: la mano va a
/// por la comida y la lleva a la boca). Los cambios se funden y, al final, todo pasa por muelles: las
/// manos llegan con un poco de inercia, el cuerpo es blandito y el brote y las orejas se balancean con
/// cada sacudida. Así nada salta de una postura a otra.
/// </summary>
public sealed class Animator
{
    /// <summary>Lo que tarda en fundirse un cambio de postura.</summary>
    public const double BlendSeconds = 0.24;

    /// <summary>Lo que tarda en aparecer y desaparecer un gesto.</summary>
    private const double GestureFade = 0.22;

    /// <summary>Lo que tarda en entrar y salir lo que el juego mueve a mano.</summary>
    private const double OverrideFade = 0.16;

    private readonly RigFollow _follow = new();
    private Rig _from;
    private double _blend = 1;
    private Pose _basePose = Pose.Stand;
    private double _baseTime;
    private AnimationClip? _clip;
    private double _clipTime;
    private Gesture _gesture;
    private double _gestureTime;
    private double _gestureSeconds;
    private Rig _override;
    private RigMask _overrideMask;
    private double _overrideWeight;
    private bool _overrideFresh;
    private bool _snap = true;
    private double _appliedLift;

    /// <summary>El esqueleto que hay que dibujar.</summary>
    public Rig Rig { get; private set; }

    /// <summary>El esqueleto del paso anterior (para dibujar entre dos pasos de la simulación).</summary>
    public Rig PreviousRig { get; private set; }

    /// <summary>Lo que piden las capas antes de pasar por los muelles.</summary>
    public Rig Target { get; private set; }

    /// <summary>Postura de base (la de lo que está haciendo).</summary>
    public Pose BasePose => _basePose;

    /// <summary>Postura que se ve (la del clip, si hay uno).</summary>
    public Pose Pose => _clip?.Pose ?? _basePose;

    public AnimationClip? Clip => _clip;

    /// <summary>Segundos desde que empezó el clip (o la postura de base si no hay clip).</summary>
    public double Time => _clip is null ? _baseTime : _clipTime;

    /// <summary>El clip ha llegado a su final.</summary>
    public bool ClipDone => _clip is not null && _clipTime >= _clip.Seconds;

    /// <summary>Gesto suelto en curso.</summary>
    public Gesture Overlay => _gesture;

    /// <summary>Hacia dónde mira (−1 o 1): la mano que usa para los gestos de una mano.</summary>
    public double Side { get; set; } = 1;

    /// <summary>Objeto que lleva cuando no hay clip (el bastón de los mayores).</summary>
    public Prop BaseProp { get; set; }

    /// <summary>Lo que lleva puesto cuando no hay clip.</summary>
    public Wear BaseWear { get; set; }

    /// <summary>Objeto en la mano ahora mismo.</summary>
    public Prop Prop => _clip is { } clip ? clip.Prop : _gesture == Gesture.None || !Gestures.Info(_gesture).UsesHands ? BaseProp : Prop.None;

    /// <summary>
    /// Tiene las manos libres: de pie, sin nada en ellas, sin un gesto que las use y sin que una rutina las mueva
    /// a mano (para gestos sueltos como taparse la boca al estornudar).
    /// </summary>
    public bool HandsFree =>
        Poses.Info(Pose).Upright && Prop == Prop.None && (_overrideWeight <= 0 || (_overrideMask & RigMask.Hands) == 0)
        && (_clip is null || !Gestures.Info(_clip.Gesture).UsesHands)
        && (_gesture == Gesture.None || !Gestures.Info(_gesture).UsesHands);

    public Wear Wear => _clip is { Wear: not Wear.None } clip ? clip.Wear : BaseWear;

    /// <summary>La mano que sujeta el objeto (la del lado hacia el que mira).</summary>
    public double PropSide => Side;

    /// <summary>Lo que se eleva el cuerpo entero sobre sus pies (en unidades): dentro de la cama, sobre el colchón.</summary>
    public double Lift { get; set; }

    /// <summary>El juego está moviendo partes a mano (comer, beber, jugar).</summary>
    public bool Overridden => _overrideWeight > 0;

    /// <summary>Algo se está moviendo (hay que dibujar a la frecuencia alta).</summary>
    public bool IsMoving => _blend < 1 || _gesture != Gesture.None || _overrideWeight > 0 || _follow.Settling
        || _clip?.Lively == true || (_clip is null && Poses.Info(_basePose).Lively);

    /// <summary>
    /// Se mueve de verdad (un cambio de postura, un gesto que no se queda quieto, un clip que no para): lo de
    /// <see cref="IsMoving"/> sin los muelles que se asientan, el vaivén de estar de pie ni un gesto ya quieto (abrazar la
    /// almohada), que se ven bien a pocos fotogramas por segundo.
    /// </summary>
    public bool IsMovingMuch => _blend < 1 || (_gesture != Gesture.None && !AnimationClip.IsStill(_gesture)) || _overrideWeight > 0 || _clip?.Lively == true;

    /// <summary>Cambia la postura de base (se funde con la anterior).</summary>
    public void SetBasePose(Pose pose)
    {
        if (pose == _basePose)
        {
            return;
        }

        if (_clip is null)
        {
            StartBlend();
        }

        _basePose = pose;
        _baseTime = 0;
    }

    /// <summary>Empieza un clip (sustituye al que hubiera).</summary>
    public void Play(AnimationClip clip)
    {
        StartBlend();
        _clip = clip;
        _clipTime = 0;
    }

    /// <summary>Termina el clip y vuelve a la postura de base.</summary>
    public void StopClip()
    {
        if (_clip is null)
        {
            return;
        }

        StartBlend();
        _clip = null;
        _baseTime = 0;
    }

    /// <summary>Un gesto suelto encima de lo que esté haciendo.</summary>
    public void PlayGesture(Gesture gesture, double seconds)
    {
        _gesture = gesture;
        _gestureTime = 0;
        _gestureSeconds = seconds;
    }

    /// <summary>
    /// Mueve a mano las partes indicadas en este paso (por encima de todo lo demás). Hay que llamarlo en
    /// cada paso mientras dure: en cuanto se deja de llamar, esas partes vuelven solas a su animación.
    /// </summary>
    public void Override(in Rig rig, RigMask mask)
    {
        _override = rig;
        _overrideMask = mask;
        _overrideFresh = true;
    }

    /// <summary>Salta al final de las mezclas en curso y quita la inercia (vistas previas y tests).</summary>
    public void Snap()
    {
        _blend = 1;
        _overrideWeight = _overrideFresh ? 1 : 0;
        _snap = true;
    }

    public void Update(double dt, in AnimationContext context)
    {
        PreviousRig = Rig;
        _baseTime += dt;
        _clipTime += dt;
        _gestureTime += dt;
        if (_gesture != Gesture.None && _gestureTime >= _gestureSeconds)
        {
            _gesture = Gesture.None;
        }

        var pose = Pose;
        var c = new PoseContext(Side, context.Form, _clip?.Seconds ?? 0, context.WalkPhase, context.Walk, context.Facing);
        var target = Poses.Evaluate(pose, Time, c);
        if (context.Hunch > 0 && Poses.Info(pose).Upright)
        {
            target.Hunch = Math.Max(target.Hunch, context.Hunch);
        }

        if (_clip is { Gesture: not Gesture.None } clip)
        {
            var g = Gestures.Evaluate(clip.Gesture, _clipTime, c);
            if (clip.Prop != Prop.None && Gestures.Holds(clip.Gesture))
            {
                // Cierra la mano sobre lo que lleva (las dos si es de llevar con las dos manos).
                g.SetGrip(Side, 1);
                if (clip.Prop.TwoHanded() || clip.Gesture is Gesture.HoldBoth or Gesture.Carry or Gesture.Hug)
                {
                    g.SetGrip(-Side, 1);
                }
            }

            target = Rig.Mix(target, g, Gestures.Mask(clip.Gesture, Side), Fade(_clipTime, clip.Seconds));
        }

        if (_gesture != Gesture.None)
        {
            var g = Gestures.Evaluate(_gesture, _gestureTime, c with { Duration = _gestureSeconds });
            target = Rig.Mix(target, g, Gestures.Mask(_gesture, Side), Fade(_gestureTime, _gestureSeconds));
        }

        if (Prop == Prop.Cane && (_clip is null || _clip.Gesture == Gesture.Hold))
        {
            // Con bastón: la mano en el puño, que se mece un poco al andar.
            var hand = new Vec2(Side * (1.10 + 0.04 * Math.Sin(context.WalkPhase) * context.Walk), -0.66);
            target.SetHand(Side, hand, context.Form, front: false, grip: 1);
        }

        _overrideWeight = _overrideFresh
            ? Math.Min(1, _overrideWeight + dt / OverrideFade)
            : Math.Max(0, _overrideWeight - dt / OverrideFade);
        if (_overrideWeight > 0)
        {
            target = Rig.Mix(target, _override, _overrideMask, Poses.Smooth(_overrideWeight));
        }

        _overrideFresh = false;
        _blend = Math.Min(1, _blend + dt / BlendSeconds);
        Target = _blend >= 1 ? target : Rig.Lerp(_from, target, Poses.Smooth(_blend));
        _appliedLift = Lift;
        if (Lift != 0)
        {
            var lifted = Target;
            lifted.OffsetY -= Lift;
            Target = lifted;
        }

        if (_snap)
        {
            _snap = false;
            _follow.Reset(Target);
            Rig = Target;
            PreviousRig = Target;
            return;
        }

        Rig = _follow.Step(dt, Target, context);
    }

    /// <summary>Peso de un gesto: aparece y desaparece en unas décimas.</summary>
    private static double Fade(double t, double seconds)
    {
        var fadeIn = Math.Clamp(t / GestureFade, 0, 1);
        var fadeOut = seconds > 0 ? Math.Clamp((seconds - t) / GestureFade, 0, 1) : 1;
        return Poses.Smooth(Math.Min(fadeIn, fadeOut));
    }

    private void StartBlend()
    {
        // Se funde desde la postura sin la elevación (que se suma al final).
        _from = Target;
        _from.OffsetY += _appliedLift;
        var wrapped = MathUtil.WrapAngle(_from.Spin);
        _follow.ShiftSpin(wrapped - _from.Spin);
        _from.Spin = wrapped;
        _blend = 0;
    }
}

/// <summary>
/// Los muelles que siguen al esqueleto: inercia en las manos, el cuerpo blandito, la cabeza que llega un
/// poco tarde y el brote y las orejas que se balancean con las sacudidas del cuerpo.
/// </summary>
internal sealed class RigFollow
{
    private const double MaxAcceleration = 60;

    private SecondOrder2 _offset = new(4.5, 0.8, 0.4);
    private SecondOrder _spin = new(4, 0.75, 0.6);
    private SecondOrder _stretch = new(4.2, 0.42, 0);
    private SecondOrder _hunch = new(2, 1, 0);
    private SecondOrder2 _handL = new(3.6, 0.66, 0.9);
    private SecondOrder2 _handR = new(3.6, 0.66, 0.9);
    private SecondOrder _headTilt = new(3, 0.5, 0.4);
    private SecondOrder2 _face = new(6, 0.75, 1);
    private SecondOrder _earL = new(3.2, 0.35, 0);
    private SecondOrder _earR = new(3.2, 0.35, 0);
    private SecondOrder _sprout = new(2.6, 0.32, 0);
    private Jiggle _sway = new(110, 5.5);
    private Jiggle _bob = new(140, 6);
    private Vec2 _lastVelocity;
    private double _lastSpin;
    private double _energy;

    /// <summary>Los muelles aún se mueven (hay que seguir dibujando).</summary>
    public bool Settling => _energy > 1e-4;

    public void Reset(in Rig rig)
    {
        _offset.Reset(new Vec2(rig.OffsetX, rig.OffsetY));
        _spin.Reset(rig.Spin);
        _stretch.Reset(rig.Stretch);
        _hunch.Reset(rig.Hunch);
        _handL.Reset(new Vec2(rig.HandLX, rig.HandLY));
        _handR.Reset(new Vec2(rig.HandRX, rig.HandRY));
        _headTilt.Reset(rig.HeadTilt);
        _face.Reset(new Vec2(rig.FaceX, rig.FaceY));
        _earL.Reset(rig.EarL);
        _earR.Reset(rig.EarR);
        _sprout.Reset(rig.Sprout);
        _sway.Reset();
        _bob.Reset();
        _lastSpin = rig.Spin;
        _energy = 0;
    }

    /// <summary>El giro se ha llevado a su vuelta más corta: los muelles también (sin perder su velocidad).</summary>
    public void ShiftSpin(double delta)
    {
        if (Math.Abs(delta) < 1e-9)
        {
            return;
        }

        _spin.Shift(delta);
        _lastSpin += delta;
    }

    public Rig Step(double dt, in Rig target, in AnimationContext context)
    {
        var r = target;
        var offset = _offset.Step(dt, new Vec2(target.OffsetX, target.OffsetY));
        (r.OffsetX, r.OffsetY) = (offset.X, offset.Y);
        r.Spin = _spin.Step(dt, target.Spin);
        r.Stretch = _stretch.Step(dt, target.Stretch);
        r.Hunch = _hunch.Step(dt, target.Hunch);
        var handL = _handL.Step(dt, new Vec2(target.HandLX, target.HandLY));
        var handR = _handR.Step(dt, new Vec2(target.HandRX, target.HandRY));
        (r.HandLX, r.HandLY, r.HandRX, r.HandRY) = (handL.X, handL.Y, handR.X, handR.Y);
        r.HeadTilt = _headTilt.Step(dt, target.HeadTilt);
        var face = _face.Step(dt, new Vec2(target.FaceX, target.FaceY));
        (r.FaceX, r.FaceY) = (face.X, face.Y);

        // Sacudidas: al arrancar, frenar, girar o aterrizar, el brote y las orejas se quedan atrás y rebotan.
        var velocity = context.Velocity;
        var acceleration = dt > 0 ? ((velocity - _lastVelocity) / dt).ClampLength(MaxAcceleration) : Vec2.Zero;
        _lastVelocity = velocity;
        var spinVelocity = dt > 0 ? (r.Spin - _lastSpin) / dt : 0;
        _lastSpin = r.Spin;
        var sway = _sway.Step(dt, -acceleration.X * 1.6 - spinVelocity * 3);
        var bob = _bob.Step(dt, acceleration.Y * 1.4);
        r.Sprout = _sprout.Step(dt, target.Sprout) + sway * 0.9;
        r.EarL = _earL.Step(dt, target.EarL) + bob * 0.5 - sway * 0.4;
        r.EarR = _earR.Step(dt, target.EarR) + bob * 0.5 + sway * 0.4;

        _energy = Math.Abs(sway) + Math.Abs(bob) + Math.Abs(r.Stretch - target.Stretch) + (handL - new Vec2(target.HandLX, target.HandLY)).Length
            + (handR - new Vec2(target.HandRX, target.HandRY)).Length + Math.Abs(r.Spin - target.Spin) + (offset - new Vec2(target.OffsetX, target.OffsetY)).Length;
        return r;
    }
}
