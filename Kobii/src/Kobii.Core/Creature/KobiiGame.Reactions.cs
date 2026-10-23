using Kobii.Core.Animation;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Creature;

/// <summary>
/// Reacciones a lo que pasa en el PC (las de Kobii AI): te avisa de las notificaciones, baila al compás de la música que
/// suena (o lleva el ritmo con el pie), se cansa con la batería baja y se alegra al enchufarlo, y barre con una escobita
/// cuando vacías la papelera. Aquí está también la ropa de dormir, que va con la hora: pijama y gorro por la noche.
/// </summary>
public sealed partial class KobiiGame
{
    /// <summary>Tras avisarte de una notificación, en este rato las siguientes solo le hacen levantar la vista.</summary>
    private const double NotifyCooldownSeconds = 20;

    private bool _dressSeen;
    private bool _dressed;
    private double _dressWait;
    private double _musicSeconds;
    private double _danceCooldown;
    private double _beat;
    private double _sensorBeat = double.NaN;
    private double _bpm;
    private bool _musicPlaying;
    private bool _batterySeen;
    private bool _onBattery;
    private bool _batteryLow;
    private double _batteryReminder = 120;
    private double _notifyCooldown;

    /// <summary>Lleva puesto el pijama (o se lo está poniendo).</summary>
    public bool InPajamas => _dressed;

    /// <summary>
    /// Te ha llegado una notificación en <paramref name="where"/> (su centro, en el mundo): se sobresalta, la mira y la
    /// señala. Devuelve false si ahora no le hace caso (dormido, en el aire, con una orden…) o si acaba de avisarte de otra
    /// (una ráfaga de mensajes no le tiene señalando sin parar).
    /// </summary>
    public bool NoticeNotification(Vec2 where)
    {
        if (!IsInitialized || Activity == Activity.Sleep)
        {
            return false;
        }

        if (_notifyCooldown > 0 || !CanPlay || (Activity == Activity.Play && _routine is not (DanceRoutine or NotifyRoutine)))
        {
            // Ocupado (o ya te ha avisado hace nada): solo levanta la vista.
            Visual.AddEmote(EmoteKind.Exclamation, HeadPoint, Body.Scale);
            return false;
        }

        _notifyCooldown = NotifyCooldownSeconds;
        StartRoutine(new NotifyRoutine(this, where));
        return true;
    }

    /// <summary>Has vaciado la papelera: saca una escobita y barre. Devuelve false si ahora no puede.</summary>
    public bool RecycleBinEmptied()
    {
        if (!CanPlay || Activity == Activity.Sleep)
        {
            return false;
        }

        StartRoutine(new SweepRoutine(this));
        return true;
    }

    /// <summary>Se pone a bailar ahora (sin música, al pulso que lleve).</summary>
    public bool StartDance()
    {
        if (!CanPlay || Activity == Activity.Sleep)
        {
            return false;
        }

        StartRoutine(new DanceRoutine(this));
        return true;
    }

    /// <summary>Lo de cada paso: el pulso de la música, la batería y la ropa de dormir.</summary>
    private void UpdateReactions(Sensors sensors, double dt)
    {
        _notifyCooldown -= dt;
        UpdateBeat(sensors, dt);
        UpdateBattery(sensors, dt);
        UpdateDress(sensors, dt);
    }

