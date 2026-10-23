using Kobii.Core.Animation;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Creature;

/// <summary>
/// Los juguetes que le das tú (de Kobii AI): la pelota (si se la lanzas va a por ella y te la trae), el peluche (lo
/// abraza, lo pasea y duerme con él) y la cama (bota encima, se sienta o duerme en ella, acurrucado). Se quedan en el
/// escritorio hasta que los guardas; él los usa cuando le apetece.
/// </summary>
public sealed partial class KobiiGame
{
    /// <summary>Lo que espera un juguete tuyo en otro monitor antes de volver a su lado.</summary>
    private const double StrayToySeconds = 20;

    private Toy? _bed;
    private double _bedLift;
    private Toy? _cuddled;

    /// <summary>La cama en la que está (null si no está en ninguna).</summary>
    public Toy? Bed => _bed;

    /// <summary>Le das un juguete: cae cerca de él (la cama, un poco más lejos). Si ya lo tenía, se queda el que había.</summary>
    public Toy GiveToy(ToyKind kind, World world)
    {
        // La pelota que había sacado él pasa a ser tuya (no desaparece al rato).
        if (_playground.Find(kind) is { Ephemeral: true } own)
        {
            RemoveToy(own);
        }

        return _playground.Add(kind, world, new Vec2(FreeSpotFor(kind), Body.Feet.Y));
    }

    /// <summary>
    /// Dónde dejar caer un juguete nuevo: a su lado (la cama, algo más lejos), en el primer hueco de su plataforma que no
    /// pise otro juguete; hacia el lado con más sitio primero.
    /// </summary>
    private double FreeSpotFor(ToyKind kind)
    {
        var h = Body.Height;
        var scale = Body.Scale;
        var half = (kind == ToyKind.Bed ? Toy.BedWidthDip / 2 : kind == ToyKind.Plush ? Toy.PlushHeightDip * 0.42 : Toy.BallDiameterDip / 2) * scale;
        var support = Body.Support;
        var side = support is not null && support.Right - Body.Feet.X < Body.Feet.X - support.Left ? -1 : 1;
        var first = kind == ToyKind.Bed ? 2.2 : 1.6;
        foreach (var distance in new[] { first, first + 1.4, first + 2.8, first + 4.2 })
        {
            foreach (var s in new[] { side, -side })
            {
                var x = Body.Feet.X + s * distance * h;
                var fits = support is null || (x - half > support.Left && x + half < support.Right);
                var free = _playground.Toys.All(t => Math.Abs(t.Body.Feet.X - x) > t.Body.HalfWidth + half + 4 * scale);
                if (fits && free)
                {
                    return x;
                }
            }
        }

        return Body.Feet.X + side * first * h;
    }

    /// <summary>Guarda un juguete (¡puf!). False si no estaba.</summary>
    public bool PutAwayToy(ToyKind kind)
    {
        if (_playground.Find(kind) is not { } toy)
        {
            return false;
        }

        Visual.AddEmote(EmoteKind.Sparkle, toy.Held ? HeadPoint : toy.Center, Body.Scale, 4);
        RemoveToy(toy);
        return true;
    }

    /// <summary>Quita un juguete del escritorio dejando lo que hacía con él.</summary>
    private void RemoveToy(Toy toy)
    {
        if (_routine?.UsedToy == toy || (_routine is BedRoutine bedRoutine && bedRoutine.Carrying == toy))
        {
            EndRoutine();
        }

        if (_bed == toy)
        {
            _bed = null;
        }

        if (_cuddled == toy)
        {
            _cuddled = null;
        }

        _playground.Remove(toy.Kind);
    }

    /// <summary>La pelota que acabas de lanzar, si sigue libre.</summary>
    private Toy? ThrownBall() => _playground.Find(ToyKind.Ball) is { ThrownByYou: true } ball && ball.Free ? ball : null;

