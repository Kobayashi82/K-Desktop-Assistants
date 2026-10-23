using Kobii.Core.Animation;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Creature;

/// <summary>
/// Kobii en el escritorio como asistente: une el cuerpo que se posa sobre las ventanas, el comportamiento autónomo, el
/// ánimo y la animación. Es el <c>PetGame</c> de Kobii AI sin la vida (ni hambre, ni salud, ni edades, ni aprender):
/// siempre con la forma de joven y todo lo que sabe hacer desde el primer día. Avanza con paso fijo y no depende de
/// Windows, así que se puede probar entero.
/// </summary>
public sealed partial class KobiiGame
{
    public const double TimeStep = 1.0 / 60;

    /// <summary>Cuánto más rápido va corriendo que andando.</summary>
    private const double RunFactor = 2.4;

    /// <summary>Segundos sin tocar el PC a partir de los que, al volver, te saluda.</summary>
    private const double AwaySeconds = 300;

    /// <summary>Segundos sin tocar el PC tras los que se duerme (de noche, mucho antes).</summary>
    private const double SleepAfterSeconds = 900;

    private const double NightSleepAfterSeconds = 180;

    /// <summary>Lo que tarda en volver a la normalidad la alegría de una caricia (constante de tiempo).</summary>
    private const double CheerSeconds = 1800;

    /// <summary>Segundos sin hacerle caso (estando tú delante) a partir de los que se aburre.</summary>
    private const double BoredAfterSeconds = 1500;

    /// <summary>Las veces (de cada decisión en el suelo) que sube a ver qué hay en una ventana.</summary>
    private const double ExploreChance = 0.06;

    private readonly IRandom _random;
    private readonly List<GameEvent> _events = [];
    private readonly RubDetector _rub = new();
    private readonly Autonomy _autonomy = new();
    private readonly Queue<AnimationClip> _performance = new();
    private readonly List<ToyEvent> _toyEvents = [];

    private GameOptions _options;
    private World _world = World.Empty;
    private Sensors _sensors = new();
    private double _activityTimer;
    private double _lookTimer;
    private double _targetX;
    private double _emoteTimer;
    private double _clipEmoteTimer;
    private double _hopTimer;
    private double _rollTargetX;
    private double _wokeAt = double.NegativeInfinity;
    private double _lastNoticed;
    private double _cheer = 0.55;
    private bool _userAway;
    private bool _greetPending;
    private bool _running;
    private bool _wasHeldFast;
    private bool _dropAtEdge;
    private int _dropSide;
    private double _ownHopUntil = double.NegativeInfinity;
    private double _attendTimer;
    private double _thinkGestureTimer;
    private double _stepPhase;
    private long _platformOwner;
    private double _platformSince;
    private List<AnimationClip>? _pendingAtEdge;
    private double _edgeSide = 1;
    private Vec2? _dragOffset;
    private Vec2 _lastFeet = new(double.NaN, double.NaN);
    private Vec2 _scaleFeet = new(double.NaN, double.NaN);

    public KobiiGame(GameOptions options, IRandom random)
    {
        _options = options;
        _random = random;
        Body = new CreatureBody(Species.HeightDip, options.Size);
    }

    public CreatureBody Body { get; }

    public CreatureVisual Visual { get; } = new();

    public Animator Animator { get; } = new();

    public Activity Activity { get; private set; } = Activity.Fall;

    /// <summary>Lo que hace el módulo ahora.</summary>
    public Attention Attention { get; private set; }

    public Mood Mood { get; private set; } = Mood.Content;

    /// <summary>Segundos de simulación.</summary>
    public double Time { get; private set; }

    public bool IsInitialized { get; private set; }

    /// <summary>Ya está en marcha y no cogido ni en el aire: puede hacer cosas.</summary>
    public bool IsSettled => IsInitialized && Body.IsGrounded && Activity is not (Activity.Held or Activity.Fall);

    /// <summary>Hace falta simular y dibujar a la frecuencia alta.</summary>
    public bool NeedsHighFrequency =>
        Activity is Activity.Walk or Activity.Held or Activity.Fall or Activity.Celebrate or Activity.Travel or Activity.Play
        || _butterfly is not null || _playground.IsMoving || !Body.IsGrounded || Visual.IsAnimating || _rub.Intensity > 0 || Animator.IsMoving;

    /// <summary>
    /// Se mueve tanto que hay que dibujarlo a cada fotograma (andar, caer, un gesto, hablar). Lo demás que pide
    /// <see cref="NeedsHighFrequency"/> (respirar, los muelles que se asientan) se ve bien a pocos fotogramas por segundo.
    /// </summary>
    public bool NeedsFullRate =>
        Activity is Activity.Walk or Activity.Held or Activity.Fall or Activity.Celebrate or Activity.Travel or Activity.Play
        || _butterfly is not null || _playground.IsMoving || _foods.Any(f => !f.Held && !f.Body.IsGrounded) || !Body.IsGrounded || _rub.Intensity > 0 || Visual.Talking || Visual.Mouth > 0.02 || Visual.IsBouncing || Animator.IsMovingMuch;

    /// <summary>Qué le mantiene a la frecuencia alta (diagnóstico).</summary>
    public string BusyReason => string.Join(",", new[]
    {
        Activity is Activity.Walk or Activity.Held or Activity.Fall or Activity.Celebrate or Activity.Travel or Activity.Play ? Activity.ToString() : null,
        _butterfly is not null ? "mariposa" : null,
        _playground.IsMoving ? "juguetes" : null,
        !Body.IsGrounded ? "aire" : null,
        Visual.IsAnimating ? "visual" : null,
        _rub.Intensity > 0 ? "frotar" : null,
        Animator.IsMoving ? "animador" : null,
    }.Where(r => r is not null));

    /// <summary>La cabeza (donde apuntan los bocadillos).</summary>
    public Vec2 HeadPoint => new(Body.Feet.X, Body.Feet.Y - Body.Height * 1.02);

    /// <summary>Coloca a Kobii en el mundo al empezar: en el suelo del monitor principal, cerca de <paramref name="fraction"/>.</summary>
    public void Initialize(World world, double fraction = 0.72)
    {
        _world = world;
        UpdateScale(world);
        if (world.PrimaryGround is { } ground)
        {
            var x = ground.Left + ground.Width * fraction;
            Body.PlaceOn(ground, FreeX(ground, x) ?? x);
            Activity = Activity.Idle;
        }

        _activityTimer = 1;
        _lastNoticed = 0;
        IsInitialized = true;
    }

