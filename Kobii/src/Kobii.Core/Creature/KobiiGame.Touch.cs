using Kobii.Core.Animation;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Creature;

/// <summary>
/// Lo que hace contigo a través del cursor, que es tu mano (de Kobii AI): cosquillas en la barriga, cepillarle, chocar los
/// cinco y darse la mano. No hace falta hacer clic: basta con llevar el cursor adonde toca.
/// </summary>
public sealed partial class KobiiGame
{
    /// <summary>Pasadas seguidas (intensidad del frotar) a partir de las que frotar la barriga son cosquillas.</summary>
    private const double TickleIntensity = 2.2;

    /// <summary>Lo cerca de la mano (en unidades) que tiene que estar el cursor para tocarla.</summary>
    private const double HandTouchRadius = 0.42;

    private double _tickleCooldown;

    /// <summary>El cursor es un cepillo: frotarle es cepillarle (en vez de caricias o cosquillas).</summary>
    public bool BrushMode { get; set; }

    /// <summary>Levanta la mano para chocar los cinco. Devuelve false si ahora no puede.</summary>
    public bool OfferHighFive()
    {
        if (!CanPlay)
        {
            return false;
        }

        StartRoutine(new HighFiveRoutine(this, SideTowardCursor(_sensors)));
        return true;
    }

    /// <summary>Te ofrece la mano para que se la estreches. Devuelve false si ahora no puede.</summary>
    public bool OfferHandshake()
    {
        if (!CanPlay)
        {
            return false;
        }

        StartRoutine(new HandshakeRoutine(this, SideTowardCursor(_sensors)));
        return true;
    }

    /// <summary>Lo que hace cada pasada del frotar: con el cepillo, cepillarle; deprisa por la barriga, cosquillas. True si se ha ocupado.</summary>
    private bool TouchStroke(Vec2 cursor, Sensors sensors)
    {
        if (BrushMode)
        {
            BrushStroke(cursor);
            return true;
        }

        return TryTickle(cursor);
    }

    /// <summary>Una pasada del cepillo. Dormido, sonríe en sueños sin despertarse.</summary>
    private void BrushStroke(Vec2 cursor)
    {
        Raise(GameEventKind.Brushed);
        if (_routine is BrushRoutine brush && Activity == Activity.Play)
        {
            brush.Stroke(cursor);
            return;
        }

        if (Activity == Activity.Sleep)
        {
            Visual.Shine = Math.Min(1, Visual.Shine + 0.02);
            Visual.AddEmote(EmoteKind.Fluff, cursor, Body.Scale);
            if (_random.Chance(0.2))
            {
                Visual.AddEmote(EmoteKind.Heart, HeadPoint, Body.Scale);
            }

            return;
        }

        if (!CanPlay)
        {
            return;
        }

        var routine = new BrushRoutine(this);
        StartRoutine(routine);
        routine.Stroke(cursor);
    }

    /// <summary>¿Está el cursor sobre la barriga (la mitad de abajo del cuerpo)?</summary>
    private bool OnBelly(Vec2 cursor) => cursor.Y > Body.Feet.Y - Body.Height * 0.55;

    /// <summary>
    /// Una pasada del frotar. Deprisa y por la barriga son cosquillas; si ya se está riendo, cualquier pasada por encima
    /// sigue haciéndole reír. Devuelve true si han sido cosquillas.
    /// </summary>
    private bool TryTickle(Vec2 cursor)
    {
        if (_routine is TickleRoutine tickles && Activity == Activity.Play)
        {
            tickles.Stroke();
            return true;
        }

        if (!OnBelly(cursor) || _rub.Intensity < TickleIntensity || _tickleCooldown > 0 || !CanPlay || Activity == Activity.Sleep)
        {
            return false;
        }

        var routine = new TickleRoutine(this, -Math.Sign(cursor.X - Body.Feet.X + 1e-6));
        StartRoutine(routine);
        routine.Stroke();
        return true;
    }

