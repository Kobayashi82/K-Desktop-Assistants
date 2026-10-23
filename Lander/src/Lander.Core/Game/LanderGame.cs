using System.Globalization;
using Lander.Core.Autopilot;
using Lander.Core.Geometry;
using Lander.Core.Missions;
using Lander.Core.Physics;
using Lander.Core.Progression;
using Lander.Core.Racing;
using Lander.Core.Terrain;

namespace Lander.Core.Game;

/// <summary>
/// La simulación completa del lander: física, piloto automático, control manual, puntuación y misiones.
/// No depende de Windows; el anfitrión le da el mundo y los sensores y dibuja el resultado.
/// </summary>
public sealed class LanderGame
{
    /// <summary>Paso fijo de la física: 60 actualizaciones por segundo.</summary>
    public const double TimeStep = 1.0 / 60;

    public const double RespawnDelay = 1.8;

    /// <summary>Duración mínima de un vuelo para que su aterrizaje puntúe (evita «tocar y despegar»).</summary>
    public const double MinScoringFlight = 1.5;

    /// <summary>Con cuánta antelación pide ver el contenido antes de elegir destino (da para dos escaneos).</summary>
    public const double ContentLookahead = 1.6;

    private readonly IRandom _random;
    private readonly PhysicsEngine _physics;
    private readonly PhysicsConfig _baseConfig;
    private readonly List<PhysicsEvent> _physicsEvents = [];
    private readonly AutopilotContext _context = new();
    private readonly List<GameEvent> _events = [];

    private ManualControls _previousControls;
    private double _lastManualInput;
    private bool _manualLegsDown = true;
    private Vec2 _dragOffset;
    private Vec2? _dragTarget;
    private Vec2 _gust;
    private double _gustTime;
    private double _gustDuration;
    private double _slipCooldown;
    private int _handledEvents;
    private double _flightStart;
    private long _flightOrigin;
    private double _flightOriginFeetY;
    private double _flightClimb;
    private HashSet<string> _earnedAchievements;
    private int _achievementVersion;
    private double _nextAchievementCheck;
    private bool _initialized;

    public LanderGame(GameOptions options, PilotProgress progress, IRandom random, PhysicsConfig? physics = null)
    {
        _random = random;
        _baseConfig = physics ?? new PhysicsConfig();
        Options = options;
        Progress = progress;
        Autopilot = new Autopilot.Autopilot(random);
        Missions = new MissionSystem(random);
        Races = new RaceSystem(random);
        _earnedAchievements = Achievements.EarnedBy(progress).Select(a => a.Id).ToHashSet();
        _physics = new PhysicsEngine(Unlocks.ApplyUpgrades(_baseConfig, progress.Level));
        Body.FuelCapacity = _physics.Config.FuelCapacity;
        Body.Fuel = Body.FuelCapacity;
        ApplyOptions(options);

        _context.Body = Body;
        _context.Visual = Visual;
    }

    public GameOptions Options { get; private set; }

    public PilotProgress Progress { get; }

    /// <summary>Se incrementa cada vez que cambia el progreso, para guardarlo cuando haga falta.</summary>
    public int ProgressVersion { get; private set; }

    public LanderBody Body { get; } = new();

    public Autopilot.Autopilot Autopilot { get; }

    public MissionSystem Missions { get; }

    public RaceSystem Races { get; }

    public LanderVisual Visual { get; } = new();

    public ControlMode Mode { get; private set; } = ControlMode.Auto;

    public bool IsPaused { get; private set; }

    public double Time { get; private set; }

    public World World { get; private set; } = World.Empty;

    public Sensors Sensors { get; private set; } = new();

    public PhysicsConfig Physics => _physics.Config;

    public ThrustInput LastInput { get; private set; }

    public bool IsInitialized => _initialized;

    public bool IsHeld => Body.State == BodyState.Held;

    /// <summary>
    /// Si el anfitrión debe seguir actualizando a 60 Hz. Cuando es falso, el lander está quieto y basta
    /// con despertar de vez en cuando.
    /// </summary>
    public bool NeedsHighFrequency =>
        Body.State != BodyState.Landed
        || Mode == ControlMode.Manual
        || Autopilot.IsFlightActive
        || Visual.IsAnimating
        || Math.Abs(Body.Angle) > 0.002
        || Math.Abs(Body.AngularVelocity) > 0.01
        || Body.Support is { } support && support.Velocity.LengthSquared > 1;

