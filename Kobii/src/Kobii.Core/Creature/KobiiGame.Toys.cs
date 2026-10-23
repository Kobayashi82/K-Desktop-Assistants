using Kobii.Core.Animation;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Creature;

/// <summary>
/// Sus juguetes (de Kobii AI): la pelota, que saca él solo de detrás de la espalda cuando le apetece jugar (la patea, la
/// persigue y luego la guarda), o la que le des tú, que se queda en el escritorio. Los coge con las manos de verdad: se
/// agacha, lo agarra con una mano y lo sube con las dos.
/// </summary>
public sealed partial class KobiiGame
{
    /// <summary>Lo que tarda en desaparecer un juguete que ha sacado él si se queda tirado (le han interrumpido).</summary>
    private const double EphemeralSeconds = 12;

    private readonly Playground _playground = new();

    /// <summary>Los juguetes del escritorio (para dibujarlos).</summary>
    public Playground Playground => _playground;

    /// <summary>Saca la pelota y juega con ella (o juega con la que haya cerca). False si ahora no puede.</summary>
    public bool PlayWithBall()
    {
        if (!CanPlay)
        {
            return false;
        }

        StartRoutine(ReachableToy(ToyKind.Ball) is { } ball ? new BallRoutine(this, ball) : new TakeOutRoutine(this, ToyKind.Ball));
        return true;
    }

    /// <summary>Un juguete de ese tipo libre y en su misma plataforma, a su alcance andando.</summary>
    private Toy? ReachableToy(ToyKind kind, double maxHeights = 18)
    {
        if (_playground.Find(kind) is not { } toy || !toy.Free || !toy.Body.IsGrounded || toy.Body.SupportId != Body.SupportId
            || Math.Abs(toy.Body.Feet.X - Body.Feet.X) > Body.Height * maxHeights)
        {
            return null;
        }

        return toy;
    }

    /// <summary>
    /// Cada paso: si guardan el juguete con el que jugaba, lo deja; y lo que sacó él y se ha quedado tirado (le han
    /// interrumpido) desaparece al rato con un ¡puf!.
    /// </summary>
    private void UpdateToys()
    {
        _playground.Size = _options.Size;
        if (Activity == Activity.Play && _routine?.UsedToy is { } used && !_playground.Toys.Contains(used))
        {
            EndRoutine();
        }

        foreach (var toy in _playground.Toys.ToList())
        {
            if (toy.Ephemeral && toy.Free && toy.RestSeconds > EphemeralSeconds && _routine?.UsedToy != toy)
            {
                _playground.Remove(toy.Kind);
                Visual.AddEmote(EmoteKind.Dust, toy.Body.Feet, Body.Scale, 5);
                Visual.AddEmote(EmoteKind.Sparkle, toy.Center, Body.Scale, 3);
            }
        }
    }

    /// <summary>
    /// Saca un juguete de detrás de la espalda (¡puf!): echa la mano atrás mirando por encima del hombro, la trae con el
    /// juguete, lo enseña en alto y lo deja caer delante; luego se pone a jugar con él (y al acabar lo guarda).
    /// </summary>
    private sealed class TakeOutRoutine(KobiiGame game, ToyKind kind) : Routine(game)
    {
        private const double RummageSeconds = 0.5;
        private const double ShowSeconds = 0.55;
        private const double DropSeconds = 0.35;

        private Toy? _toy;
        private bool _dropped;

        public override PlayKind Kind => PlayKind.TakeOut;

        public override Toy? UsedToy => _toy;

        public override void Begin()
        {
            Side = Game.Body.Support is { } support && support.Right - Game.Body.Feet.X < Game.Body.Feet.X - support.Left ? -1 : 1;
            Game.Body.WalkVelocity = 0;
        }

        public override void Stop()
        {
            // Si le interrumpen con la pelota en la mano, se le cae (y desaparece al rato).
            if (_toy is { Held: true } toy)
            {
                toy.Drop(Game.WorldPoint(toy.HeldLocal), new Vec2(Side * 40, -80) * Game.Body.Scale);
            }
        }