    private Vec2 HandWorld(double side) => WorldPoint(Animator.Rig.Hand(side, Species.Form));

    /// <summary>El cursor toca la mano de ese lado.</summary>
    private bool CursorOnHand(Sensors sensors, double side, double radius = HandTouchRadius) =>
        sensors.Cursor is { } cursor && Vec2.Distance(cursor, HandWorld(side)) <= radius * Unit + 6 * Body.Scale;

    /// <summary>
    /// Cosquillas: se retuerce de la risa tapándose la barriga; si sigues, le da un ataque de risa y se cae de lado
    /// pataleando; si te pasas, te aparta la mano entre risas. Cuando paras, recupera el aliento.
    /// </summary>
    private sealed class TickleRoutine(KobiiGame game, double fallSide) : Routine(game)
    {
        /// <summary>Segundos de cosquillas seguidas a partir de los que le da el ataque de risa.</summary>
        private const double FitAfter = 3.2;

        /// <summary>A partir de aquí ya no puede más y te aparta la mano.</summary>
        private const double EnoughAfter = 9;

        private const double RecoverSeconds = 1.5;

        private double _sinceStroke = double.PositiveInfinity;
        private double _tickled;
        private double _heat;
        private double _fall;
        private double _laughTimer;
        private double _recover = -1;
        private double _push = -1;
        private double _pushDelay;
        private double _fallSide = 1;
        private bool _counted;

        public override PlayKind Kind => PlayKind.Tickles;

        public override void Begin()
        {
            Side = fallSide == 0 ? 1 : fallSide;
            _fallSide = Side;
            Game.Visual.SetExpression(Expression.Joy, 0.6);
        }

        /// <summary>Otra pasada por la barriga.</summary>
        public void Stroke()
        {
            _sinceStroke = 0;
            if (_push >= 0)
            {
                return;
            }

            _recover = -1;
            Game.Visual.Bounce(-1.3 * (1 - 0.6 * _fall));
            if (!_counted)
            {
                _counted = true;
                Game.Raise(GameEventKind.Tickled);
            }

            if (Random.Chance(0.22))
            {
                Game.Visual.AddEmote(Random.Chance(0.6) ? EmoteKind.Sparkle : EmoteKind.Heart, Game.HeadPoint, Game.Body.Scale);
            }
        }

        protected override bool Update(World world, Sensors sensors, double dt)
        {
            _sinceStroke += dt;
            var tickling = _sinceStroke < 0.8 && _push < 0;
            if (tickling)
            {
                _tickled += dt;
            }

            _heat = MathUtil.Damp(_heat, tickling ? 1 : 0, tickling ? 9 : 2.5, dt);
            if (tickling && _tickled > EnoughAfter)
            {
                // ¡Basta, basta! Te aparta la mano entre risas.
                _push = 0;
                _pushDelay = _fall > 0.05 ? 0.7 : 0;
                Side = Game.SideTowardCursor(sensors);
            }

            var lying = tickling && _tickled > FitAfter;
            _fall = MathUtil.MoveTowards(_fall, lying ? 1 : 0, dt / (lying ? 0.45 : 0.6));
            if (!tickling && _push < 0)
            {
                _recover = Math.Max(0, _recover) + dt;
            }

            if (_push >= 0)
            {
                _push += dt;
            }

            // Ríe a carcajadas mientras dura (la boca se mueve con cada risita).
            _laughTimer -= dt;
            if ((tickling || (_push >= 0 && _push < 1)) && _laughTimer <= 0)
            {
                _laughTimer = Random.Range(0.7, 1.2);
                Game.Visual.Babble(0.45);
                Game.Raise(GameEventKind.Giggled, _fall);
            }

            Game.Visual.SetExpression(_recover > 0.4 ? Expression.Happy : Expression.Joy, 0.3);
            Game.Visual.LookTarget = new Vec2(0, -0.2);
            Face(_fall > 0.05 ? 0 : -Side * 0.25 * _heat);

            Move(Squirm(), RigMask.Hands | RigMask.Body | RigMask.Head | RigMask.Feet);
            if (_push > _pushDelay + 1.0)
            {
                Game._tickleCooldown = 5;
                return false;
            }

            return !(_recover >= RecoverSeconds && _fall <= 0);
        }