    /// <summary>Sigue el pulso (entre lecturas, al tempo que lleva) y, si suena música un rato, se pone a bailar.</summary>
    private void UpdateBeat(Sensors sensors, double dt)
    {
        var music = sensors.Music;
        _musicPlaying = music.Playing && !sensors.Quiet;
        if (music.Bpm > 0)
        {
            _bpm = music.Bpm;
        }

        if (music.Beat != _sensorBeat && music.Bpm > 0)
        {
            _sensorBeat = music.Beat;
            _beat = music.Beat;
        }
        else
        {
            _beat += dt * (_bpm > 0 ? _bpm : 110) / 60;
        }

        _musicSeconds = _musicPlaying ? _musicSeconds + dt : 0;
        _danceCooldown -= dt;
        if (_musicSeconds > 2.5 && _danceCooldown <= 0 && CanPlay && Activity is Activity.Idle or Activity.Walk or Activity.Perform
            && Mood != Mood.Sleepy)
        {
            StartRoutine(new DanceRoutine(this));
            return;
        }

        // Si no baila, lleva el ritmo con el pie y la cabeza.
        if (_musicPlaying && Activity == Activity.Idle && Body.IsGrounded && !_errand)
        {
            var phase = _beat - Math.Floor(_beat);
            var hit = Math.Exp(-phase * 8);
            var rig = default(Rig);
            rig.HeadTilt = 0.07 * Math.Cos(Math.PI * _beat);
            rig.FaceY = 0.025 * hit;
            var tap = Animator.Side;
            if (tap < 0)
            {
                rig.FootLY = -0.07 * (1 - hit);
            }
            else
            {
                rig.FootRY = -0.07 * (1 - hit);
            }

            Animator.Override(rig, RigMask.Head | RigMask.Face | RigMask.Feet);
        }
    }

    /// <summary>
    /// Batería baja: se cansa (bosteza) y de vez en cuando lo recuerda con otro bostezo; al enchufarlo, se llena de energía
    /// (destellos y un saltito). En un PC sin batería nunca pasa nada de esto.
    /// </summary>
    private void UpdateBattery(Sensors sensors, double dt)
    {
        var awake = Activity != Activity.Sleep;
        if (_batterySeen && awake && !sensors.Quiet)
        {
            if (_onBattery && !sensors.OnBattery)
            {
                // ¡Corriente! Destellos y un saltito de alegría (más si estaba agotado).
                Visual.SetExpression(Expression.Joy, 1.8);
                Visual.AddEmote(EmoteKind.Sparkle, Body.Center, Body.Scale, _batteryLow ? 10 : 5);
                if (Activity is Activity.Idle or Activity.Perform && Body.IsGrounded && !_errand)
                {
                    // Deja lo que estuviera haciendo para celebrarlo.
                    StopPerforming();
                    Body.Hop(_batteryLow ? 420 : 300);
                    Visual.Bounce(-2);
                    SetActivity(Activity.Celebrate, 0.6);
                }
            }
            else if (sensors.BatteryLow && !_batteryLow)
            {
                // Se queda sin pilas: bosteza, cansado.
                Visual.SetExpression(Expression.Sleepy, 2.5);
                if (Activity == Activity.Idle && Body.IsGrounded && !_errand)
                {
                    Perform([AnimationCatalog.Get("bostezar")]);
                }

                _batteryReminder = _random.Range(240, 360);
            }
        }

        if (sensors.BatteryLow && awake)
        {
            _batteryReminder -= dt;
            if (_batteryReminder <= 0)
            {
                _batteryReminder = _random.Range(240, 360);
                Visual.SetExpression(Expression.Sleepy, 2);
                if (Activity == Activity.Idle && Body.IsGrounded && !_errand)
                {
                    Perform([AnimationCatalog.Get("bostezar")]);
                }
            }
        }

        _onBattery = sensors.OnBattery;
        _batteryLow = sensors.BatteryLow;
        _batterySeen = true;
    }

    /// <summary>
    /// A la hora de dormir (de 22:00 a 7:00) lleva pijama y gorro: se los pone al hacerse de noche y se los quita por la
    /// mañana; si entonces está dormido, al despertarse. No depende del tema de Windows (hay quien lo tiene siempre oscuro).
    /// </summary>
    private void UpdateDress(Sensors sensors, double dt)
    {
        var night = IsBedtime(sensors.LocalTime);
        var asleep = Activity == Activity.Sleep;
        if (!_dressSeen)
        {
            // Al empezar ya va vestido como toca.
            _dressSeen = true;
            _dressed = night;
            Visual.Pajamas = Visual.Nightcap = night ? 1 : 0;
        }
        else if (night != _dressed && (night || !asleep))
        {
            _dressed = night;
            _dressWait = 6;
        }

        if (_dressWait > 0)
        {
            // Se cambia en cuanto pueda (si está en el aire o liado, espera un poco; dormido, ni se entera).
            _dressWait -= dt;
            if (CanPlay && !asleep && Activity is Activity.Idle or Activity.Walk or Activity.Perform)
            {
                _dressWait = 0;
                StartRoutine(new DressRoutine(this, _dressed));
                return;
            }

            if (!asleep)
            {
                return;
            }

            _dressWait = 0;
        }

        if (Activity == Activity.Play && _routine is DressRoutine)
        {
            return;
        }

        // Ocupado o dormido: se cambia sin más (poco a poco).
        var target = _dressed ? 1.0 : 0.0;
        Visual.Pajamas = MathUtil.MoveTowards(Visual.Pajamas, target, dt / 0.8);
        Visual.Nightcap = MathUtil.MoveTowards(Visual.Nightcap, target, dt / 0.8);
    }