        protected override bool Update(World world, Sensors sensors, double dt)
        {
            var rig = default(Rig);
            var t = Time;
            var s = Side;
            var behind = new Vec2(-s * 0.32, -0.62);
            var up = new Vec2(s * 0.92, -1.30);
            if (t < RummageSeconds)
            {
                // La mano a la espalda, mirando por encima del hombro.
                var u = Easing.Segment(t, 0, RummageSeconds * 0.7);
                rig.SetHand(s, Easing.Arc(Rig.RestHand(s, Form), behind, new Vec2(0, 0.1), u), Form, front: false, grip: u);
                rig.HeadTilt = -s * 0.10 * u;
                Face(-s * 0.35);
                Game.Visual.LookTarget = new Vec2(-s * 0.8, 0.2);
                Game.Visual.SetExpression(Expression.Curious, 0.3);
            }
            else
            {
                if (_toy is null)
                {
                    // ¡Puf! Ya lo tiene.
                    _toy = new Toy(kind, Game.Body.Scale) { Ephemeral = true, Held = true, HeldLocal = behind };
                    _toy.PreviousHeldLocal = behind;
                    Game._playground.Adopt(_toy);
                    Game.Raise(GameEventKind.Pulled, text: "out");
                    Game.Visual.SetExpression(Expression.Joy, 1.2);
                }

                var r = _toy.Radius / Game.Unit;
                var show = Easing.Segment(t, RummageSeconds, ShowSeconds, Ease.Out);
                var drop = Easing.Segment(t, RummageSeconds + ShowSeconds, DropSeconds);
                var hand = Vec2.Lerp(Easing.Arc(behind, up, new Vec2(0, -0.3), show), new Vec2(s * 1.0, -0.62), drop);
                rig.SetHand(s, hand, Form, front: false, grip: 1 - drop);
                Face(s * 0.5);
                LookAt(Game.WorldPoint(hand));
                if (!_dropped)
                {
                    _toy.HeldLocal = hand - new Vec2(0, r * 0.9);
                    if (show >= 1 && Math.Abs(t - RummageSeconds - ShowSeconds) < dt)
                    {
                        Game.Visual.AddEmote(EmoteKind.Sparkle, Game.WorldPoint(_toy.HeldLocal), Game.Body.Scale, 4);
                    }

                    if (drop > 0.5)
                    {
                        _dropped = true;
                        _toy.Drop(Game.WorldPoint(_toy.HeldLocal), new Vec2(s * 70, -60) * Game.Body.Scale);
                    }
                }

                if (t >= RummageSeconds + ShowSeconds + DropSeconds)
                {
                    Game.StartRoutine(new BallRoutine(Game, _toy) { PutAwayAfter = true });
                    return true;
                }
            }

            Move(rig, s < 0 ? RigMask.LeftHand | RigMask.Head : RigMask.RightHand | RigMask.Head);
            return true;
        }
    }

    /// <summary>Guarda el juguete que sacó: va a por él, lo coge, lo sube y se lo lleva a la espalda (¡puf!, ya no está).</summary>
    private sealed class PutAwayRoutine(KobiiGame game, Toy toy) : ToyRoutine(game, toy)
    {
        private Phase _phase;

        private enum Phase
        {
            Approach,
            Reach,
            Lift,
            Hide,
        }

        public override PlayKind Kind => PlayKind.TakeOut;

        public override Pose? Pose => _phase == Phase.Reach ? Animation.Pose.Crouch : null;