    /// <summary>Lo de los juguetes que le das, en cada paso: ir a por la pelota que lanzas, la cama y el peluche.</summary>
    private void UpdateGifts()
    {
        // ¡La has lanzado! Lo deja todo (salvo otro juego con un juguete que no sea la cama) y va a por ella.
        if (ThrownBall() is { } ball && CanPlay && Activity != Activity.Sleep && _routine is not FetchRoutine
            && (Activity != Activity.Play || _routine is BallRoutine or PlushRoutine))
        {
            StartRoutine(new FetchRoutine(this, ball));
        }

        // Si le quitan la cama (o la cogen con el ratón, o se ha alejado de ella), se queda fuera.
        if (_bed is { } bed && (!_playground.Toys.Contains(bed) || !bed.Free || Math.Abs(Body.Feet.X - bed.Body.Feet.X) > bed.Body.HalfWidth * 1.2))
        {
            _bed = null;
        }

        // Un juguete tuyo que ha acabado en otro monitor (rodando o al caerse por un borde) vuelve a su lado al rato: ¡puf!
        if (Body.IsGrounded && Body.Support is { } here && Activity != Activity.Sleep)
        {
            foreach (var toy in _playground.Toys)
            {
                if (!toy.Ephemeral && toy.Free && toy.RestSeconds > StrayToySeconds && _world.MonitorAt(toy.Center) is { } there && there.Index != here.MonitorIndex)
                {
                    Visual.AddEmote(EmoteKind.Sparkle, toy.Center, Body.Scale, 3);
                    toy.PlaceOn(here, FreeSpotFor(toy.Kind));
                    Visual.AddEmote(EmoteKind.Sparkle, toy.Center, Body.Scale, 3);
                }
            }
        }

        // Si le quitan el peluche mientras duerme abrazado a él, ya no lo tiene.
        if (_cuddled is { } plush && (!_playground.Toys.Contains(plush) || !plush.Held))
        {
            _cuddled = null;
        }
    }

    /// <summary>Cuánto se eleva sobre el suelo (en unidades): dentro de la cama, el colchón.</summary>
    private void UpdateBedLift(double dt)
    {
        var lift = 0.0;
        if (_bed is { } bed)
        {
            // Más elevado cuanto más dentro de la cama (así entra y sale sin saltos).
            var inside = 1 - Poses.Smooth((Math.Abs(Body.Feet.X - bed.Body.Feet.X) - bed.Body.HalfWidth * 0.45) / (bed.Body.HalfWidth * 0.5));
            lift = bed.BedLift / Unit * inside * bed.Squash;
        }

        _bedLift = MathUtil.Damp(_bedLift, lift, 16, dt);
        Animator.Lift = _bedLift;
    }

    /// <summary>Durmiendo abrazado al peluche: las manos lo rodean.</summary>
    private void HugWhileAsleep()
    {
        if (_cuddled is not { Held: true } plush)
        {
            return;
        }

        var side = Animator.Side;
        var center = new Vec2(side * 0.10, -0.50);
        plush.HeldLocal = center;
        plush.HeldTilt = side * 0.25;
        var rig = default(Rig);
        rig.SetHand(side, center + new Vec2(-side * 0.06, 0.04), Form, front: true, grip: 0.7);
        rig.SetHand(-side, center + new Vec2(side * 0.08, -0.04), Form, front: true, grip: 0.7);
        Animator.Override(rig, RigMask.Hands);
    }

    /// <summary>Al despertarse con el peluche abrazado, lo deja a su lado.</summary>
    private void PutDownCuddled()
    {
        if (_cuddled is not { } plush)
        {
            return;
        }

        _cuddled = null;
        if (plush.Held)
        {
            plush.Drop(WorldPoint(plush.HeldLocal), new Vec2(Animator.Side * 70 * Body.Scale, -60 * Body.Scale));
        }
    }

