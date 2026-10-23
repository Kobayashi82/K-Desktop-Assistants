using Kobii.Core.Animation;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Creature;

/// <summary>
/// Perseguir mariposas. Aparece una revoloteando, la mira encantado y sale corriendo detrás; cuando la tiene a
/// tiro se agacha, salta y da una palmada con las manos ahuecadas. Casi siempre se le escapa por los pelos (y se
/// mira las manos vacías, extrañado), pero a veces la atrapa, se asoma entre los dedos y la suelta; o se le posa
/// en el brote y se queda quietecito mirándola hasta que se va… o hasta que estornuda. Es la de Kobii AI sin edades.
/// </summary>
public sealed partial class KobiiGame
{
    /// <summary>Entre una mariposa y la siguiente pasan de 12 a 25 minutos (en segundos de simulación).</summary>
    private const double ButterflyGapMin = 720;
    private const double ButterflyGapMax = 1500;

    private Butterfly? _butterfly;

    /// <summary>Desde cuándo puede pasar otra mariposa (la primera, a los pocos minutos).</summary>
    private double _butterflyAt = 240;

    /// <summary>La mariposa que revolotea a su alrededor (también mientras se aleja), o null.</summary>
    public Butterfly? Butterfly => _butterfly;

    /// <summary>Pasa una mariposa por aquí (no son de cada rato).</summary>
    private bool ButterflyPassing => _butterfly is null && Time >= _butterflyAt;

    /// <summary>
    /// Aparece una mariposa y va a por ella (con sueño, de noche, solo la mira). Devuelve false si ahora no puede o si aún
    /// hay otra mariposa.
    /// </summary>
    /// <param name="ending">Cómo termina (tests y vistas previas); null, al azar.</param>
    /// <param name="misses">Cuántas veces se le escapa antes (tests y vistas previas); null, al azar.</param>
    /// <param name="chase">Si la persigue o solo la mira (null: según su ánimo).</param>
    public bool ChaseButterfly(ButterflyEnding? ending = null, int? misses = null, bool? chase = null)
    {
        if (!CanPlay || Activity == Activity.Sleep || !Body.IsGrounded || _butterfly is not null)
        {
            return false;
        }

        StartRoutine(new ButterflyRoutine(this, chase: chase ?? Mood != Mood.Sleepy, ending, misses));
        return true;
    }

    /// <summary>La mariposa vuela (también cuando ya ha terminado el juego, mientras se aleja).</summary>
    private void StepButterfly(World world, Sensors sensors, double dt)
    {
        if (_butterfly is not { } butterfly)
        {
            return;
        }

        var monitor = world.MonitorAt(butterfly.Position) ?? world.NearestMonitor(butterfly.Position);
        butterfly.Scale = MathUtil.MoveTowards(butterfly.Scale, _options.Size * (monitor?.DpiScale ?? 1), CreatureBody.ScaleRate * dt);

        // El cursor que pasa deprisa a su lado la asusta.
        if (sensors.Cursor is { } cursor && butterfly.Mode == ButterflyMode.Flutter && butterfly.Burst < 0.3
            && sensors.CursorVelocity.Length > 600 * butterfly.Scale && Vec2.Distance(cursor, butterfly.Position) < butterfly.Wingspan * 2.5)
        {
            butterfly.Startle(butterfly.Position - cursor + new Vec2(0, -butterfly.Wingspan));
        }

        butterfly.Step(dt, _random);

        // Posada en el brote o entre las manos: va donde estén ahora (después de animar el esqueleto).
        if (Activity == Activity.Play && _routine is ButterflyRoutine routine && routine.Anchor() is { } anchor)
        {
            butterfly.HoldAt(anchor);
        }

        if (butterfly.Gone)
        {
            _butterfly = null;
        }
    }