        protected override bool Update(World world, Sensors sensors, double dt)
        {
            var rig = default(Rig);
            var mask = RigMask.Hands | RigMask.Head | RigMask.Face;
            switch (_phase)
            {
                case Phase.Approach:
                    mask = RigMask.None;
                    LookAt(Toy.Center);
                    if (!Toy.Free)
                    {
                        return false;
                    }

                    if (WalkUpTo(run: false, out var unreachable) && Math.Abs(Toy.Body.Velocity.X) < 40 * Game.Body.Scale)
                    {
                        NewPhase();
                        _phase = Phase.Reach;
                    }
                    else if (unreachable)
                    {
                        // No llega: ya desaparecerá sola.
                        return false;
                    }

                    break;

                case Phase.Reach:
                    if (Reach(ref rig, dt))
                    {
                        NewPhase();
                        _phase = Phase.Lift;
                    }

                    break;

                case Phase.Lift:
                    if (Lift(ref rig, dt, Chest, 0.4))
                    {
                        NewPhase();
                        _phase = Phase.Hide;
                    }

                    break;

                case Phase.Hide:
                {
                    // A la espalda con la mano de su lado; la otra la suelta.
                    PhaseTime += dt;
                    var u = Easing.Segment(PhaseTime, 0, 0.4);
                    var behind = new Vec2(-Side * 0.32, -0.62);
                    var hand = Easing.Arc(GripPoint(Chest), behind, new Vec2(0, 0.15), u);
                    rig.SetHand(Side, hand, Form, front: u < 0.4, grip: 1);
                    rig.SetHand(-Side, Vec2.Lerp(HandsAround(Chest).Other, Rig.RestHand(-Side, Form), u), Form, front: u < 0.3, grip: 1 - u);
                    Center = hand - new Vec2(-Side * R * 0.7, R * 0.35);
                    Place();
                    Face(-Side * 0.3);
                    Game.Visual.LookTarget = new Vec2(-Side * 0.7, 0.2);
                    if (PhaseTime >= 0.45)
                    {
                        Game._playground.Remove(Toy.Kind);
                        Game.Raise(GameEventKind.Pulled, text: "in");
                        Game.Visual.SetExpression(Expression.Happy, 1.2);
                        return false;
                    }

                    break;
                }
            }

            if (mask != RigMask.None)
            {
                Move(rig, mask);
            }

            return true;
        }
    }

    /// <summary>
    /// Base de los juegos con un juguete que hay que coger: llegar hasta él, agacharse, agarrarlo con una mano,
    /// subirlo con las dos, llevarlo (o abrazarlo) y dejarlo en el suelo.
    /// </summary>
    private abstract class ToyRoutine(KobiiGame game, Toy toy) : Routine(game)
    {
        private Vec2 _fromLead;
        private Vec2 _fromOther;
        private Vec2 _fromCenter;

        protected Toy Toy { get; } = toy;

        public override Toy? UsedToy => Toy;

        /// <summary>Radio del juguete en unidades de la criatura.</summary>
        protected double R => Toy.Radius / Game.Unit;

        /// <summary>Centro del juguete en las manos (en el espacio de la criatura).</summary>
        protected Vec2 Center { get; set; }

        /// <summary>Segundos desde que empezó el paso actual.</summary>
        protected double PhaseTime { get; set; }

        protected bool Holding => Toy.Held;

        /// <summary>Se lo pasa a otro juego sin soltarlo (del peluche a la cama).</summary>
        public bool HandingOver { get; set; }

        /// <summary>Lo lleva abrazado (el peluche) en vez de entre las manos (la pelota).</summary>
        protected bool Hug => Toy.Kind == ToyKind.Plush;

        public override void Begin()
        {
            Side = Math.Sign(Toy.Body.Feet.X - Game.Body.Feet.X) is var s && s != 0 ? s : 1;
        }

        public override void Stop()
        {
            if (HandingOver)
            {
                return;
            }

            if (Holding)
            {
                // Se le cae de las manos.
                Toy.Drop(Game.WorldPoint(Toy.HeldLocal), new Vec2(Side * 40 * Game.Body.Scale, -80 * Game.Body.Scale));
            }

        }

        /// <summary>Empieza un paso nuevo, saliendo de donde están ahora las manos y el juguete.</summary>
        protected void NewPhase()
        {
            _fromLead = Game.Animator.Rig.Hand(Side, Form);
            _fromOther = Game.Animator.Rig.Hand(-Side, Form);
            _fromCenter = Center;
            PhaseTime = 0;
        }