    /// <summary>Entra andando desde el borde de la pantalla hasta su sitio, y saluda al llegar.</summary>
    public void WalkIn(bool fromRight)
    {
        if (Body.Support is not { } ground)
        {
            return;
        }

        var target = Body.Feet.X;
        Body.PlaceOn(ground, fromRight ? ground.Right : ground.Left);
        if (TryWalkTo(target))
        {
            _greetPending = true;
        }
    }

    /// <summary>Se muda a otra plataforma al momento (otro monitor, porque en el suyo hay algo a pantalla completa).</summary>
    public void Relocate(Platform platform, double x)
    {
        SetActivity(Activity.Idle, 1);
        Body.PlaceOn(platform, x);
        _lastFeet = new(double.NaN, double.NaN);
        _scaleFeet = new(double.NaN, double.NaN);
        UpdateScale(_world);
        Animator.Snap();
    }

    public void ApplyOptions(GameOptions options)
    {
        _options = options;
        UpdateScale(_world, instant: true);
    }

    /// <summary>Un paso de simulación.</summary>
    public void Step(World world, Sensors sensors)
    {
        var dt = TimeStep;
        Time += dt;
        _world = world;
        _sensors = sensors;
        Visual.Remember();
        UpdateScale(world, dt: dt);
        UpdateMood(sensors, dt);
        _tickleCooldown = Math.Max(0, _tickleCooldown - dt);
        UpdateRub(sensors, dt);
        UpdateReactions(sensors, dt);
        _playground.Step(world, dt);
        DrainToyEvents();
        StepFoods(world, dt);
        UpdateToys();
        UpdateGifts();
        UpdateBehavior(world, sensors, dt);

        var landing = Body.Step(world, dt);
        if (landing is { } land)
        {
            OnLanded(land);
        }

        TrackPlatform();
        UpdateAnimation(sensors, dt);
        StepButterfly(world, sensors, dt);
    }

    public void DrainEvents(List<GameEvent> target)
    {
        target.AddRange(_events);
        _events.Clear();
    }

    // Órdenes del exterior ----------------------------------------------------------------------------

    /// <summary>¿Está el punto sobre el cuerpo de Kobii?</summary>
    public bool HitTest(Vec2 point)
    {
        var center = Body.Center;
        var rx = Body.HalfWidth * 0.95;
        var ry = Body.Height * 0.52;
        var dx = (point.X - center.X) / rx;
        var dy = (point.Y - center.Y) / ry;
        return dx * dx + dy * dy <= 1;
    }

    /// <summary>Clic (sin arrastrar): una caricia.</summary>
    public void Click()
    {
        Notice(0.12);
        Visual.Bounce(-3.2);
        if (Activity == Activity.Sleep)
        {
            WakeUp();
            Visual.SetExpression(Expression.Surprised, 1.2);
            SetActivity(Activity.Idle, 2);
        }
        else if (Activity == Activity.Play && _routine?.Click() == true)
        {
            // La rutina lo usa (no es una caricia).
            return;
        }
        else if (Activity == Activity.Attend)
        {
            // Te está escuchando o hablando: agradece la caricia sin dejar de atenderte.
            Visual.SetExpression(Expression.Happy, 1.2);
        }
        else
        {
            if (Activity == Activity.Perform && Animator.Clip?.Face == Expression.Sleeping)
            {
                // Echaba una siesta: se despierta del susto.
                StopPerforming();
                Visual.SetExpression(Expression.Surprised, 1.2);
            }
            else if (Activity == Activity.Perform && !Animator.IsMoving)
            {
                // Si estaba leyendo o descansando, sigue; si no, te hace caso.
                Visual.SetExpression(Expression.Happy, 1.2);
            }
            else
            {
                StopPerforming();
                Visual.SetExpression(_random.Chance(0.5) ? Expression.Joy : Expression.Love, 1.6);
            }
        }

        Visual.AddEmote(EmoteKind.Heart, HeadPoint, Body.Scale);
        Raise(GameEventKind.Petted);
    }

    public void BeginDrag(Vec2 cursor)
    {
        if (!IsInitialized)
        {
            return;
        }

        _dragOffset = Body.Feet - cursor;
        Body.Grab();
        if (Activity == Activity.Sleep)
        {
            WakeUp();
        }

        SetActivity(Activity.Held, 0);
        Visual.SetExpression(Expression.Surprised, 0.8);
        Visual.Bounce(4);
        Notice(0.02);
        Raise(GameEventKind.Grabbed);
    }

    public void Drag(Vec2 cursor)
    {
        if (_dragOffset is { } offset && Body.IsHeld)
        {
            Body.MoveHeld(cursor + offset, TimeStep);
        }
    }

    public void EndDrag()
    {
        if (!Body.IsHeld)
        {
            return;
        }

        _dragOffset = null;
        var speed = Body.Velocity.Length / Body.Scale;
        Body.Release();
        SetActivity(Activity.Fall, 0);
        if (speed > 700)
        {
            _wasHeldFast = true;
            Raise(GameEventKind.Thrown, speed);
        }
    }

    /// <summary>Lo que hace el módulo (escuchar, pensar, hablar…): deja lo que hace y te atiende.</summary>
    public void SetAttention(Attention attention)
    {
        if (attention == Attention)
        {
            return;
        }

        var previous = Attention;
        Attention = attention;
        Visual.Talking = attention == Attention.Speaking;
        if (attention != Attention.Speaking)
        {
            Visual.VoiceLevel = -1;
        }

        Visual.AntennaTarget = attention switch
        {
            Attention.Listening => 0.75,
            Attention.Thinking => 1,
            Attention.Waiting => 0.5,
            _ => 0,
        };
        Visual.AntennaPulse = attention == Attention.Thinking;

        if (attention is Attention.None or Attention.Busy)
        {
            if (Activity == Activity.Attend)
            {
                // Acaba de atenderte: se queda un momento mirándote antes de volver a lo suyo.
                SetActivity(Activity.Idle, _random.Range(1.5, 3));
            }

            return;
        }

        Notice(0.02);
        if (Activity == Activity.Sleep)
        {
            WakeUp();
        }

        if (Activity is Activity.Idle or Activity.Walk or Activity.Perform or Activity.Play)
        {
            SetActivity(Activity.Attend, 0);
        }

        switch (attention)
        {
            case Attention.Thinking or Attention.Speaking:
                // La cara de lo anterior (la sorpresa de oírte) deja paso a la de pensar o hablar.
                Visual.ClearExpression();
                break;
            case Attention.Listening when previous != Attention.Thinking:
                Visual.SetExpression(Expression.Curious, 1.5);
                Visual.AddEmote(EmoteKind.Exclamation, HeadPoint, Body.Scale);
                break;
            case Attention.Waiting:
                Visual.SetExpression(Expression.Curious, 2);
                Visual.AddEmote(EmoteKind.Question, HeadPoint, Body.Scale);
                break;
        }
    }