    /// <summary>La hora de dormir: de 22:00 a 7:00 (la misma noche que la de su autonomía).</summary>
    private static bool IsBedtime(DateTime now) => now.Hour is >= 22 or < 7;

    /// <summary>
    /// Una notificación: da un respingo (orejas arriba), se gira hacia ella, la señala y te lo dice; si está
    /// contento, da un saltito.
    /// </summary>
    private sealed class NotifyRoutine(KobiiGame game, Vec2 where) : Routine(game)
    {
        private bool _said;

        public override PlayKind Kind => PlayKind.Notification;

        public override bool AllowsAirborne => true;

        public override void Begin()
        {
            Side = Math.Sign(where.X - Game.Body.Feet.X) is var s && s != 0 ? s : 1;
            Game.Body.WalkVelocity = 0;
            Game.Visual.SetExpression(Expression.Surprised, 0.7);
            Game.Visual.AddEmote(EmoteKind.Exclamation, Game.HeadPoint, Game.Body.Scale);
            Game.Visual.Bounce(3);
        }

        protected override bool Update(World world, Sensors sensors, double dt)
        {
            var rig = default(Rig);
            var t = Time;
            Face(Side * 0.6);
            LookAt(where);

            // Respingo: se estira y las orejas se levantan.
            var startle = Easing.Pulse(t, 0.08, 0.5);
            rig.Stretch = 0.08 * startle;
            rig.EarL = -0.35 * startle;
            rig.EarR = 0.35 * startle;
            var mask = RigMask.Body | RigMask.Head;

            // La señala.
            var point = Gestures.Evaluate(Gesture.Point, Math.Max(0, t - 0.35), new PoseContext(Side, Form));
            var w = Easing.Segment(t, 0.35, 0.3) * (1 - Easing.Segment(t, 2.0, 0.35));
            if (w > 0)
            {
                var hand = Side < 0 ? RigMask.LeftHand : RigMask.RightHand;
                rig = Rig.Mix(rig, point, hand, w);
                mask |= hand;
            }

            if (!_said && t > 0.5)
            {
                _said = true;
                Game.Visual.SetExpression(Game.Mood == Mood.Happy ? Expression.Joy : Expression.Curious, 1.6);
                if (Game.Mood == Mood.Happy && Game.Body.IsGrounded)
                {
                    Game.Body.Hop(200);
                }
            }

            Move(rig, mask);
            return t < 2.4 || !Game.Body.IsGrounded;
        }
    }

    /// <summary>
    /// Baila al compás de la música: en cada pulso flexiona (se aplasta y rebota), se balancea de un lado a
    /// otro cada dos pulsos y cambia de paso cada ocho (brazos arriba y abajo, saludando en lo alto, paso a un
    /// lado o el de la fiebre del sábado noche). Suelta notas de música y se divierte. Cuando para la música
    /// (o se cansa), lo deja.
    /// </summary>
    private sealed class DanceRoutine(KobiiGame game) : Routine(game)
    {
        private double _length;
        private double _silence;
        private double _lastNote = double.NaN;
        private bool _withoutMusic;

        public override PlayKind Kind => PlayKind.Dance;

        public override bool ControlsLook => true;

