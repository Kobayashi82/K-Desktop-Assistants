using Kobii.Core.Animation;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Creature;

/// <summary>Cómo acabó el último recorrido.</summary>
public enum TravelOutcome
{
    Going,
    Arrived,
    Failed,
}

/// <summary>
/// Ir de un sitio a otro del escritorio (el de Kobii AI, sin la lección: aquí sabe trepar desde el primer día): salta
/// entre plataformas, trepa por el lado de las ventanas hasta su barra de título, se deja caer de los bordes o se
/// descuelga de la barra. Lo usa por su cuenta (subir a ver qué hay en una ventana, bajar al suelo) y para las órdenes
/// (posarse en la ventana que acabas de traer delante).
/// </summary>
public sealed partial class KobiiGame
{
    /// <summary>Joven y en forma: alturas del cuerpo que salta hacia arriba y a lo lejos.</summary>
    private const double JumpHeights = 2.9;

    private const double JumpLengths = 5.5;

    private RouteRunner? _runner;
    private bool _travelErrand;
    private double _travelEnd;
    private TravelOutcome _travelOutcome = TravelOutcome.Arrived;

    /// <summary>Va de camino a otro sitio (andando, saltando o trepando).</summary>
    public bool IsTraveling => Activity == Activity.Travel;

    /// <summary>Cómo va (o cómo acabó) el último recorrido.</summary>
    public TravelOutcome Travel => IsTraveling ? TravelOutcome.Going : _travelOutcome;

    /// <summary>Va trepando por una ventana o colgado de su barra de título.</summary>
    public bool IsClimbing => IsTraveling && _runner?.Climbing == true;

    /// <summary>Por qué no ha podido ponerse en camino la última vez (para el registro).</summary>
    public string? TravelProblem { get; private set; }

    /// <summary>Píxeles por unidad de la criatura (la mitad de su altura).</summary>
    private double Unit => Body.Height / 2;

    /// <summary>Hasta dónde llega saltando y si trepa por los lados (con algo en las manos, solo salta y baja).</summary>
    public Reach MoveReach(bool handsFree = true) =>
        new(Body.Height * JumpHeights, Body.Height * JumpLengths, Climb: true, Walls: handsFree);

    /// <summary>
    /// Va hasta <paramref name="x"/> en <paramref name="target"/> (saltando, trepando o dejándose caer). Para una orden,
    /// <paramref name="errand"/>: no se distrae ni se encoge de hombros si no llega. Devuelve false si no hay forma de llegar.
    /// </summary>
    public bool GoTo(Platform target, double x, bool run = false, bool errand = false)
    {
        if (!IsSettled || Body.Support is null)
        {
            TravelProblem = $"no está de pie ({Activity})";
            return false;
        }

        var canonical = Navigation.Canonical(_world, target);
        if (Navigation.Same(canonical, Body.Support))
        {
            // En la misma plataforma: andando basta.
            if (!TryWalkTo(x))
            {
                return Math.Abs(x - Body.Feet.X) < Body.Height;
            }

            _running = run;
            return true;
        }

        // Si lleva algo en las manos no trepa por los lados (un gesto que mueve las manos, como asentir hablando, no cuenta).
        if (Navigation.Route(_world, Body, canonical, x, MoveReach(_heldProp == Prop.None && Animator.Prop == Prop.None)) is not { } route)
        {
            TravelProblem = $"sin ruta desde {Body.Support.OwnerId} ({Body.Support.Left:0}..{Body.Support.Right:0}, y {Body.Support.Y:0}, pies en {Body.Feet.X:0}) a {canonical.OwnerId} ({canonical.Left:0}..{canonical.Right:0}, y {canonical.Y:0}); ventanas: " +
                string.Join(", ", _world.Windows.Select(w => $"{w.Handle} {w.Bounds.Left},{w.Bounds.Top}-{w.Bounds.Right},{w.Bounds.Bottom}"));
            return false;
        }

        StartTravel(new RouteRunner(this, canonical, x, route, run), errand);
        return true;
    }

    /// <summary>Va a lo alto de una ventana (cerca de <paramref name="x"/>, o de su centro). False si no hay forma.</summary>
    public bool GoToWindow(long handle, double? x = null, bool run = false, bool errand = false)
    {
        var segments = _world.SegmentsOf(handle);
        if (segments.Count == 0)
        {
            TravelProblem = "la ventana no tiene por dónde ponerse encima";
            return false;
        }

        var margin = Body.Height * 0.8;
        var wanted = x ?? segments.OrderByDescending(s => s.Width).First().CenterX;
        var best = segments.Where(s => s.Width > margin * 2).OrderBy(s => wanted < s.Left ? s.Left - wanted : wanted > s.Right ? wanted - s.Right : 0).FirstOrDefault();
        return best is not null && GoTo(best, Math.Clamp(wanted, best.Left + margin, best.Right - margin), run, errand);
    }

