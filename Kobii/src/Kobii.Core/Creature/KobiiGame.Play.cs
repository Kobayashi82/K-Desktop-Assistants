using Kobii.Core.Animation;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Creature;

/// <summary>
/// Lo que hace paso a paso (el «jugar» de Kobii AI): la mariposa, la pelota, comer y beber, barrer, bailar, cambiarse de
/// ropa… Cada cosa es una rutina que avanza a cada paso y mueve a mano las partes del esqueleto que necesita, así las
/// manos van de verdad adonde tienen que ir (a la pelota, a la boca, a la mariposa).
/// </summary>
public sealed partial class KobiiGame
{
    private Routine? _routine;

    /// <summary>A qué está jugando ahora (null si no juega).</summary>
    public PlayKind? Playing => Activity == Activity.Play ? _routine?.Kind : null;

    /// <summary>Puede ponerse a jugar ahora: de pie, sin un encargo y sin que le estés hablando.</summary>
    private bool CanPlay =>
        IsInitialized && Body.IsGrounded && !_errand && Attention is Attention.None or Attention.Busy
        && Activity is Activity.Idle or Activity.Walk or Activity.Perform or Activity.Play or Activity.Sleep;

    /// <summary>Empieza una rutina (sustituye a la que hubiera).</summary>
    private void StartRoutine(Routine routine)
    {
        if (Activity == Activity.Sleep)
        {
            WakeUp();
        }

        var previous = _routine;
        _routine = null;
        previous?.Stop();
        if (Activity != Activity.Play)
        {
            SetActivity(Activity.Play, 0);
        }

        _routine = routine;
        routine.Begin();
    }

    /// <summary>Termina la rutina en curso y vuelve a estar tranquilo.</summary>
    private void EndRoutine(double idleSeconds = -1)
    {
        if (Activity == Activity.Play)
        {
            SetActivity(Activity.Idle, idleSeconds >= 0 ? idleSeconds : _random.Range(1.5, 3));
        }
        else
        {
            _routine?.Stop();
            _routine = null;
        }
    }

    private void StepPlay(World world, Sensors sensors, double dt)
    {
        var routine = _routine;
        if (routine is null)
        {
            SetActivity(Activity.Idle, 1);
            return;
        }

        // Le hablas: lo deja para atenderte (la pelota se queda donde esté, la mariposa se va).
        if (Attention is Attention.Listening or Attention.Thinking or Attention.Speaking or Attention.Waiting && Body.IsGrounded)
        {
            SetActivity(Activity.Attend, 0);
            return;
        }

        if (!routine.Step(world, sensors, dt) && _routine == routine)
        {
            EndRoutine();
        }
    }

    /// <summary>Al dejar de jugar (por lo que sea), la rutina suelta lo que tuviera en las manos.</summary>
    private void LeavePlay()
    {
        var routine = _routine;
        _routine = null;
        routine?.Stop();
    }

    /// <summary>Un punto del mundo en el espacio del cuerpo, sin el giro ni el desplazamiento del esqueleto.</summary>
    private Vec2 BodyLocal(Vec2 world) =>
        CreatureSpace.ToLocal(world, default, Body.Feet, Unit, Visual.Squash, Visual.Lean);

    /// <summary>Dónde está ahora una mano en el mundo (−1 la de la izquierda de la pantalla, 1 la de la derecha).</summary>
    public Vec2 HandPosition(double side) => WorldPoint(Animator.Rig.Hand(side, Species.Form));

    /// <summary>La boca, en el mundo.</summary>
    public Vec2 MouthPoint => WorldPoint(new Vec2(Visual.Facing * 0.16, -0.78));

    /// <summary>El punto más cercano a <paramref name="target"/> al que llega la mano de ese lado (con el brazo estirado).</summary>
    private Vec2 WithinReach(double side, Vec2 target, double reach = 0.95)
    {
        var shoulder = Rig.Shoulder(side, Species.Form, Visual.Facing);
        var arm = target - shoulder;
        return arm.Length <= reach ? target : shoulder + arm.Normalized() * reach;
    }

    /// <summary>Lado hacia el que está el cursor (o hacia el que mira, si no hay cursor cerca).</summary>
    private double SideTowardCursor(Sensors? sensors) =>
        sensors?.Cursor is { } cursor && Math.Abs(cursor.X - Body.Feet.X) > Body.HalfWidth * 0.3
            ? Math.Sign(cursor.X - Body.Feet.X)
            : Visual.FacingTarget < 0 ? -1 : 1;

    /// <summary>
    /// Una rutina: avanza paso a paso y mueve a mano lo que necesita. Cuando <see cref="Step"/> devuelve false, ha
    /// terminado y vuelve a estar tranquilo.
    /// </summary>
    private abstract class Routine(KobiiGame game)
    {
        protected KobiiGame Game { get; } = game;

        public abstract PlayKind Kind { get; }

        /// <summary>Segundos desde que empezó.</summary>
        public double Time { get; private set; }

        /// <summary>Postura de base mientras dura (null: la de siempre, o andar y correr si se mueve).</summary>
        public virtual Pose? Pose => null;

        /// <summary>Decide ella hacia dónde se mira (si no, mira al cursor o alrededor).</summary>
        public virtual bool ControlsLook => true;

        /// <summary>Puede estar en el aire sin que cuente como una caída (saltos).</summary>
        public virtual bool AllowsAirborne => false;

        /// <summary>El juguete con el que juega (si se guarda, deja de jugar).</summary>
        public virtual Toy? UsedToy => null;

        /// <summary>Lo que lleva en la mano mientras dura (null: nada, o lo de siempre).</summary>
        public virtual Prop? Prop => null;

        protected IRandom Random => Game._random;

        protected static double Form => Species.Form;

        protected double Side { get; set; } = 1;

        /// <summary>Se llama una vez al empezar.</summary>
        public virtual void Begin()
        {
        }

        /// <summary>Un paso. Devuelve false al terminar.</summary>
        public bool Step(World world, Sensors sensors, double dt)
        {
            Time += dt;
            return Update(world, sensors, dt);
        }

        /// <summary>Lo deja (terminado o interrumpido): suelta lo que tuviera en las manos.</summary>
        public virtual void Stop()
        {
        }

        /// <summary>Un clic sobre él mientras juega. True si la rutina lo usa (y entonces no es una caricia).</summary>
        public virtual bool Click() => false;

        protected abstract bool Update(World world, Sensors sensors, double dt);

        /// <summary>Mueve a mano las partes indicadas en este paso.</summary>
        protected void Move(in Rig rig, RigMask mask) => Game.Animator.Override(rig, mask);

        protected void Face(double facing)
        {
            Game.Visual.FacingTarget = facing;
            Game.Animator.Side = Side;
        }

        protected void LookAt(Vec2? world)
        {
            if (world is { } point)
            {
                var delta = (point - Game.Body.Center) / (Game.Body.Height * 2.5);
                Game.Visual.LookTarget = delta.ClampLength(1);
            }
        }
    }
}