        public override void Begin()
        {
            _length = Random.Range(20, 40);
            _withoutMusic = !Game._musicPlaying;
            Side = Game.Visual.FacingTarget < 0 ? -1 : 1;
            Game.Visual.SetExpression(Expression.Joy, 1.5);
        }

        public override void Stop() => Game._danceCooldown = Random.Range(60, 150);

        protected override bool Update(World world, Sensors sensors, double dt)
        {
            if (!_withoutMusic)
            {
                _silence = Game._musicPlaying ? 0 : _silence + dt;
                if (_silence > 1.2)
                {
                    return false;
                }
            }

            if (Time > _length)
            {
                Game.Visual.SetExpression(Expression.Happy, 1.5);
                return false;
            }

            Game.Body.WalkVelocity = 0;
            // Los muelles de la animación van un pelín por detrás: el paso se adelanta un poco al pulso, y la
            // flexión es una curva suave (un golpe seco llegaría tarde y rebotaría).
            var beat = Game._beat + 0.12;
            var phase = beat - Math.Floor(beat);
            var hit = Math.Pow(0.5 + 0.5 * Math.Cos(Math.Tau * phase), 2);
            var sway = Math.Cos(Math.PI * beat);
            var form = Form;
            var rig = Dance(beat, hit, sway, form);
            Move(rig, RigMask.Body | RigMask.Head | RigMask.Hands | RigMask.Feet);
            Game.Visual.FacingTarget = 0.3 * sway;
            Game.Visual.LookTarget = new Vec2(0.3 * sway, -0.2);
            Game.Visual.SetExpression(Math.Floor(beat / 4) % 3 == 2 ? Expression.Happy : Expression.Joy, 0.3);

            // Una nota cada dos pulsos y un poco de diversión cada cuatro.
            var two = Math.Floor(beat / 2);
            if (two != _lastNote)
            {
                _lastNote = two;
                Game.Visual.AddEmote(EmoteKind.Note, Game.HeadPoint + new Vec2(sway * Game.Body.HalfWidth * 0.8, 0), Game.Body.Scale);
            }

            return true;
        }

        /// <summary>El paso de baile en este pulso (cambia cada ocho pulsos).</summary>
        private Rig Dance(double beat, double hit, double sway, double form)
        {
            var rig = default(Rig);
            var s = Side;
            rig.OffsetY = 0.05 * hit;
            rig.Stretch = 0.03 - 0.10 * hit;
            rig.HeadTilt = 0.12 * sway;
            var count = (int)Math.Floor(beat);
            switch ((int)(Math.Floor(beat / 8) % 4 + 4) % 4)
            {
                case 0:
                    // Rebotar con los brazos subiendo y bajando, cada uno en un pulso.
                    rig.OffsetX = 0.05 * sway;
                    rig.Spin = 0.08 * sway;
                {
                    var left = count % 2 == 0 ? hit : 0;
                    var right = count % 2 == 1 ? hit : 0;
                    rig.SetHand(-1, Rig.RestHand(-1, form) + new Vec2(-0.18 * left, -0.75 * left - 0.12), form, front: false);
                    rig.SetHand(1, Rig.RestHand(1, form) + new Vec2(0.18 * right, -0.75 * right - 0.12), form, front: false);
                }

                    break;

                case 1:
                    // Los brazos en alto, moviéndose de un lado a otro como en un concierto.
                    rig.Spin = 0.06 * sway;
                    rig.SetHand(-1, new Vec2(-0.72 + 0.28 * sway, -1.58 + 0.08 * hit), form, front: false);
                    rig.SetHand(1, new Vec2(0.72 + 0.28 * sway, -1.58 + 0.08 * hit), form, front: false);
                    break;

                case 2:
                {
                    // Paso a un lado y al otro, con los brazos al contrario.
                    rig.OffsetX = 0.10 * sway;
                    var lift = -0.12 * (1 - hit);
                    if (count % 2 == 0)
                    {
                        rig.FootLY = lift;
                    }
                    else
                    {
                        rig.FootRY = lift;
                    }

                    rig.SetHand(-1, Rig.RestHand(-1, form) + new Vec2(-0.16 * sway, -0.12), form, front: false);
                    rig.SetHand(1, Rig.RestHand(1, form) + new Vec2(-0.16 * sway, -0.12), form, front: false);
                    break;
                }

                default:
                    // El de la fiebre del sábado noche: un dedo arriba, luego abajo cruzando, y la otra mano en la cadera.
                    rig.Spin = 0.10 * s * sway;
                    rig.SetHand(s, sway > 0 ? new Vec2(s * 0.85, -1.78) : new Vec2(-s * 0.25, -0.30), form, front: sway <= 0);
                    rig.SetHand(-s, new Vec2(-s * 1.0, -0.55), form, front: false, grip: 0.4);
                    break;
            }

            return rig;
        }
    }