    /// <summary>Antes de ir a otro sitio, si está en la cama, sale de un saltito.</summary>
    private bool HopOutOfBed()
    {
        if (_bed is not { } bed || !Body.IsGrounded || Body.Support is not { } support)
        {
            return false;
        }

        var side = bed.Body.Feet.X - support.Left > support.Right - bed.Body.Feet.X ? -1 : 1;
        var distance = bed.Body.HalfWidth + Body.HalfWidth * 0.9 - side * (Body.Feet.X - bed.Body.Feet.X);
        Body.Launch(new Vec2(side * distance / 0.32, -300 * Body.Scale));
        Visual.FacingTarget = side;
        Visual.Bounce(-1.5);
        _bed = null;
        Raise(GameEventKind.Hop);
        return true;
    }

    /// <summary>Juega ahora con un juguete que le has dado: abrazar el peluche (o pasearlo) o botar en la cama (o sentarse).</summary>
    public bool PlayWith(ToyKind kind, bool alternative = false) => kind switch
    {
        ToyKind.Plush => PlayWithGift(alternative ? "peluche-pasear" : "peluche-abrazar"),
        ToyKind.Bed => PlayWithGift(alternative ? "cama-sentarse" : "cama-botar"),
        _ => PlayWithBall(),
    };

    /// <summary>Un juego con los juguetes que le has dado, por su cuenta. False si ese no es de juguetes o no puede.</summary>
    private bool PlayWithGift(string id)
    {
        Routine? routine = id switch
        {
            "peluche-abrazar" when ReachableToy(ToyKind.Plush) is { } plush => new PlushRoutine(this, plush, PlushPlan.Hug),
            "peluche-pasear" when ReachableToy(ToyKind.Plush) is { } plush => new PlushRoutine(this, plush, PlushPlan.Stroll),
            "cama-botar" when ReachableToy(ToyKind.Bed) is { } bed => new BedRoutine(this, bed, BedPlan.Bounce),
            "cama-sentarse" when ReachableToy(ToyKind.Bed) is { } bed => new BedRoutine(this, bed, BedPlan.Sit),
            _ => null,
        };

        if (routine is null || !CanPlay)
        {
            return false;
        }

        StartRoutine(routine);
        return true;
    }

    /// <summary>
    /// Tiene sueño: si tiene el peluche o la cama cerca, va a por ellos antes de dormirse. Devuelve true si se ha puesto
    /// en camino.
    /// </summary>
    private bool GoToSleepWithToys()
    {
        if (_bed is not null || !CanPlay || Activity == Activity.Sleep)
        {
            return false;
        }

        if (ReachableToy(ToyKind.Plush) is { } plush && _cuddled is null)
        {
            StartRoutine(new PlushRoutine(this, plush, PlushPlan.Sleep));
            return true;
        }

        if (ReachableToy(ToyKind.Bed) is { } bed)
        {
            StartRoutine(new BedRoutine(this, bed, BedPlan.Sleep));
            return true;
        }

        return false;
    }

    /// <summary>Se duerme en el sitio en que esté, abrazado al peluche si lo lleva.</summary>
    private void FallAsleepNow(Toy? plush)
    {
        if (plush is { Held: true })
        {
            _cuddled = plush;
        }

        Animator.Side = Visual.FacingTarget < 0 ? -1 : 1;
        FallAsleep();
    }

    /// <summary>
    /// Ir a por la pelota que le lanzas: la sigue con la mirada, sale corriendo (trepando o saltando si cae en otra
    /// ventana), la coge, te la trae a los pies —donde tengas el cursor, o desde donde la lanzaste— y te mira moviendo el
    /// culete: «¡otra vez!».
    /// </summary>
    private sealed class FetchRoutine(KobiiGame game, Toy ball) : ToyRoutine(game, ball)
    {
        private Phase _phase;
        private double _destination;
        private RouteRunner? _runner;

        private enum Phase
        {
            Watch,
            Chase,

            /// <summary>La pelota ha caído en otra plataforma: va hasta allí (trepando, saltando o bajando).</summary>
            Travel,

            Reach,
            Lift,
            Return,
            PutDown,
            Wag,
            Stuck,
        }

        public override PlayKind Kind => PlayKind.Fetch;