    /// <summary>
    /// Qué contenido de las ventanas necesita ver ahora el lander, para que el anfitrión capture la pantalla
    /// solo cuando hace falta: posado o volando hacia un borde de contenido, pilotando a mano o en los
    /// segundos previos a elegir destino. El resto del tiempo, nada.
    /// </summary>
    public ContentInterest ContentInterest
    {
        get
        {
            if (!_initialized || Body.State is BodyState.Destroyed or BodyState.Held)
            {
                return default;
            }

            // Posado sobre contenido hay que seguirlo incluso en pausa: si la página se mueve, se cae.
            var manual = Mode == ControlMode.Manual;
            if (Body.IsLanded && Body.Support is { Platform.Kind: PlatformKind.Content } support)
            {
                return new ContentInterest(ContentNeed.Landed, Platform.ContentWindow(support.OwnerId), support.OwnerId, manual && !IsPaused);
            }

            if (IsPaused)
            {
                return default;
            }

            if (manual)
            {
                return new ContentInterest(ContentNeed.Manual, Piloting: true);
            }

            if (Autopilot.IsFlightActive && Autopilot.Destination is { Kind: PlatformKind.Content } destination)
            {
                return new ContentInterest(ContentNeed.Destination, Platform.ContentWindow(destination.OwnerId), destination.OwnerId);
            }

            return Autopilot.TimeUntilExploring is { } wait && wait <= ContentLookahead
                ? new ContentInterest(ContentNeed.ChoosingSoon)
                : default;
        }
    }

    public void ApplyOptions(GameOptions options)
    {
        Options = options;
        Autopilot.Personality.Shyness = Math.Clamp(options.Shyness, 0, 1);
        Missions.Frequency = options.MissionFrequency;
    }

    /// <summary>Coloca el lander en la base del monitor principal.</summary>
    public void Initialize(World world)
    {
        World = world;
        _initialized = true;
        UpdateScale();
        PlaceAtBase();
        Visual.Opacity = 0;
        PrepareContext(Sensors, TimeStep);
        Autopilot.Resume(_context);
    }

    /// <summary>Avanza la simulación un paso fijo de <see cref="TimeStep"/>.</summary>
    public void Step(World world, Sensors sensors, ManualControls controls)
    {
        const double dt = TimeStep;
        World = world;
        Sensors = sensors;
        if (world.IsEmpty)
        {
            return;
        }

        if (!_initialized)
        {
            Initialize(world);
        }

        Time += dt;
        _slipCooldown -= dt;
        UpdateScale();
        UpdateWind(dt);
        PrepareContext(sensors, dt);

        if (Mode == ControlMode.Manual && !controls.Any && Time - _lastManualInput > Options.ManualTimeoutSeconds)
        {
            SetMode(ControlMode.Auto, ModeChangeReason.Timeout);
        }

        var stateBefore = Body.State;
        var supportBefore = Body.Support?.OwnerId ?? 0;
        ThrustInput input;
        if (Body.State == BodyState.Held)
        {
            input = ThrustInput.None;
            if (_dragTarget is { } target)
            {
                _physics.StepHeld(Body, target, dt);
            }
        }
        else
        {
            input = Mode == ControlMode.Manual ? MapManual(controls) : Autopilot.Update(_context);
            var fuel = Body.Fuel;
            _physics.Step(Body, input, world, dt, _physicsEvents);

            // «Modo asistente»: lo que hace para una orden no gasta combustible (nunca se queda sin él a medias).
            if (Options.AssistantMode && Autopilot.OnErrand && Body.Fuel < fuel)
            {
                Body.Fuel = fuel;
            }
        }

        LastInput = input;
        TrackFlight(stateBefore, supportBefore);
        ProcessPhysicsEvents();
        ProcessSignals();
        UpdateRespawn();
        UpdateStats(dt);

        Races.Update(world, Body, dt, _events);
        Missions.Suspended = Races.IsActive;
        Missions.Update(world, Body, Mode, sensors, dt, _events);
        ProcessMissionEvents();
        CheckAchievements();

        Visual.ShowHud = Mode == ControlMode.Manual;
        Visual.CarryingPackage = Missions.Current is { Status: MissionStatus.Active, Kind: MissionKind.Delivery };
        Visual.Update(dt, Body, input);
        _previousControls = controls;
    }

    /// <summary>Copia los eventos pendientes en <paramref name="into"/> y los vacía.</summary>
    public void DrainEvents(List<GameEvent> into)
    {
        ProcessMissionEvents();
        into.AddRange(_events);
        _events.Clear();
        _handledEvents = 0;
    }

    public void Notify(SystemEvent systemEvent)
    {
        if (!_initialized)
        {
            return;
        }

        PrepareContext(Sensors, TimeStep);
        switch (systemEvent)
        {
            case WindGust gust:
                _gust = gust.Acceleration * Body.Scale;
                _gustTime = _gustDuration = Math.Max(0.05, gust.Duration);
                if (Body.IsLanded)
                {
                    Body.AngularVelocity += Math.Sign(gust.Acceleration.X) * 0.6;
                }

                break;

            case NotificationArrived when Mode == ControlMode.Manual:
                Visual.Flash(Expression.Scared, 1.2);
                break;

            case WindowOpened when Mode == ControlMode.Manual:
                Autopilot.Personality.Curiosity = Math.Min(1, Autopilot.Personality.Curiosity + 0.3);
                break;

            default:
                if (Mode == ControlMode.Auto)
                {
                    Autopilot.Notify(systemEvent, _context);
                    ProcessSignals();
                }

                break;
        }
    }

    public void ToggleMode() =>
        SetMode(Mode == ControlMode.Manual ? ControlMode.Auto : ControlMode.Manual, ModeChangeReason.User);