        /// <summary>El esqueleto: se retuerce, se cae de lado pataleando, te aparta o jadea al recuperarse.</summary>
        private Rig Squirm()
        {
            var t = Time;
            var context = new PoseContext(Side, Form, Facing: Game.Visual.Facing);
            var squirm = Gestures.Evaluate(Gesture.Tickled, t, context);
            var heat = _heat;
            var stand = 1 - Easing.Apply(Ease.InOut, _fall);
            var fall = Easing.Apply(Ease.InOut, _fall);

            var rig = default(Rig);
            rig.SetHand(-1, Vec2.Lerp(Rig.RestHand(-1, Form), squirm.Hand(-1, Form), heat), Form, front: true, grip: 0.6 * heat);
            rig.SetHand(1, Vec2.Lerp(Rig.RestHand(1, Form), squirm.Hand(1, Form), heat), Form, front: true, grip: 0.6 * heat);
            rig.FrontL = rig.FrontR = Math.Clamp(heat * 1.5, 0, 1);
            rig.ShowArms = Math.Max(heat, fall);

            // De pie se contonea; caído, rueda un poquito de un lado a otro.
            rig.Spin = squirm.Spin * heat * stand + _fallSide * Math.PI / 2 * fall + 0.12 * Math.Sin(t * 8) * heat * fall;
            rig.OffsetX = squirm.OffsetX * heat * stand;
            rig.OffsetY = 0.02 * fall;
            rig.Stretch = squirm.Stretch * heat * stand;
            rig.HeadTilt = squirm.HeadTilt * heat;
            rig.Sprout = squirm.Sprout * heat;

            // Pataleo de risa.
            var kick = fall * heat;
            rig.FootLY = -0.13 * kick * Math.Max(0, Math.Sin(t * 13));
            rig.FootRY = -0.13 * kick * Math.Max(0, Math.Sin(t * 13 + Math.PI));
            rig.FootLX = 0.05 * kick * Math.Sin(t * 13);
            rig.FootRX = -0.05 * kick * Math.Sin(t * 13);

            if (_push >= 0)
            {
                // Ya de pie: «¡para, para!», con las dos manos en alto y las palmas hacia ti, echándose hacia atrás sin
                // dejar de reírse.
                var p = _push - _pushDelay;
                var w = Easing.Pulse(p, 0.18, 0.8);
                var pump = 0.5 + 0.5 * Math.Sin(p * 16);
                var push = default(Rig);
                push.SetHand(Side, new Vec2(Side * (1.04 + 0.06 * pump), -1.18 + 0.05 * pump), Form, front: false);
                push.SetHand(-Side, new Vec2(-Side * (1.0 + 0.06 * (1 - pump)), -1.16 + 0.05 * (1 - pump)), Form, front: false);
                rig = Rig.Mix(rig, push, RigMask.Hands, w);
                rig.Spin += -Side * 0.07 * w;
                rig.HeadTilt += -Side * 0.08 * w;
            }

            if (_recover > 0)
            {
                // Recupera el aliento: respira hondo y deprisa, cada vez más despacio.
                var u = Math.Clamp(_recover / RecoverSeconds, 0, 1);
                rig.Stretch += 0.04 * Math.Sin(_recover * 11) * (1 - u) * stand;
            }

            return rig;
        }
    }

    /// <summary>
    /// Cepillarle: cierra los ojos de gusto y se inclina hacia el cepillo, que le hace vibrar un poco (como un ronroneo);
    /// suelta pelusas y el pelo se le va poniendo brillante. Cuando paras, se sacude contento.
    /// </summary>
    private sealed class BrushRoutine(KobiiGame game) : Routine(game)
    {
        private const double StopAfter = 2.2;

