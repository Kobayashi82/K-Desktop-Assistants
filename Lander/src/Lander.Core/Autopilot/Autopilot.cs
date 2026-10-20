using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Terrain;

namespace Lander.Core.Autopilot;

/// <summary>Los 7 estados del modo automático.</summary>
public enum AutopilotState
{
    Resting,
    Choosing,
    Flying,
    Landing,
    Sleeping,
    Dodging,
    ReturningToBase,
}

/// <summary>Por qué vuela el lander.</summary>
public enum FlightPurpose
{
    Explore,
    Investigate,
    Follow,
    Dodge,
    ReturnToBase,
    Recover,
    Celebrate,
    Hop,

    /// <summary>Saltito por la misma plataforma.</summary>
    Stroll,

    /// <summary>Se tira desde el borde de una ventana y frena en el último momento.</summary>
    EdgeDrop,

    /// <summary>Va a posarse sobre la ventana que el usuario acaba de activar.</summary>
    Perch,

    /// <summary>Visita el reloj de la barra de tareas a la hora en punto.</summary>
    Clock,

    /// <summary>Vuelo panorámico: recorre el monitor por el aire y se posa en otro sitio.</summary>
    Sightsee,
}

/// <summary>Cómo vuela hasta su destino.</summary>
public enum FlightStyle
{
    /// <summary>Saltos cortos y recuperaciones: lo más directo posible.</summary>
    Direct,

    /// <summary>Sube, se desplaza a una altura de crucero y baja con calma.</summary>
    Cruise,

    /// <summary>Rasante y rápido, frenando en seco al llegar.</summary>
    Brisk,

    /// <summary>Despacio y bajando como una hoja (cuando está cansado).</summary>
    Lazy,

    /// <summary>Un arco amplio y continuo hasta el destino.</summary>
    Arc,

    /// <summary>Sube por encima del destino, apaga el motor, cae y frena en el último instante.</summary>
    HoverSlam,

    /// <summary>Salto balístico: un empujón inclinado, la parábola con el motor apagado y una frenada final.</summary>
    Ballistic,

    /// <summary>A saltitos por la misma plataforma, posándose entre salto y salto.</summary>
    Hopper,

    /// <summary>Rasante siguiendo el relieve: sube y baja con las ventanas que va dejando debajo.</summary>
    Contour,

    /// <summary>Nivelado todo el vuelo, desplazándose con los propulsores de traslación (como al atracar).</summary>
    Precision,

    /// <summary>En picado hacia el destino y, al final, encabritado para frenar de golpe.</summary>
    Swoop,

    /// <summary>Ondulando arriba y abajo como un delfín.</summary>
    Dolphin,
}

/// <summary>Despistes que puede tener en un vuelo (para que no parezca un robot perfecto).</summary>
public enum PilotMistake
{
    None,

    /// <summary>Enciende el motor un poco tarde en la frenada final.</summary>
    LateIgnition,

    /// <summary>Calcula mal la altura y llega al suelo demasiado deprisa.</summary>
    FastTouchdown,

    /// <summary>Frena tarde y se pasa de largo (luego corrige).</summary>
    Overshoot,

    /// <summary>Se tambalea en los últimos metros y puede tocar torcido.</summary>
    Wobble,
}

public sealed class Personality
{
    /// <summary>Baja con cada vuelo y sube descansando. Con poca energía vuela más despacio y más cerca.</summary>
    public double Energy { get; set; } = 0.85;

    /// <summary>Sube cuando aparece una ventana nueva.</summary>
    public double Curiosity { get; set; } = 0.3;

    /// <summary>0 = sigue al cursor, 1 = huye de él.</summary>
    public double Shyness { get; set; } = 0.5;
}

/// <summary>Todo lo que el piloto automático necesita en cada paso (objeto reutilizable).</summary>
public sealed class AutopilotContext
{
    public World World { get; set; } = World.Empty;

    public LanderBody Body { get; set; } = new();

    public PhysicsConfig Physics { get; set; } = new();

    public Sensors Sensors { get; set; } = new();

    public LanderVisual Visual { get; set; } = new();

    public double Time { get; set; }

    public double Dt { get; set; }

    public double Activity { get; set; } = 1;