    public void SetMode(ControlMode mode, ModeChangeReason reason)
    {
        if (Mode == mode || !_initialized)
        {
            return;
        }

        if (mode == ControlMode.Manual && IsPaused)
        {
            IsPaused = false;
        }

        Mode = mode;
        PrepareContext(Sensors, TimeStep);
        if (mode == ControlMode.Manual)
        {
            _lastManualInput = Time;
            _manualLegsDown = Body.LegsDeploy > 0.5;
            Visual.ClearBubble();
            Visual.SetBaseExpression(Expression.Focused);
            Missions.OnModeChanged(mode, World, Body, _events);
            ProcessMissionEvents();
        }
        else
        {
            Visual.SetBaseExpression(Expression.Neutral);
            Races.OnModeChanged(mode, Body, _events);
            Autopilot.Resume(_context);
        }

        _events.Add(new GameEvent(GameEventKind.ModeChanged, Body.Position) { Mode = mode, Reason = reason });
    }

    /// <summary>En pausa el lander aterriza donde pueda y se queda quieto.</summary>
    public void SetPaused(bool paused)
    {
        if (IsPaused == paused)
        {
            return;
        }

        IsPaused = paused;
        if (paused)
        {
            SetMode(ControlMode.Auto, ModeChangeReason.Paused);
            PrepareContext(Sensors, TimeStep);
            Autopilot.LandNow(_context);
        }
    }

    /// <summary>
    /// Para pantalla completa: si estaba volando, lo deja posado en la plataforma de debajo al instante.
    /// </summary>
    public void SnapToSurface()
    {
        if (!_initialized)
        {
            return;
        }

        if (Mode == ControlMode.Manual)
        {
            SetMode(ControlMode.Auto, ModeChangeReason.Fullscreen);
        }

        if (Body.State is BodyState.Flying or BodyState.Held or BodyState.Destroyed)
        {
            var below = DestinationPicker.PickBelow(World, Body, Body.Position);
            if (below is not null && below.TryResolve(World, Body, out var center, out var segment))
            {
                Body.Velocity = Vec2.Zero;
                Body.PlaceOn(segment, center.X, World.Timestamp);
            }
            else
            {
                PlaceAtBase();
            }

            Visual.Opacity = 1;
            PrepareContext(Sensors, TimeStep);
            Autopilot.LandNow(_context);
        }
    }

    /// <summary>
    /// La rueda ha girado sobre la ventana en cuyo contenido está posado (o sobre la que espera para volver a posarse):
    /// la página va a moverse. En automático se eleva y espera a que pare (<see cref="Autopilot.OnContentScrolling"/>);
    /// pilotando a mano, un saltito, y el resto es cosa del piloto. En pausa, nada (si el borde se va, se cae).
    /// </summary>
    public void OnContentScrolling()
    {
        if (!_initialized || IsPaused || Body.State is BodyState.Destroyed or BodyState.Held)
        {
            return;
        }

        if (Mode == ControlMode.Auto)
        {
            PrepareContext(Sensors, TimeStep);
            Autopilot.OnContentScrolling(_context);
            ProcessSignals();
            return;
        }

        if (Body.IsLanded && Body.Support is { Kind: PlatformKind.Content } support)
        {
            Body.Position = Body.Position.WithY(Body.Position.Y - 1);
            Body.Fly(new Vec2(Body.Velocity.X, -110 * Body.Scale));
            StartFlight(support.OwnerId);
            _events.Add(new GameEvent(GameEventKind.SupportLost, Body.Position));
        }
    }

    /// <summary>Un clic sobre el lander: le hace gracia.</summary>
    public void Poke()
    {
        if (!_initialized || Body.State == BodyState.Destroyed)
        {
            return;
        }

        Visual.Flash(Expression.Happy, 1.5);
        Visual.ShowBubble(new Bubble(BubbleIcon.Heart, 1.4));
    }

    // Encargos del asistente -------------------------------------------------------------------------------

    /// <summary>
    /// Puede hacer maniobras para K-Desktop: en automático, sin carrera, sin pausa y sin estar agarrado ni destruido.
    /// </summary>
    public bool CanRunErrands =>
        _initialized && Mode == ControlMode.Auto && !IsPaused && !Races.IsActive && Body.State is not (BodyState.Destroyed or BodyState.Held)
        && (Options.AssistantMode || Body.FuelFraction > ErrandFuelReserve);

    /// <summary>Sin el modo asistente, con menos de esto en el depósito no hace maniobras (que lo diga el módulo).</summary>
    public const double ErrandFuelReserve = 0.25;

    /// <summary>Vuela a un punto en el aire y espera allí mirando a <paramref name="look"/> (ver <see cref="Autopilot.Autopilot.BeginErrand"/>).</summary>
    public void BeginErrand(Vec2 point, double speed, Vec2? look = null)
    {
        if (!CanRunErrands)
        {
            return;
        }

        PrepareContext(Sensors, TimeStep);
        Autopilot.BeginErrand(_context, point, speed);
        Autopilot.ErrandLook = look;
    }