        /// <summary>El juguete en el suelo, en el espacio de la criatura (con su postura de ahora).</summary>
        protected Vec2 ToyLocal() =>
            CreatureSpace.ToLocal(Toy.Center, Game.Animator.Rig, Game.Body.Feet, Game.Unit, Game.Visual.Squash * (1 + Game.Animator.Rig.Stretch), Game.Visual.Lean);

        /// <summary>
        /// Anda (o corre) hasta tener el juguete delante, al alcance de la mano. Devuelve true al llegar; si no
        /// llega (está en otra plataforma), <paramref name="unreachable"/>.
        /// </summary>
        protected bool WalkUpTo(bool run, out bool unreachable)
        {
            unreachable = false;
            if (!Toy.Body.IsGrounded)
            {
                // Aún cae o bota: se acerca a donde está.
                Game._running = run;
                if (!Game.WalkTowards(Toy.Body.Feet.X - Side * Game.Unit * 1.6, run ? RunFactor : 1.2))
                {
                    Game.Body.WalkVelocity = 0;
                }

                return false;
            }

            if (Toy.Body.SupportId != Game.Body.SupportId)
            {
                unreachable = true;
                Game.Body.WalkVelocity = 0;
                return false;
            }

            var dx = Toy.Body.Feet.X - Game.Body.Feet.X;
            if (Math.Abs(dx) > Game.Unit * 0.3)
            {
                Side = Math.Sign(dx);
            }

            var reach = Game.Unit * (1.05 + R * 0.5);
            var target = Toy.Body.Feet.X - Side * reach;
            Game._running = run;
            if (Math.Abs(target - Game.Body.Feet.X) > 4 * Game.Body.Scale && Game.WalkTowards(target, run ? RunFactor : 1.2))
            {
                return false;
            }

            Game.Body.WalkVelocity = 0;
            if (Math.Abs(dx) > reach * 1.8)
            {
                // Se ha parado en el borde y no llega.
                unreachable = true;
                return false;
            }

            Game._running = false;
            return true;
        }

        /// <summary>Se agacha y lo agarra con la mano de su lado. Devuelve true cuando ya lo tiene.</summary>
        protected bool Reach(ref Rig rig, double dt, double duration = 0.5)
        {
            PhaseTime += dt;
            var u = Easing.Segment(PhaseTime, 0, duration);
            var at = ToyLocal();
            Center = at;
            var grip = GripPoint(at);
            rig.SetHand(Side, Easing.Arc(_fromLead, grip, new Vec2(0, -0.22), u), Form, front: false, grip: Easing.Segment(PhaseTime, duration * 0.75, duration * 0.3));
            rig.SetHand(-Side, Easing.Arc(_fromOther, Rig.RestHand(-Side, Form) + new Vec2(Side * 0.12, -0.10), new Vec2(0, -0.05), u), Form, front: false);
            rig.HeadTilt = Side * 0.10;
            rig.FaceY = 0.03 * u;
            Game.Visual.LookTarget = new Vec2(Side * 0.6, 0.9);
            if (PhaseTime < duration + 0.06)
            {
                return false;
            }

            Toy.Held = true;
            Toy.HeldLocal = at;
            Toy.PreviousHeldLocal = at;
            Toy.HeldTilt = 0;
            Toy.ThrownByYou = false;
            return true;
        }

        /// <summary>Lo sube al pecho; la otra mano se une a medio camino. Devuelve true al acabar.</summary>
        protected bool Lift(ref Rig rig, double dt, Vec2 chest, double duration = 0.5)
        {
            PhaseTime += dt;
            var u = Easing.Segment(PhaseTime, 0, duration);
            Center = Easing.Arc(_fromCenter, chest, new Vec2(Side * 0.08, -0.10), u);
            var join = Easing.Segment(PhaseTime, duration * 0.2, duration * 0.7);
            var (lead, other) = HandsAround(Center);
            rig.SetHand(Side, Vec2.Lerp(GripPoint(Center), lead, join), Form, front: false, grip: 1);
            rig.SetHand(-Side, Easing.Arc(_fromOther, other, new Vec2(0, -0.04), join), Form, front: false, grip: Easing.Segment(PhaseTime, duration * 0.75, duration * 0.25));
            SetFront(ref rig, Side, Easing.Segment(PhaseTime, duration * 0.2, duration * 0.4));
            SetFront(ref rig, -Side, join);
            Place();
            Game.Visual.LookTarget = new Vec2(Side * 0.3, 0.5);
            return PhaseTime >= duration;
        }