    /// <summary>Cuánto suena su voz ahora (0–1): la boca la sigue.</summary>
    public void SetVoiceLevel(double level)
    {
        Visual.VoiceLevel = Math.Clamp(level, 0, 1);
        if (Attention != Attention.Speaking)
        {
            // El nivel llega antes que el estado: ya está hablando.
            Visual.Talking = true;
        }
    }

    /// <summary>
    /// Te dice algo (el bocadillo lo pone el host): se vuelve hacia ti y mueve la boca mientras dura, aunque no tenga voz.
    /// </summary>
    public void Communicate(bool talking)
    {
        Visual.Talking = talking || Attention == Attention.Speaking;
        if (!talking)
        {
            return;
        }

        Notice(0.01);
        if (Activity == Activity.Sleep)
        {
            WakeUp();
        }

        Visual.SetExpression(Expression.Happy, 1.5);
        if (Activity is Activity.Idle or Activity.Walk or Activity.Perform or Activity.Play)
        {
            SetActivity(Activity.Attend, 0);
            _attendTimer = 0;
        }
    }

    /// <summary>
    /// Reacciona a cómo ha ido una orden: por defecto asiente, señala, se encoge de hombros o salta de alegría; con
    /// <paramref name="clip"/>, otra animación del catálogo (las elige el asistente para no hacer siempre lo mismo), con su
    /// cara. <paramref name="emote"/> y <paramref name="emotes"/> cambian lo que le sale de la cabeza.
    /// </summary>
    public void React(ReactionKind reaction, string? clip = null, EmoteKind? emote = null, int emotes = 0)
    {
        if (Activity == Activity.Sleep)
        {
            WakeUp();
        }

        var (fallback, face, defaultEmote, count) = reaction switch
        {
            ReactionKind.Success => ("asentir", Expression.Happy, EmoteKind.Sparkle, 3),
            ReactionKind.Warning => ("senalar", Expression.Surprised, EmoteKind.Exclamation, 1),
            ReactionKind.Failure => ("encogerse", Expression.Confused, EmoteKind.Question, 1),
            _ => ("brazos-arriba", Expression.Joy, EmoteKind.Confetti, 24),
        };

        var chosen = clip is not null && AnimationCatalog.Find(clip) is { } found ? found : AnimationCatalog.Get(fallback);
        Visual.SetExpression(chosen.Face ?? face, 1.8);
        Visual.AddEmote(emote ?? defaultEmote, HeadPoint, Body.Scale, emotes > 0 ? emotes : count);
        if (Body.IsGrounded && Activity is not (Activity.Held or Activity.Fall))
        {
            Perform([chosen]);
        }

        Raise(GameEventKind.Reacted, text: reaction.ToString());
    }

    /// <summary>
    /// Entra cayendo: aparece en el aire en (<paramref name="x"/>, <paramref name="y"/>) y cae en lo que haya debajo; si
    /// <paramref name="greet"/>, al rato de aterrizar te saluda.
    /// </summary>
    public void DropIn(double x, double y, bool greet)
    {
        Body.Feet = new Vec2(x, y);
        Body.Launch(new Vec2(0, 0));
        SetActivity(Activity.Fall, 0);
        _greetPending = greet;
        Visual.SetExpression(Expression.Joy, 1.5);
    }

    /// <summary>En cuanto esté a lo suyo, te saluda (lo has puesto tú en el escritorio).</summary>
    public void Greet() => _greetPending = true;

    /// <summary>Hace una animación ahora mismo.</summary>
    public bool PlayClip(string id)
    {
        if (!IsSettled || AnimationCatalog.Find(id) is not { } clip)
        {
            return false;
        }

        if (Activity == Activity.Sleep)
        {
            WakeUp();
        }

        Perform([clip]);
        return true;
    }

    /// <summary>Va andando (o corriendo) hasta x en su plataforma. Devuelve false si no hace falta o no puede.</summary>
    public bool WalkTo(double x, bool run = false)
    {
        if (!IsSettled || !TryWalkTo(x))
        {
            return false;
        }

        _running = run;
        return true;
    }

    // Comportamiento ----------------------------------------------------------------------------------

    private void Raise(GameEventKind kind, double magnitude = 0, string? text = null) =>
        _events.Add(new GameEvent(kind, Body.Feet, magnitude, text));

    /// <summary>Los botes de los juguetes, como sucesos (para que suenen).</summary>
    private void DrainToyEvents()
    {
        _toyEvents.Clear();
        _playground.DrainEvents(_toyEvents);
        foreach (var toy in _toyEvents)
        {
            var speed = toy.Landing.Speed / Math.Max(0.1, toy.Toy.Body.Scale);
            if (speed > 120)
            {
                _events.Add(new GameEvent(GameEventKind.BallBounced, toy.Landing.Point, speed, toy.Toy.Kind.ToString()));
            }
        }
    }

    /// <summary>Le has hecho caso (una caricia, hablarle): se alegra un poco y deja de aburrirse.</summary>
    private void Notice(double cheer)
    {
        _cheer = Math.Clamp(_cheer + cheer, 0, 1);
        _lastNoticed = Time;
    }

    /// <summary>El ánimo: contento si le haces caso, aburrido si llevas mucho sin hacérselo estando delante, con sueño de noche.</summary>
    private void UpdateMood(Sensors sensors, double dt)
    {
        _cheer = MathUtil.Damp(_cheer, 0.5, 1 / CheerSeconds, dt);
        var hour = sensors.LocalTime.Hour;
        var present = sensors.UserIdleSeconds < 60;
        if (!present)
        {
            // Sin ti delante no se aburre: no cuenta el rato que no estás.
            _lastNoticed = Math.Max(_lastNoticed, Time - BoredAfterSeconds * 0.5);
        }

        Mood = hour is >= 23 or < 7 ? Mood.Sleepy
            : _cheer > 0.68 ? Mood.Happy
            : Time - _lastNoticed > BoredAfterSeconds ? Mood.Bored
            : Mood.Content;
    }

    /// <summary>Su escala es la de su monitor (tamaño elegido × escalado): al cruzar a otro con distinto escalado cambia en un momento.</summary>
    private void UpdateScale(World world, double dt = 0, bool instant = false)
    {
        var monitor = world.MonitorAt(Body.Center) ?? world.NearestMonitor(Body.Center);
        var jumped = double.IsNaN(_scaleFeet.X) || Vec2.Distance(Body.Feet, _scaleFeet) > Body.Height * 2;
        _scaleFeet = Body.Feet;
        Body.ScaleToward(_options.Size * (monitor?.DpiScale ?? 1), dt, instant: instant || dt <= 0 || jumped);
    }