        private double _sinceStroke;
        private double _heat;
        private double _lean;
        private double _shake = -1;

        public override PlayKind Kind => PlayKind.Brush;

        public void Stroke(Vec2 cursor)
        {
            _sinceStroke = 0;
            _shake = -1;
            Game.Visual.Shine = Math.Min(1, Game.Visual.Shine + 0.035);
            Game.Visual.AddEmote(EmoteKind.Fluff, cursor, Game.Body.Scale, Random.Chance(0.5) ? 2 : 1);
            Game.Visual.Bounce(-0.5);
            Side = Math.Sign(cursor.X - Game.Body.Feet.X) is var s && s != 0 ? s : Side;
            if (Random.Chance(0.12))
            {
                Game.Visual.AddEmote(EmoteKind.Heart, Game.HeadPoint, Game.Body.Scale);
            }
        }

        protected override bool Update(World world, Sensors sensors, double dt)
        {
            _sinceStroke += dt;
            var brushing = _sinceStroke < 0.9;
            _heat = MathUtil.Damp(_heat, brushing ? 1 : 0, brushing ? 6 : 3, dt);
            _lean = MathUtil.Damp(_lean, brushing ? Side : 0, 3, dt);
            Game.Visual.EyesClosed = _heat;
            Game.Visual.SetExpression(_heat > 0.3 ? Expression.Happy : Expression.Joy, 0.3);
            Face(_lean * 0.3);

            // Se inclina hacia el cepillo y vibra un poquito, de gusto.
            var rig = default(Rig);
            rig.Spin = 0.10 * _lean * _heat;
            rig.HeadTilt = 0.12 * _lean * _heat;
            rig.Stretch = 0.012 * Math.Sin(Time * 38) * _heat;
            rig.Sprout = -0.25 * _lean * _heat;
            if (!brushing && _sinceStroke > StopAfter && _shake < 0)
            {
                _shake = 0;
                Game.Raise(GameEventKind.Shook);
                if (Game.Visual.Shine > 0.5)
                {
                    Game.Visual.AddEmote(EmoteKind.Sparkle, Game.Body.Center, Game.Body.Scale, 6);
                }
            }

            if (_shake >= 0)
            {
                // Se sacude como un perrito y queda tan contento.
                _shake += dt;
                var w = Easing.Pulse(_shake, 0.08, 0.45);
                rig.Spin += 0.16 * Math.Sin(_shake * 34) * w;
                rig.OffsetX = 0.03 * Math.Sin(_shake * 34) * w;
                rig.Sprout += 0.4 * Math.Sin(_shake * 34 + 1) * w;
                if (_shake >= 0.6)
                {
                    Game.Visual.EyesClosed = 0;
                    Game.Visual.SetExpression(Expression.Joy, 1.2);
                    return false;
                }
            }

            Move(rig, RigMask.Body | RigMask.Head);
            return true;
        }

        public override void Stop() => Game.Visual.EyesClosed = 0;
    }

    /// <summary>
    /// Chocar los cinco: levanta la mano abierta y espera a que la toques con el cursor. Si la chocas, la mano sale
    /// despedida, salta una estrella y lo celebra; si le dejas colgado, la baja algo chafado.
    /// </summary>
    private sealed class HighFiveRoutine(KobiiGame game, double side) : Routine(game)
    {
        private const double RaiseSeconds = 0.4;
        private const double WaitSeconds = 4.5;

        private Phase _phase;
        private double _t;
        private Vec2 _from;
        private Vec2 _impact;
        private bool _inside = true;

        private enum Phase
        {
            Raise,
            Wait,
            Slap,
            Celebrate,
            Disappointed,
        }

        public override PlayKind Kind => PlayKind.HighFive;