    /// <summary>Termina el encargo: se posa en la ventana <paramref name="perchOn"/> si puede, o en lo que tenga debajo.</summary>
    public void EndErrand(long perchOn = 0)
    {
        if (!_initialized)
        {
            return;
        }

        PrepareContext(Sensors, TimeStep);
        var landOn = perchOn != 0 ? DestinationPicker.PickOnOwner(World, Body, perchOn, _random) : null;
        Autopilot.EndErrand(_context, landOn);
    }

    /// <summary>El retroceso de un disparo: en el aire empuja la nave; posada, la sacude.</summary>
    public void Recoil(Vec2 impulse)
    {
        if (Body.IsFlying)
        {
            Body.Velocity += impulse * Body.Scale;
            Body.AngularVelocity += Math.Clamp(impulse.X * 0.01, -0.8, 0.8);
        }
        else
        {
            Visual.StartWiggle();
        }
    }

    /// <summary>Borra el progreso del piloto y quita las mejoras.</summary>
    public void ResetProgress()
    {
        Progress.CopyFrom(new PilotProgress());
        ProgressVersion++;
        _earnedAchievements.Clear();
        RefreshUpgrades();
    }

    private double _errandBoost = 1;

    /// <summary>
    /// Para remolcar una ventana a tiempo: la física va más deprisa (las aceleraciones por este factor, las velocidades
    /// por su raíz), como un vídeo acelerado: la nave vuela igual, pero antes. 1 = normal (hasta 6).
    /// </summary>
    public double ErrandBoost
    {
        get => _errandBoost;
        set
        {
            var boost = Math.Clamp(value, 1, 6);
            if (Math.Abs(boost - _errandBoost) < 1e-9)
            {
                return;
            }

            _errandBoost = boost;
            Autopilot.TimeScale = Math.Sqrt(boost);
            RefreshUpgrades();
        }
    }

    /// <summary>La física con el tiempo acelerado <paramref name="k"/> veces (aceleraciones k², velocidades k).</summary>
    private static PhysicsConfig TimeScaled(PhysicsConfig config, double k) => config with
    {
        Gravity = config.Gravity * k * k,
        MainThrust = config.MainThrust * k * k,
        LateralThrust = config.LateralThrust * k * k,
        RotationAcceleration = config.RotationAcceleration * k * k,
        MaxAngularSpeed = config.MaxAngularSpeed * k,
        LinearDrag = config.LinearDrag * k,
        AngularDrag = config.AngularDrag * k,
        StabilizerStiffness = config.StabilizerStiffness * k * k,
        StabilizerDamping = config.StabilizerDamping * k,
        MainBurnRate = config.MainBurnRate * k,
        LateralBurnRate = config.LateralBurnRate * k,
        RotationBurnRate = config.RotationBurnRate * k,
        Perfect = config.Perfect with { VerticalSpeed = config.Perfect.VerticalSpeed * k, HorizontalSpeed = config.Perfect.HorizontalSpeed * k },
        Hard = config.Hard with { VerticalSpeed = config.Hard.VerticalSpeed * k, HorizontalSpeed = config.Hard.HorizontalSpeed * k },
    };

    private void RefreshUpgrades()
    {
        var config = Unlocks.ApplyUpgrades(_baseConfig, Progress.Level);
        if (_errandBoost > 1)
        {
            config = TimeScaled(config, Math.Sqrt(_errandBoost));
        }

        var fuelFraction = Body.FuelFraction;
        _physics.Config = config;
        Body.FuelCapacity = config.FuelCapacity;
        Body.Fuel = fuelFraction * config.FuelCapacity;
    }

    public void BeginDrag(Vec2 cursor)
    {
        if (!_initialized || Body.State == BodyState.Destroyed)
        {
            return;
        }

        Body.Hold();
        _dragOffset = Body.Position - cursor;
        _dragTarget = Body.Position;
        Visual.Flash(Expression.Scared, 0.8);
    }

    public void Drag(Vec2 cursor)
    {
        if (Body.State == BodyState.Held)
        {
            _dragTarget = cursor + _dragOffset;
        }
    }

    public void EndDrag()
    {
        _dragTarget = null;
        PhysicsEngine.Release(Body);
        StartFlight(0);
    }

    /// <summary>Ofrece una misión ahora mismo (para el menú de la bandeja).</summary>
    public bool OfferMission(MissionKind? kind = null)
    {
        if (!_initialized)
        {
            return false;
        }

        var offered = Missions.OfferNow(World, Body, Mode, _events, kind);
        ProcessMissionEvents();
        return offered;
    }

    /// <summary>
    /// Traza un circuito (o repite el último) y toma el control para correrlo. El cronómetro arranca al
    /// despegar.
    /// </summary>
    public RaceStartResult StartRace(bool repeat = false)
    {
        if (!_initialized)
        {
            return RaceStartResult.NotReady;
        }

        var result = Races.Start(World, Body, repeat, _events);
        if (result == RaceStartResult.Started)
        {
            Missions.Suspended = true;
            Missions.Update(World, Body, Mode, Sensors, 0, _events);
            SetMode(ControlMode.Manual, ModeChangeReason.User);
            _lastManualInput = Time;
            ProcessMissionEvents();
        }

        return result;
    }