    private void SetActivity(Activity activity, double seconds)
    {
        if (Activity is Activity.Walk or Activity.Perform && activity != Activity)
        {
            Body.WalkVelocity = 0;
        }

        if (Activity == Activity.Perform && activity != Activity.Perform)
        {
            // Cualquier otra cosa interrumpe lo que estaba haciendo por su cuenta.
            _performance.Clear();
            Animator.StopClip();
        }

        if (activity != Activity.Walk)
        {
            _pendingAtEdge = null;
            _dropAtEdge = false;
        }

        if (Activity == Activity.Play && activity != Activity.Play)
        {
            // Lo deja a medias (o ya ha acabado): la rutina suelta lo que tuviera.
            LeavePlay();
        }

        if (Activity == Activity.Sleep && activity != Activity.Sleep)
        {
            // Al despertarse deja el peluche a su lado.
            PutDownCuddled();
        }

        if (Activity == Activity.Travel && activity != Activity.Travel)
        {
            // Le han interrumpido (o ya ha llegado): si estaba trepando, se suelta.
            _runner?.Abort();
            _runner = null;
            if (_travelOutcome == TravelOutcome.Going)
            {
                _travelOutcome = TravelOutcome.Failed;
            }
        }

        Activity = activity;
        _activityTimer = seconds;
    }

    /// <summary>Deja esa animación si es la que está haciendo.</summary>
    public void StopClip(string id)
    {
        if (Activity == Activity.Perform && Animator.Clip?.Id == id)
        {
            _performance.Clear();
            StopPerforming();
        }
    }

    /// <summary>Deja lo que estaba haciendo por su cuenta (si hacía algo).</summary>
    private void StopPerforming()
    {
        if (Activity == Activity.Perform)
        {
            SetActivity(Activity.Idle, _random.Range(1.5, 3));
        }
    }

    /// <summary>Encadena unas animaciones.</summary>
    private void Perform(IEnumerable<AnimationClip> clips)
    {
        _performance.Clear();
        foreach (var clip in clips)
        {
            _performance.Enqueue(clip);
        }

        if (_performance.Count == 0)
        {
            return;
        }

        SetActivity(Activity.Perform, 0);
        StartNextClip();
    }

    private void StartNextClip()
    {
        var clip = _performance.Dequeue();
        Animator.Side = Visual.FacingTarget < 0 ? -1 : 1;
        Animator.Play(clip);
        _clipEmoteTimer = clip.EmoteEvery > 0 ? Math.Min(0.5, clip.EmoteEvery) : 0.25;
        _hopTimer = 0;
        if (clip.Face is { } face)
        {
            Visual.SetExpression(face, clip.Seconds);
        }

        Raise(GameEventKind.Performed, text: clip.Id);
        switch (clip.Motion)
        {
            case ClipMotion.Hop:
                Body.Hop(380);
                Visual.Bounce(-2.5);
                Raise(GameEventKind.Hop);
                break;
            case ClipMotion.BigHop:
                Body.Hop(600);
                Visual.Bounce(-3);
                Raise(GameEventKind.Hop);
                break;
            case ClipMotion.Roll when Body.Support is { } support:
            {
                // Rueda hacia donde haya más sitio.
                var margin = Body.HalfWidth + 6 * Body.Scale;
                var room = Animator.Side > 0 ? support.Right - margin - Body.Feet.X : Body.Feet.X - (support.Left + margin);
                if (room < Body.Height)
                {
                    Animator.Side = -Animator.Side;
                    Animator.Play(clip);
                }

                var distance = RollSpeed * clip.Seconds;
                _rollTargetX = Math.Clamp(Body.Feet.X + Animator.Side * distance, support.Left + margin, Math.Max(support.Left + margin, support.Right - margin));
                break;
            }
        }
    }

    /// <summary>Velocidad al rodar: la que corresponde a su giro sin resbalar.</summary>
    private double RollSpeed => Math.Tau / 1.1 * 0.95 * Body.Height / 2;

    private double WalkSpeed => Species.WalkSpeedDip * Body.Scale * (Mood == Mood.Sleepy ? 0.85 : 1);

    private void StepPerform(Sensors sensors, double dt)
    {
        if (Animator.Clip is not { } clip)
        {
            SetActivity(Activity.Idle, 1);
            return;
        }

        // Si vuelves, lo deja para saludarte.
        if (Body.IsGrounded && _greetPending && clip.Motion == ClipMotion.None)
        {
            SetActivity(Activity.Idle, 0);
            return;
        }

        switch (clip.Motion)
        {
            case ClipMotion.Roll:
                if (!WalkTowards(_rollTargetX, RollSpeed / Math.Max(1e-6, WalkSpeed)))
                {
                    Body.WalkVelocity = 0;
                }

                break;
            case ClipMotion.Hops:
                _hopTimer -= dt;
                if (_hopTimer <= 0 && Body.IsGrounded && Animator.Time < clip.Seconds - 0.4)
                {
                    _hopTimer = 0.55;
                    Body.Hop(240);
                    Visual.Bounce(-1.5);
                    Raise(GameEventKind.Hop);
                }

                break;
        }

        if (clip.Emote is { } emote)
        {
            _clipEmoteTimer -= dt;
            if (_clipEmoteTimer <= 0 && (clip.EmoteEvery > 0 || Animator.Time < 1))
            {
                _clipEmoteTimer = clip.EmoteEvery > 0 ? clip.EmoteEvery * _random.Range(0.8, 1.25) : double.PositiveInfinity;
                Visual.AddEmote(emote, EmotePoint(clip.EmoteFrom), Body.Scale, emote is EmoteKind.Bubble or EmoteKind.Paint ? 2 : 1);
            }
        }

        if (Animator.ClipDone && Body.IsGrounded)
        {
            if (_performance.Count > 0)
            {
                StartNextClip();
            }
            else
            {
                SetActivity(Activity.Idle, _random.Range(2, 5) / Math.Max(0.3, _options.Liveliness));
            }
        }
    }

    /// <summary>De dónde salen los efectos de un clip.</summary>
    private Vec2 EmotePoint(EmoteSource source) => source switch
    {
        EmoteSource.Hand => WorldPoint(Animator.Rig.Hand(Animator.Side, Species.Form)),
        EmoteSource.Mouth => WorldPoint(new Vec2(Visual.Facing * 0.16 + Animator.Side * 0.25, -0.80)),
        EmoteSource.Feet => Body.Feet,
        _ => HeadPoint,
    };