    /// <summary>Sube a alguna ventana cercana del mismo monitor.</summary>
    public bool Explore()
    {
        if (!IsSettled || Body.Support is not { } here)
        {
            return false;
        }

        var h = Body.Height;
        var feet = Body.Feet;
        var options = _world.Platforms
            .Where(p => p.Kind == PlatformKind.Window && p.MonitorIndex == here.MonitorIndex && !Navigation.Same(p, here) && p.Width > h * 2.6
                && Math.Abs(Math.Clamp(feet.X, p.Left, p.Right) - feet.X) < h * 14)
            .OrderBy(_ => _random.NextDouble())
            .Take(4)
            .ToList();
        foreach (var platform in options)
        {
            var margin = h * 0.9;
            var x = _random.Range(platform.Left + margin, platform.Right - margin);
            if (Navigation.Route(_world, Body, Navigation.Canonical(_world, platform), x, MoveReach()) is { } route && route.Any(s => s.Kind != MoveKind.Walk))
            {
                StartTravel(new RouteRunner(this, platform, x, route, run: false), errand: false);
                return true;
            }
        }

        return false;
    }

    /// <summary>Baja al suelo de su monitor desde la ventana en la que está.</summary>
    public bool ComeDown()
    {
        if (!IsSettled || Body.Support is not { IsBase: false } here
            || (_world.GroundOf(here.MonitorIndex) ?? _world.NearestGround(Body.Feet)) is not { } ground)
        {
            return false;
        }

        var h = Body.Height;
        var x = Math.Clamp(Body.Feet.X + _random.Range(-3, 3) * h, ground.Left + h, Math.Max(ground.Left + h, ground.Right - h));
        if (Navigation.Route(_world, Body, ground, x, MoveReach()) is not { } route)
        {
            return false;
        }

        StartTravel(new RouteRunner(this, ground, x, route, run: false), errand: false);
        return true;
    }

    /// <summary>Deja el recorrido (si estaba trepando, se suelta).</summary>
    public void StopTravel()
    {
        if (IsTraveling)
        {
            SetActivity(Activity.Idle, _random.Range(1, 2));
        }
    }

    private void StartTravel(RouteRunner runner, bool errand)
    {
        if (Activity == Activity.Sleep)
        {
            WakeUp();
        }

        SetActivity(Activity.Travel, 0);
        _runner = runner;
        _travelErrand = errand;
        _travelEnd = double.NaN;
        _travelOutcome = TravelOutcome.Going;
    }

    /// <summary>Un paso del recorrido; al llegar mira alrededor tan contento, y si no llega se encoge de hombros.</summary>
    private void StepTravel(double dt)
    {
        if (_runner is not { } runner)
        {
            SetActivity(Activity.Idle, 1);
            return;
        }

        // Si le hablas mientras pasea por su cuenta, lo deja para atenderte (si es una orden, sigue).
        if (!_travelErrand && Attention is Attention.Listening or Attention.Thinking or Attention.Speaking or Attention.Waiting && !runner.Climbing
            && Body.IsGrounded)
        {
            SetActivity(Activity.Attend, 0);
            return;
        }

        var rig = default(Rig);
        var mask = RigMask.None;
        if (double.IsNaN(_travelEnd))
        {
            switch (runner.Step(_world, dt, ref rig, ref mask))
            {
                case TravelOutcome.Arrived:
                    _travelOutcome = TravelOutcome.Arrived;
                    _travelEnd = Time;
                    Raise(GameEventKind.Arrived, text: Body.Support?.IsBase == false ? "window" : "ground");
                    if (Body.Support is { IsBase: false } && !_travelErrand)
                    {
                        Visual.SetExpression(Expression.Joy, 1.6);
                    }

                    break;
                case TravelOutcome.Failed:
                    _travelOutcome = TravelOutcome.Failed;
                    _travelEnd = Time;
                    Raise(GameEventKind.Lost);
                    Visual.SetExpression(Expression.Confused, 1.4);
                    break;
            }
        }
        else
        {
            // Ya ha llegado (o no ha podido): mira alrededor desde lo alto, o se encoge de hombros.
            var t = Time - _travelEnd;
            var gesture = _travelOutcome == TravelOutcome.Arrived ? Gesture.LookAround : Gesture.Shrug;
            var seconds = _travelOutcome == TravelOutcome.Arrived ? (_travelErrand ? 0.6 : 1.8) : 1.4;
            var mix = Gestures.Evaluate(gesture, t, new PoseContext(Animator.Side, Species.Form));
            mask = Gestures.Mask(gesture, Animator.Side);
            rig = Rig.Mix(rig, mix, mask, Easing.Segment(t, 0, 0.2) * (1 - Easing.Segment(t, seconds - 0.3, 0.3)));
            if (t > seconds)
            {
                Animator.Override(rig, mask);
                SetActivity(Activity.Idle, _random.Range(1.5, 3));
                return;
            }
        }

        if (mask != RigMask.None)
        {
            Animator.Override(rig, mask);
        }
    }

    /// <summary>
    /// Lleva a cabo una ruta tramo a tramo (el de Kobii AI): anda hasta donde empieza cada uno; para saltar se agacha,
    /// coge impulso, salta y amortigua al caer; para trepar se agarra al borde de la ventana, sube mano sobre mano y se
    /// encarama a la barra de título; para bajar, se deja caer de un saltito o se descuelga de la barra y se suelta. Si cae
    /// donde no esperaba o se mueve una ventana, vuelve a planear desde donde esté.
    /// </summary>
    private sealed class RouteRunner
    {
        /// <summary>Parte del ciclo en que una mano sube a por el siguiente agarre.</summary>
        private const double ReachShare = 0.32;