        public override Pose? Pose => _phase switch
        {
            Phase.Reach or Phase.PutDown => Animation.Pose.Crouch,
            Phase.Travel => _runner?.Pose,
            _ => null,
        };

        public override bool AllowsAirborne => _phase == Phase.Travel;

        public override void Begin()
        {
            base.Begin();
            Game.Visual.SetExpression(Expression.Surprised, 0.5);
            Game.Visual.Bounce(1.5);
        }

        public override void Stop()
        {
            _runner?.Abort();
            base.Stop();
        }

        /// <summary>Cómo llegar hasta la pelota, que está en otra plataforma (null si no sabe llegar).</summary>
        private RouteRunner? RouteToBall(World world)
        {
            if (Toy.Body.Support is not { } there)
            {
                return null;
            }

            var side = Math.Sign(Toy.Body.Feet.X - Game.Body.Feet.X) is var s && s != 0 ? s : 1;
            var x = Toy.Body.Feet.X - side * Game.Unit * 1.4;
            var target = Navigation.Canonical(world, there);
            return Navigation.Route(world, Game.Body, target, x, Game.MoveReach()) is { } route ? new RouteRunner(Game, target, x, route, run: true) : null;
        }

        protected override bool Update(World world, Sensors sensors, double dt)
        {
            var rig = default(Rig);
            var mask = RigMask.None;
            switch (_phase)
            {
                case Phase.Watch:
                    // La sigue con la mirada, con ganas de salir corriendo.
                    PhaseTime += dt;
                    LookAt(Toy.Center);
                    Face(Math.Sign(Toy.Center.X - Game.Body.Feet.X) * 0.6);
                    Game.Visual.SetExpression(Expression.Joy, 0.3);
                    if (PhaseTime > 0.3)
                    {
                        _phase = Phase.Chase;
                    }

                    break;

                case Phase.Chase:
                    if (!Toy.Free)
                    {
                        return false;
                    }

                    LookAt(Toy.Center);
                    if (WalkUpTo(run: true, out var unreachable) && Math.Abs(Toy.Body.Velocity.X) < 60 * Game.Body.Scale)
                    {
                        NewPhase();
                        _phase = Phase.Reach;
                    }
                    else if (unreachable && Toy.Body.IsGrounded && Math.Abs(Toy.Body.Velocity.X) < 30 * Game.Body.Scale)
                    {
                        // En otra plataforma: si sabe llegar (trepando, saltando o bajando), va a por ella.
                        _runner = RouteToBall(world);
                        if (_runner is null)
                        {
                            Toy.ThrownByYou = false;
                            NewPhase();
                            _phase = Phase.Stuck;
                        }
                        else
                        {
                            _phase = Phase.Travel;
                        }
                    }

                    break;

                case Phase.Travel:
                    if (!Toy.Free)
                    {
                        return false;
                    }

                    switch (_runner!.Step(world, dt, ref rig, ref mask))
                    {
                        case TravelOutcome.Arrived:
                            _runner = null;
                            _phase = Phase.Chase;
                            break;
                        case TravelOutcome.Failed:
                            _runner = null;
                            Toy.ThrownByYou = false;
                            NewPhase();
                            _phase = Phase.Stuck;
                            break;
                        default:
                            if (!_runner.Climbing)
                            {
                                LookAt(Toy.Center);
                            }

                            break;
                    }

                    break;

                case Phase.Reach:
                    if (!Toy.Free && !Holding)
                    {
                        return false;
                    }

                    mask = RigMask.Hands | RigMask.Head | RigMask.Face;
                    if (Reach(ref rig, dt))
                    {
                        Toy.ThrownByYou = false;
                        Game.Visual.SetExpression(Expression.Joy, 1.5);
                        NewPhase();
                        _phase = Phase.Lift;
                    }

                    break;

                case Phase.Lift:
                    mask = RigMask.Hands | RigMask.Head | RigMask.Face;
                    if (Lift(ref rig, dt, Chest))
                    {
                        _destination = Destination(sensors);
                        NewPhase();
                        _phase = Phase.Return;
                    }

                    break;

                case Phase.Return:
                {
                    // Te la trae a paso alegre, con la pelota delante del pecho.
                    PhaseTime += dt;
                    HoldAt(ref rig, Chest + new Vec2(0, 0.02 * Math.Sin(Game.Visual.WalkPhase * 2)));
                    mask = RigMask.Hands | RigMask.Head | RigMask.Face;
                    LookAt(sensors.Cursor);
                    Game._running = false;
                    if (PhaseTime > 8 || !Game.WalkTowards(_destination, 1.15))
                    {
                        Game.Body.WalkVelocity = 0;
                        Side = Game.SideTowardCursor(sensors);
                        Face(Side * 0.5);
                        NewPhase();
                        _phase = Phase.PutDown;
                    }

                    break;
                }

                case Phase.PutDown:
                    mask = RigMask.Hands | RigMask.Head | RigMask.Face;
                    if (PutDown(ref rig, dt))
                    {
                        Game.Raise(GameEventKind.Fetched);
                        Game.Visual.SetExpression(Expression.Joy, 1.6);
                        NewPhase();
                        _phase = Phase.Wag;
                    }

                    break;

                case Phase.Wag:
                {
                    // Te mira moviendo el culete: «¡otra vez!».
                    PhaseTime += dt;
                    LookAt(sensors.Cursor);
                    var wiggle = Gestures.Evaluate(Gesture.Wiggle, PhaseTime, new PoseContext(Side, Form));
                    var w = Easing.Segment(PhaseTime, 0, 0.2) * (1 - Easing.Segment(PhaseTime, 1.1, 0.3));
                    rig.Spin = wiggle.Spin * w;
                    rig.OffsetX = wiggle.OffsetX * w;
                    mask = RigMask.Body;
                    if (PhaseTime >= 1.4)
                    {
                        return false;
                    }

                    break;
                }

                case Phase.Stuck:
                    mask = RigMask.Hands | RigMask.Head | RigMask.Face;
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

        /// <summary>A tus pies: donde está el cursor si está cerca de su plataforma; si no, desde donde la lanzaste.</summary>
        private double Destination(Sensors sensors)
        {
            var feet = Game.Body.Feet;
            var x = sensors.Cursor is { } cursor && cursor.Y > feet.Y - Game.Body.Height * 8 && cursor.Y < feet.Y + Game.Body.Height * 2
                ? cursor.X
                : Toy.ThrowOrigin.X;
            if (Game.Body.Support is { } support)
            {
                var margin = Game.Body.HalfWidth + 6 * Game.Body.Scale;
                x = Math.Clamp(x, support.Left + margin, Math.Max(support.Left + margin, support.Right - margin));
            }

            var side = Math.Sign(x - feet.X);
            return x - side * Game.Body.Height * 0.9;
        }
    }

    /// <summary>Lo que quiere hacer con el peluche.</summary>
    private enum PlushPlan
    {
        /// <summary>Abrazarlo un rato, meciéndose.</summary>
        Hug,

        /// <summary>Llevarlo de paseo abrazado.</summary>
        Stroll,

        /// <summary>Llevárselo para dormir con él (a la cama si la hay).</summary>
        Sleep,
    }

    /// <summary>El peluche: lo coge, lo abraza meciéndose con corazones, lo pasea o se lo lleva a dormir.</summary>
    private sealed class PlushRoutine(KobiiGame game, Toy plush, PlushPlan plan) : ToyRoutine(game, plush)
    {
        private Phase _phase;
        private double _hugSeconds;
        private double _heartTimer = 0.6;
        private double _strollTo = double.NaN;

        private enum Phase
        {
            Approach,
            Reach,
            Lift,
            Hug,
            Stroll,
            PutDown,
        }

        public override PlayKind Kind => PlayKind.Plush;

        public override Pose? Pose => _phase is Phase.Reach or Phase.PutDown ? Animation.Pose.Crouch : null;

        public override void Begin()
        {
            base.Begin();
            _hugSeconds = Random.Range(4, 6.5);
        }

        protected override bool Update(World world, Sensors sensors, double dt)
        {
            var rig = default(Rig);
            var mask = RigMask.Hands | RigMask.Head | RigMask.Face;
            switch (_phase)
            {
                case Phase.Approach:
                    if (!Toy.Free)
                    {
                        return false;
                    }

                    LookAt(Toy.Center);
                    if (WalkUpTo(run: false, out var unreachable))
                    {
                        Game.Visual.SetExpression(Expression.Love, 0.8);
                        NewPhase();
                        _phase = Phase.Reach;
                    }
                    else if (unreachable)
                    {
                        return false;
                    }

                    break;

                case Phase.Reach:
                    if (!Toy.Free && !Holding)
                    {
                        return false;
                    }

                    if (Reach(ref rig, dt, 0.55))
                    {
                        NewPhase();
                        _phase = Phase.Lift;
                    }

                    break;

                case Phase.Lift:
                    if (Lift(ref rig, dt, Chest, 0.55))
                    {
                        NewPhase();
                        if (plan == PlushPlan.Sleep)
                        {
                            Sleep();
                            return true;
                        }

                        if (plan == PlushPlan.Hug)
                        {
                            Game.Raise(GameEventKind.Hugged);
                        }

                        _phase = plan == PlushPlan.Stroll ? Phase.Stroll : Phase.Hug;
                    }

                    break;

                case Phase.Hug:
                {
                    // Lo abraza fuerte, meciéndose de un lado a otro, con los ojos cerrados de gusto.
                    PhaseTime += dt;
                    var sway = Math.Sin(PhaseTime * 2.4);
                    HoldAt(ref rig, Chest + new Vec2(0.02 * sway, 0));
                    rig.Spin = 0.06 * sway;
                    rig.HeadTilt = 0.12 * sway;
                    rig.FaceY = 0.02;
                    mask |= RigMask.Body;
                    Toy.HeldTilt = Side * 0.12 + 0.10 * sway;
                    Game.Visual.SetExpression(Expression.Love, 0.4);
                    Game.Visual.LookTarget = new Vec2(0, 0.5);
                    Face(0.15 * sway);
                    _heartTimer -= dt;
                    if (_heartTimer <= 0)
                    {
                        _heartTimer = Random.Range(1.1, 1.8);
                        Game.Visual.AddEmote(EmoteKind.Heart, Game.HeadPoint, Game.Body.Scale);
                    }

                    if (PhaseTime >= _hugSeconds)
                    {
                        NewPhase();
                        _phase = Phase.PutDown;
                    }

                    break;
                }

                case Phase.Stroll:
                {
                    // Lo pasea abrazado.
                    PhaseTime += dt;
                    HoldAt(ref rig, Chest + new Vec2(0, 0.015 * Math.Sin(Game.Visual.WalkPhase * 2)));
                    Game.Visual.SetExpression(Expression.Happy, 0.4);
                    if (double.IsNaN(_strollTo) && Game.Body.Support is { } support)
                    {
                        var margin = Game.Body.HalfWidth + 6 * Game.Body.Scale;
                        _strollTo = Math.Clamp(Game.Body.Feet.X + Random.Range(120, 320) * Game.Body.Scale * Random.Sign(), support.Left + margin, Math.Max(support.Left + margin, support.Right - margin));
                    }

                    if (PhaseTime > 8 || !Game.WalkTowards(_strollTo, 0.9))
                    {
                        Game.Body.WalkVelocity = 0;
                        NewPhase();
                        _phase = Phase.PutDown;
                    }

                    break;
                }

                case Phase.PutDown:
                    if (PutDown(ref rig, dt))
                    {
                        return false;
                    }

                    break;
            }

            Move(rig, mask);
            return true;
        }

        /// <summary>A dormir con él: a la cama si la hay cerca; si no, aquí mismo.</summary>
        private void Sleep()
        {
            HandingOver = true;
            if (Game.ReachableToy(ToyKind.Bed) is { } bed)
            {
                Game.StartRoutine(new BedRoutine(Game, bed, BedPlan.Sleep, Toy));
                return;
            }

            Game.FallAsleepNow(Toy);
        }
    }

    /// <summary>Lo que quiere hacer en la cama.</summary>
    private enum BedPlan
    {
        /// <summary>Meterse, dar un par de vueltas para acomodarse y dormirse.</summary>
        Sleep,

        /// <summary>Botar encima como en una cama elástica.</summary>
        Bounce,

        /// <summary>Sentarse un rato en ella.</summary>
        Sit,
    }

    /// <summary>
    /// La cama: va hasta ella y se mete de un saltito. Para dormir, da un par de vueltas para acomodarse y se acurruca;
    /// para jugar, bota encima cada vez más alto; o se sienta un rato balanceando los pies. Al acabar, sale de otro saltito.
    /// </summary>
    private sealed class BedRoutine(KobiiGame game, Toy bed, BedPlan plan, Toy? carrying = null) : Routine(game)
    {
        private Phase _phase;
        private double _t;
        private int _hops;
        private int _wanted;
        private bool _wasGrounded = true;
        private bool _launched;

        private enum Phase
        {
            Approach,
            HopIn,
            Settle,
            Bounce,
            Sit,
            HopOut,
        }

        public override PlayKind Kind => PlayKind.Bed;

        public override Toy? UsedToy => bed;

        /// <summary>El peluche que lleva abrazado para dormir con él.</summary>
        public Toy? Carrying => carrying;

        public override bool AllowsAirborne => _phase is Phase.HopIn or Phase.Bounce or Phase.HopOut;

        public override Pose? Pose => _phase switch
        {
            Phase.Settle when _t > 1.1 => Animation.Pose.Curl,
            Phase.Bounce when !Game.Body.IsGrounded => Animation.Pose.Hop,
            Phase.Sit => Animation.Pose.Sit,
            _ => null,
        };

        public override void Begin()
        {
            _wanted = Random.Next(3) + 4;
            Side = Math.Sign(bed.Body.Feet.X - Game.Body.Feet.X) is var s && s != 0 ? s : 1;
        }

        public override void Stop()
        {
            if (carrying is { Held: true } && Game._cuddled != carrying)
            {
                carrying.Drop(Game.WorldPoint(carrying.HeldLocal), new Vec2(Side * 40 * Game.Body.Scale, -80 * Game.Body.Scale));
            }
        }

        protected override bool Update(World world, Sensors sensors, double dt)
        {
            _t += dt;
            if (!bed.Free || (Game._bed is null && _phase is Phase.Settle or Phase.Bounce or Phase.Sit))
            {
                return false;
            }

            var rig = default(Rig);
            var mask = RigMask.None;
            if (carrying is { Held: true })
            {
                // Lleva el peluche abrazado todo el rato.
                var center = new Vec2(Side * 0.06 + Game.Visual.Facing * 0.10, -0.58);
                carrying.HeldLocal = center;
                carrying.HeldTilt = Side * 0.12;
                rig.SetHand(Side, center + new Vec2(-Side * 0.06, 0.05), Form, front: true, grip: 0.7);
                rig.SetHand(-Side, center + new Vec2(Side * 0.08, -0.04), Form, front: true, grip: 0.7);
                mask |= RigMask.Hands;
            }

            switch (_phase)
            {
                case Phase.Approach:
                {
                    // Hasta el borde de la cama.
                    if (bed.Body.SupportId != Game.Body.SupportId)
                    {
                        return false;
                    }

                    Side = Math.Sign(bed.Body.Feet.X - Game.Body.Feet.X) is var s && s != 0 ? s : Side;
                    var edge = bed.Body.Feet.X - Side * (bed.Body.HalfWidth + Game.Body.HalfWidth * 0.7);
                    LookAt(bed.Center);
                    if (!Game.WalkTowards(edge, 1))
                    {
                        Game.Body.WalkVelocity = 0;
                        Face(Side * 0.5);
                        _t = 0;
                        _phase = Phase.HopIn;
                        Game.Visual.Bounce(-1.8);
                    }

                    break;
                }

                case Phase.HopIn:
                    // Coge impulso y salta dentro, justo al centro (sube al colchón ya en el aire).
                    if (!_launched)
                    {
                        if (_t > 0.14 && Game.Body.IsGrounded)
                        {
                            var distance = bed.Body.Feet.X - Game.Body.Feet.X;
                            Game.Body.Launch(new Vec2(distance / 0.34, -330 * Game.Body.Scale));
                            Game.Raise(GameEventKind.Hop);
                            _launched = true;
                        }
                    }
                    else if (!Game.Body.IsGrounded && Game._bed is null && Math.Abs(Game.Body.Feet.X - bed.Body.Feet.X) < bed.Body.HalfWidth)
                    {
                        Game._bed = bed;
                    }
                    else if (Game.Body.IsGrounded)
                    {
                        Game._bed ??= bed;
                        bed.Bounce(-1.6);
                        Game.Visual.Bounce(-2);
                        Game.Raise(GameEventKind.BedBounced, 0.5);
                        _t = 0;
                        _phase = plan switch
                        {
                            BedPlan.Bounce => Phase.Bounce,
                            BedPlan.Sit => Phase.Sit,
                            _ => Phase.Settle,
                        };
                    }

                    rig.Stretch = -0.10 * Easing.Pulse(_t, 0.07, 0.07);
                    mask |= RigMask.Body;
                    break;

                case Phase.Settle:
                    // Da un par de vueltas para acomodarse, como los gatitos, y se acurruca.
                    Face(_t < 0.45 ? -0.8 : _t < 0.9 ? 0.8 : 0);
                    Game.Visual.SetExpression(Expression.Sleepy, 0.4);
                    if (_t > 1.6)
                    {
                        Game.FallAsleepNow(carrying);
                        return true;
                    }

                    break;

                case Phase.Bounce:
                {
                    // Cada vez que cae en el colchón, otro bote (más alto); los brazos arriba de alegría.
                    var grounded = Game.Body.IsGrounded;
                    if (grounded && !_wasGrounded)
                    {
                        bed.Bounce(-2.6);
                        Game.Visual.Bounce(-2.4);
                        Game.Raise(GameEventKind.BedBounced, 1);
                        _t = 0;
                    }

                    if (grounded && _t > 0.12)
                    {
                        if (_hops >= _wanted)
                        {
                            _t = 0;
                            _phase = Phase.HopOut;
                            break;
                        }

                        _hops++;
                        Game.Body.Hop(380 + 40 * Math.Min(_hops, 4));
                    }

                    Game.Visual.SetExpression(Expression.Joy, 0.3);
                    if (!grounded)
                    {
                        var arms = Gestures.Evaluate(Gesture.ArmsUp, _t, new PoseContext(Side, Form));
                        rig = Rig.Mix(rig, arms, RigMask.Hands, 1);
                        mask |= RigMask.Hands;
                    }

                    break;
                }

                case Phase.Sit:
                    // Sentado en su cama, balanceando los pies y mirando alrededor.
                    Game.Visual.SetExpression(Expression.Happy, 0.4);
                    rig.FootLY = 0.04 * Math.Sin(_t * 3);
                    rig.FootRY = 0.04 * Math.Sin(_t * 3 + 2);
                    mask |= RigMask.Feet;
                    if (_t > 9)
                    {
                        _t = 0;
                        _phase = Phase.HopOut;
                    }

                    break;

                case Phase.HopOut:
                    if (Game._bed is not null && Game.Body.IsGrounded)
                    {
                        Game.HopOutOfBed();
                    }
                    else if (Game._bed is null && Game.Body.IsGrounded && _t > 0.2)
                    {
                        return false;
                    }

                    break;
            }

            _wasGrounded = Game.Body.IsGrounded;
            if (mask != RigMask.None)
            {
                Move(rig, mask);
            }

            return true;
        }
    }
}