    /// <summary>En pausa o en «No molestar»: termina lo que esté haciendo y no inicia vuelos nuevos.</summary>
    public bool HoldStill { get; set; }

    /// <summary>Visita el reloj de la barra de tareas a las horas en punto.</summary>
    public bool VisitClock { get; set; } = true;

    /// <summary>Torpeza del piloto, de 0 a 1 (ver <see cref="GameOptions.PilotErrors"/>).</summary>
    public double PilotErrors { get; set; }

    /// <summary>Ganas de acrobacias, de 0 a 1 (ver <see cref="GameOptions.Acrobatics"/>).</summary>
    public double Acrobatics { get; set; } = 0.5;

    /// <summary>Señales para la capa de juego (celebraciones, sobresaltos...).</summary>
    public List<GameEventKind> Signals { get; } = [];
}

/// <summary>
/// La IA del modo automático. Igual que el jugador, solo produce <see cref="ThrustInput"/>: el vuelo está
/// en <c>Autopilot.Flight.cs</c> y los comportamientos en reposo en <c>Autopilot.Behaviors.cs</c>.
/// </summary>
public sealed partial class Autopilot(IRandom random)
{
    public const double IdleSleepSeconds = 300;
    public const double ChoosingDuration = 1.2;

    private const double MaxFlightSeconds = 40;
    private const double NoveltySeconds = 90;

    private readonly Dictionary<long, double> _newWindows = [];

    private Destination? _destination;
    private FlightPurpose _purpose;
    private bool _sleepAfterLanding;
    private double _stateTime;
    private double _restDuration = 8;
    private Vec2? _waypoint;
    private double _waypointHold;
    private bool _celebrated;
    private long? _investigateOwner;
    private double _investigateAt;
    private Vec2? _lookOverride;
    private double _lookOverrideTime;
    private double _glanceTimer = 5;
    private Vec2? _glance;
    private double _dodgeCooldown;
    private double _followCooldown;
    private double _sleepBubbleTimer;
    private bool _hasFlown;

    public AutopilotState State { get; private set; } = AutopilotState.Resting;

    public FlightPurpose Purpose => _purpose;

    public Destination? Destination => _destination;

    public Personality Personality { get; } = new();

    public bool IsFlightActive => State is AutopilotState.Choosing or AutopilotState.Flying or AutopilotState.Landing
        or AutopilotState.Dodging or AutopilotState.ReturningToBase;

    /// <summary>Segundos que faltan para que, descansando, busque otro destino (null si no está esperando a eso).</summary>
    public double? TimeUntilExploring => State == AutopilotState.Resting && !_napping ? Math.Max(0, _restDuration - _stateTime) : null;

    public ThrustInput Update(AutopilotContext ctx)
    {
        var dt = ctx.Dt;
        _stateTime += dt;
        _dodgeCooldown -= dt;
        _followCooldown -= dt;
        _lookOverrideTime -= dt;
        UpdatePersonality(dt);
        UpdateCooldowns(dt);
        PruneNovelty(ctx.Time);

        var body = ctx.Body;
        if (body.State is BodyState.Destroyed or BodyState.Held)
        {
            return ThrustInput.None;
        }

        // En el aire sin un vuelo planeado: se ha caído, lo han lanzado o soltado.
        if (!body.IsLanded && State is AutopilotState.Resting or AutopilotState.Sleeping or AutopilotState.Choosing)
        {
            StartRecovery(ctx, startled: true);
        }

        var input = State switch
        {
            AutopilotState.Resting => UpdateResting(ctx),
            AutopilotState.Sleeping => UpdateSleeping(ctx),
            AutopilotState.Choosing => UpdateChoosing(ctx),
            AutopilotState.Landing => UpdateLanding(ctx),
            _ => UpdateCruise(ctx),
        };

        // Pase lo que pase, nunca contra el borde superior de la pantalla.
        return FlightEnvelope.Guard(ctx.World, body, ctx.Physics, input);
    }