        /// <summary>Parte del ciclo en que un pie sube a por el siguiente apoyo.</summary>
        private const double StepShare = 0.30;

        /// <summary>Altura del agarre alto sobre los pies (en alturas del cuerpo).</summary>
        private const double HighGrip = 0.92;

        /// <summary>Lo que sube la rodilla al dar un paso en la pared (en alturas del cuerpo).</summary>
        private const double KneeUp = 0.30;

        /// <summary>Lo que tarda en descolgarse: arrodillarse y agarrar la barra, bajar el cuerpo y quedarse colgando.</summary>
        private const double KneelSeconds = 0.28;
        private const double LowerSeconds = 0.45;
        private const double DangleSeconds = 0.55;

        /// <summary>Lo que se adelantan las manos al movimiento del cuerpo colgado (segundos).</summary>
        private const double HandLead = 0.04;

        private const int MaxMisses = 3;

        private readonly KobiiGame _game;
        private readonly long _targetOwner;
        private readonly double _targetXFromOrigin;
        private readonly bool _run;
        private readonly Dictionary<long, Vec2> _origins = [];
        private readonly double[] _hand = new double[2];
        private readonly double[] _handFrom = new double[2];
        private readonly double[] _handPhase = new double[2];
        private readonly double[] _handReach = new double[2];
        private readonly Vec2[] _handStart = new Vec2[2];
        private readonly double[] _foot = new double[2];
        private readonly double[] _footFrom = new double[2];
        private readonly double[] _footPhase = new double[2];
        private List<RouteStep> _route;
        private Platform _from;
        private int _index;
        private Leg _leg;
        private double _t;
        private int _misses;
        private bool _missed;
        private double _replanCooldown;
        private double _fallSpeed;
        private double _landing;
        private double _side = 1;
        private double _depth;
        private double _phi;
        private double _mantleDepth;
        private double _cling;
        private int _lead;
        private double _sweat;
        private bool _leap;
        private double _hangX;
        private bool _dangling;

        public RouteRunner(KobiiGame game, Platform target, double x, List<RouteStep> route, bool run)
        {
            _game = game;
            _targetOwner = target.OwnerId;
            _targetXFromOrigin = x - target.OwnerOrigin.X;
            _route = route;
            _run = run;
            _from = game.Body.Support!;
            RememberOrigins();
        }

        private enum Leg
        {
            /// <summary>Anda hasta donde empieza el tramo.</summary>
            Approach,

            /// <summary>Se agacha para coger impulso.</summary>
            Crouch,

            /// <summary>En el aire (saltando o cayendo).</summary>
            Air,

            /// <summary>Amortigua la caída.</summary>
            Land,

            /// <summary>Mira hacia arriba y se agarra al borde de la ventana.</summary>
            Grab,

            /// <summary>Salta para agarrarse al borde (la ventana no llega hasta el suelo).</summary>
            Leap,

            /// <summary>Sube mano sobre mano.</summary>
            Climb,

            /// <summary>Se encarama a la barra de título.</summary>
            Mantle,

            /// <summary>Se descuelga de la barra de título y se suelta.</summary>
            Hang,
        }

        /// <summary>Postura de base del tramo (null: andar, correr o estar de pie).</summary>
        public Pose? Pose => _leg switch
        {
            Leg.Crouch or Leg.Land => Animation.Pose.Crouch,
            Leg.Air or Leg.Leap => Animation.Pose.Jump,
            Leg.Grab when _leap => Animation.Pose.Crouch,
            Leg.Grab or Leg.Climb or Leg.Mantle => Animation.Pose.Stand,
            Leg.Hang => _dangling ? Animation.Pose.Dangle : Animation.Pose.Crouch,
            _ when !_game.Body.IsGrounded => Animation.Pose.Tumble,
            _ => null,
        };

        /// <summary>Va trepando por una ventana o colgado de ella.</summary>
        public bool Climbing => _leg is Leg.Climb or Leg.Mantle or Leg.Hang;

        /// <summary>Plataforma de destino tal como está ahora en el mundo (sigue a la ventana si se mueve).</summary>
        private (Platform Platform, double X)? Target(World world)
        {
            var segments = world.SegmentsOf(_targetOwner);
            if (segments.Count == 0)
            {
                return null;
            }

            var x = segments[0].OwnerOrigin.X + _targetXFromOrigin;
            var best = segments.OrderBy(s => x < s.Left ? s.Left - x : x > s.Right ? x - s.Right : 0).First();
            return (best, x);
        }

        /// <summary>La ventana deja de estar donde estaba al planear (se ha movido o cerrado).</summary>
        private bool Moved(World world) =>
            _origins.Any(o => !world.TryGetOwnerOrigin(o.Key, out var now) || (now - o.Value).Length > 1.5);

        private void RememberOrigins()
        {
            _origins.Clear();
            _origins[_from.OwnerId] = _from.OwnerOrigin;
            foreach (var step in _route)
            {
                _origins[step.To.OwnerId] = step.To.OwnerOrigin;
            }
        }