    public void AbandonRace()
    {
        Races.Abandon(Body, _events);
        ProcessMissionEvents();
    }

    private ThrustInput MapManual(ManualControls controls)
    {
        if (controls.Any)
        {
            _lastManualInput = Time;
        }

        var boostPressed = controls.Boost && !_previousControls.Boost;
        if (controls.Legs && !_previousControls.Legs)
        {
            _manualLegsDown = !_manualLegsDown;
        }

        // Giro con los propulsores de giro. Sin teclas de giro, los mismos propulsores frenan el giro y
        // mantienen la inclinación (como el control de actitud de una nave real); ↓ la endereza.
        var turn = (controls.Right ? 1 : 0) - (controls.Left ? 1 : 0);
        double rotate = turn;
        if (turn == 0 && Body.IsFlying)
        {
            rotate = controls.Down
                ? FlightGuidance.RotateToward(Body, _physics.Config, 0)
                : Math.Abs(Body.AngularVelocity) > 0.05 ? Math.Clamp(-Body.AngularVelocity * 0.9, -1, 1) : 0;
        }

        return new ThrustInput(
            Throttle: controls.Up ? 1 : 0,
            Rotate: rotate,
            Lateral: (controls.SlideRight ? 1 : 0) - (controls.SlideLeft ? 1 : 0),
            Boost: boostPressed,
            RetractLegs: !_manualLegsDown,
            DropThrough: controls.Down && !_previousControls.Down && Body.IsLanded);
    }

    /// <summary>Patas pedidas en modo manual (G las saca o las recoge).</summary>
    public bool ManualLegsDown => _manualLegsDown;

    private void ProcessPhysicsEvents()
    {
        foreach (var physicsEvent in _physicsEvents)
        {
            var manual = Mode == ControlMode.Manual;
            switch (physicsEvent.Kind)
            {
                case PhysicsEventKind.TookOff:
                    Missions.OnTakeoff(physicsEvent.Platform, Body);
                    Races.OnTakeoff();
                    Visual.PushOff(0.4 + Visual.CrouchLevel);
                    Emit(GameEventKind.TookOff, physicsEvent);
                    break;

                case PhysicsEventKind.Touchdown:
                    HandleTouchdown(physicsEvent, manual);
                    break;

                case PhysicsEventKind.Crashed:
                    // Los despistes del piloto automático no cuentan en las estadísticas del jugador.
                    if (manual)
                    {
                        Progress.Crashes++;
                        Progress.CurrentStreak = 0;
                        ProgressVersion++;
                    }

                    Missions.OnCrash(Body, _events);
                    Races.OnCrash(Body, _events);
                    Emit(GameEventKind.Crashed, physicsEvent, manual);
                    ProcessMissionEvents();
                    break;

                case PhysicsEventKind.SupportLost:
                    if (manual)
                    {
                        Visual.Flash(Expression.Scared, 1.2);
                    }

                    Emit(GameEventKind.SupportLost, physicsEvent);
                    break;

                case PhysicsEventKind.Launched:
                case PhysicsEventKind.Toppled:
                    Visual.Flash(Expression.Scared, 1.5);
                    Emit(physicsEvent.Kind == PhysicsEventKind.Launched ? GameEventKind.Launched : GameEventKind.Toppled, physicsEvent);
                    break;

                case PhysicsEventKind.Slipped:
                    if (_slipCooldown <= 0)
                    {
                        _slipCooldown = 0.4;
                        Emit(GameEventKind.Slipped, physicsEvent);
                    }

                    break;

                case PhysicsEventKind.HitCeiling:
                case PhysicsEventKind.HitWall:
                    if (physicsEvent.Magnitude > 30 * Body.Scale)
                    {
                        Emit(physicsEvent.Kind == PhysicsEventKind.HitCeiling ? GameEventKind.HitCeiling : GameEventKind.HitWall, physicsEvent);
                    }

                    break;

                case PhysicsEventKind.Boosted:
                    Visual.BoostFlash = 0.35;
                    Emit(GameEventKind.Boosted, physicsEvent);
                    break;

                case PhysicsEventKind.LegsDeployed:
                    Emit(GameEventKind.LegsDeployed, physicsEvent);
                    break;

                case PhysicsEventKind.LegsRetracted:
                    Emit(GameEventKind.LegsRetracted, physicsEvent);
                    break;

                case PhysicsEventKind.FuelEmpty:
                    Emit(GameEventKind.FuelEmpty, physicsEvent);
                    break;
            }
        }

        _physicsEvents.Clear();
    }