        /// <summary>Lo sujeta (o lo abraza) en <paramref name="center"/>.</summary>
        protected void HoldAt(ref Rig rig, Vec2 center, double grip = 1)
        {
            Center = center;
            var (lead, other) = HandsAround(center);
            rig.SetHand(Side, lead, Form, front: true, grip: grip);
            rig.SetHand(-Side, other, Form, front: true, grip: grip);
            Place();
        }

        /// <summary>Se agacha y lo deja en el suelo delante de él. Devuelve true cuando ya lo ha dejado.</summary>
        protected bool PutDown(ref Rig rig, double dt, double duration = 0.55)
        {
            PhaseTime += dt;
            var u = Easing.Segment(PhaseTime, 0, duration);
            var floor = Game.Body.Feet + new Vec2(Side * Game.Unit * (1.0 + R * 0.6), -Toy.Radius);
            var target = CreatureSpace.ToLocal(floor, Game.Animator.Rig, Game.Body.Feet, Game.Unit, Game.Visual.Squash * (1 + Game.Animator.Rig.Stretch), Game.Visual.Lean);
            if (Holding)
            {
                Center = Easing.Arc(_fromCenter, target, new Vec2(Side * 0.10, -0.06), u);
                var (lead, other) = HandsAround(Center);
                var open = Easing.Segment(PhaseTime, duration * 0.85, duration * 0.15);
                rig.SetHand(Side, Vec2.Lerp(lead, GripPoint(Center), u), Form, front: u < 0.5, grip: 1 - open);
                rig.SetHand(-Side, Easing.Arc(other, Rig.RestHand(-Side, Form), new Vec2(0, -0.04), Easing.Segment(PhaseTime, duration * 0.4, duration * 0.6)), Form, front: u < 0.4, grip: 1 - u);
                Place();
                if (PhaseTime >= duration && Game.Body.Support is { } support)
                {
                    Toy.PlaceOn(support, floor.X);
                    _fromLead = Game.Animator.Rig.Hand(Side, Form);
                }

                Game.Visual.LookTarget = new Vec2(Side * 0.6, 0.9);
                return false;
            }

            // Ya lo ha dejado: se incorpora con las manos vacías.
            rig.SetHand(Side, Easing.Arc(_fromLead, Rig.RestHand(Side, Form), new Vec2(0, -0.05), Easing.Segment(PhaseTime, duration, 0.3)), Form, front: false);
            return PhaseTime >= duration + 0.3;
        }

        /// <summary>Dónde agarra el juguete la primera mano: por su lado, un poco por debajo.</summary>
        protected Vec2 GripPoint(Vec2 center) => Hug
            ? center + new Vec2(-Side * R * 0.55, 0.04)
            : center + new Vec2(-Side * R * 0.7, R * 0.35);

        /// <summary>Las dos manos alrededor del juguete (o abrazándolo, cruzadas por delante).</summary>
        protected (Vec2 Lead, Vec2 Other) HandsAround(Vec2 center) => Hug
            ? (center + new Vec2(-Side * 0.06, 0.05), center + new Vec2(Side * 0.08, -0.04))
            : (center + new Vec2(Side * (R * 0.95 + 0.03), R * 0.1), center + new Vec2(-Side * (R * 0.95 + 0.03), R * 0.1));

        /// <summary>Delante del pecho (un peluche abrazado va algo más abajo y en medio).</summary>
        protected Vec2 Chest => Hug
            ? new Vec2(Side * 0.06 + Game.Visual.Facing * 0.10, -0.58)
            : new Vec2(Side * 0.06 + Game.Visual.Facing * 0.08, -0.62);