    /// <summary>
    /// Barrer: saca una escobita (¡puf!), la coge con las dos manos y barre de un lado a otro levantando
    /// polvo mientras avanza despacito; luego se apoya en ella, se seca la frente satisfecho y la guarda.
    /// </summary>
    private sealed class SweepRoutine(KobiiGame game) : Routine(game)
    {
        private const double SweepSeconds = 3.6;
        private const double Strokes = 1.5;

        private Phase _phase;
        private double _t;
        private double _walkTo = double.NaN;
        private double _lastStroke;

        private enum Phase
        {
            Appear,
            Sweep,
            Rest,
            Vanish,
        }

        public override PlayKind Kind => PlayKind.Sweep;

        public override Prop? Prop => (_phase == Phase.Appear && _t < 0.22) || (_phase == Phase.Vanish && _t > 0.1) ? null : Animation.Prop.Broom;

        public override void Begin()
        {
            // Barre hacia donde haya más sitio.
            if (Game.Body.Support is { } support)
            {
                Side = support.Right - Game.Body.Feet.X > Game.Body.Feet.X - support.Left ? 1 : -1;
                var margin = Game.Body.HalfWidth + 8 * Game.Body.Scale;
                _walkTo = Math.Clamp(Game.Body.Feet.X + Side * Game.Body.Height * 1.4, support.Left + margin, support.Right - margin);
            }

            Game.Visual.SetExpression(Expression.Happy, 1);
            Game.Visual.AddEmote(EmoteKind.Sparkle, Game.HandPosition(Side), Game.Body.Scale, 4);
            Game.Raise(GameEventKind.Pulled, text: "out");
        }