    private void HandleTouchdown(PhysicsEvent physicsEvent, bool manual)
    {
        var landing = physicsEvent.Landing!;
        var platform = physicsEvent.Platform!;
        var points = 0;
        Visual.Impact(landing.VerticalSpeed / (_physics.Config.Perfect.VerticalSpeed * Body.Scale));

        // Solo puntúa un vuelo de verdad: ni un salto de una décima ni subir un palmo y volver al mismo sitio.
        var scores = Time - _flightStart >= MinScoringFlight
            && (platform.OwnerId != _flightOrigin || _flightClimb >= Body.Height * 1.5);

        if (manual && scores)
        {
            if (landing.Quality == LandingQuality.Perfect)
            {
                Progress.CurrentStreak++;
                Progress.BestStreak = Math.Max(Progress.BestStreak, Progress.CurrentStreak);
                Progress.PerfectLandings++;
                Visual.Flash(Expression.Happy, 2);
                Visual.CelebrateTime = 1.2;
            }
            else
            {
                Progress.CurrentStreak = 0;
                Progress.HardLandings++;
                Visual.Flash(Expression.Scared, 1.2);
            }

            points = Scoring.Points(landing.Quality, platform, Body.HalfWidth * 2, Progress.CurrentStreak);
            var popupAt = PopupAnchor(1.1);
            Visual.AddPopup(
                "+" + points.ToString(CultureInfo.InvariantCulture),
                popupAt,
                landing.Quality == LandingQuality.Perfect ? PopupStyle.Perfect : PopupStyle.Points);
            if (landing.Quality == LandingQuality.Perfect && Progress.CurrentStreak > 1)
            {
                var multiplier = Scoring.StreakMultiplier(Progress.CurrentStreak);
                Visual.AddPopup("×" + multiplier.ToString("0.#", CultureInfo.CurrentCulture), popupAt + new Vec2(0, Body.Height * 0.55), PopupStyle.Bonus);
            }

            AddPoints(points);
        }
        else if (!manual)
        {
            Progress.AutoLandings++;
            ProgressVersion++;
            Autopilot.OnTouchdown(landing, _context);
        }

        _events.Add(new GameEvent(GameEventKind.Touchdown, Body.Position)
        {
            Landing = landing,
            Platform = platform,
            Manual = manual,
            Points = points,
            Streak = Progress.CurrentStreak,
            Magnitude = landing.VerticalSpeed,
        });

        Missions.OnTouchdown(platform, landing.Quality, manual, World, Body, _events);
        if (manual)
        {
            Races.OnTouchdown(platform, landing.Quality, Body, _events);
        }

        ProcessMissionEvents();
    }

    /// <summary>Aplica los efectos de los eventos de misión que aún no se han tratado.</summary>
    private void ProcessMissionEvents()
    {
        for (; _handledEvents < _events.Count; _handledEvents++)
        {
            var gameEvent = _events[_handledEvents];
            if (gameEvent.Race is { } race)
            {
                ApplyRaceEvent(gameEvent, race);
                continue;
            }

            if (gameEvent.Mission is not { } mission)
            {
                continue;
            }

            switch (gameEvent.Kind)
            {
                case GameEventKind.MissionOffered when mission.Status == MissionStatus.Offered:
                    Visual.ShowBubble(new Bubble(mission.Icon, MissionSystem.OfferDuration, AppHandle: mission.AppHandle));
                    break;

                case GameEventKind.MissionStarted:
                    Visual.ShowBubble(new Bubble(mission.Icon, 3, AppHandle: mission.AppHandle));
                    break;

                case GameEventKind.CrystalCollected:
                    Progress.CrystalsCollected++;
                    Visual.AddPopup("+" + gameEvent.Points.ToString(CultureInfo.InvariantCulture), gameEvent.Position, PopupStyle.Bonus);
                    AddPoints(gameEvent.Points);
                    break;

                case GameEventKind.MissionCompleted:
                    Progress.MissionsCompleted++;
                    Visual.ShowBubble(new Bubble(BubbleIcon.Trophy, 2.5));
                    Visual.CelebrateTime = 2;
                    Visual.Flash(Expression.Happy, 2.5);
                    Visual.AddPopup("+" + gameEvent.Points.ToString(CultureInfo.InvariantCulture), PopupAnchor(1.6), PopupStyle.Bonus);
                    AddPoints(gameEvent.Points);
                    break;

                case GameEventKind.MissionExpired:
                    if (Visual.Bubble is { } bubble && bubble.Icon == mission.Icon)
                    {
                        Visual.ClearBubble();
                    }

                    break;
            }
        }
    }