    /// <summary>Reacciona a un suceso del sistema.</summary>
    public void Notify(SystemEvent systemEvent, AutopilotContext ctx)
    {
        var body = ctx.Body;
        switch (systemEvent)
        {
            case WindowOpened opened:
                Personality.Curiosity = Math.Min(1, Personality.Curiosity + 0.3);
                _newWindows[opened.Handle] = ctx.Time;
                if (State == AutopilotState.Resting && ctx.World.SegmentsOf(opened.Handle) is { Count: > 0 } segments)
                {
                    var widest = segments.MaxBy(s => s.Width)!;
                    _lookOverride = new Vec2(widest.CenterX, widest.Y);
                    _lookOverrideTime = 2.5;
                    WakeFromNap(ctx);
                    ctx.Visual.Flash(Expression.Curious, 2);
                    _investigateOwner = opened.Handle;
                    _investigateAt = ctx.Time + random.Range(1.5, 4);
                }

                break;

            case WindowActivated activated:
                OnWindowActivated(activated.Handle, ctx);
                break;

            case NotificationArrived:
                if (State == AutopilotState.Sleeping)
                {
                    break;
                }

                WakeFromNap(ctx);
                ctx.Visual.Flash(Expression.Scared, 1.2);
                ctx.Signals.Add(GameEventKind.Startled);
                if (State == AutopilotState.Resting && body.IsLanded && !ctx.HoldStill && body.Support is { } support
                    && ctx.World.FindSegment(support.OwnerId, body.Position.X) is { } here)
                {
                    ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Exclamation, 1.2));
                    StartFlight(ctx, Destination.On(here, body.Position.X), FlightPurpose.Hop, AutopilotState.Flying);
                }

                break;

            case RecycleBinEmptied emptied:
                if (State != AutopilotState.Resting || ctx.HoldStill)
                {
                    break;
                }

                var waypoint = ClampToScreen(ctx, emptied.Location + new Vec2(0, -body.Height * 1.6), body.Height * 1.4);
                if (DestinationPicker.PickBelow(ctx.World, body, waypoint) is { } below)
                {
                    WakeFromNap(ctx);
                    _waypoint = waypoint;
                    _waypointHold = 1.8;
                    BeginChoosing(ctx, below, FlightPurpose.Celebrate, BubbleIcon.Star, waypoint);
                }