        protected override bool Update(World world, Sensors sensors, double dt)
        {
            _t += dt;
            var rig = default(Rig);
            var s = Side;
            var form = Form;
            Face(s * 0.5);
            switch (_phase)
            {
                case Phase.Appear:
                {
                    // Las manos van al palo.
                    var u = Easing.Segment(_t, 0, 0.35);
                    rig.SetHand(s, Vec2.Lerp(Rig.RestHand(s, form), Lower(0), u), form, front: true, grip: u);
                    rig.SetHand(-s, Vec2.Lerp(Rig.RestHand(-s, form), Upper(0), u), form, front: true, grip: u);
                    if (_t > 0.4)
                    {
                        Next(Phase.Sweep);
                    }

                    break;
                }

                case Phase.Sweep:
                {
                    // De un lado a otro, levantando polvo en cada pasada.
                    var stroke = Math.Sin(_t * Math.Tau * Strokes);
                    rig.SetHand(s, Lower(stroke), form, front: true, grip: 1);
                    rig.SetHand(-s, Upper(stroke), form, front: true, grip: 1);
                    rig.Spin = 0.05 * stroke * s;
                    rig.OffsetX = 0.03 * stroke;
                    var count = Math.Floor(_t * Strokes * 2 + 0.5);
                    if (count != _lastStroke)
                    {
                        _lastStroke = count;
                        Game.Visual.AddEmote(EmoteKind.Dust, Bristles(rig), Game.Body.Scale, 2);
                        Game.Raise(GameEventKind.Swept);
                    }

                    Game.Visual.LookTarget = new Vec2(s * 0.6, 0.8);
                    Game.Visual.SetExpression(Expression.Curious, 0.3);
                    if (double.IsNaN(_walkTo) || !Game.WalkTowards(_walkTo, 0.3))
                    {
                        Game.Body.WalkVelocity = 0;
                    }

                    if (_t > SweepSeconds)
                    {
                        Game.Body.WalkVelocity = 0;
                        Next(Phase.Rest);
                        Game.Visual.SetExpression(Expression.Happy, 2);
                        Game.Visual.AddEmote(EmoteKind.Sparkle, Game.Body.Feet + new Vec2(s * Game.Body.HalfWidth * 1.5, -Game.Body.Height * 0.2), Game.Body.Scale, 5);
                    }

                    break;
                }

                case Phase.Rest:
                {
                    // Apoyado en la escoba, se seca la frente con la otra mano.
                    rig.SetHand(s, Lower(0) + new Vec2(-s * 0.06, -0.08), form, front: true, grip: 1);
                    var wipe = Easing.Segment(_t, 0.1, 0.35) * (1 - Easing.Segment(_t, 1.2, 0.3));
                    var brow = Poses.FacePoint(new PoseContext(s, form, Facing: Game.Visual.Facing), -s * 0.28 + 0.08 * Math.Sin(_t * 9) * wipe, -1.34);
                    rig.SetHand(-s, Vec2.Lerp(Upper(0), brow, wipe), form, front: true, grip: 1 - wipe);
                    rig.Stretch = -0.03 * wipe;
                    Game.Visual.LookTarget = new Vec2(-s * 0.3, -0.2);
                    if (_t > 1.6)
                    {
                        Next(Phase.Vanish);
                        Game.Visual.AddEmote(EmoteKind.Dust, Game.HandPosition(s), Game.Body.Scale, 4);
                    }

                    break;
                }

                case Phase.Vanish:
                {
                    var u = Easing.Segment(_t, 0, 0.35);
                    rig.SetHand(s, Vec2.Lerp(Lower(0), Rig.RestHand(s, form), u), form, front: u < 0.5, grip: 1 - u);
                    rig.SetHand(-s, Vec2.Lerp(Upper(0), Rig.RestHand(-s, form), u), form, front: u < 0.5, grip: 1 - u);
                    if (_t > 0.4)
                    {
                        Game.Raise(GameEventKind.Pulled, text: "in");
                        return false;
                    }

                    break;
                }
            }

            Move(rig, RigMask.Hands | RigMask.Body);
            return true;
        }

        /// <summary>La mano de abajo del palo (la de su lado), en una pasada <paramref name="stroke"/> (−1..1).</summary>
        private Vec2 Lower(double stroke) => new(Side * (0.74 + 0.20 * stroke), -0.30 - 0.04 * Math.Abs(stroke));

        /// <summary>La mano de arriba del palo (la otra, que cruza por delante del cuerpo).</summary>
        private Vec2 Upper(double stroke) => new(Side * (0.36 + 0.08 * stroke), -0.58);

        /// <summary>Dónde están las cerdas de la escoba (en el mundo), siguiendo el palo hasta el suelo.</summary>
        private Vec2 Bristles(in Rig rig)
        {
            var lower = Game.WorldPoint(rig.Hand(Side, Form));
            var upper = Game.WorldPoint(rig.Hand(-Side, Form));
            var direction = (lower - upper).Normalized();
            var down = Math.Max(0.2, direction.Y);
            return lower + direction * ((Game.Body.Feet.Y - lower.Y) / down);
        }

        private void Next(Phase phase)
        {
            _phase = phase;
            _t = 0;
        }
    }

    /// <summary>
    /// Vestirse para dormir (al hacerse de noche): ¡puf!, aparece con el pijama entre destellos y un saltito;
    /// luego se pone el gorro tirando de él con las dos manos hasta las orejas y bosteza. Al revés (por la
    /// mañana): se quita el gorro, ¡puf!, fuera el pijama, y se estira.
    /// </summary>
    private sealed class DressRoutine(KobiiGame game, bool on) : Routine(game)
    {
        private Phase _phase;
        private double _t;

        private enum Phase
        {
            Poof,
            Cap,
            Finish,
        }