        public override bool AllowsAirborne => _phase == Phase.Celebrate;

        public override Pose? Pose => _phase == Phase.Celebrate ? Animation.Pose.Hop : null;

        private Vec2 Up => new(Side * 1.14, -1.56 - 0.04 * Rig.Grow(Form));

        public override void Begin()
        {
            Side = side;
            _from = Game.Animator.Rig.Hand(Side, Form);
            Game.Visual.SetExpression(Expression.Joy, 1.5);
        }

        protected override bool Update(World world, Sensors sensors, double dt)
        {
            _t += dt;
            Face(Side * 0.45);
            LookAt(sensors.Cursor);
            var rig = default(Rig);
            switch (_phase)
            {
                case Phase.Raise:
                {
                    // Coge impulso agachándose un poco y sube la mano en arco, pasándose un pelín.
                    var u = Easing.Segment(_t, 0.08, RaiseSeconds - 0.08, Ease.Back);
                    rig.SetHand(Side, Easing.Arc(_from, Up, new Vec2(Side * 0.14, 0.04), u), Form, front: false);
                    rig.Stretch = -0.07 * Easing.Pulse(_t, 0.07, 0.12) + 0.04 * Easing.Segment(_t, 0.1, 0.25);
                    if (_t >= RaiseSeconds)
                    {
                        Next(Phase.Wait);
                    }

                    break;
                }

                case Phase.Wait:
                {
                    // Espera impaciente, botando un poco; la mano se mueve como diciendo «¡venga!».
                    rig.SetHand(Side, Up + new Vec2(0.02 * Math.Sin(_t * 9), 0.03 * Math.Sin(_t * 7)), Form, front: false);
                    rig.Stretch = 0.04 + 0.025 * Math.Sin(_t * 7);
                    Game.Visual.SetExpression(Expression.Joy, 0.3);
                    var inside = Game.CursorOnHand(sensors, Side);
                    if (inside && !_inside)
                    {
                        Slap(sensors);
                    }
                    else if (_t >= WaitSeconds)
                    {
                        Next(Phase.Disappointed);
                        Game.Visual.SetExpression(Expression.Sad, 1.6);
                    }

                    _inside = inside;
                    break;
                }

                case Phase.Slap:
                {
                    // La mano sale despedida en la dirección del golpe y vuelve con el muelle.
                    var hit = Easing.Pulse(_t, 0.04, 0.3);
                    rig.SetHand(Side, Up + _impact * 0.30 * hit, Form, front: false);
                    rig.Spin = -Side * 0.07 * hit;
                    rig.Stretch = 0.04 * (1 - hit);
                    if (_t >= 0.32)
                    {
                        Next(Phase.Celebrate);
                        Game.Body.Hop(260);
                    }

                    break;
                }

                case Phase.Celebrate:
                {
                    // ¡Bien! Los dos brazos arriba con un saltito.
                    var arms = Gestures.Evaluate(Gesture.ArmsUp, _t, new PoseContext(Side, Form));
                    var w = Easing.Segment(_t, 0, 0.15) * (1 - Easing.Segment(_t, 0.6, 0.3));
                    rig = Rig.Mix(rig, arms, RigMask.Hands, w);
                    if (_t >= 0.9 && Game.Body.IsGrounded)
                    {
                        return false;
                    }

                    break;
                }

                case Phase.Disappointed:
                {
                    // Le han dejado colgado: baja la mano despacio y se encoge un poco.
                    var u = Easing.Segment(_t, 0.1, 0.8);
                    rig.SetHand(Side, Easing.Arc(_from, Rig.RestHand(Side, Form), new Vec2(Side * 0.08, 0), u), Form, front: false);
                    rig.HeadTilt = Side * 0.10 * Easing.Pulse(_t, 0.3, 0.6);
                    rig.Stretch = -0.05 * Easing.Pulse(_t, 0.3, 0.6);
                    Game.Visual.LookTarget = new Vec2(0, 0.6);
                    if (_t >= 1.1)
                    {
                        return false;
                    }

                    break;
                }
            }

            Move(rig, (Side < 0 ? RigMask.LeftHand : RigMask.RightHand) | RigMask.Body | RigMask.Head
                | (_phase == Phase.Celebrate ? RigMask.Hands : RigMask.None));
            return true;
        }