        /// <summary>Vuelve a planear desde donde está. Devuelve false si ya no hay forma de llegar.</summary>
        private bool Replan(World world)
        {
            var body = _game.Body;
            if (Target(world) is not { } target || body.Support is not { } here
                || Navigation.Route(world, body, target.Platform, target.X, _game.MoveReach()) is not { } route)
            {
                return false;
            }

            _route = route;
            _from = here;
            _index = 0;
            _leg = Leg.Approach;
            _t = 0;
            RememberOrigins();
            return true;
        }

        /// <summary>Si estaba trepando, se suelta (le han interrumpido).</summary>
        public void Abort()
        {
            if (_game.Body.IsClimbing)
            {
                _game.Body.Launch(Vec2.Zero);
            }

            _game.Body.WalkVelocity = 0;
            _game._running = false;
        }

        /// <summary>Un paso del recorrido. Mueve a mano lo que necesite en <paramref name="rig"/>.</summary>
        public TravelOutcome Step(World world, double dt, ref Rig rig, ref RigMask mask)
        {
            var body = _game.Body;
            if (_index >= _route.Count)
            {
                return TravelOutcome.Arrived;
            }

            var step = _route[_index];
            switch (_leg)
            {
                case Leg.Approach:
                    return Approach(world, dt, step);

                case Leg.Crouch:
                    Crouch(world, dt, step, ref rig, ref mask);
                    break;

                case Leg.Air:
                    if (!body.IsGrounded)
                    {
                        // En el aire: mira hacia donde va a caer.
                        _fallSpeed = body.Velocity.Y;
                        var heading = Math.Abs(body.Velocity.X) > 20 * body.Scale ? Math.Sign(body.Velocity.X) : _side;
                        Face(heading * 0.6, heading);
                        _game.Visual.LookTarget = new Vec2(heading * 0.5, body.Velocity.Y > 0 ? 0.7 : -0.2);
                        break;
                    }

                    // Ha caído: amortigua, más cuanto más fuerte.
                    _missed |= body.Support is not { } support || !Navigation.Same(support, step.To);
                    _landing = Math.Clamp(_fallSpeed / (body.Scale * 1000), 0, 1);
                    _leg = Leg.Land;
                    _t = 0;
                    break;

                case Leg.Land:
                {
                    _t += dt;
                    if (_landing > 0.55)
                    {
                        // Aterrizaje de superhéroe: una mano al suelo.
                        var touch = Easing.Pulse(_t, 0.08, 0.3);
                        rig.SetHand(_side, Vec2.Lerp(Rig.RestHand(_side, Species.Form), new Vec2(_side * 0.78, -0.10), touch), Species.Form, front: false);
                        rig.SetHand(-_side, Rig.RestHand(-_side, Species.Form) + new Vec2(-_side * 0.10 * touch, -0.25 * touch), Species.Form, front: false);
                        mask |= RigMask.Hands;
                    }

                    if (_t < 0.12 + 0.25 * _landing)
                    {
                        break;
                    }

                    if (_missed)
                    {
                        _missed = false;
                        return ++_misses > MaxMisses || !Replan(world) ? Fail() : TravelOutcome.Going;
                    }

                    _from = step.To;
                    _index++;
                    _leg = Leg.Approach;
                    return _index >= _route.Count ? TravelOutcome.Arrived : TravelOutcome.Going;
                }

                case Leg.Grab:
                    Grab(world, dt, step, ref rig, ref mask);
                    break;

                case Leg.Leap:
                    Leap(world, step, ref rig, ref mask);
                    break;

                case Leg.Climb:
                    ClimbStep(world, dt, step, ref rig, ref mask);
                    break;

                case Leg.Mantle:
                    Mantle(world, dt, step, ref rig, ref mask);
                    break;

                case Leg.Hang:
                    Hang(world, dt, step, ref rig, ref mask);
                    break;
            }

            return TravelOutcome.Going;
        }

        /// <summary>Anda (o corre) hasta donde empieza el tramo.</summary>
        private TravelOutcome Approach(World world, double dt, RouteStep step)
        {
            var body = _game.Body;
            if (!body.IsGrounded)
            {
                // Se ha caído (la ventana se ha ido): al posarse, vuelve a planear.
                _fallSpeed = body.Velocity.Y;
                _missed = true;
                _leg = Leg.Air;
                return TravelOutcome.Going;
            }

            _replanCooldown -= dt;
            if (_replanCooldown <= 0 && (Moved(world) || body.Support is not { } here || !Navigation.Same(here, _from)))
            {
                _replanCooldown = 0.5;
                if (!Replan(world))
                {
                    return Fail();
                }

                step = _route[_index];
            }

            var goal = step.Kind == MoveKind.Walk ? step.EndX : step.StartX;
            var far = Math.Abs(goal - body.Feet.X) > body.Height * 1.2;
            _game._running = _run && far;
            LookAhead(step);
            if (_game.WalkTowards(goal, _game._running ? RunFactor : 1.1))
            {
                return TravelOutcome.Going;
            }

            body.WalkVelocity = 0;
            _game._running = false;
            if (Math.Abs(goal - body.Feet.X) > body.Height * 0.6)
            {
                // No llega hasta allí (se ha topado con el borde): a planear otra vez.
                return ++_misses > MaxMisses || !Replan(world) ? Fail() : TravelOutcome.Going;
            }

            if (step.Kind == MoveKind.Walk)
            {
                _index++;
                return _index >= _route.Count ? TravelOutcome.Arrived : TravelOutcome.Going;
            }

            _side = step.Kind == MoveKind.Jump ? (Math.Sign(step.EndX - body.Feet.X) is var s && s != 0 ? s : step.Side) : step.Side;
            _leg = step.Kind switch
            {
                MoveKind.ClimbUp => Leg.Grab,
                MoveKind.HangDrop => Leg.Hang,
                _ => Leg.Crouch,
            };
            _hangX = body.Feet.X - (body.Support?.OwnerOrigin.X ?? 0);
            _t = 0;
            return TravelOutcome.Going;
        }