    private void ApplyRaceEvent(GameEvent gameEvent, Race race)
    {
        switch (gameEvent.Kind)
        {
            case GameEventKind.RaceStarted:
                // Se sale con el depósito lleno y cada baliza lo rellena: la carrera va de rapidez, no de
                // ahorrar combustible.
                Body.Fuel = Body.FuelCapacity;
                Visual.ShowBubble(new Bubble(BubbleIcon.Flag, 2.5));
                break;

            case GameEventKind.RaceCheckpoint when gameEvent.Magnitude > 0:
                Body.Fuel = Body.FuelCapacity;
                var popupAt = PopupAnchor(1.1) + new Vec2(0, -Body.Height * 0.55);
                var reached = race.Next.ToString(CultureInfo.InvariantCulture) + "/" + race.Checkpoints.Count.ToString(CultureInfo.InvariantCulture);
                if (race.Next < race.Checkpoints.Count)
                {
                    Visual.AddPopup(reached, popupAt, PopupStyle.Bonus);
                }

                if (race.LastLegPenalized)
                {
                    Visual.AddPopup("+" + Race.HardLandingPenalty.ToString(CultureInfo.CurrentCulture) + " s", popupAt + new Vec2(0, Body.Height * 1.1), PopupStyle.Penalty);
                }

                break;

            case GameEventKind.RaceFinished:
                Progress.RacesCompleted++;
                if (race.Medal == RaceMedal.Gold)
                {
                    Progress.GoldMedals++;
                }

                ProgressVersion++;
                var medal = race.Medal switch
                {
                    RaceMedal.Gold => BubbleIcon.MedalGold,
                    RaceMedal.Silver => BubbleIcon.MedalSilver,
                    _ => BubbleIcon.MedalBronze,
                };
                Visual.ShowBubble(new Bubble(medal, 4));
                Visual.CelebrateTime = 2;
                Visual.Flash(Expression.Happy, 3);
                var at = PopupAnchor(1.6);
                Visual.AddPopup(Race.FormatTime(race.Elapsed) + " s", at, PopupStyle.Perfect);
                Visual.AddPopup("+" + gameEvent.Points.ToString(CultureInfo.InvariantCulture), at + new Vec2(0, Body.Height * 0.55), PopupStyle.Bonus);
                if (race.IsNewRecord && race.PreviousRecord is not null)
                {
                    Visual.AddPopup("¡Récord!", at - new Vec2(0, Body.Height * 0.55), PopupStyle.Perfect);
                }

                AddPoints(gameEvent.Points);
                break;

            case GameEventKind.RaceFailed:
                if (race.Failure == RaceFailure.TimeUp)
                {
                    Visual.ShowBubble(new Bubble(BubbleIcon.Exclamation, 2.5));
                    Visual.Flash(Expression.Tired, 2);
                }
                else if (Visual.Bubble is { Icon: BubbleIcon.Flag })
                {
                    Visual.ClearBubble();
                }

                break;
        }
    }

    /// <summary>Avisa de los logros recién conseguidos (cuando cambia el progreso o, a lo sumo, cada segundo).</summary>
    private void CheckAchievements()
    {
        if (ProgressVersion == _achievementVersion && Time < _nextAchievementCheck)
        {
            return;
        }

        _achievementVersion = ProgressVersion;
        _nextAchievementCheck = Time + 1;
        foreach (var achievement in Achievements.All)
        {
            if (!_earnedAchievements.Contains(achievement.Id) && achievement.IsEarned(Progress))
            {
                _earnedAchievements.Add(achievement.Id);
                _events.Add(new GameEvent(GameEventKind.AchievementUnlocked, Body.Position) { Achievement = achievement });
            }
        }
    }

    /// <summary>
    /// Dónde mostrar unos puntos: encima del lander o, si no caben bajo el borde superior del monitor, a su
    /// lado.
    /// </summary>
    private Vec2 PopupAnchor(double heightsAbove)
    {
        var preferred = Body.Position - new Vec2(0, Body.Height * heightsAbove);
        var monitor = World.MonitorAt(Body.Position) ?? World.NearestMonitor(Body.Position);
        return monitor is not null && preferred.Y - Body.Height * 0.9 < monitor.Bounds.Top
            ? Body.Position + new Vec2(Body.Height * 1.5, -Body.Height * 0.1)
            : preferred;
    }

    private void ProcessSignals()
    {
        foreach (var signal in _context.Signals)
        {
            _events.Add(new GameEvent(signal, Body.Position));
        }

        _context.Signals.Clear();
    }

    private void AddPoints(int points)
    {
        if (points <= 0)
        {
            return;
        }

        var before = Progress.Level;
        Progress.TotalPoints += points;
        ProgressVersion++;
        var after = Progress.Level;
        if (after > before)
        {
            RefreshUpgrades();
            Visual.CelebrateTime = 2;
            Visual.ShowBubble(new Bubble(BubbleIcon.Trophy, 2.5));
            _events.Add(new GameEvent(GameEventKind.LevelUp, Body.Position)
            {
                Level = after,
                Unlocks = Unlocks.Between(before, after),
            });
        }
    }

    private void UpdateRespawn()
    {
        if (Body.State != BodyState.Destroyed || Body.StateTime < RespawnDelay)
        {
            return;
        }

        PlaceAtBase();
        Body.Fuel = Body.FuelCapacity;
        Visual.Opacity = 0;
        Visual.Flash(Expression.Tired, 2);
        _events.Add(new GameEvent(GameEventKind.Respawned, Body.Position));
        if (Mode == ControlMode.Auto)
        {
            Autopilot.Resume(_context);
        }
    }

    /// <summary>
    /// Muda el lander a otra base (por ejemplo, a otro monitor cuando el suyo pasa a pantalla completa).
    /// </summary>
    public void Relocate(Platform ground)
    {
        if (!_initialized)
        {
            return;
        }

        if (Mode == ControlMode.Manual)
        {
            SetMode(ControlMode.Auto, ModeChangeReason.Fullscreen);
        }

        PlaceOnGround(ground);
        UpdateScale();
        PlaceOnGround(ground);
        Visual.Opacity = 0;
        PrepareContext(Sensors, TimeStep);
        Autopilot.Resume(_context);
    }