        private void Slap(Sensors sensors)
        {
            var velocity = sensors.CursorVelocity;
            _impact = velocity.Length > 30 * Game.Body.Scale ? velocity.Normalized() : new Vec2(Side * 0.5, -0.5).Normalized();
            var hand = Game.HandWorld(Side);
            Game.Visual.AddEmote(EmoteKind.Burst, hand, Game.Body.Scale);
            Game.Visual.AddEmote(EmoteKind.Sparkle, hand, Game.Body.Scale, 6);
            Game.Visual.Bounce(-2.4);
            Game.Visual.SetExpression(Expression.Joy, 1.8);
            Game.Raise(GameEventKind.HighFive);
            Next(Phase.Slap);
        }

        private void Next(Phase phase)
        {
            _phase = phase;
            _t = 0;
            _from = Game.Animator.Rig.Hand(Side, Form);
        }
    }

    /// <summary>
    /// Darse la mano: te ofrece la mano y, cuando llevas el cursor a ella, te la coge y la mano sigue al cursor: si lo
    /// mueves arriba y abajo, os la estrecháis (el cuerpo acompaña). Después te hace una pequeña reverencia, contento. Si
    /// no se la coges, la retira.
    /// </summary>
    private sealed class HandshakeRoutine(KobiiGame game, double side) : Routine(game)
    {
        private const double WaitSeconds = 5;

        private Phase _phase;
        private double _t;
        private Vec2 _from;
        private Vec2 _hand;
        private double _lastY;
        private double _travel;
        private int _direction;
        private int _shakes;

        private enum Phase
        {
            Offer,
            Wait,
            Shake,
            Done,
            Slipped,
        }

        public override PlayKind Kind => PlayKind.Handshake;

        /// <summary>Donde ofrece la mano: delante, a la altura del pecho.</summary>
        private Vec2 Offered => new(Side * 1.16, -0.82);

        public override void Begin()
        {
            Side = side;
            _from = Game.Animator.Rig.Hand(Side, Form);
            Game.Visual.SetExpression(Expression.Happy, 1.5);
        }