                break;
        }
    }

    /// <summary>La simulación avisa de un aterrizaje en modo automático.</summary>
    public void OnTouchdown(LandingResult landing, AutopilotContext ctx)
    {
        switch (landing.Quality)
        {
            case LandingQuality.Perfect when _sleepAfterLanding || _finalDestination is not null:
                // Va a dormir, o solo es un saltito de una serie: nada de celebraciones.
                break;

            case LandingQuality.Perfect when (_purpose is FlightPurpose.Explore or FlightPurpose.Investigate && random.Chance(0.15))
                || (_style is FlightStyle.HoverSlam or FlightStyle.Ballistic or FlightStyle.Swoop && _purpose != FlightPurpose.Recover && random.Chance(0.5)):
                ctx.Visual.Flash(Expression.Happy, 2);
                ctx.Visual.CelebrateTime = 1;
                ctx.Signals.Add(GameEventKind.Celebrated);
                break;
            case LandingQuality.Hard:
                ctx.Visual.Flash(Expression.Scared, 1.5);
                ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Exclamation, 1.4));
                break;
        }
    }

    /// <summary>Se devuelve el control a la IA (fin del modo manual o reaparición).</summary>
    public void Resume(AutopilotContext ctx)
    {
        _waypoint = null;
        _sleepAfterLanding = false;
        _pendingDrop = null;
        _napping = false;
        if (ctx.Body.IsFlying)
        {
            StartRecovery(ctx, startled: false);
        }
        else
        {
            EnterResting(ctx, NewRestDuration(ctx));
        }
    }

    /// <summary>Aterriza en la plataforma más cercana por debajo (pausa, pantalla completa).</summary>
    public void LandNow(AutopilotContext ctx)
    {
        _waypoint = null;
        _sleepAfterLanding = false;
        _pendingDrop = null;
        if (ctx.Body.IsFlying)
        {
            StartRecovery(ctx, startled: false);
        }
        else if (State != AutopilotState.Sleeping)
        {
            EnterResting(ctx, NewRestDuration(ctx));
        }
    }

    private ThrustInput UpdateResting(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var sensors = ctx.Sensors;
        UpdateNap(ctx);
        if (!_napping)
        {
            ctx.Visual.SetBaseExpression(Personality.Energy < 0.25 ? Expression.Tired : Expression.Neutral);
            UpdateGaze(ctx);
        }

        UpdateRide(ctx);
        UpdatePetting(ctx);
        if (ctx.HoldStill)
        {
            return ThrustInput.None;
        }

        if (sensors.UserIdleSeconds >= IdleSleepSeconds)
        {
            _pendingDrop = null;
            if (body.IsOnBase)
            {
                EnterSleeping(ctx);
            }
            else
            {
                StartReturnToBase(ctx, sleep: true, BubbleIcon.Sleep);
            }

            return ThrustInput.None;
        }

        UpdateGreeting(ctx);
        UpdateIdleAnimations(ctx);

        if (!body.IsOnBase)
        {
            if (sensors.BatteryLow)
            {
                StartReturnToBase(ctx, sleep: false, BubbleIcon.Battery);
                return ThrustInput.None;
            }

            if (NeedsFuel(ctx))
            {
                _pendingDrop = null;
                var home = FuelPlanner.ToBase(ctx.World, body, ctx.Physics, body.Position);
                if (body.Fuel >= home + FuelPlanner.Reserve(body) * 0.5)
                {
                    StartReturnToBase(ctx, sleep: false, BubbleIcon.Fuel);
                }
                else if (ctx.Visual.Bubble is null && random.Chance(ctx.Dt / 15))
                {
                    // Ni para volver: espera a que la recarga lenta le dé para el viaje.
                    ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Fuel, 2));
                    ctx.Visual.Flash(Expression.Tired, 2);
                }

                return ThrustInput.None;
            }

            if (body.Damage > 0.5)
            {
                StartReturnToBase(ctx, sleep: false, BubbleIcon.Wrench);
                return ThrustInput.None;
            }
        }

        if (TryReactToCursor(ctx))
        {
            return ThrustInput.None;
        }

        if (_napping)
        {
            return ThrustInput.None;
        }

        if (TryVisitClock(ctx) || TryPerch(ctx))
        {
            return ThrustInput.None;
        }

        if (_investigateOwner is { } owner && ctx.Time >= _investigateAt)
        {
            _investigateOwner = null;
            if (CanExplore(ctx) && random.Chance(0.35 + 0.5 * Personality.Curiosity)
                && owner != body.Support?.OwnerId
                && DestinationPicker.PickOnOwner(ctx.World, body, owner, random) is { } investigate
                && IsReachable(ctx, investigate))
            {
                BeginChoosing(ctx, investigate, FlightPurpose.Investigate, BubbleIcon.Question);
                return ThrustInput.None;
            }
        }

        if (_stateTime >= _restDuration)
        {
            // El primer vuelo siempre es a otro sitio; después, a veces solo un paseo por la misma ventana o
            // un vuelo panorámico. Sin ventanas a las que ir, sobre todo vuelos panorámicos.
            var next = CanExplore(ctx) ? DestinationPicker.PickExploration(ctx.World, body, BuildQuery(ctx), random) : null;
            if (next is null)
            {
                if ((random.Chance(0.6) && TrySightsee(ctx)) || TryIdleTrip(ctx) || TrySightsee(ctx))
                {
                    return ThrustInput.None;
                }

                _restDuration = _stateTime + random.Range(15, 30);
            }
            else if (!(_hasFlown && TryIdleTrip(ctx)) && !(_hasFlown && random.Chance(0.15) && TrySightsee(ctx)))
            {
                BeginChoosing(ctx, next, FlightPurpose.Explore, BubbleIcon.Arrow);
            }
        }

        return ThrustInput.None;
    }

    private ThrustInput UpdateSleeping(AutopilotContext ctx)
    {
        var visual = ctx.Visual;
        visual.SetBaseExpression(Expression.Asleep);
        visual.LookTarget = null;

        _sleepBubbleTimer -= ctx.Dt;
        if (_sleepBubbleTimer <= 0)
        {
            visual.ShowBubble(new Bubble(BubbleIcon.Sleep, 3.5));
            _sleepBubbleTimer = random.Range(10, 18);
        }

        if (ctx.Sensors.UserIdleSeconds < 2)
        {
            visual.SetBaseExpression(Expression.Neutral);
            visual.Flash(Expression.Tired, 2.5);
            visual.ClearBubble();
            visual.StartStretch();
            EnterResting(ctx, random.Range(20, 45));

            // Recién despierto: un saludo cuando se le pase el sueño.
            _greetAt = ctx.Time + 2.6;
        }

        return ThrustInput.None;
    }

    private ThrustInput UpdateChoosing(AutopilotContext ctx)
    {
        if (_destination is null || !_destination.TryResolve(ctx.World, ctx.Body, out var target, out _))
        {
            _waypoint = null;
            EnterResting(ctx, random.Range(5, 12));
            return ThrustInput.None;
        }

        ctx.Visual.LookTarget = _waypoint ?? target;
        if (_stateTime >= ChoosingDuration)
        {
            var state = _purpose switch
            {
                FlightPurpose.ReturnToBase => AutopilotState.ReturningToBase,
                FlightPurpose.Dodge => AutopilotState.Dodging,
                _ => AutopilotState.Flying,
            };
            StartFlight(ctx, _destination, _purpose, state);
        }

        return ThrustInput.None;
    }

    private void OnArrived(AutopilotContext ctx)
    {
        var body = ctx.Body;
        Personality.Energy = Math.Max(0, Personality.Energy - Math.Min(0.25, 0.03 + _flightTime * 0.008));
        _waypoint = null;

        if (_purpose == FlightPurpose.ReturnToBase && !body.IsOnBase)
        {
            // Se posó por el camino: lo vuelve a intentar enseguida.
            EnterResting(ctx, random.Range(1.5, 3));
            return;
        }

        if (_sleepAfterLanding && body.IsOnBase)
        {
            EnterSleeping(ctx);
            return;
        }

        if (OnBehaviorArrived(ctx))
        {
            return;
        }

        EnterResting(ctx, NewRestDuration(ctx));
    }

    private bool TryReactToCursor(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var sensors = ctx.Sensors;
        if (sensors.Cursor is not { } cursor)
        {
            return false;
        }

        var distance = Vec2.Distance(cursor, body.Position);
        var height = body.Height;

        if (distance < height * 1.8 && sensors.CursorVelocity.Length > 60 * body.Scale && _dodgeCooldown <= 0)
        {
            _dodgeCooldown = 6;
            WakeFromNap(ctx);
            if (random.NextDouble() < Personality.Shyness)
            {
                var dodge = DestinationPicker.PickDodge(ctx.World, body, cursor, random)
                    ?? DestinationPicker.PickExploration(ctx.World, body, BuildQuery(ctx), random);
                if (dodge is not null)
                {
                    _pendingDrop = null;
                    ctx.Visual.Flash(Expression.Scared, 1);
                    StartFlight(ctx, dodge, FlightPurpose.Dodge, AutopilotState.Dodging);
                    return true;
                }
            }

            ctx.Visual.Flash(Expression.Happy, 1.5);
            return false;
        }

        // Un lander poco tímido se acerca a donde se ha quedado quieto el cursor.
        if (Personality.Shyness < 0.4 && _followCooldown <= 0 && sensors.CursorStillSeconds > 2.5
            && distance > height * 3 && distance < 700 * body.Scale && CanExplore(ctx) && !_napping)
        {
            _followCooldown = 25;
            if (random.Chance((0.4 - Personality.Shyness) * 2))
            {
                var margin = DestinationPicker.LandingMargin(body);
                var below = ctx.World.PlatformBelow(cursor.X, cursor.Y, margin);
                if (below is not null && !ctx.Sensors.FullscreenMonitors.Contains(below.MonitorIndex))
                {
                    var x = Math.Clamp(cursor.X, below.Left + margin, below.Right - margin);
                    var follow = Destination.On(below, x);
                    if (IsReachable(ctx, follow))
                    {
                        _pendingDrop = null;
                        BeginChoosing(ctx, follow, FlightPurpose.Follow, BubbleIcon.Heart);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void UpdateGaze(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var visual = ctx.Visual;

        if (_lookOverride is { } point && _lookOverrideTime > 0)
        {
            visual.LookTarget = point;
            return;
        }

        if (ctx.Sensors.Cursor is { } cursor && Vec2.Distance(cursor, body.Position) < body.Height * 6)
        {
            visual.LookTarget = cursor;
            return;
        }

        // Miradas ocasionales alrededor.
        _glanceTimer -= ctx.Dt;
        if (_glanceTimer <= 0)
        {
            if (_glance is null && random.Chance(0.7))
            {
                _glance = body.Position + new Vec2(random.Range(-1, 1), random.Range(-0.6, 0.3)) * body.Height * 3;
                _glanceTimer = random.Range(1, 2);
            }
            else
            {
                _glance = null;
                _glanceTimer = random.Range(4, 10);
            }
        }

        visual.LookTarget = _glance;
    }

    private void BeginChoosing(AutopilotContext ctx, Destination destination, FlightPurpose purpose, BubbleIcon icon, Vec2? lookAt = null)
    {
        _destination = destination;
        _purpose = purpose;
        State = AutopilotState.Choosing;
        _stateTime = 0;
        _napping = false;

        var body = ctx.Body;
        var target = lookAt ?? (destination.TryResolve(ctx.World, body, out var center, out _) ? center : body.Position);
        var direction = Math.Atan2(target.Y - body.Position.Y, target.X - body.Position.X);
        ctx.Visual.LookTarget = target;
        ctx.Visual.ShowBubble(new Bubble(icon, ChoosingDuration + 0.8, direction));
        ctx.Visual.Flash(purpose == FlightPurpose.Investigate ? Expression.Curious : Expression.Focused, ChoosingDuration + 0.5);
    }

    private void StartRecovery(AutopilotContext ctx, bool startled)
    {
        if (startled)
        {
            ctx.Visual.Flash(Expression.Scared, 1.5);
            ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Exclamation, 1.2));
            ctx.Signals.Add(GameEventKind.Startled);
        }

        _sleepAfterLanding = false;
        _waypoint = null;
        _pendingDrop = null;
        _napping = false;
        var body = ctx.Body;
        StartFlight(ctx, DestinationPicker.PickBelow(ctx.World, body, body.Position, BrakingDistance(body, ctx.Physics)),
            FlightPurpose.Recover, AutopilotState.Flying);
    }

    /// <summary>Distancia que necesita para frenar la caída actual, con margen.</summary>
    private static double BrakingDistance(LanderBody body, PhysicsConfig physics)
    {
        if (body.Fuel <= 0 || body.Velocity.Y <= 0)
        {
            return 0;
        }

        var deceleration = (physics.MainThrust * FuelPlanner.ThrustFactor(body, physics) - physics.Gravity) * body.Scale * 0.6;
        return deceleration > 0 ? body.Velocity.Y * body.Velocity.Y / (2 * deceleration) : double.MaxValue / 4;
    }

    private void StartReturnToBase(AutopilotContext ctx, bool sleep, BubbleIcon icon)
    {
        if (DestinationPicker.PickBase(ctx.World, ctx.Body, random) is not { } home)
        {
            return;
        }

        _sleepAfterLanding = sleep;
        BeginChoosing(ctx, home, FlightPurpose.ReturnToBase, icon);
    }

    private void Replan(AutopilotContext ctx)
    {
        var body = ctx.Body;
        if (_purpose == FlightPurpose.ReturnToBase)
        {
            Retarget(ctx, DestinationPicker.PickBase(ctx.World, body, random), FlightPurpose.ReturnToBase);
        }
        else
        {
            Retarget(ctx, DestinationPicker.PickBelow(ctx.World, body, body.Position, BrakingDistance(body, ctx.Physics)), FlightPurpose.Recover);
        }
    }

    private void EnterResting(AutopilotContext ctx, double duration)
    {
        State = AutopilotState.Resting;
        _stateTime = 0;
        _restDuration = duration;
        _destination = null;
        _plannedTour.Clear();
        ResetManeuvers();
        ctx.Visual.LookTarget = null;
    }

    private void EnterSleeping(AutopilotContext ctx)
    {
        State = AutopilotState.Sleeping;
        _stateTime = 0;
        _destination = null;
        _sleepBubbleTimer = 0.5;
        _napping = false;
        ctx.Visual.SetBaseExpression(Expression.Asleep);
        ctx.Visual.ClearFlash();
    }

    private static bool CanExplore(AutopilotContext ctx)
    {
        var body = ctx.Body;
        if (ctx.Sensors.BatteryLow || ctx.HoldStill)
        {
            return false;
        }

        // En la base, espera a repostar y reparar antes de salir.
        return !body.IsOnBase || (body.FuelFraction > 0.6 && body.Damage < 0.3);
    }

    /// <summary>Con lo que queda en el depósito ya no puede alejarse más: toca volver a la base.</summary>
    private static bool NeedsFuel(AutopilotContext ctx)
    {
        var body = ctx.Body;
        if (body.FuelFraction < 0.2)
        {
            return true;
        }

        var home = FuelPlanner.ToBase(ctx.World, body, ctx.Physics, body.Position);
        return body.Fuel < home * 1.25 + FuelPlanner.Reserve(body);
    }

    /// <summary>Hay combustible para ir hasta allí y, después, volver a la base.</summary>
    private static bool IsReachable(AutopilotContext ctx, Destination destination) =>
        destination.TryResolve(ctx.World, ctx.Body, out var center, out var segment)
        && FlightEnvelope.HasHeadroom(ctx.World, ctx.Body, center)
        && FuelPlanner.CanReach(ctx.World, ctx.Body, ctx.Physics, segment, center);

    private DestinationQuery BuildQuery(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var needsBase = body.FuelFraction < 0.5 || body.Damage > 0.3;
        return new DestinationQuery
        {
            Energy = Personality.Energy,
            Curiosity = Personality.Curiosity,
            BaseBias = needsBase ? 2.5 : body.IsOnBase ? 0.3 : 0.7,
            ExcludeOwner = body.Support?.OwnerId,
            NewWindows = _newWindows.Keys.ToHashSet(),
            AvoidMonitors = ctx.Sensors.FullscreenMonitors,
            ActiveWindow = ctx.Sensors.ActiveWindow,
            CanReach = (platform, target) => FuelPlanner.CanReach(ctx.World, body, ctx.Physics, platform, target),
        };
    }

    private double NewRestDuration(AutopilotContext ctx)
    {
        if (!_hasFlown)
        {
            return random.Range(6, 12);
        }

        var duration = random.Range(50, 150);
        duration *= 0.6 + (1 - Personality.Energy) * 0.9;
        duration *= 1 - 0.45 * Personality.Curiosity;
        duration /= Math.Clamp(ctx.Activity, 0.25, 4);
        if (ctx.Sensors.PowerSaver)
        {
            duration *= 2;
        }

        return duration;
    }

    private void UpdatePersonality(double dt)
    {
        var energyRate = State switch
        {
            AutopilotState.Resting => _napping ? 0.012 : 0.004,
            AutopilotState.Sleeping => 0.012,
            AutopilotState.Choosing => 0,
            _ => -0.004,
        };

        Personality.Energy = Math.Clamp(Personality.Energy + energyRate * dt, 0, 1);
        Personality.Curiosity = Math.Clamp(Personality.Curiosity - 0.004 * dt, 0.1, 1);
    }

    private void PruneNovelty(double time)
    {
        if (_newWindows.Count == 0)
        {
            return;
        }

        foreach (var (handle, seen) in _newWindows)
        {
            if (time - seen > NoveltySeconds)
            {
                _newWindows.Remove(handle);
            }
        }
    }

    private static double MonitorTop(AutopilotContext ctx, Vec2 point) =>
        (ctx.World.MonitorAt(point) ?? ctx.World.NearestMonitor(point))?.Bounds.Top ?? point.Y - 1000;

    private static Vec2 ClampToScreen(AutopilotContext ctx, Vec2 point, double margin)
    {
        if ((ctx.World.MonitorAt(point) ?? ctx.World.NearestMonitor(point)) is not { } monitor)
        {
            return point;
        }

        var bounds = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
        return new Vec2(
            Math.Clamp(point.X, bounds.Left + margin, Math.Max(bounds.Left + margin, bounds.Right - margin)),
            Math.Clamp(point.Y, bounds.Top + margin, Math.Max(bounds.Top + margin, bounds.Bottom - margin * 2)));
    }
}