    /// <summary>Un punto del cuerpo (en unidades de la criatura) en el mundo.</summary>
    public Vec2 WorldPoint(Vec2 local) =>
        CreatureSpace.ToWorld(local, Animator.Rig, Body.Feet, Body.Height / 2, Visual.Squash * (1 + Animator.Rig.Stretch), Visual.Lean);

    private void UpdateBehavior(World world, Sensors sensors, double dt)
    {
        _activityTimer -= dt;
        UpdatePresence(sensors);
        switch (Activity)
        {
            case Activity.Held:
            case Activity.Fall:
                if (Body.IsGrounded && Activity == Activity.Fall)
                {
                    SetActivity(Activity.Idle, _random.Range(1, 3));
                }

                break;

            case Activity.Celebrate:
                if (Body.IsGrounded && _activityTimer <= 0)
                {
                    SetActivity(Activity.Idle, _random.Range(1.5, 3));
                }

                break;

            case Activity.Sleep:
                StepSleep(sensors, dt);
                break;

            case Activity.Perform:
                StepPerform(sensors, dt);
                break;

            case Activity.Attend:
                StepAttend(dt);
                break;

            case Activity.Walk:
                StepWalk();
                break;

            case Activity.Travel:
                StepTravel(dt);
                break;

            case Activity.Play:
                StepPlay(world, sensors, dt);
                break;

            default:
                StepIdle(world, sensors);
                break;
        }

        var hopping = Activity == Activity.Perform && Animator.Clip?.Motion is ClipMotion.Hop or ClipMotion.Hops or ClipMotion.BigHop;

        // Si lo que hacía se acaba (o le haces clic) en mitad de un saltito suyo, el saltito sigue siendo un saltito:
        // aterriza y ya está (no es una caída). Solo lo que dura un salto normal; más, sí que se está cayendo.
        if (Body.IsGrounded)
        {
            _ownHopUntil = double.NegativeInfinity;
        }
        else if (hopping)
        {
            _ownHopUntil = Time + 0.6;
        }

        hopping |= Time < _ownHopUntil && Activity is Activity.Idle or Activity.Perform or Activity.Play;
        hopping |= Activity == Activity.Play && _routine?.AllowsAirborne == true;
        if (!Body.IsGrounded && !hopping && _dropSide == 0 && Activity is Activity.Idle or Activity.Walk or Activity.Attend or Activity.Sleep or Activity.Perform or Activity.Play)
        {
            if (Activity == Activity.Sleep)
            {
                WakeUp();
            }

            SetActivity(Activity.Fall, 0);
        }

        if (Body.IsGrounded)
        {
            _dropSide = 0;
        }
    }

    private void StepIdle(World world, Sensors sensors)
    {
        if (!Body.IsGrounded || _errand)
        {
            return;
        }

        if (Attention is Attention.Listening or Attention.Thinking or Attention.Speaking or Attention.Waiting)
        {
            SetActivity(Activity.Attend, 0);
            return;
        }

        if (_activityTimer > 0 && !_greetPending)
        {
            return;
        }

        // Llevas un buen rato sin tocar el PC (de noche, mucho menos): se echa a dormir.
        var night = sensors.LocalTime.Hour is >= 23 or < 7;
        if (!sensors.Quiet && sensors.UserIdleSeconds > (night ? NightSleepAfterSeconds : SleepAfterSeconds))
        {
            // Si tiene cerca el peluche o la cama, va a por ellos antes de dormirse.
            if (!GoToSleepWithToys())
            {
                FallAsleep();
            }

            return;
        }

        // Se ha quedado encima de otro asistente (llegaron a la vez, o lo han dejado caer ahí): se aparta.
        if (StepAside())
        {
            return;
        }

        // Lleva mucho rato en lo alto de una ventana: se baja (descolgándose o dejándose caer).
        if (Body.Support is { IsBase: false } && Time - _platformSince > 180 && !_greetPending && (ComeDown() || TryGoDown()))
        {
            return;
        }

        // De vez en cuando sube a ver qué hay en alguna ventana cercana.
        if (Body.Support is { IsBase: true } && !_greetPending && !sensors.Quiet && Mood != Mood.Sleepy
            && _random.Chance(ExploreChance * _options.Liveliness) && Explore())
        {
            return;
        }

        Decide(sensors);
    }

    /// <summary>Decide qué hacer por su cuenta y empieza a hacerlo.</summary>
    private void Decide(Sensors sensors)
    {
        var context = AutonomyContextFor(sensors);
        if (_greetPending)
        {
            // Vuelves después de un rato: lo primero es saludarte.
            _greetPending = false;
            Execute(Autonomy.Get(_random.Chance(0.4) ? "saludar-dos" : "saludar"), sensors);
            Raise(GameEventKind.Greeted);
            return;
        }

        Execute(_autonomy.Choose(context, Time, _random), sensors);
    }

    /// <summary>Hace lo que ha decidido (o lo que le toca por rutina, como desperezarse).</summary>
    private void Execute(AutonomyAction action, Sensors sensors)
    {
        if (_bed is not null && action.Kind is ChoiceKind.Wander or ChoiceKind.ApproachCursor or ChoiceKind.ToEdge && HopOutOfBed())
        {
            // Para ir a algún sitio, primero sale de la cama de un saltito (y luego vuelve a decidir).
            SetActivity(Activity.Celebrate, 0.4);
            return;
        }

        _autonomy.Remember(action, Time);
        var clips = action.Clips.Select(AnimationCatalog.Get).ToList();
        switch (action.Kind)
        {
            case ChoiceKind.Wander:
                if (Body.Support is { } support)
                {
                    var distance = _random.Range(80, 380) * Body.Scale * _random.Sign();
                    var margin = Body.HalfWidth + 6 * Body.Scale;
                    var target = Math.Clamp(Body.Feet.X + distance, support.Left + margin, support.Right - margin);
                    if (TryWalkTo(target))
                    {
                        return;
                    }
                }

                break;

            case ChoiceKind.ApproachCursor when sensors.Cursor is { } cursor:
                if (Math.Abs(cursor.X - Body.Feet.X) > Body.Height && TryWalkTo(cursor.X - Math.Sign(cursor.X - Body.Feet.X) * Body.Height * 0.9))
                {
                    return;
                }

                break;

            case ChoiceKind.ToEdge when Body.Support is { } edge:
            {
                // Al borde más cercano, mirando hacia fuera.
                var margin = Body.HalfWidth * 0.95;
                var toRight = edge.Right - Body.Feet.X < Body.Feet.X - edge.Left;
                if (TryWalkTo(toRight ? edge.Right - margin : edge.Left + margin))
                {
                    _pendingAtEdge = clips;
                    _edgeSide = toRight ? 1 : -1;
                    return;
                }

                Visual.FacingTarget = toRight ? 1 : -1;
                Perform(clips);
                return;
            }

            case ChoiceKind.Perform:
                Perform(clips);
                return;

            case ChoiceKind.Play when StartPlay(action.Id):
                return;
        }

        // Quedarse un rato mirando (o no ha podido hacer lo que quería).
        SetActivity(Activity.Idle, _random.Range(2, 6) / Math.Max(0.3, _options.Liveliness));
    }

