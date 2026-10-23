using Kobii.Core.Animation;
using Kobii.Core.Geometry;

namespace Kobii.Core.Creature;

/// <summary>
/// Lo que necesitan las maniobras de las órdenes de K-Desktop (las dirige Kobii.Assistant): mientras dura un encargo no
/// decide nada por su cuenta, mira adonde se le dice, lleva en la mano o puesto lo que haga falta (la pelota que va a
/// lanzar, las gafas para leer la pantalla) y hace los gestos que se le piden.
/// </summary>
public sealed partial class KobiiGame
{
    private bool _errand;
    private Vec2? _errandLook;
    private Prop _heldProp;
    private Wear _heldWear;

    /// <summary>Está haciendo algo que le han pedido: no se pone a hacer cosas por su cuenta.</summary>
    public bool OnErrand => _errand;

    /// <summary>Puede ponerse con un encargo: en marcha, en el suelo (no cogido ni cayendo) y sin estar trepando.</summary>
    public bool CanRunErrands => IsInitialized && Body.IsGrounded && Activity is not (Activity.Held or Activity.Fall) && !IsClimbing;

    /// <summary>Lo que lleva en la mano sin que lo pida una animación (la pelota antes de lanzarla).</summary>
    public Prop HeldProp
    {
        get => _heldProp;
        set => _heldProp = value;
    }

    /// <summary>Lo que lleva puesto sin que lo pida una animación (las gafas al leer la pantalla).</summary>
    public Wear HeldWear
    {
        get => _heldWear;
        set => _heldWear = value;
    }

    /// <summary>La mano con la que lanza y señala (la del lado hacia el que mira), en el mundo.</summary>
    public Vec2 HandPoint => WorldPoint(Animator.Rig.Hand(Animator.Side, Species.Form));

    /// <summary>Deja lo que hacía por su cuenta y se pone a lo que le piden.</summary>
    public void BeginErrand()
    {
        _errand = true;
        Notice(0.01);
        if (Activity == Activity.Sleep)
        {
            WakeUp();
        }

        if (Activity is Activity.Perform or Activity.Celebrate or Activity.Attend or Activity.Play || (Activity == Activity.Travel && !IsClimbing))
        {
            SetActivity(Activity.Idle, 1);
        }
    }

    /// <summary>Ya ha terminado: guarda lo que llevaba y al rato vuelve a lo suyo.</summary>
    public void EndErrand()
    {
        if (!_errand)
        {
            return;
        }

        _errand = false;
        _errandLook = null;
        _heldProp = Prop.None;
        _heldWear = Wear.None;
        if (Activity == Activity.Idle)
        {
            _activityTimer = _random.Range(1.5, 3);
        }
    }

    /// <summary>Mira (y se vuelve) hacia un punto del escritorio; null = como siempre.</summary>
    public void LookAt(Vec2? point) => _errandLook = point;

    /// <summary>Hace un gesto hacia <paramref name="side"/> (−1 izquierda, 1 derecha) durante unos segundos.</summary>
    public void PlayGesture(Gesture gesture, double seconds, double side)
    {
        if (Activity == Activity.Walk)
        {
            SetActivity(Activity.Idle, 1);
        }

        Animator.Side = side < 0 ? -1 : 1;
        Visual.FacingTarget = Animator.Side * 0.6;
        Animator.PlayGesture(gesture, seconds);
    }

    /// <summary>Se para donde esté (iba andando a algún sitio y ya no hay tiempo).</summary>
    public void StopWalking()
    {
        if (Activity == Activity.Walk)
        {
            Body.WalkVelocity = 0;
            SetActivity(Activity.Idle, 1);
        }
    }

    /// <summary>Un brinco de sorpresa o de ánimo (como el de las reacciones).</summary>
    public void Hop()
    {
        if (Body.IsGrounded)
        {
            Body.Hop(380);
            Visual.Bounce(-2.5);
            Raise(GameEventKind.Hop);
        }
    }

    /// <summary>
    /// Salta de donde está con esa velocidad (se suelta de la ventana que arrastra cuando va a quedar pegada arriba, donde no
    /// cabe de pie): cae y aterriza como siempre.
    /// </summary>
    public void JumpOff(Vec2 velocity)
    {
        if (!Body.IsGrounded)
        {
            return;
        }

        Body.Launch(velocity);
        Visual.Bounce(-2.5);
        Visual.SetExpression(Expression.Joy, 1);
        Raise(GameEventKind.Hop);
    }

    /// <summary>Si hay un encargo mirando a un sitio, la mirada y el cuerpo van hacia él.</summary>
    private bool LookAtErrand()
    {
        if (_errandLook is not { } point)
        {
            return false;
        }

        var delta = (point - Body.Center) / (Body.Height * 2.5);
        Visual.LookTarget = delta.ClampLength(1);
        if (Activity == Activity.Idle && Animator.Overlay == Gesture.None)
        {
            Visual.FacingTarget = Math.Clamp(delta.X, -0.6, 0.6);
        }

        return true;
    }
}