        /// <summary>
        /// Se descuelga de la barra de título: se arrodilla y la agarra con las dos manos, baja el cuerpo por delante de la
        /// ventana con las manos en la barra, se queda colgando un momento mirando abajo y se suelta. Las manos van referidas
        /// a la ventana: si se mueve, va con ella; si se cierra, se cae.
        /// </summary>
        private void Hang(World world, double dt, RouteStep step, ref Rig rig, ref RigMask mask)
        {
            var body = _game.Body;
            var h = body.Height;
            _t += dt;
            if (!world.TryGetOwnerOrigin(step.Window, out var origin) || (body.IsClimbing && body.SupportId != step.Window)
                || (!body.IsClimbing && !body.IsGrounded))
            {
                Abort();
                _missed = true;
                _leg = Leg.Air;
                return;
            }

            var x = origin.X + _hangX;
            var bar = (world.FindSegment(step.Window, x) ?? world.SegmentsOf(step.Window)[0]).Y;
            if (_t >= KneelSeconds && !body.IsClimbing)
            {
                // Agarrado a la barra: ya no le sostiene, le sostienen las manos.
                body.Cling(step.Window);
            }

            var down = Easing.Segment(_t, KneelSeconds, LowerSeconds);
            var hanging = Math.Max(0, _t - KneelSeconds - LowerSeconds);

            // Ya abajo, las piernas cuelgan (antes no: al cambiar de postura mientras baja, las manos se soltarían).
            _dangling = down >= 1;

            // Colgando, se balancea como un péndulo desde las manos (cada vez menos).
            var swing = 0.09 * Math.Sin(Math.Tau * hanging / 0.8) * Math.Exp(-1.6 * hanging) * down;
            if (body.IsClimbing)
            {
                body.ClimbTo(new Vec2(x - Math.Sin(swing) * Navigation.HangDepth * h, bar + Navigation.HangDepth * h * down), dt);
            }

            rig.Spin = swing;
            rig.Stretch = 0.06 * down;
            for (var i = 0; i < 2; i++)
            {
                // Las manos bajan a la barra, delante de los pies, y se quedan agarradas a ella.
                var side = i == 0 ? -1.0 : 1.0;
                var grip = new Vec2(x + side * 0.5 * h, bar) - (body.IsClimbing ? body.Velocity * HandLead : Vec2.Zero);
                var local = Vec2.Lerp(Rig.RestHand(side, Species.Form), Local(grip, rig), Easing.Segment(_t, 0, KneelSeconds * 0.85));
                rig.SetHand(side, local, Species.Form, front: true, grip: Easing.Segment(_t, KneelSeconds * 0.7, KneelSeconds * 0.3));
            }

            mask |= body.IsClimbing ? RigMask.Hands | RigMask.Body : RigMask.Hands;
            Face(_side * 0.15, _side);
            _game.Visual.LookTarget = new Vec2(_side * 0.15, down > 0.5 ? 1 : 0.6);
            _game.Visual.SetExpression(Expression.Curious, 0.3);
            if (_t < KneelSeconds + LowerSeconds + DangleSeconds)
            {
                return;
            }

            // ¡Allá va! Se suelta y cae.
            body.Launch(new Vec2(0, 40 * body.Scale));
            _game.Visual.Bounce(1.2);
            _fallSpeed = 0;
            _leg = Leg.Air;
            _t = 0;
        }

        /// <summary>Se agacha, echa los brazos atrás… ¡y salta!</summary>
        private void Crouch(World world, double dt, RouteStep step, ref Rig rig, ref RigMask mask)
        {
            var body = _game.Body;
            _t += dt;
            Face(_side * 0.7, _side);
            LookAhead(step);
            var u = Easing.Segment(_t, 0, 0.18);
            rig.SetHand(-1, Rig.RestHand(-1, Species.Form) + new Vec2(-_side * 0.16 * u, 0.10 * u), Species.Form, front: false);
            rig.SetHand(1, Rig.RestHand(1, Species.Form) + new Vec2(-_side * 0.16 * u, 0.10 * u), Species.Form, front: false);
            mask |= RigMask.Hands;

            var anticipation = step.Kind == MoveKind.Drop ? 0.16 : 0.24;
            if (_t < anticipation)
            {
                return;
            }

            var velocity = step.Velocity;
            if (step.Kind == MoveKind.Drop && body.Support is { } edge)
            {
                // Lo justo para pasar el borde desde donde se ha parado.
                var side = (int)_side;
                velocity = Navigation.DropVelocity(body, side, Math.Abs((side > 0 ? edge.Right : edge.Left) - body.Feet.X));
            }
            else if (step.Kind == MoveKind.Jump && body.Support is { } from)
            {
                // Desde donde está de verdad (se puede haber quedado a unos píxeles del sitio planeado).
                velocity = Navigation.PlanJump(world, body, from, body.Feet.X, Navigation.Canonical(world, step.To), step.EndX, _game.MoveReach()) ?? step.Velocity;
            }

            body.Launch(velocity);
            _game.Visual.Bounce(2.2);
            _game.Raise(GameEventKind.Hop);
            _fallSpeed = 0;
            _leg = Leg.Air;
            _t = 0;
        }