        protected void Place()
        {
            Toy.HeldLocal = Center;
            Toy.HeldTilt = Hug ? Side * 0.12 : 0;
        }

        protected static void SetFront(ref Rig rig, double side, double front)
        {
            if (side < 0)
            {
                rig.FrontL = front;
            }
            else
            {
                rig.FrontR = front;
            }
        }

        /// <summary>No llega: se queda mirándolo, lo señala y pone cara de pena.</summary>
        protected bool Stuck(ref Rig rig, double dt)
        {
            PhaseTime += dt;
            var side = Math.Sign(Toy.Body.Feet.X - Game.Body.Feet.X) is var s && s != 0 ? s : Side;
            Face(side * 0.6);
            LookAt(Toy.Center);
            var point = Gestures.Evaluate(Gesture.Point, PhaseTime, new PoseContext(side, Form));
            rig = Rig.Mix(rig, point, side < 0 ? RigMask.LeftHand : RigMask.RightHand, Easing.Segment(PhaseTime, 0.4, 0.3) * (1 - Easing.Segment(PhaseTime, 1.8, 0.3)));
            if (PhaseTime < dt * 1.5)
            {
                Game.Visual.SetExpression(Expression.Sad, 2.2);
            }

            return PhaseTime < 2.2;
        }
    }

    /// <summary>
    /// Jugar solo con la pelota: va hasta ella, la patea (con el pie, echando el cuerpo atrás), la mira rodar y
    /// la persigue para patearla otra vez; al final lo celebra.
    /// </summary>
    private sealed class BallRoutine(KobiiGame game, Toy ball) : ToyRoutine(game, ball)
    {
        private Phase _phase;
        private int _kicks;
        private int _wanted;
        private double _direction = 1;
        private bool _kicked;

        /// <summary>Ha jugado hasta el final (no se ha interrumpido ni se le ha ido la pelota).</summary>
        public bool Finished { get; private set; }

        /// <summary>La sacó él: al acabar la guarda.</summary>
        public bool PutAwayAfter { get; init; }

        private enum Phase
        {
            Approach,
            Kick,
            Watch,
            Celebrate,
            Stuck,
        }

        public override PlayKind Kind => PlayKind.Ball;

        public override bool AllowsAirborne => _phase == Phase.Celebrate;

        public override Pose? Pose => _phase == Phase.Celebrate ? Animation.Pose.Hop : null;

        public override void Begin()
        {
            base.Begin();
            _wanted = Random.Next(3) + 2;
            Game.Visual.SetExpression(Expression.Joy, 1);
        }