    private AutonomyContext AutonomyContextFor(Sensors sensors)
    {
        var cursorNear = sensors.Cursor is { } cursor && sensors.CursorStillSeconds > 1.0
            && Math.Abs(cursor.X - Body.Feet.X) < Body.Height * 7 && cursor.Y < Body.Feet.Y + Body.Height
            && cursor.Y > Body.Feet.Y - Body.Height * 6;
        var hour = sensors.LocalTime.Hour;
        return new AutonomyContext
        {
            Mood = Mood,
            Energy = Mood == Mood.Sleepy ? 0.3 : Mood == Mood.Happy ? 0.95 : 0.75,
            Hour = hour,
            CursorNear = cursorNear,
            FollowCursor = _options.FollowCursor,
            UserReturned = _greetPending,
            OnWindow = Body.Support is { IsBase: false } window && window.Width > Body.Height * 3,
            SecondsSinceWake = Time - _wokeAt,
            RoomToRun = Body.Support is { } support && support.Width > Body.Height * 4,
            ButterflyPassing = ButterflyPassing,
            BallNear = ReachableToy(ToyKind.Ball) is { Ephemeral: false },
            PlushNear = ReachableToy(ToyKind.Plush) is not null,
            BedNear = ReachableToy(ToyKind.Bed) is not null && _bed is null,
        };
    }

    /// <summary>Lo que ha decidido hacer por su cuenta paso a paso (ver <see cref="ChoiceKind.Play"/>).</summary>
    private bool StartPlay(string id) => id switch
    {
        "mariposa" => ChaseButterfly(),
        "pelota" => PlayWithBall(),
        "merendar" => Eat(own: _random.Pick([FoodKind.Apple, FoodKind.Berries, FoodKind.Cookie])),
        "beber" => Eat(own: _random.Pick([FoodKind.Juice, FoodKind.Milk, FoodKind.Water])),
        _ => PlayWithGift(id),
    };

    /// <summary>Lleva la cuenta de desde cuándo está en la plataforma en la que está.</summary>
    private void TrackPlatform()
    {
        if (Body.IsGrounded && Body.SupportId != _platformOwner)
        {
            _platformOwner = Body.SupportId;
            _platformSince = Time;
        }
    }

    /// <summary>Se da cuenta de cuándo te vas y cuándo vuelves (para saludarte).</summary>
    private void UpdatePresence(Sensors sensors)
    {
        if (sensors.UserIdleSeconds > AwaySeconds)
        {
            _userAway = true;
        }
        else if (_userAway && sensors.UserIdleSeconds < 3)
        {
            _userAway = false;
            _greetPending = true;
        }
    }

    private void FallAsleep()
    {
        SetActivity(Activity.Sleep, 0);
        Body.WalkVelocity = 0;
        _emoteTimer = 1;
        Raise(GameEventKind.FellAsleep);
    }

    private void WakeUp()
    {
        if (Activity != Activity.Sleep)
        {
            return;
        }

        _wokeAt = Time;
        SetActivity(Activity.Idle, 0.5);
        Visual.SetExpression(Expression.Sleepy, 1.5);
        Raise(GameEventKind.WokeUp);
    }

    private void StepSleep(Sensors sensors, double dt)
    {
        if (sensors.UserIdleSeconds < 3)
        {
            // Has vuelto: se frota los ojos, bosteza, se estira y te saluda.
            WakeUp();
            _greetPending = true;
            if (Body.IsGrounded)
            {
                Execute(Autonomy.Get("despertar"), sensors);
            }

            return;
        }

        _emoteTimer -= dt;
        if (_emoteTimer <= 0)
        {
            _emoteTimer = 2.6;
            Visual.AddEmote(EmoteKind.Sleep, HeadPoint + new Vec2(Body.HalfWidth * 0.5, 0), Body.Scale);
        }
    }

    /// <summary>Te atiende: te escucha, piensa (la mano en la barbilla de vez en cuando) o te habla.</summary>
    private void StepAttend(double dt)
    {
        Body.WalkVelocity = 0;
        if (Attention is Attention.None or Attention.Busy && !Visual.Talking)
        {
            _attendTimer += dt;
            if (_attendTimer > 0.6)
            {
                SetActivity(Activity.Idle, _random.Range(1.5, 3));
            }

            return;
        }

        _attendTimer = 0;
        if (Attention == Attention.Thinking)
        {
            _thinkGestureTimer -= dt;
            if (_thinkGestureTimer <= 0 && Animator.Overlay == Gesture.None)
            {
                _thinkGestureTimer = _random.Range(2.5, 4.5);
                Animator.PlayGesture(Gesture.Think, _random.Range(1.8, 2.6));
            }
        }
        else if (Attention == Attention.Speaking)
        {
            _thinkGestureTimer -= dt;
            if (_thinkGestureTimer <= 0 && Animator.Overlay == Gesture.None)
            {
                // Mientras habla, de vez en cuando asiente o mueve una mano.
                _thinkGestureTimer = _random.Range(2.2, 4.5);
                Animator.PlayGesture(_random.Chance(0.5) ? Gesture.Nod : Gesture.Point, 1.4);
            }
        }
    }

    /// <summary>Se baja de la ventana en la que está: va al borde con sitio debajo y se deja caer de un saltito.</summary>
    private bool TryGoDown()
    {
        if (Body.Support is not { IsBase: false } here)
        {
            return false;
        }

        var options = new List<(int Side, double Edge)>();
        foreach (var side in new[] { -1, 1 })
        {
            var edge = side < 0 ? here.Left : here.Right;
            var outside = edge + side * Body.HalfWidth * 1.3;
            if (_world.PlatformBelow(outside, here.Y + 1) is not null)
            {
                options.Add((side, edge));
            }
        }

        if (options.Count == 0)
        {
            return false;
        }

        var (chosen, at) = options.OrderBy(o => Math.Abs(o.Edge - Body.Feet.X)).First();
        if (!TryWalkTo(at - chosen * Body.HalfWidth * 0.85, clampMargin: Body.HalfWidth * 0.8))
        {
            Jump(chosen);
            return true;
        }

        _dropAtEdge = true;
        _edgeSide = chosen;
        return true;
    }