    /// <summary>
    /// Donde se posa una mariposa: sobre la hoja del brote, con las mismas cuentas que el dibujo (KobiiPainter.DrawSprout; el
    /// vaivén que pone el dibujo con su reloj no cuenta: es de un par de píxeles).
    /// </summary>
    private Vec2 SproutPerch()
    {
        var rig = Animator.Rig;
        var top = -1.72 - 0.08 * Rig.Grow(Form);
        var sway = rig.Sprout + 0.5 * rig.Melt;
        var tip = new Vec2(0.10 + sway, top - 0.24 + 0.06 * Math.Abs(rig.Sprout) + 0.12 * rig.Melt);
        var angle = -0.6 + sway * 1.4;
        var perch = tip + new Vec2(Math.Cos(angle), Math.Sin(angle)) * 0.22 + new Vec2(0, -0.05);

        // Del espacio de la cabeza al del cuerpo: inclinación de la cabeza, encorvado y derretido.
        var head = new Vec2(0, -1.0);
        var hunch = new Vec2(Math.Clamp(Visual.Facing * 2.0, -1, 1) * 0.12, 0.09) * rig.Hunch + new Vec2(0, 0.62 * rig.Melt);
        var h = perch - head;
        var cos = Math.Cos(rig.HeadTilt);
        var sin = Math.Sin(rig.HeadTilt);
        return WorldPoint(new Vec2(cos * h.X - sin * h.Y, sin * h.X + cos * h.Y) + head + hunch);
    }

    /// <summary>Entre las dos manos, en el mundo.</summary>
    private Vec2 BetweenHands() => WorldPoint((Animator.Rig.Hand(-1, Form) + Animator.Rig.Hand(1, Form)) * 0.5);

    /// <summary>Forma del cuerpo (siempre la de joven).</summary>
    private static double Form => Species.Form;

    /// <summary>La rutina de la mariposa (ver la clase). Sin final indicado, lo decide el azar.</summary>
    private sealed class ButterflyRoutine(KobiiGame game, bool chase, ButterflyEnding? ending = null, int? misses = null) : Routine(game)
    {
        private const double ChaseSeconds = 24;

        private Butterfly _b = null!;
        private Phase _phase;
        private ButterflyEnding _ending;
        private double _t;
        private int _misses;
        private double _retarget;
        private double _pounceCooldown;
        private double _chaseTime;
        private double _perchSeconds;
        private bool _noticed;
        private bool _airborne;
        private bool _clapped;
        private bool _caught;
        private bool _shown;
        private bool _once;
        private Vec2 _clapAt;
        private double _clapTime;
        private bool _low;
        private double _leaveSide = 1;

        /// <summary>Las manos ahuecadas delante del pecho (lo que hay entre ellas, no se ve).</summary>
        private static Vec2 Cupped => new(0, -0.84);

        /// <summary>Las manos algo más arriba y hacia donde mira, para asomarse entre ellas.</summary>
        private Vec2 PeekAt => new(Game.Visual.Facing * 0.12, -0.70);

        private enum Phase
        {
            /// <summary>Llega revoloteando: la ve, se sorprende y la señala.</summary>
            Arrive,

            /// <summary>Le revolotea alrededor de la cabeza y la sigue con la mirada, encantado.</summary>
            Watch,

            /// <summary>Corre detrás de ella.</summary>
            Chase,

            /// <summary>Se agacha para saltar.</summary>
            Crouch,

            /// <summary>En el aire, con las manos hacia ella, y la palmada.</summary>
            Pounce,

            /// <summary>Al caer, se sienta de culete (las crías).</summary>
            Plop,

            /// <summary>Se mira entre las manos ahuecadas.</summary>
            Peek,

            /// <summary>Levanta las manos y la deja ir.</summary>
            Release,

            /// <summary>Se queda quieto mientras se le posa en el brote.</summary>
            Settle,

            /// <summary>La tiene posada en el brote y la mira hacia arriba.</summary>
            Perched,

            /// <summary>Le hace cosquillas en la nariz… ¡achís!</summary>
            Sneeze,

            /// <summary>Le dice adiós con la mano mientras se aleja.</summary>
            Farewell,
        }

        public override PlayKind Kind => PlayKind.Butterfly;

        public override bool AllowsAirborne => _phase is Phase.Crouch or Phase.Pounce;

        public override Pose? Pose => _phase switch
        {
            Phase.Crouch => Animation.Pose.Crouch,
            Phase.Pounce when !Game.Body.IsGrounded => Animation.Pose.Jump,
            Phase.Plop => Animation.Pose.Sit,
            _ => null,
        };

        private static bool Elderly => false;

        /// <summary>Ya no quedan fallos y el final es atraparla: en este salto la atrapa.</summary>
        private bool WillCatch => _misses == 0 && _ending == ButterflyEnding.Catch;

        private static bool Baby => false;