        protected override bool Update(World world, Sensors sensors, double dt)
        {
            var rig = default(Rig);
            var mask = RigMask.None;
            if (!Toy.Free)
            {
                return false;
            }

            switch (_phase)
            {
                case Phase.Approach:
                {
                    LookAt(Toy.Center);
                    if (Toy.Body.IsGrounded && Toy.Body.SupportId != Game.Body.SupportId)
                    {
                        NewPhase();
                        _phase = Phase.Stuck;
                        break;
                    }

                    // Se pone detrás de la pelota, hacia donde haya más sitio para patearla.
                    if (Game.Body.Support is { } support && Toy.Body.IsGrounded)
                    {
                        _direction = support.Right - Toy.Body.Feet.X > Toy.Body.Feet.X - support.Left ? 1 : -1;
                    }

                    var stand = Toy.Body.Feet.X - _direction * Game.Unit * 1.1;
                    Game._running = Math.Abs(stand - Game.Body.Feet.X) > Game.Body.Height * 1.5;
                    var slow = Math.Abs(Toy.Body.Velocity.X) < 50 * Game.Body.Scale && Toy.Body.IsGrounded;
                    if (!Game.WalkTowards(stand, Game._running ? RunFactor : 1.2) && slow)
                    {
                        Game.Body.WalkVelocity = 0;
                        Side = _direction;
                        Face(Side * 0.6);
                        NewPhase();
                        _kicked = false;
                        _phase = Phase.Kick;
                    }

                    break;
                }

                case Phase.Kick:
                {
                    // Coge impulso con el pie hacia atrás y patea; el cuerpo se echa atrás y los brazos se abren.
                    PhaseTime += dt;
                    rig = KickRig(PhaseTime, Side, Form);
                    mask = KickMask;
                    LookAt(Toy.Center);
                    if (!_kicked && PhaseTime >= 0.22)
                    {
                        _kicked = true;
                        var foot = Game.Body.Feet.X + Side * Game.Unit * 0.85;
                        if (Math.Abs(Toy.Body.Feet.X - foot) < Game.Unit * 0.8 + Toy.Radius)
                        {
                            var s = Game.Body.Scale;
                            Toy.Body.Launch(new Vec2(Side * Random.Range(170, 280) * s, -Random.Range(260, 400) * s));
                            Toy.Bounce(-1.6);
                            Game.Visual.AddEmote(EmoteKind.Dust, Toy.Body.Feet, s, 3);
                            Game.Visual.SetExpression(Expression.Joy, 1.2);
                            Game.Raise(GameEventKind.Kicked, Toy.Body.Velocity.Length / s);
                            _kicks++;
                        }
                        else
                        {
                            Game.Visual.SetExpression(Expression.Confused, 1);
                        }
                    }

                    if (PhaseTime >= 0.6)
                    {
                        NewPhase();
                        _phase = Phase.Watch;
                    }

                    break;
                }

                case Phase.Watch:
                    // La mira irse, y vuelta a por ella (o, si ya ha jugado bastante, lo celebra).
                    PhaseTime += dt;
                    LookAt(Toy.Center);
                    if (PhaseTime >= 0.45)
                    {
                        NewPhase();
                        if (_kicks >= _wanted)
                        {
                            _phase = Phase.Celebrate;
                            Game.Body.Hop(300);
                            Game.Visual.SetExpression(Expression.Joy, 1.2);
                        }
                        else
                        {
                            _phase = Phase.Approach;
                        }
                    }

                    break;

                case Phase.Celebrate:
                {
                    PhaseTime += dt;
                    var arms = Gestures.Evaluate(Gesture.ArmsUp, PhaseTime, new PoseContext(Side, Form));
                    rig = Rig.Mix(rig, arms, RigMask.Hands, Easing.Segment(PhaseTime, 0, 0.15) * (1 - Easing.Segment(PhaseTime, 0.6, 0.3)));
                    mask = RigMask.Hands;
                    if (PhaseTime >= 0.9 && Game.Body.IsGrounded)
                    {
                        Finished = true;
                        if (PutAwayAfter)
                        {
                            Game.StartRoutine(new PutAwayRoutine(Game, Toy));
                        }

                        return false;
                    }

                    break;
                }

                case Phase.Stuck:
                    mask = RigMask.Hands;
                    if (!Stuck(ref rig, dt))
                    {
                        return false;
                    }

                    break;
            }

            if (mask != RigMask.None)
            {
                Move(rig, mask);
            }

            return true;
        }
    }

    /// <summary>Partes que mueve la patada.</summary>
    private const RigMask KickMask = RigMask.Feet | RigMask.Body | RigMask.Hands;

    /// <summary>
    /// Una patada a la pelota <paramref name="t"/> segundos después de empezar: coge impulso con el pie hacia atrás y
    /// patea (el golpe es hacia los 0,22 s); el cuerpo se echa atrás y los brazos se abren.
    /// </summary>
    private static Rig KickRig(double t, double side, double form)
    {
        var rig = Gestures.Evaluate(Gesture.Kick, t, new PoseContext(side, form));
        var swing = Easing.Pulse(t - 0.12, 0.12, 0.35);
        rig.SetHand(-1, Rig.RestHand(-1, form) + new Vec2(-0.10 * swing, -0.18 * swing), form, front: false);
        rig.SetHand(1, Rig.RestHand(1, form) + new Vec2(0.10 * swing, -0.18 * swing), form, front: false);
        return rig;
    }
}