    /// <summary>Se deja caer de la plataforma hacia <paramref name="side"/> con un saltito.</summary>
    private void Jump(int side)
    {
        _dropSide = side;
        Visual.FacingTarget = side;
        Body.Launch(Navigation.DropVelocity(Body, side, Body.HalfWidth * 0.85));
        Visual.Bounce(-1.5);
        SetActivity(Activity.Fall, 0);
        Raise(GameEventKind.Hop);
    }

    /// <summary>Donde están quietos otros asistentes de K-Desktop (lo pone el asistente en cada ciclo): no se queda encima.</summary>
    public IReadOnlyList<PixelRect> Occupied { get; set; } = [];

    /// <summary>De pie en <paramref name="x"/> sobre una plataforma a la altura <paramref name="y"/>, quedaría encima de otro asistente.</summary>
    public bool IsTaken(double x, double y)
    {
        if (Occupied.Count == 0)
        {
            return false;
        }

        var half = Body.HalfWidth + 4 * Body.Scale;
        var spot = new PixelRect((int)(x - half), (int)(y - Body.Height), (int)Math.Ceiling(x + half), (int)Math.Ceiling(y));
        foreach (var other in Occupied)
        {
            if (other.Intersects(spot))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>El sitio libre de otros asistentes más cercano a <paramref name="x"/> en esa plataforma, o null si no cabe.</summary>
    public double? FreeX(Platform platform, double x)
    {
        var margin = Body.HalfWidth + 4 * Body.Scale;
        var left = platform.Left + margin;
        var right = platform.Right - margin;
        if (right < left)
        {
            return null;
        }

        x = Math.Clamp(x, left, right);
        var step = Math.Max(4, Body.HalfWidth / 2);
        for (var d = 0.0; d <= right - left; d += step)
        {
            if (x + d <= right && !IsTaken(x + d, platform.Y))
            {
                return x + d;
            }

            if (x - d >= left && !IsTaken(x - d, platform.Y))
            {
                return x - d;
            }
        }

        return null;
    }

    /// <summary>Está de pie donde otro asistente se ha quedado: se va andando al sitio libre más cercano.</summary>
    private bool StepAside()
    {
        if (Body.Support is not { } support || !Body.IsGrounded || !IsTaken(Body.Feet.X, support.Y)
            || FreeX(support, Body.Feet.X) is not { } free || !TryWalkTo(free))
        {
            return false;
        }

        Raise(GameEventKind.SteppedAside);
        return true;
    }

    private bool TryWalkTo(double x, double? clampMargin = null)
    {
        if (Body.Support is not { } support)
        {
            return false;
        }

        var margin = clampMargin ?? Body.HalfWidth + 4 * Body.Scale;
        _targetX = Math.Clamp(x, support.Left + margin, Math.Max(support.Left + margin, support.Right - margin));
        // Otro asistente se ha quedado ahí: al lado (pasar por delante sí vale; quedarse encima, no).
        if (IsTaken(_targetX, support.Y))
        {
            if (FreeX(support, _targetX) is not { } free)
            {
                return false;
            }

            _targetX = free;
        }

        if (Math.Abs(_targetX - Body.Feet.X) < 8 * Body.Scale)
        {
            return false;
        }

        SetActivity(Activity.Walk, 25);
        _running = false;
        return true;
    }

    private void StepWalk()
    {
        if (!Body.IsGrounded || _activityTimer <= 0)
        {
            SetActivity(Activity.Idle, 1);
            return;
        }

        if (Attention is Attention.Listening or Attention.Thinking or Attention.Speaking or Attention.Waiting)
        {
            SetActivity(Activity.Attend, 0);
            return;
        }

        if (!WalkTowards(_targetX, _running ? RunFactor : 1, _dropAtEdge))
        {
            if (_dropAtEdge)
            {
                // Ha llegado al borde: salta abajo.
                var side = (int)_edgeSide;
                _dropAtEdge = false;
                Jump(side);
                return;
            }

            if (_pendingAtEdge is { } clips)
            {
                // Ha llegado al borde: se vuelve hacia fuera y hace lo que venía a hacer.
                _pendingAtEdge = null;
                Visual.FacingTarget = _edgeSide;
                Perform(clips);
                return;
            }

            SetActivity(Activity.Idle, _random.Range(1.5, 5) / Math.Max(0.3, _options.Liveliness));
        }
    }

    /// <summary>Camina hacia x. Devuelve false al llegar.</summary>
    private bool WalkTowards(double x, double speedFactor, bool toEdge = false)
    {
        var dx = x - Body.Feet.X;
        var arrive = 3 * Body.Scale;
        if (Math.Abs(dx) <= arrive)
        {
            Body.WalkVelocity = 0;
            return false;
        }

        // Frena al borde de su plataforma (salvo si va a saltar desde él).
        if (Body.Support is { } support)
        {
            var margin = Body.HalfWidth * (toEdge ? 0.1 : 0.8);
            var next = Body.Feet.X + Math.Sign(dx) * margin;
            if (next < support.Left || next > support.Right)
            {
                Body.WalkVelocity = 0;
                return false;
            }
        }

        var speed = WalkSpeed * speedFactor;

        // Llegada suave: frena en los últimos píxeles.
        speed = Math.Min(speed, Math.Abs(dx) * 6 + 12 * Body.Scale);
        Body.WalkVelocity = Math.Sign(dx) * speed;
        Visual.FacingTarget = Math.Sign(dx);
        return true;
    }

    private void OnLanded(Landing landing)
    {
        var speed = landing.Speed / Math.Max(0.1, Body.Scale);
        Visual.Bounce(-Math.Clamp(speed / 260, 0.5, 6));
        if (speed > 1500 && _dropSide == 0)
        {
            Visual.SetExpression(Expression.Dizzy, 1.8);
        }
        else if (_wasHeldFast)
        {
            Visual.SetExpression(Expression.Joy, 1.2);
        }

        if (speed > 500)
        {
            Visual.AddEmote(EmoteKind.Dust, landing.Point, Body.Scale, 6);
        }

        _wasHeldFast = false;
        Raise(GameEventKind.Landed, speed);
    }

    private void UpdateRub(Sensors sensors, double dt)
    {
        var over = sensors.Cursor is { } cursor && !Body.IsHeld && HitTest(cursor) ? sensors.Cursor : null;
        var strokes = _rub.Update(over, 10 * Body.Scale, dt);
        if (strokes == 0)
        {
            return;
        }

        Notice(0.03);
        if (over is { } point && TouchStroke(point, sensors))
        {
            return;
        }

        if (Activity == Activity.Sleep)
        {
            return;
        }

        Visual.SetExpression(_rub.Intensity > 2 ? Expression.Love : Expression.Happy, 1.2);
        if (_rub.Intensity > 2 && _random.Chance(0.4))
        {
            Visual.AddEmote(EmoteKind.Heart, HeadPoint, Body.Scale);
        }

        Raise(GameEventKind.Rubbed, _rub.Intensity);
    }

    private void UpdateAnimation(Sensors sensors, double dt)
    {
        var walking = Activity is Activity.Walk or Activity.Travel or Activity.Play && Body.IsGrounded && Math.Abs(Body.WalkVelocity) > 1;
        var rolling = Activity is Activity.Perform && Animator.Clip?.Motion == ClipMotion.Roll;
        Visual.Walk = MathUtil.Damp(Visual.Walk, walking ? 1 : 0, 10, dt);
        if (walking)
        {
            Visual.WalkPhase += dt * 9 * (_running ? 1.5 : 1);

            // Un paso cada medio ciclo (para los pasitos que suenan).
            var phase = Math.Floor(Visual.WalkPhase / Math.PI);
            if (phase != _stepPhase)
            {
                _stepPhase = phase;
                Raise(GameEventKind.Step);
            }
        }

        // Inclinación: al caminar, al ser arrastrado y al caer.
        var lean = Body.IsHeld ? Math.Clamp(Body.Velocity.X / (Body.Scale * 2000), -0.5, 0.5)
            : walking ? Math.Sign(Body.WalkVelocity) * 0.06
            : !Body.IsGrounded && !rolling ? Math.Clamp(Body.Velocity.X / (Body.Scale * 3000), -0.3, 0.3)
            : 0;
        Visual.Lean = MathUtil.Damp(Visual.Lean, lean, 8, dt);

        UpdateLook(sensors, dt);
        RefreshBaseExpression();
        Visual.Update(dt, Time);
        UpdateRig(dt);
    }

    /// <summary>La postura de base sale de lo que está haciendo; encima van el clip y los gestos.</summary>
    private void UpdateRig(double dt)
    {
        var pose = Activity switch
        {
            Activity.Sleep => Pose.Curl,
            Activity.Held => Pose.Dangle,
            Activity.Fall when Body.Velocity.Y > 250 * Body.Scale => Pose.Tumble,
            Activity.Travel when _runner?.Pose is { } travel => travel,
            Activity.Play when _routine?.Pose is { } playing => playing,
            Activity.Walk or Activity.Travel or Activity.Play when _running && Visual.Walk > 0.05 => Pose.Run,
            Activity.Walk or Activity.Travel or Activity.Play when Visual.Walk > 0.05 => Pose.Walk,
            Activity.Celebrate => Pose.Hop,
            _ => Pose.Stand,
        };

        Animator.SetBasePose(pose);
        UpdateBedLift(dt);
        if (Activity == Activity.Sleep)
        {
            HugWhileAsleep();
        }

        if (Activity is not Activity.Perform and not Activity.Sleep and not Activity.Play)
        {
            Animator.Side = Visual.FacingTarget < -0.05 ? -1 : Visual.FacingTarget > 0.05 ? 1 : Animator.Side;
        }

        Animator.BaseProp = Activity == Activity.Play && _routine?.Prop is { } routineProp ? routineProp : _heldProp;
        Animator.BaseWear = _heldWear;

        // Velocidad real de los pies (andar, caer, la ventana que se mueve): sus sacudidas mueven el brote.
        var velocity = dt > 0 && !double.IsNaN(_lastFeet.X) ? (Body.Feet - _lastFeet) / dt / Math.Max(1, Body.Height / 2) : Vec2.Zero;
        _lastFeet = Body.Feet;
        Animator.Update(dt, new AnimationContext(Species.Form, Visual.WalkPhase, Visual.Walk, Visual.Facing, 0, velocity));
    }

    private void UpdateLook(Sensors sensors, double dt)
    {
        if (Activity is Activity.Sleep)
        {
            Visual.LookTarget = new Vec2(0, 0.3);
            Visual.FacingTarget = 0;
            return;
        }

        if (Activity == Activity.Travel || (Activity == Activity.Play && _routine?.ControlsLook == true))
        {
            // Mira hacia donde va (lo pone el recorrido) o adonde le dice lo que está haciendo.
            return;
        }

        if (LookAtErrand())
        {
            return;
        }

        if (Activity is Activity.Attend)
        {
            // Te mira a ti (o hacia arriba, pensando), de frente.
            Visual.FacingTarget = 0;
            Visual.LookTarget = Attention == Attention.Thinking ? new Vec2(0.35, -0.7)
                : sensors.Cursor is { } at && Vec2.Distance(at, Body.Center) < Body.Height * 8 ? ((at - Body.Center) / (Body.Height * 2.5)).ClampLength(1)
                : new Vec2(0, -0.3);
            return;
        }

        if (Activity == Activity.Perform && Animator.Clip is { } clip)
        {
            // Tumbado o rodando, la cara va en el centro; algunos clips miran a un sitio concreto.
            if (!Poses.Info(clip.Pose).Upright)
            {
                Visual.FacingTarget = 0;
            }

            if (clip.Look is { } look)
            {
                Visual.LookTarget = new Vec2(look.X * Animator.Side, look.Y);
                return;
            }
        }

        if (sensors.Cursor is { } cursor && Vec2.Distance(cursor, Body.Center) < Body.Height * 7)
        {
            var delta = (cursor - Body.Center) / (Body.Height * 2.5);
            Visual.LookTarget = delta.ClampLength(1);
            if (Activity == Activity.Idle)
            {
                Visual.FacingTarget = Math.Clamp(delta.X, -0.5, 0.5);
            }

            return;
        }

        _lookTimer -= dt;
        if (_lookTimer <= 0)
        {
            _lookTimer = _random.Range(1.5, 4.5);
            Visual.LookTarget = _random.Chance(0.4) ? Vec2.Zero : new Vec2(_random.Range(-0.9, 0.9), _random.Range(-0.5, 0.3));
            if (Activity == Activity.Idle)
            {
                Visual.FacingTarget = _random.Chance(0.6) ? 0 : _random.Range(-0.4, 0.4);
            }
        }
    }

    /// <summary>La cara de fondo sale de lo que hace y de su ánimo.</summary>
    private void RefreshBaseExpression()
    {
        Visual.BaseExpression = Activity is Activity.Sleep ? Expression.Sleeping
            : Attention == Attention.Thinking ? Expression.Thinking
            : Attention == Attention.Listening ? Expression.Curious
            : Attention == Attention.Speaking ? Expression.Happy
            : Mood switch
            {
                Mood.Sleepy => Expression.Sleepy,
                Mood.Bored => Expression.Sad,
                Mood.Happy => Expression.Happy,
                _ => Expression.Neutral,
            };
    }
}