        public override void Begin()
        {
            var body = Game.Body;
            var h = body.Height;

            // Llega por el lado en el que hay más sitio (hacia donde luego correrá).
            Side = body.Support is { } support && support.Right - body.Feet.X < body.Feet.X - support.Left ? -1 : 1;
            var start = body.Feet + new Vec2(Side * h * 4.5, -h * 2.4);
            if (Game._world.MonitorAt(body.Center) is { } monitor)
            {
                start = new Vec2(start.X, Math.Max(start.Y, monitor.Bounds.Top + h * 0.6));
            }

            _b = new Butterfly(start, body.Scale, Random.Next(Butterfly.Palettes), Random.Range(0, Math.Tau)) { Hurry = 1.3 };
            _b.Target = Game.HeadPoint + new Vec2(Side * h * 0.8, -h * 0.8);
            Game._butterfly = _b;
            Game._butterflyAt = Game.Time + Random.Range(ButterflyGapMin, ButterflyGapMax);

            // Cuántas veces se le escapa antes del final, y cuál es el final.
            _misses = chase ? misses ?? 1 + Random.Next(2) : 0;
            _ending = ending ?? (!chase ? (Random.Chance(0.85) ? ButterflyEnding.Perch : ButterflyEnding.Escape)
                : Random.Chance(Baby ? 0.35 : 0.5) ? ButterflyEnding.Catch
                : Random.Chance(0.6) ? ButterflyEnding.Perch
                : ButterflyEnding.Escape);
        }

        public override void Stop()
        {
            // Si le interrumpen (le coges, se cae…), la mariposa se va.
            if (_b.Mode != ButterflyMode.Leave)
            {
                _b.Leave(_b.Position.X >= Game.Body.Feet.X ? 1 : -1);
            }
        }

        /// <summary>Dónde la sostiene ahora (el brote o las manos), si la sostiene.</summary>
        public Vec2? Anchor() => _b.Mode switch
        {
            ButterflyMode.Perch when _phase is Phase.Settle or Phase.Perched or Phase.Sneeze => Game.SproutPerch() - new Vec2(0, _b.Wingspan * 0.24),
            ButterflyMode.Perch => Game.BetweenHands() - new Vec2(0, _b.Wingspan * 0.32),
            ButterflyMode.Caught => Game.BetweenHands(),
            _ => null,
        };