        /// <summary>Mira hacia arriba y se agarra al borde (o se prepara para saltar a por él).</summary>
        private void Grab(World world, double dt, RouteStep step, ref Rig rig, ref RigMask mask)
        {
            var body = _game.Body;
            var h = body.Height;
            _t += dt;
            if (!Wall(world, step, out var edge, out var top, out var bottom))
            {
                _missed = true;
                _leg = Leg.Land;
                return;
            }

            Face(_side * 0.55, _side);
            _game.Visual.LookTarget = new Vec2(_side * 0.25, -1);
            var grip = body.Feet.Y - HighGrip * h;
            var leap = bottom < grip + 0.05 * h;
            _leap = leap;
            var u = Easing.Segment(_t, 0, 0.3);
            if (!leap)
            {
                // Las manos suben al borde de la ventana: una arriba y otra a media altura.
                var near = Local(new Vec2(edge - _side * 0.03 * h, Math.Max(grip, bottom + 0.05 * h)), rig);
                var far = Local(new Vec2(edge - _side * 0.05 * h, Math.Max(body.Feet.Y - 0.5 * h, bottom + 0.05 * h)), rig);
                rig.SetHand(_side, Vec2.Lerp(Rig.RestHand(_side, Species.Form), near, u), Species.Form, front: false, grip: Easing.Segment(_t, 0.22, 0.08));
                rig.SetHand(-_side, Vec2.Lerp(Rig.RestHand(-_side, Species.Form), far, u), Species.Form, front: false, grip: Easing.Segment(_t, 0.24, 0.08));
                mask |= RigMask.Hands;
            }

            if (_t < (leap ? 0.22 : 0.32))
            {
                return;
            }

            // Se pega a la pared justo en el sitio (por si se ha quedado a unos píxeles).
            body.Feet = body.Feet.WithX(edge - _side * body.HalfWidth * Navigation.ClimbOffset);
            if (leap)
            {
                // Salta en vertical lo justo para que las manos lleguen al borde.
                _cling = bottom + 0.72 * h;
                var rise = Math.Max(0, body.Feet.Y - _cling);
                body.Launch(new Vec2(0, -Math.Sqrt(2 * CreatureBody.GravityDip * body.Scale * rise * 1.06)));
                _game.Visual.Bounce(2);
                _leg = Leg.Leap;
                _t = 0;
                return;
            }

            StartClimb(step, edge, top);
        }

        /// <summary>Salta con los brazos estirados hacia el borde y se agarra en lo alto del salto.</summary>
        private void Leap(World world, RouteStep step, ref Rig rig, ref RigMask mask)
        {
            var body = _game.Body;
            if (!Wall(world, step, out var edge, out var top, out _))
            {
                _missed = true;
                _leg = Leg.Air;
                return;
            }

            rig.SetHand(_side, new Vec2(_side * 0.92, -1.72), Species.Form, front: false, grip: 0.2);
            rig.SetHand(-_side, new Vec2(_side * 0.80, -1.50), Species.Form, front: false, grip: 0.2);
            mask |= RigMask.Hands;
            _game.Visual.LookTarget = new Vec2(_side * 0.25, -1);
            if (body.IsGrounded)
            {
                // No ha llegado: vuelta a intentarlo (o a buscar otro camino).
                _missed = true;
                _leg = Leg.Land;
                return;
            }

            if (body.Velocity.Y >= -30 * body.Scale || body.Feet.Y <= _cling)
            {
                StartClimb(step, edge, top);
            }
        }

        /// <summary>Se agarra a la pared y empieza a subir: la mano de su lado arriba y la otra a media altura.</summary>
        private void StartClimb(RouteStep step, double edge, double top)
        {
            var body = _game.Body;
            var h = body.Height;
            body.Cling(step.Window);
            body.Feet = new Vec2(edge - _side * body.HalfWidth * Navigation.ClimbOffset, body.Feet.Y);
            _depth = body.Feet.Y - top;
            _lead = _side > 0 ? 1 : 0;
            _hand[_lead] = _depth - HighGrip * h;
            _hand[1 - _lead] = _depth - 0.5 * h;
            _foot[0] = _foot[1] = _depth;
            _phi = ReachShare;
            for (var i = 0; i < 2; i++)
            {
                _handPhase[i] = Frac(_phi + (i == _lead ? 0 : 0.5));
                _footPhase[i] = Frac(_phi + 0.25 + (i == _lead ? 0.5 : 0));
                _handFrom[i] = _hand[i];
                _footFrom[i] = _foot[i];
            }

            _sweat = 3;
            _leg = Leg.Climb;
            _t = 0;
        }