        public override PlayKind Kind => PlayKind.Dress;

        public override bool AllowsAirborne => true;

        public override void Begin()
        {
            Game.Body.WalkVelocity = 0;
            Side = Game.Visual.FacingTarget < 0 ? -1 : 1;
            _phase = on ? Phase.Poof : Phase.Cap;
        }

        public override void Stop()
        {
            // Si le interrumpen, termina de cambiarse sin más.
            Game.Visual.Pajamas = Game.Visual.Nightcap = on ? 1 : 0;
        }

        protected override bool Update(World world, Sensors sensors, double dt)
        {
            _t += dt;
            var rig = default(Rig);
            var mask = RigMask.None;
            var form = Form;
            Face(0);
            switch (_phase)
            {
                case Phase.Poof:
                    // ¡Puf! Un saltito entre destellos y ya lleva (o ya no lleva) el pijama.
                    if (_t < 0.05 && Game.Body.IsGrounded)
                    {
                        Game.Body.Hop(260);
                        Game.Visual.AddEmote(EmoteKind.Sparkle, Game.Body.Center, Game.Body.Scale, 9);
                        Game.Raise(GameEventKind.Dressed, text: on ? "on" : "off");
                    }

                    Game.Visual.Pajamas = MathUtil.MoveTowards(Game.Visual.Pajamas, on ? 1 : 0, dt / 0.15);
                    rig.SetHand(-1, Rig.RestHand(-1, form) + new Vec2(-0.12, -0.35), form, front: false);
                    rig.SetHand(1, Rig.RestHand(1, form) + new Vec2(0.12, -0.35), form, front: false);
                    mask = RigMask.Hands;
                    if (_t > 0.6 && Game.Body.IsGrounded)
                    {
                        Next(on ? Phase.Cap : Phase.Finish);
                    }

                    break;

                case Phase.Cap:
                {
                    // Las manos a lo alto de la cabeza y, al bajar, el gorro baja con ellas (o sube, al quitárselo).
                    var reach = Easing.Segment(_t, 0, 0.35);
                    var pull = Easing.Segment(_t, 0.4, 0.5);
                    var back = Easing.Segment(_t, 1.0, 0.35);
                    var high = on ? -1.98 : -1.72;
                    var low = on ? -1.70 : -2.05;
                    for (var side = -1; side <= 1; side += 2)
                    {
                        var top = new Vec2(side * 0.34, MathUtil.Lerp(high, low, pull));
                        var hand = Vec2.Lerp(Vec2.Lerp(Rig.RestHand(side, form), new Vec2(side * 0.34, high), reach), top, Math.Min(1, pull * 2));
                        rig.SetHand(side, Vec2.Lerp(hand, Rig.RestHand(side, form), back), form, front: true, grip: reach * (1 - back));
                    }

                    Game.Visual.Nightcap = on ? pull : 1 - pull;
                    rig.HeadTilt = 0.06 * Math.Sin(_t * 9) * pull * (1 - pull) * 4;
                    mask = RigMask.Hands | RigMask.Head;
                    Game.Visual.LookTarget = new Vec2(0, -0.8);
                    if (_t > 1.4)
                    {
                        Next(on ? Phase.Finish : Phase.Poof);
                    }

                    break;
                }

                case Phase.Finish:
                {
                    // Con pijama, bosteza; sin él, se estira con los brazos arriba.
                    var gesture = on ? Gesture.Yawn : Gesture.ArmsUp;
                    var g = Gestures.Evaluate(gesture, _t, new PoseContext(Side, form, 1.6, Facing: Game.Visual.Facing));
                    mask = Gestures.Mask(gesture, Side);
                    rig = Rig.Mix(rig, g, mask, Easing.Segment(_t, 0, 0.2) * (1 - Easing.Segment(_t, 1.3, 0.3)));
                    if (_t < 0.05)
                    {
                        Game.Visual.SetExpression(on ? Expression.Yawn : Expression.Joy, 1.4);
                    }

                    if (_t > 1.7)
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

        private void Next(Phase phase)
        {
            _phase = phase;
            _t = 0;
        }
    }
}