        protected override bool Update(World world, Sensors sensors, double dt)
        {
            _t += dt;
            var body = Game.Body;
            var rig = default(Rig);
            var mask = RigMask.None;
            var toward = Math.Sign(_b.Position.X - body.Feet.X) is var s && s != 0 ? s : Side;
            if (_b.Visible && _phase is not (Phase.Peek or Phase.Perched or Phase.Sneeze))
            {
                LookAt(_b.Position);
            }

            if (Game._greetPending && _phase is Phase.Arrive or Phase.Watch or Phase.Chase && body.IsGrounded)
            {
                // Vuelves después de un rato: la deja ir para saludarte.
                _b.Leave(toward);
                body.WalkVelocity = 0;
                return false;
            }

            switch (_phase)
            {
                case Phase.Arrive:
                    Arrive(ref rig, ref mask);
                    break;

                case Phase.Watch:
                    Watch(ref rig, ref mask, toward);
                    break;

                case Phase.Chase:
                    Chase(dt, toward);
                    break;

                case Phase.Crouch:
                {
                    // Coge impulso: se agacha con los brazos atrás, sin quitarle ojo.
                    body.WalkVelocity = 0;
                    Face(toward * 0.5);
                    var wind = Easing.Segment(_t, 0, 0.14, Ease.Out);
                    rig.SetHand(-1, Vec2.Lerp(Rig.RestHand(-1, Form), new Vec2(-0.86, -0.36), wind), Form, front: false);
                    rig.SetHand(1, Vec2.Lerp(Rig.RestHand(1, Form), new Vec2(0.86, -0.36), wind), Form, front: false);
                    mask = RigMask.Hands;
                    if (_t >= 0.24)
                    {
                        Leap();
                    }

                    break;
                }

                case Phase.Pounce:
                    if (Pounce(ref rig, ref mask))
                    {
                        return true;
                    }

                    break;

                case Phase.Plop:
                    // Se ha caído de culete: se ríe y se levanta.
                    body.WalkVelocity = 0;
                    if (_t > 0.35)
                    {
                        Game.Visual.SetExpression(Expression.Joy, 0.3);
                    }

                    if (_t >= 1.3)
                    {
                        _pounceCooldown = 0.5;
                        AfterMiss();
                    }

                    break;

                case Phase.Peek:
                    Peek(ref rig, ref mask);
                    break;

                case Phase.Release:
                    Release(ref rig, ref mask);
                    break;

                case Phase.Settle:
                    Settle(ref rig, ref mask);
                    break;

                case Phase.Perched:
                    Perched(ref rig, ref mask);
                    break;

                case Phase.Sneeze:
                    Sneeze(ref rig, ref mask);
                    break;

                case Phase.Farewell:
                {
                    // Adiós con la mano mientras se aleja (si lleva bastón, que va en la mano del lado al que mira,
                    // con la otra).
                    body.WalkVelocity = 0;
                    var hand = Elderly ? -_leaveSide : _leaveSide;
                    Face(_leaveSide * 0.45);
                    var wave = Gestures.Evaluate(Gesture.Wave, _t, new PoseContext(hand, Form));
                    var w = Easing.Segment(_t, 0, 0.25) * (1 - Easing.Segment(_t, 1.7, 0.35));
                    rig = Rig.Mix(rig, wave, (hand < 0 ? RigMask.LeftHand : RigMask.RightHand) | RigMask.Head, w);
                    mask = (hand < 0 ? RigMask.LeftHand : RigMask.RightHand) | RigMask.Head;
                    if (_t >= 2.1)
                    {
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

        /// <summary>Llega revoloteando: respingo, «¡una mariposa!» y la señala con la mano.</summary>
        private void Arrive(ref Rig rig, ref RigMask mask)
        {
            var h = Game.Body.Height;
            _b.Target = Game.HeadPoint + new Vec2(Side * h * 0.8, -h * 0.8);
            Face(Side * 0.5);
            if (!_noticed && _t > 0.8)
            {
                _noticed = true;
                Game.Visual.AddEmote(EmoteKind.Exclamation, Game.HeadPoint, Game.Body.Scale);
                Game.Visual.SetExpression(Expression.Surprised, 0.6);
                Game.Visual.Bounce(-1.5);
            }
            else if (_noticed && _t > 1.4)
            {
                Game.Visual.SetExpression(Expression.Happy, 0.3);
            }

            if (!Elderly)
            {
                // La mano sube hacia ella y vuelve a bajar.
                var point = Easing.Pulse(_t - 0.4, 0.3, 0.7);
                var aim = Game.WithinReach(Side, Game.BodyLocal(_b.Position), 0.9);
                rig.SetHand(Side, Vec2.Lerp(Rig.RestHand(Side, Form), aim, point), Form, front: false);
                mask = Side < 0 ? RigMask.LeftHand : RigMask.RightHand;
            }

            if ((_t > 1.5 && Vec2.Distance(_b.Position, _b.Target) < h * 0.6) || _t > 3.5)
            {
                _b.Hurry = 1;
                Next(Phase.Watch);
            }
        }

        /// <summary>Le revolotea alrededor de la cabeza: la sigue con los ojos y la cabeza, con las manos juntas.</summary>
        private void Watch(ref Rig rig, ref RigMask mask, double toward)
        {
            var body = Game.Body;
            var h = body.Height;
            var spin = 1.1;
            _b.Target = Game.HeadPoint + new Vec2(Math.Cos(_t * spin + (Side > 0 ? 0 : Math.PI)) * h * 0.75, -h * (0.65 + 0.2 * Math.Sin(_t * spin * 2)));
            var dx = Math.Clamp((_b.Position.X - body.Feet.X) / (h * 1.5), -1, 1);
            Face(dx * 0.5);
            rig.HeadTilt = 0.14 * dx;
            rig.Stretch = 0.03 * Math.Sin(_t * 9) * (chase ? 1 : 0.3);
            mask = RigMask.Head | RigMask.Body;
            if (!Elderly)
            {
                // «¡Ooh!»: las manos juntas delante.
                var join = Easing.Segment(_t, 0, 0.3);
                rig.SetHand(-1, Vec2.Lerp(Rig.RestHand(-1, Form), new Vec2(-0.14, -0.70), join), Form, front: true, grip: 0.4 * join);
                rig.SetHand(1, Vec2.Lerp(Rig.RestHand(1, Form), new Vec2(0.14, -0.72), join), Form, front: true, grip: 0.4 * join);
                mask |= RigMask.Hands;
            }

            Game.Visual.SetExpression(Expression.Happy, 0.3);
            if (_t >= (chase ? 1.8 : 4.0))
            {
                if (chase)
                {
                    Next(Phase.Chase);
                }
                else
                {
                    FinishOr(ButterflyEnding.Perch);
                }
            }
        }

        /// <summary>
        /// Corre detrás de ella. La mariposa va siempre un poco por delante (huye si se le acerca y da la vuelta al
        /// llegar al borde) y de vez en cuando baja a tiro: entonces se agacha para saltar.
        /// </summary>
        private void Chase(double dt, double toward)
        {
            var body = Game.Body;
            var h = body.Height;
            _chaseTime += dt;
            _pounceCooldown -= dt;
            _retarget -= dt;
            if (body.Support is not { } support || !body.IsGrounded)
            {
                return;
            }

            var margin = body.HalfWidth + 8 * body.Scale;
            var left = support.Left + margin;
            var right = Math.Max(left, support.Right - margin);
            var dx = _b.Position.X - body.Feet.X;
            if (_retarget <= 0 || Vec2.Distance(_b.Position, _b.Target) < h * 0.4)
            {
                _retarget = Random.Range(1.2, 2.2);
                var x = body.Feet.X + toward * h * Random.Range(1.8, 4.2);
                if (x < left || x > right)
                {
                    x = body.Feet.X - toward * h * Random.Range(1.5, 3);
                }

                // Juguetona: después de subir alto, casi siempre baja a tiro (y al revés).
                _low = Random.Chance(_low ? 0.3 : 0.8);
                _b.Target = new Vec2(Math.Clamp(x, left, right), body.Feet.Y - h * (_low ? Random.Range(1.15, 1.6) : Random.Range(1.9, 2.6)));
            }

            Game.Visual.SetExpression(Expression.Happy, 0.3);
            if (Math.Abs(dx) > h * 0.45)
            {
                // Lejos corre; cerca anda a su paso, sin adelantarla.
                var pace = Math.Clamp(Math.Abs(dx) / (h * 1.6), 0.45, 1) * RunFactor * (Baby ? 0.75 : 0.95);
                Game._running = pace > 1.4;
                if (!Game.WalkTowards(_b.Position.X - Math.Sign(dx) * h * 0.25, pace))
                {
                    body.WalkVelocity = 0;
                }
            }
            else
            {
                body.WalkVelocity = 0;
                Game._running = false;
                Face(toward * 0.4);
            }

            // A tiro (o casi: si está alta, lo intenta igual de un buen salto).
            var rise = body.Feet.Y - _b.Position.Y;
            if (Math.Abs(dx) < h * 0.9 && rise > h * 0.75 && rise < h * 2.5 && _pounceCooldown <= 0)
            {
                body.WalkVelocity = 0;
                Game._running = false;
                Next(Phase.Crouch);
            }
            else if (_chaseTime > ChaseSeconds)
            {
                // Se cansa: la deja ir.
                body.WalkVelocity = 0;
                Game._running = false;
                FinishOr(ButterflyEnding.Escape);
            }
        }

        /// <summary>Salta hacia donde va a estar, lo justo para alcanzarla con las manos por encima de la cabeza.</summary>
        private void Leap()
        {
            var body = Game.Body;
            var h = body.Height;
            var g = CreatureBody.GravityDip * body.Scale;
            var target = _b.Position + _b.Velocity * 0.25;
            if (WillCatch)
            {
                // Esta vez se deja: baja un poco y se queda revoloteando ahí.
                target = new Vec2(target.X, Math.Max(target.Y, body.Feet.Y - h * 1.9));
            }

            var rise = Math.Clamp(body.Feet.Y - target.Y - h * 0.5, h * 0.3, h * 1.4);
            var vy = Math.Sqrt(2 * g * rise);
            var airtime = 2 * vy / g;
            var x = target.X;
            if (body.Support is { } support)
            {
                var margin = body.HalfWidth + 8 * body.Scale;
                x = Math.Clamp(x, support.Left + margin, Math.Max(support.Left + margin, support.Right - margin));
            }

            var vx = Math.Clamp((x - body.Feet.X) / airtime, -h * 2.5, h * 2.5);
            body.Launch(new Vec2(vx, -vy));
            if (WillCatch)
            {
                _b.Target = target;
                _b.Hurry = 0.3;
            }

            Game.Visual.Bounce(-2);
            _airborne = false;
            _clapped = false;
            Next(Phase.Pounce);
        }

        /// <summary>
        /// En el aire: sube con las manos abiertas hacia ella y, arriba, da la palmada. Si toca que se escape, se
        /// asusta justo antes y la palmada se cierra sobre nada. Devuelve true si ya ha cambiado de fase.
        /// </summary>
        private bool Pounce(ref Rig rig, ref RigMask mask)
        {
            var body = Game.Body;
            var h = body.Height;
            _airborne |= !body.IsGrounded;
            var local = Game.BodyLocal(_b.Position);
            if (!_clapped)
            {
                // Sube con los brazos abiertos por los lados de la cabeza, a la altura de la mariposa.
                var reach = Easing.Segment(_t, 0, 0.16, Ease.Out);
                var height = Math.Clamp(local.Y, -1.5, -0.8);
                var lean = 0.25 * Math.Clamp(local.X, -0.6, 0.6);
                rig.SetHand(-1, Vec2.Lerp(Rig.RestHand(-1, Form), Game.WithinReach(-1, new Vec2(-0.8 + lean, height), 0.95), reach), Form, front: false);
                rig.SetHand(1, Vec2.Lerp(Rig.RestHand(1, Form), Game.WithinReach(1, new Vec2(0.8 + lean, height), 0.95), reach), Form, front: false);
                Game.Visual.SetExpression(Expression.Surprised, 0.2);
                var between = Game.BetweenHands();
                var apex = body.Velocity.Y > -70 * body.Scale;
                if (_airborne && (apex || Vec2.Distance(between, _b.Position) < h * 0.3))
                {
                    Clap(local);
                }
            }
            else
            {
                // ¡Plas! delante de la frente y, enseguida, las manos ahuecadas bajan al pecho.
                var down = Easing.Segment(_t - _clapTime, 0.06, 0.24);
                var middle = Vec2.Lerp(_clapAt, Cupped, down);
                rig.SetHand(-1, middle + new Vec2(-0.09, 0.02), Form, front: true, grip: 0.75);
                rig.SetHand(1, middle + new Vec2(0.09, 0), Form, front: true, grip: 0.75);
            }

            mask = RigMask.Hands;
            if (_airborne && body.IsGrounded && _t > 0.1)
            {
                body.Velocity = Vec2.Zero;

                // Al caer: se mira las manos (o se sienta de culete, si es una cría y no la tiene).
                if (_caught)
                {
                    Next(Phase.Peek);
                }
                else if (Baby && Random.Chance(0.5))
                {
                    Game.Visual.Bounce(-3);
                    Game.Visual.SetExpression(Expression.Surprised, 0.4);
                    Next(Phase.Plop);
                }
                else if (Random.Chance(0.6))
                {
                    Next(Phase.Peek);
                }
                else
                {
                    _pounceCooldown = 0.8;
                    AfterMiss();
                }

                return true;
            }

            if (_t > 2.5)
            {
                // No ha llegado a despegar (o algo raro): sigue persiguiéndola.
                AfterMiss();
                return true;
            }

            return false;
        }

        /// <summary>¡Plas! La atrapa si le toca y la tiene entre las manos; si no, se le escapa justo antes.</summary>
        private void Clap(Vec2 local)
        {
            var body = Game.Body;

            // Las dos manos se juntan delante de la boca (más arriba se taparía los ojos).
            _clapAt = new Vec2(Math.Clamp(local.X, -0.42, 0.42), Math.Clamp(local.Y, -1.08, -0.7));
            _clapTime = _t;
            _clapped = true;
            var within = Vec2.Distance(Game.BetweenHands(), _b.Position) < body.Height * 0.6;
            _b.Hurry = 1;
            if (WillCatch && within)
            {
                _caught = true;
                _b.Hide(Game.BetweenHands());
                Game.Visual.SetExpression(Expression.Joy, 0.8);
            }
            else
            {
                // Se escapa por los pelos: arriba y a un lado.
                var away = Math.Sign(_b.Position.X - body.Feet.X) is var s && s != 0 ? s : Random.Sign();
                _b.Startle(new Vec2(away * 0.7, -1));
                _misses = Math.Max(0, _misses - 1);
                Game.Visual.AddEmote(EmoteKind.Burst, Game.BetweenHands(), body.Scale * 0.6);
            }

            Game.Raise(GameEventKind.Clapped, text: _caught ? "caught" : null);
        }

        /// <summary>Se mira entre las manos ahuecadas: vacías (¿eh?) o con la mariposa dentro.</summary>
        private void Peek(ref Rig rig, ref RigMask mask)
        {
            // Sube un poco las manos, agacha la cabeza para mirar y las abre una rendija.
            var bring = Easing.Segment(_t, 0, 0.35);
            var middle = Vec2.Lerp(Cupped, PeekAt, bring);
            var open = Easing.Segment(_t, 0.55, 0.3);
            rig.SetHand(-1, middle + new Vec2(-0.10 - 0.1 * open, 0.01), Form, front: true, grip: 0.8 - 0.5 * open);
            rig.SetHand(1, middle + new Vec2(0.10 + 0.1 * open, -0.01), Form, front: true, grip: 0.8 - 0.5 * open);
            rig.FaceY = 0.05 * bring;
            mask = RigMask.Hands | RigMask.Face;
            Face(Game.Visual.FacingTarget * 0.5);
            Game.Visual.LookTarget = new Vec2(0, 0.6);
            Game.Body.WalkVelocity = 0;
            if (_caught)
            {
                if (!_shown && _t >= 0.6)
                {
                    // ¡La tiene! Se la ve en las palmas.
                    _shown = true;
                    _b.Land();
                    Game.Visual.SetExpression(Expression.Love, 1.4);
                    Game.Visual.AddEmote(EmoteKind.Sparkle, Game.BetweenHands(), Game.Body.Scale, 4);
                }

                if (_t >= 1.7)
                {
                    Next(Phase.Release);
                }

                return;
            }

            if (_t >= 0.65 && !_once)
            {
                _once = true;
                // Vacías.
                Game.Visual.SetExpression(Expression.Confused, 0.9);
                Game.Visual.AddEmote(EmoteKind.Question, Game.HeadPoint, Game.Body.Scale);
            }

            if (_t >= 1.35)
            {
                _pounceCooldown = 0.4;
                AfterMiss();
            }
        }

        /// <summary>Levanta las manos y las abre: la mariposa se va volando y él la despide.</summary>
        private void Release(ref Rig rig, ref RigMask mask)
        {
            // Sube las manos hasta la barbilla y las abre hacia los lados, con las palmas arriba: «¡vuela!».
            var lift = Easing.Segment(_t, 0, 0.4, Ease.Out);
            var middle = Vec2.Lerp(PeekAt, new Vec2(Game.Visual.Facing * 0.1, -0.92), lift);
            var open = Easing.Segment(_t, 0.4, 0.3, Ease.Out);
            rig.SetHand(-1, middle + new Vec2(-0.10 - 0.5 * open, 0.01 - 0.12 * open), Form, front: true, grip: 0.35 * (1 - open));
            rig.SetHand(1, middle + new Vec2(0.10 + 0.5 * open, -0.01 - 0.12 * open), Form, front: true, grip: 0.35 * (1 - open));
            mask = RigMask.Hands;
            if (_t >= 0.5 && _b.Mode == ButterflyMode.Perch)
            {
                _leaveSide = Random.Sign();
                _b.Leave(_leaveSide);
                Game.Visual.SetExpression(Expression.Joy, 1.5);
                Game.Visual.AddEmote(EmoteKind.Heart, Game.HeadPoint, Game.Body.Scale, 2);
            }

            if (_t >= 1.1)
            {
                Next(Phase.Farewell);
            }
        }

        /// <summary>Se queda quieto, aguantando la respiración, mientras la mariposa baja a posarse en su brote.</summary>
        private void Settle(ref Rig rig, ref RigMask mask)
        {
            var body = Game.Body;
            body.WalkVelocity = 0;
            Face(0);
            var perch = Game.SproutPerch() - new Vec2(0, _b.Wingspan * 0.24);
            if (_b.Mode == ButterflyMode.Flutter)
            {
                _b.Settling = true;
                _b.Hurry = 0.8;
                _b.Target = perch;
                if (Vec2.Distance(_b.Position, perch) < _b.Wingspan * 0.12 || _t > 5)
                {
                    _b.Land();
                    Game.Visual.Bounce(-0.6);
                    Next(Phase.Perched);
                    _perchSeconds = Random.Range(2.8, 4.2);
                }
            }

            Game.Visual.SetExpression(Expression.Surprised, 0.3);
            Hold(ref rig, ref mask);
        }

        /// <summary>La tiene posada en el brote: la mira hacia arriba, quietecito y encantado.</summary>
        private void Perched(ref Rig rig, ref RigMask mask)
        {
            Face(0);
            Game.Visual.LookTarget = new Vec2(0.08, -1);
            Game.Visual.SetExpression(_t < 0.8 ? Expression.Surprised : Expression.Happy, 0.3);
            if (_t >= 1.2 && !_once)
            {
                _once = true;
                Game.Visual.AddEmote(EmoteKind.Heart, Game.HeadPoint + new Vec2(Game.Body.HalfWidth * 0.8, 0), Game.Body.Scale);
            }

            Hold(ref rig, ref mask);
            rig.HeadTilt = 0.03 * Math.Sin(_t * 1.6);
            if (_t >= _perchSeconds)
            {
                if (Random.Chance(0.35))
                {
                    Next(Phase.Sneeze);
                }
                else
                {
                    // Se va sola, despacito.
                    _leaveSide = Random.Sign();
                    _b.Leave(_leaveSide);
                    Next(Phase.Farewell);
                }
            }
        }

        /// <summary>Le hace cosquillas en la nariz: coge aire, ¡achís!, y la mariposa sale volando del susto.</summary>
        private void Sneeze(ref Rig rig, ref RigMask mask)
        {
            const double at = 0.7;
            var inhale = Easing.Segment(_t, 0, at, Ease.In);
            var kick = Easing.Pulse(_t - at, 0.06, 0.5);
            Hold(ref rig, ref mask);
            rig.HeadTilt = 0;
            rig.FaceY = -0.05 * inhale * (1 - kick) + 0.06 * kick;
            rig.Stretch = 0.06 * inhale * (_t < at ? 1 : 0) - 0.08 * kick;
            mask |= RigMask.Face | RigMask.Body;
            if (_t < at)
            {
                Game.Visual.LookTarget = new Vec2(0.08, -1);
                Game.Visual.SetExpression(Expression.Surprised, 0.2);
                return;
            }

            if (_b.Mode == ButterflyMode.Perch)
            {
                // ¡Achís!
                Game.Visual.Bounce(-4.5);
                Game.Visual.SetExpression(Expression.Pain, 0.5);
                Game.Visual.AddEmote(EmoteKind.Sneeze, Game.MouthPoint, Game.Body.Scale, 6);
                Game.Raise(GameEventKind.Sneezed);
                _leaveSide = Random.Sign();
                _b.Startle(new Vec2(_leaveSide * 0.5, -1));
                _b.Leave(_leaveSide);
            }

            if (_t >= at + 0.6)
            {
                Game.Visual.SetExpression(Expression.Joy, 1.2);
                Next(Phase.Farewell);
            }
        }

        /// <summary>Quieto con las manos juntas delante (los mayores, con su bastón).</summary>
        private static void Hold(ref Rig rig, ref RigMask mask)
        {
            mask |= RigMask.Head;
            if (Elderly)
            {
                return;
            }

            rig.SetHand(-1, new Vec2(-0.13, -0.66), Form, front: true, grip: 0.5);
            rig.SetHand(1, new Vec2(0.13, -0.68), Form, front: true, grip: 0.5);
            mask |= RigMask.Hands;
        }

        /// <summary>Después de fallar: sigue persiguiéndola o, si ya no quedan fallos, viene el final.</summary>
        private void AfterMiss()
        {
            if (_misses > 0 || _ending == ButterflyEnding.Catch)
            {
                Next(Phase.Chase);
            }
            else
            {
                FinishOr(_ending);
            }
        }

        /// <summary>El final: se le posa en el brote, o se va (y le dice adiós).</summary>
        private void FinishOr(ButterflyEnding ending)
        {
            Game.Body.WalkVelocity = 0;
            if (ending == ButterflyEnding.Perch && Game.Body.IsGrounded)
            {
                Next(Phase.Settle);
                return;
            }

            _leaveSide = _b.Position.X >= Game.Body.Feet.X ? 1 : -1;
            _b.Leave(_leaveSide);
            Next(Phase.Farewell);
        }

        private void Next(Phase phase)
        {
            _phase = phase;
            _t = 0;
            _once = false;
        }
    }
}