        /// <summary>
        /// Sube mano sobre mano: cada mano se queda agarrada al borde mientras el cuerpo sube y, cuando le queda baja, sube a
        /// por otro agarre por encima de la cabeza; los pies empujan contra la pared a contratiempo. Todo va referido a la
        /// ventana: si se mueve, va con ella.
        /// </summary>
        private void ClimbStep(World world, double dt, RouteStep step, ref Rig rig, ref RigMask mask)
        {
            var body = _game.Body;
            var h = body.Height;
            _t += dt;
            if (!body.IsClimbing || !Wall(world, step, out var edge, out var top, out _))
            {
                // La ventana ya no está: se cae.
                Abort();
                _missed = true;
                _leg = Leg.Air;
                return;
            }

            var speed = h * 1.25;
            const double rate = 1.78;
            var pulse = 1 + 0.55 * Math.Cos(Math.Tau * 2 * (_phi - ReachShare - 0.1));
            _depth -= speed * pulse * dt;
            _phi += rate * dt;

            for (var i = 0; i < 2; i++)
            {
                var phase = Frac(_phi + (i == _lead ? 0 : 0.5));
                if (phase < _handPhase[i])
                {
                    _handFrom[i] = _hand[i];
                }

                _handPhase[i] = phase;
                _handReach[i] = 0;
                if (phase < ReachShare)
                {
                    // Sube a por el siguiente agarre, un poco por fuera (separándose de la pared).
                    var u = phase / ReachShare;
                    var target = _depth - speed * (ReachShare - phase) / rate - HighGrip * h;
                    _hand[i] = _handFrom[i] + (target - _handFrom[i]) * Easing.Apply(Ease.InOut, u);
                    _handReach[i] = u;
                }

                var footPhase = Frac(_phi + 0.25 + (i == _lead ? 0.5 : 0));
                if (footPhase < _footPhase[i])
                {
                    _footFrom[i] = _foot[i];
                }

                _footPhase[i] = footPhase;
                if (footPhase < StepShare)
                {
                    var u = footPhase / StepShare;
                    var target = _depth - KneeUp * h - speed * (StepShare - footPhase) / rate;
                    _foot[i] = _footFrom[i] + (target - _footFrom[i]) * Easing.Apply(Ease.InOut, u);
                }

                // Un pie apoyado no se queda colgando por debajo del cuerpo: resbala con él.
                _foot[i] = Math.Min(_foot[i], _depth + 0.06 * h);
            }

            var feet = new Vec2(edge - _side * body.HalfWidth * Navigation.ClimbOffset, top + _depth);
            body.ClimbTo(feet, dt);

            // El cuerpo se pega a la pared y se balancea un poco con cada tirón.
            var sway = Math.Sin(Math.Tau * 2 * (_phi - ReachShare));
            rig.Spin = _side * (0.12 + 0.04 * sway);
            rig.OffsetX = _side * 0.04;

            // Se estira al alargar la mano y se encoge al tirar (como un acordeón).
            rig.Stretch = 0.02 - 0.08 * (pulse - 1);
            rig.HeadTilt = -_side * 0.06;
            rig.FaceY = -0.03;
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? -1.0 : 1.0;
                var near = i == _lead;
                var x = edge - _side * (near ? 0.03 : 0.05) * h - _side * 0.10 * h * Math.Sin(Math.PI * _handReach[i]);
                var grip = _handReach[i] > 0 ? Easing.Segment(_handReach[i], 0.8, 0.2) * 0.9 + 0.1 : 1;
                rig.SetHand(side, Local(new Vec2(x, top + _hand[i]), rig), Species.Form, front: false, grip: grip);

                var footX = edge - _side * (near ? 0.20 : 0.40) * h;
                var foot = Local(new Vec2(footX, top + _foot[i]), rig) - Rig.RestFoot(side);
                if (side < 0)
                {
                    (rig.FootLX, rig.FootLY) = (foot.X, Math.Min(foot.Y, 0.04));
                }
                else
                {
                    (rig.FootRX, rig.FootRY) = (foot.X, Math.Min(foot.Y, 0.04));
                }
            }

            mask |= RigMask.Hands | RigMask.Feet | RigMask.Body | RigMask.Head | RigMask.Face;
            Face(_side * 0.55, _side);
            _game.Visual.LookTarget = new Vec2(_side * 0.2, -0.9);
            _game.Visual.SetExpression(Expression.Curious, 0.3);

            // En las trepadas largas, alguna gota de sudor.
            _sweat -= dt;
            if (_sweat <= 0)
            {
                _sweat = 2.6;
                _game.Visual.AddEmote(EmoteKind.Drop, _game.HeadPoint + new Vec2(-_side * body.HalfWidth * 0.6, h * 0.2), body.Scale);
            }

            if (_depth <= 0.95 * h)
            {
                // Asoma la cabeza por encima: a encaramarse.
                for (var i = 0; i < 2; i++)
                {
                    var x = edge - _side * (i == _lead ? 0.03 : 0.05) * h;
                    _handStart[i] = new Vec2(x - edge, _hand[i]);
                }

                _mantleDepth = _depth;
                _leg = Leg.Mantle;
                _t = 0;
            }
        }