    /// <summary>
    /// Pone el lander posado en <paramref name="ground"/> cerca de <paramref name="x"/> (en un sitio libre), visible del todo
    /// si <paramref name="visible"/>: la vuelta de una recarga (donde estaba) o una entrada que aparece junto al cursor.
    /// </summary>
    public void PlaceOn(Platform ground, double x, bool visible)
    {
        if (!_initialized)
        {
            return;
        }

        var margin = DestinationPicker.LandingMargin(Body);
        x = Math.Clamp(x, ground.Left + margin, Math.Max(ground.Left + margin, ground.Right - margin));
        x = DestinationPicker.FreeX(Body, ground, x, Occupied) ?? x;
        Body.Velocity = Vec2.Zero;
        Body.AngularVelocity = 0;
        Body.PlaceOn(ground, x, World.Timestamp);
        UpdateScale();
        Body.PlaceOn(ground, x, World.Timestamp);
        Visual.Opacity = visible ? 1 : 0;
        PrepareContext(Sensors, TimeStep);
        Autopilot.Resume(_context);
    }

    /// <summary>
    /// Pone el lander en el aire en <paramref name="position"/> con esa velocidad (una entrada desde lo alto): el piloto
    /// se recupera y se posa en lo que tenga debajo.
    /// </summary>
    public void PlaceInAir(Vec2 position, Vec2 velocity)
    {
        if (!_initialized)
        {
            return;
        }

        Body.Position = position;
        Body.Angle = 0;
        Body.AngularVelocity = 0;
        Body.LegsDeploy = 1;
        Body.Fly(velocity);
        UpdateScale();
        PrepareContext(Sensors, TimeStep);
        Autopilot.Resume(_context);
    }

    private void PlaceAtBase()
    {
        var ground = World.PrimaryGround ?? (World.Platforms.Count > 0 ? World.Platforms[0] : null);
        if (ground is not null)
        {
            PlaceOnGround(ground);
        }
    }

    private void PlaceOnGround(Platform ground)
    {
        // En la barra de tareas, un poco a la derecha del centro para no tapar el botón de inicio.
        var margin = DestinationPicker.LandingMargin(Body);
        var x = Math.Clamp(ground.CenterX + ground.Width * 0.18, ground.Left + margin, ground.Right - margin);
        x = DestinationPicker.FreeX(Body, ground, x, Occupied) ?? x;
        Body.Velocity = Vec2.Zero;
        Body.AngularVelocity = 0;
        Body.PlaceOn(ground, x, World.Timestamp);
    }

    private void UpdateScale()
    {
        var monitor = World.MonitorAt(Body.Position) ?? World.NearestMonitor(Body.Position);
        var dpi = monitor?.DpiScale ?? 1;
        Body.Scale = Math.Clamp(Options.LanderSizeDip, 16, 256) / LanderBody.BaseHeight * dpi;
    }

    private void UpdateWind(double dt)
    {
        if (_gustTime > 0)
        {
            _gustTime -= dt;
            var envelope = Math.Sin(Math.Clamp(1 - _gustTime / _gustDuration, 0, 1) * Math.PI);
            Body.ExternalAcceleration = _gust * envelope;
        }
        else
        {
            Body.ExternalAcceleration = Vec2.Zero;
        }
    }

    private void TrackFlight(BodyState stateBefore, long supportBefore)
    {
        if (Body.IsFlying)
        {
            if (stateBefore != BodyState.Flying)
            {
                StartFlight(supportBefore);
            }

            _flightClimb = Math.Max(_flightClimb, _flightOriginFeetY - Body.FeetY);
        }
    }

    private void StartFlight(long origin)
    {
        _flightStart = Time;
        _flightOrigin = origin;
        _flightOriginFeetY = Body.FeetY;
        _flightClimb = 0;
    }

    private void UpdateStats(double dt)
    {
        if (Body.IsFlying)
        {
            Progress.FlightSeconds += dt;
            if (Mode == ControlMode.Manual)
            {
                Progress.ManualFlightSeconds += dt;
            }
        }
    }

    /// <summary>Donde están quietos otros asistentes de K-Desktop (lo pone el asistente en cada ciclo).</summary>
    public IReadOnlyList<PixelRect> Occupied { get; set; } = [];

    private void PrepareContext(Sensors sensors, double dt)
    {
        _context.World = World;
        _context.Occupied = Occupied;
        _context.Physics = _physics.Config;
        _context.Sensors = sensors;
        _context.Time = Time;
        _context.Dt = dt;
        _context.Activity = Options.Activity;
        _context.HoldStill = IsPaused || sensors.QuietMode;
        _context.VisitClock = Options.VisitClock;
        _context.PilotErrors = Options.PilotErrors;
        _context.Acrobatics = Options.Acrobatics;
    }

    private void Emit(GameEventKind kind, in PhysicsEvent physicsEvent, bool manual = false) =>
        _events.Add(new GameEvent(kind, Body.Position)
        {
            Magnitude = physicsEvent.Magnitude,
            Platform = physicsEvent.Platform,
            Landing = physicsEvent.Landing,
            Manual = manual,
        });
}