        protected override bool Update(World world, Sensors sensors, double dt)
        {
            _t += dt;
            Face(Side * 0.5);
            LookAt(sensors.Cursor);
            var rig = default(Rig);
            switch (_phase)
            {
                case Phase.Offer:
                {
                    var u = Easing.Segment(_t, 0, 0.45, Ease.Out);
                    rig.SetHand(Side, Easing.Arc(_from, Offered, new Vec2(Side * 0.06, 0.10), u), Form, front: false);
                    rig.HeadTilt = Side * 0.06 * u;
                    rig.FaceY = 0.03 * Easing.Pulse(_t, 0.2, 0.3);
                    if (_t >= 0.45)
                    {
                        Next(Phase.Wait);
                    }

                    break;
                }

                case Phase.Wait:
                    rig.SetHand(Side, Offered + new Vec2(0, 0.02 * Math.Sin(_t * 3.5)), Form, front: false);
                    rig.HeadTilt = Side * 0.06;
                    if (Game.CursorOnHand(sensors, Side, HandTouchRadius + 0.05))
                    {
                        // Te la coge.
                        _hand = Offered;
                        _lastY = _hand.Y;
                        Game.Visual.SetExpression(Expression.Joy, 1);
                        Game.Visual.Bounce(-1);
                        Next(Phase.Shake);
                    }
                    else if (_t >= WaitSeconds)
                    {
                        Game.Visual.SetExpression(Expression.Confused, 1.4);
                        Next(Phase.Slipped);
                    }

                    break;

                case Phase.Shake:
                {
                    if (sensors.Cursor is not { } cursor)
                    {
                        Next(Phase.Slipped);
                        break;
                    }

                    // La mano sigue al cursor (hasta donde le llega el brazo) y cuenta cada sacudida.
                    var local = Game.BodyLocal(cursor);
                    var offered = Offered;
                    _hand = Game.WithinReach(Side, new Vec2(Math.Clamp(local.X, offered.X - 0.22, offered.X + 0.18), Math.Clamp(local.Y, offered.Y - 0.32, offered.Y + 0.26)), 1.0);
                    CountShake(_hand.Y);
                    var dy = _hand.Y - offered.Y;
                    rig.SetHand(Side, _hand, Form, front: false, grip: 1);
                    rig.OffsetY = 0.16 * dy;
                    rig.Stretch = -0.10 * dy;
                    rig.HeadTilt = Side * 0.05 + 0.12 * dy;
                    Game.Visual.SetExpression(Expression.Joy, 0.3);
                    var away = Vec2.Distance(local, _hand);
                    if ((_shakes >= 6 && _t > 1.5) || _t > 4 || (away > 0.85 && _shakes >= 2))
                    {
                        Succeed();
                    }
                    else if (away > 0.85)
                    {
                        Game.Visual.SetExpression(Expression.Surprised, 1);
                        Next(Phase.Slipped);
                    }

                    break;
                }

                case Phase.Done:
                {
                    // La suelta y te hace una pequeña reverencia, muy contento.
                    var u = Easing.Segment(_t, 0, 0.5);
                    rig.SetHand(Side, Easing.Arc(_from, Rig.RestHand(Side, Form), new Vec2(Side * 0.06, 0.04), u), Form, front: false, grip: 1 - u);
                    var bow = Easing.Pulse(_t - 0.3, 0.3, 0.5);
                    rig.FaceY = 0.10 * bow;
                    rig.Stretch = -0.08 * bow;
                    rig.HeadTilt = Side * 0.04 * (1 - u);
                    if (_t >= 1.3)
                    {
                        return false;
                    }

                    break;
                }

                case Phase.Slipped:
                {
                    var u = Easing.Segment(_t, 0, 0.55);
                    rig.SetHand(Side, Easing.Arc(_from, Rig.RestHand(Side, Form), new Vec2(Side * 0.06, 0.04), u), Form, front: false);
                    rig.HeadTilt = -Side * 0.10 * Easing.Pulse(_t, 0.2, 0.6);
                    if (_t >= 0.9)
                    {
                        return false;
                    }

                    break;
                }
            }

            Move(rig, (Side < 0 ? RigMask.LeftHand : RigMask.RightHand) | RigMask.Body | RigMask.Head | RigMask.Face);
            return true;
        }

        /// <summary>Una sacudida es un cambio de sentido después de mover la mano un poco.</summary>
        private void CountShake(double y)
        {
            var dy = y - _lastY;
            _lastY = y;
            if (Math.Abs(dy) < 1e-4)
            {
                return;
            }

            var direction = Math.Sign(dy);
            if (direction == _direction || _direction == 0)
            {
                _direction = direction;
                _travel += Math.Abs(dy);
                return;
            }

            if (_travel >= 0.06)
            {
                _shakes++;
                Game.Visual.Bounce(-0.6);
            }

            _direction = direction;
            _travel = Math.Abs(dy);
        }

        private void Succeed()
        {
            Game.Visual.SetExpression(Expression.Love, 1.6);
            Game.Visual.AddEmote(EmoteKind.Heart, Game.HeadPoint, Game.Body.Scale, 2);
            Game.Raise(GameEventKind.Handshake);
            Next(Phase.Done);
        }

        private void Next(Phase phase)
        {
            _phase = phase;
            _t = 0;
            _from = Game.Animator.Rig.Hand(Side, Form);
        }
    }
}