        /// <summary>
        /// Se encarama: apoya las dos manos en la barra de título, se empuja hacia arriba echando la barriga por encima del
        /// borde y sube los pies, recogidos, hasta quedar de pie arriba.
        /// </summary>
        private void Mantle(World world, double dt, RouteStep step, ref Rig rig, ref RigMask mask)
        {
            var body = _game.Body;
            var h = body.Height;
            _t += dt;
            if (!body.IsClimbing || !Wall(world, step, out var edge, out var top, out _))
            {
                Abort();
                _missed = true;
                _leg = Leg.Air;
                return;
            }

            const double duration = 0.85;
            var u = Math.Min(1, _t / duration);
            var wallX = edge - _side * body.HalfWidth * Navigation.ClimbOffset;
            var endX = edge + (step.EndX - step.EdgeX);
            var rise = Easing.Segment(u, 0.08, 0.45);
            var over = Easing.Segment(u, 0.42, 0.5);
            var depth = MathUtil.Lerp(MathUtil.Lerp(_mantleDepth, 0.10 * h, rise), 0, over) - 0.12 * h * Math.Sin(Math.PI * over);
            var feet = new Vec2(MathUtil.Lerp(wallX, endX, over), top + depth);
            body.ClimbTo(feet, dt);

            rig.Spin = _side * (0.07 + 0.30 * Math.Sin(Math.PI * Math.Clamp((u - 0.2) / 0.72, 0, 1)));
            rig.Stretch = -0.12 * Math.Sin(Math.PI * over);
            rig.HeadTilt = _side * 0.08 * over;
            var plant = Easing.Segment(u, 0, 0.2);
            var release = Easing.Segment(u, 0.74, 0.22);
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? -1.0 : 1.0;
                var start = new Vec2(edge + _handStart[i].X, top + _handStart[i].Y);
                var palm = new Vec2(edge + _side * (i == _lead ? 0.16 : 0.04) * h, top);
                var local = Local(Vec2.Lerp(start, palm, plant), rig);
                local = Vec2.Lerp(local, Rig.RestHand(side, Species.Form), release);
                rig.SetHand(side, local, Species.Form, front: false, grip: (1 - plant * 0.7) * (1 - release));
            }

            var tuck = Math.Sin(Math.PI * over);
            (rig.FootLY, rig.FootRY) = (-0.25 * tuck, -0.22 * tuck);
            (rig.FootLX, rig.FootRX) = (_side * 0.08 * tuck, _side * 0.08 * tuck);
            mask |= RigMask.Hands | RigMask.Feet | RigMask.Body | RigMask.Head;
            Face(_side * 0.6, _side);
            _game.Visual.LookTarget = new Vec2(_side * 0.5, 0.3);
            if (over > 0.5)
            {
                _game.Visual.SetExpression(Expression.Joy, 0.6);
            }

            if (u < 1)
            {
                return;
            }

            // Ya está arriba.
            var platform = world.FindSegment(step.Window, endX) ?? world.SegmentsOf(step.Window).FirstOrDefault(p => p.ContainsX(endX, -body.HalfWidth));
            if (platform is null)
            {
                Abort();
                _missed = true;
                _leg = Leg.Air;
                return;
            }

            body.PlaceOn(platform, endX);
            _game.Raise(GameEventKind.Climbed);
            _game.Visual.Bounce(-1.5);
            _landing = 0.3;
            _leg = Leg.Land;
            _t = 0;
        }

        /// <summary>Dónde está la pared por la que trepa: su borde, su barra de título y su parte de abajo.</summary>
        private static bool Wall(World world, RouteStep step, out double edge, out double top, out double bottom)
        {
            edge = top = bottom = 0;
            WindowInfo? window = null;
            foreach (var w in world.Windows)
            {
                if (w.Handle == step.Window)
                {
                    window = w;
                    break;
                }
            }

            if (window is null)
            {
                return false;
            }

            var r = window.Bounds;
            edge = step.Side > 0 ? r.Left : r.Right;
            var segments = world.SegmentsOf(step.Window);
            top = segments.Count > 0 ? segments[0].Y : r.Top;
            bottom = r.Bottom;
            return true;
        }

        /// <summary>Mira hacia donde va: adelante, arriba a la ventana o al sitio donde caerá.</summary>
        private void LookAhead(RouteStep step)
        {
            var body = _game.Body;
            var point = step.Kind switch
            {
                MoveKind.ClimbUp => new Vec2(step.EdgeX, step.To.Y),
                MoveKind.Walk => new Vec2(step.EndX, body.Feet.Y - body.Height * 0.6),
                _ => new Vec2(step.EndX, step.To.Y - body.Height * 0.5),
            };
            var delta = (point - body.Center) / (body.Height * 2.5);
            _game.Visual.LookTarget = delta.ClampLength(1);
        }

        private Vec2 Local(Vec2 world, in Rig rig) =>
            CreatureSpace.ToLocal(world, rig, _game.Body.Feet, _game.Unit, _game.Visual.Squash * (1 + rig.Stretch), _game.Visual.Lean);

        private void Face(double facing, double side)
        {
            _game.Visual.FacingTarget = facing;
            _game.Animator.Side = side < 0 ? -1 : 1;
        }

        private TravelOutcome Fail()
        {
            Abort();
            return TravelOutcome.Failed;
        }

        private static double Frac(double x) => x - Math.Floor(x);
    }
}
