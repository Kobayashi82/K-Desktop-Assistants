using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Terrain;

namespace Lander.Core.Autopilot;

/// <summary>
/// El vuelo del piloto automático: elige una forma de llegar (ver <see cref="FlightStyle"/>), la sigue con el
/// controlador de vuelo y vigila el combustible y el techo durante todo el trayecto. Las maniobras están en
/// <c>Autopilot.Maneuvers.cs</c>, el salto balístico y el picado en <c>Autopilot.Ballistic.cs</c> y el
/// aterrizaje en <c>Autopilot.Landing.cs</c>.
/// </summary>
public sealed partial class Autopilot
{
    /// <summary>Velocidad de contacto de un descenso con frenada final (a escala 1, muy por debajo del límite).</summary>
    public const double SlamTouchdownSpeed = 32;

    private const double SpoolSeconds = 0.4;

    /// <summary>Vuelo de un tirón máximo, aunque se encadenen recuperaciones: después, a la base.</summary>
    private const double MaxTotalFlightSeconds = 90;

    private static readonly int StyleCount = Enum.GetValues<FlightStyle>().Length;

    private enum FlightPhase
    {
        Transit,
        Flip,
        Poise,
        Drop,
        Burn,
        Approach,
        Boost,
        Coast,
        Brake,
        Flare,
    }

    /// <summary>Lander de pega para prever trayectorias sin tocar el de verdad.</summary>
    private readonly LanderBody _probe = new();

    private FlightStyle _style;
    private FlightPhase _phase;
    private double _flightTime;
    private double _totalFlightTime;
    private double _airTime;
    private bool _airborneSeen;
    private double _cruiseY;
    private bool _spool;
    private bool _boostTakeoff;
    private double _nextFuelCheck;
    private double _travelStartX;
    private double _waggleCooldown = 10;

    private PilotMistake _mistake;
    private double _mistakeSeverity;

    /// <summary>Segundos de vuelo a los que se replanteará el destino en este vuelo (NaN: en este, no).</summary>
    private double _rethinkAt = double.NaN;

    /// <summary>Hasta que pase este tiempo no vuelve a cambiar de idea (para que sea algo raro).</summary>
    private double _rethinkCooldown = 90;

    /// <summary>El destino nuevo mientras duda un instante, frenando y mirándolo.</summary>
    private Destination? _rethinkTo;

    private double _hesitation;

    /// <summary>Forma de volar del vuelo actual.</summary>
    public FlightStyle Style => _style;

    /// <summary>El despiste que va a tener en este vuelo, si lo hay.</summary>
    public PilotMistake Mistake => _mistake;

    /// <summary>Dando una voltereta en el aire.</summary>
    public bool IsFlipping => _phase == FlightPhase.Flip || _coastFlipping;

    /// <summary>Con el motor apagado a propósito: cayendo antes de la frenada final o en plena parábola.</summary>
    public bool IsFreeFalling => _phase is FlightPhase.Drop or FlightPhase.Coast;

    /// <summary>Frenando a fondo: frenada final, frenada de un salto balístico o encabritado tras un picado.</summary>
    public bool IsBurning => _phase is FlightPhase.Burn or FlightPhase.Brake or FlightPhase.Flare;

    /// <summary>Estilo con el que vuela siempre que sea posible (demostraciones y tests); null, el que toque.</summary>
    public FlightStyle? ForcedStyle { get; set; }

    private void StartFlight(AutopilotContext ctx, Destination? destination, FlightPurpose purpose, AutopilotState state)
    {
        var body = ctx.Body;
        _destination = destination;
        _purpose = purpose;
        State = state;
        _stateTime = 0;
        _flightTime = 0;
        _totalFlightTime = 0;
        _airTime = 0;
        _celebrated = false;
        _airborneSeen = body.IsFlying;
        _hasFlown = true;
        _napping = false;
        _travelStartX = body.Position.X;

        // Paseos seguidos (el salto al vacío empieza con un paseo hasta el borde y no cuenta).
        _strollStreak = purpose switch
        {
            FlightPurpose.Stroll => _strollStreak + 1,
            FlightPurpose.EdgeDrop => _strollStreak,
            _ => 0,
        };
        ResetManeuvers();
        _nextFuelCheck = 0;
        PlanRethink(purpose);
        if (purpose != FlightPurpose.Celebrate)
        {
            _waypoint = null;
        }

        // Antes de despegar, el motor se enciende un instante sin levantar la nave (salvo con prisa).
        _spool = body.IsLanded && purpose is not (FlightPurpose.Hop or FlightPurpose.Dodge or FlightPurpose.Recover);

        var targetPoint = body.Position;
        if (destination is not null && destination.TryResolve(ctx.World, body, out var center, out _))
        {
            targetPoint = center;
        }

        // Para esquivar con prisa, si hay sitio por encima, sale disparado con el impulso.
        _boostTakeoff = purpose == FlightPurpose.Dodge && body.IsLanded && body.FuelFraction > 0.45 && HasRoomToBoost(ctx);

        _style = ChooseStyle(ctx, purpose, destination, targetPoint);
        _cruiseY = PlanAltitude(ctx, _style, purpose, targetPoint);
        RollMistake(ctx, purpose);

        // Un vuelo panorámico pasa antes por sus puntos en el aire (sin arcos: ya es un recorrido).
        _tour.Clear();
        if (_plannedTour.Count > 0 && purpose is FlightPurpose.Sightsee or FlightPurpose.Explore)
        {
            _tour.AddRange(_plannedTour);
            if (_style is FlightStyle.Arc or FlightStyle.Ballistic or FlightStyle.Swoop or FlightStyle.Hopper)
            {
                _style = FlightStyle.Cruise;
            }
        }

        _plannedTour.Clear();
        if (_style == FlightStyle.Hopper)
        {
            StartHopChain(ctx);
        }

        PlanFlip(ctx, purpose);
    }

    /// <summary>
    /// Decide si en este vuelo va a despistarse y cuánto. Cansado se equivoca más. La mayoría de despistes
    /// acaban en un aterrizaje duro; solo los más graves, en accidente.
    /// </summary>
    private void RollMistake(AutopilotContext ctx, FlightPurpose purpose)
    {
        _mistake = PilotMistake.None;
        _mistakeSeverity = 0;
        var clumsiness = Math.Clamp(ctx.PilotErrors, 0, 1);
        if (clumsiness <= 0 || purpose is FlightPurpose.Recover or FlightPurpose.Dodge or FlightPurpose.Hop || _style == FlightStyle.Hopper)
        {
            return;
        }

        if (!random.Chance(clumsiness * (0.14 + 0.12 * (1 - Personality.Energy))))
        {
            return;
        }

        _mistakeSeverity = random.NextDouble();
        if (_style == FlightStyle.HoverSlam)
        {
            _mistake = random.Chance(0.75) ? PilotMistake.LateIgnition : PilotMistake.FastTouchdown;
            return;
        }

        var roll = random.NextDouble();
        _mistake = roll < 0.35 ? PilotMistake.Overshoot : roll < 0.7 ? PilotMistake.FastTouchdown : PilotMistake.Wobble;
    }

    /// <summary>Margen con el que enciende el motor en la frenada final (más tarde si se ha despistado).</summary>
    private double IgnitionThreshold =>
        0.78 + (_mistake == PilotMistake.LateIgnition ? 0.12 + 0.5 * _mistakeSeverity * _mistakeSeverity : 0);

    /// <summary>Velocidad de contacto que busca en la aproximación final (más si se ha despistado).</summary>
    private double ContactSpeed(double normal, double scale) =>
        (normal + (_mistake == PilotMistake.FastTouchdown ? 60 * _mistakeSeverity : 0)) * scale;

    /// <summary>
    /// Elige cómo volar según la distancia, el desnivel, la energía, el combustible, lo que haya por el camino
    /// y las ganas de acrobacias.
    /// </summary>
    private FlightStyle ChooseStyle(AutopilotContext ctx, FlightPurpose purpose, Destination? destination, Vec2 target)
    {
        if (purpose is FlightPurpose.Hop or FlightPurpose.Dodge or FlightPurpose.Recover or FlightPurpose.Stroll)
        {
            return FlightStyle.Direct;
        }

        if (purpose == FlightPurpose.EdgeDrop)
        {
            return FlightStyle.HoverSlam;
        }

        var body = ctx.Body;
        var height = body.Height;
        var dx = Math.Abs(target.X - body.Position.X);
        var drop = target.Y - body.Position.Y;
        var energy = Personality.Energy;
        var lowFuel = body.FuelFraction < 0.45;
        var healthy = body.FuelFraction > 0.3 && body.Damage < 0.35;

        // 1 es lo normal; 0, ninguna acrobacia; 2, todas las que pueda.
        var stunts = Math.Clamp(ctx.Acrobatics, 0, 1) * 2;
        var weights = new double[StyleCount];
        var hopper = body.IsLanded && destination is not null && _waypoint is null && purpose != FlightPurpose.Celebrate && TryPlanHops(ctx, destination);

        if (purpose == FlightPurpose.Sightsee)
        {
            if (_plannedTour.Count == 0 && hopper)
            {
                return FlightStyle.Hopper;
            }

            // El final de un vuelo panorámico, muchas veces, en caída libre con frenada en el último momento.
            weights[(int)FlightStyle.Cruise] = 1;
            weights[(int)FlightStyle.HoverSlam] = body.FuelFraction > 0.55 && body.Damage < 0.35 ? 0.8 * stunts : 0;
            return Pick(weights);
        }

        // La frenada en el último instante solo con la nave sana y combustible para corregir si algo va mal.
        var slamFeasible = healthy && target.Y - PlanAltitude(ctx, FlightStyle.HoverSlam, purpose, target) >= height * 6;

        weights[(int)FlightStyle.Cruise] = 1;
        weights[(int)FlightStyle.Brisk] = dx > height * 5 && !lowFuel ? 0.25 + 0.5 * energy : 0;
        weights[(int)FlightStyle.Lazy] = energy < 0.5 && !lowFuel && dx < height * 15 ? (0.5 - energy) * 2.4 : 0;
        weights[(int)FlightStyle.Arc] = dx > height * 4 ? 0.7 : 0;
        weights[(int)FlightStyle.HoverSlam] = slamFeasible ? (drop > height * 8 ? 1 : 0.55) * stunts : 0;
        if (purpose == FlightPurpose.ReturnToBase && slamFeasible)
        {
            // Caer con el motor apagado es la forma más económica de bajar.
            weights[(int)FlightStyle.HoverSlam] *= 2;
        }

        // Saltitos: el destino está en la misma plataforma, un poco más allá.
        weights[(int)FlightStyle.Hopper] = hopper ? 2.5 : 0;

        // Nivelado y con los propulsores de traslación: para distancias cortas y sitios delicados.
        var delicate = purpose == FlightPurpose.Perch || destination?.Kind == PlatformKind.Content;
        weights[(int)FlightStyle.Precision] = dx < height * 14 && Math.Abs(drop) < height * 8 ? (delicate ? 1.6 : 0.45) : 0;

        weights[(int)FlightStyle.Contour] = dx > height * 10 && !lowFuel && SkylineVaries(ctx, target) ? 1 : 0;
        weights[(int)FlightStyle.Swoop] = drop > height * 3 && dx > height * 5 && energy > 0.4 && healthy && !lowFuel
            ? 1.1 * (0.4 + 0.6 * stunts) : 0;
        weights[(int)FlightStyle.Dolphin] = dx > height * 10 && energy > 0.5 && !lowFuel && HasRoomToUndulate(ctx, purpose, target) ? 0.8 * stunts : 0;
        weights[(int)FlightStyle.Ballistic] = stunts > 0 && dx > height * 7 && healthy && energy > 0.25 && TryPlanBallistic(ctx, target)
            ? 1.6 * stunts : 0;

        return Pick(weights);

        FlightStyle Pick(double[] candidates)
        {
            if (ForcedStyle is { } forced && candidates[(int)forced] > 0)
            {
                return forced;
            }

            var index = random.PickWeighted(candidates);
            return index < 0 ? FlightStyle.Cruise : (FlightStyle)index;
        }
    }

    /// <summary>Hay ventanas a alturas bastante distintas entre el lander y su destino: vale la pena seguir el relieve.</summary>
    private static bool SkylineVaries(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var low = double.MinValue;
        var high = double.MaxValue;
        for (var i = 1; i < 8; i++)
        {
            var x = MathUtil.Lerp(body.Position.X, target.X, i / 8.0);
            if (ctx.World.PlatformBelow(x, body.Position.Y - body.Height * 6) is { } below)
            {
                low = Math.Max(low, below.Y);
                high = Math.Min(high, below.Y);
            }
        }

        return low - high > body.Height * 2.5;
    }

    /// <summary>Altura (Y del centro) a la que viajar, siempre bajo el techo y nunca por debajo del destino.</summary>
    private double PlanAltitude(AutopilotContext ctx, FlightStyle style, FlightPurpose purpose, Vec2 target)
    {
        var body = ctx.Body;
        var height = body.Height;
        var from = body.Position;
        var top = Math.Max(
            FlightEnvelope.HighestCenter(ctx.World, body, from, 0.3),
            FlightEnvelope.HighestCenter(ctx.World, body, target, 0.3));
        if (double.IsNegativeInfinity(top))
        {
            top = Math.Max(MonitorTop(ctx, from), MonitorTop(ctx, target)) + body.HalfHeight + height * 0.5;
        }

        var higher = Math.Min(from.Y, target.Y);
        switch (style)
        {
            case FlightStyle.HoverSlam when purpose == FlightPurpose.EdgeDrop:
                return Math.Max(top, from.Y - height * 0.7);

            case FlightStyle.HoverSlam:
                // Lo más alto que permita el techo, pero sin subir más de la cuenta.
                return Math.Max(top, higher - height * random.Range(1, 2.6));

            case FlightStyle.Arc:
                _arcApex = height * random.Range(2, 5);
                break;
        }

        var climb = style switch
        {
            FlightStyle.Direct => purpose switch
            {
                FlightPurpose.Recover => height * 0.3,
                FlightPurpose.Stroll => height * random.Range(0.5, 0.9),
                _ => height * random.Range(0.8, 1.3),
            },
            FlightStyle.Hopper => height * random.Range(0.55, 0.9),
            FlightStyle.Brisk => height * random.Range(0.6, 1.1),
            FlightStyle.Lazy => height * random.Range(0.8, 1.5),
            FlightStyle.Precision => height * random.Range(0.9, 1.4),
            FlightStyle.Swoop => height * random.Range(1.5, 3),
            _ => height * random.Range(1.4, 3),
        };

        var floor = style switch
        {
            FlightStyle.Cruise or FlightStyle.Arc => top + height * 0.8,
            FlightStyle.Dolphin => top + DolphinAmplitude(height) + height * 0.4,
            _ => top,
        };
        return ReachableCruise(ctx, target, Math.Max(higher - climb, floor));
    }

    /// <summary>
    /// Ajusta una altura de crucero para que siempre se pueda llegar al destino desde arriba: nunca por debajo
    /// de él ni pegada al techo (con el destino pegado al techo, lo justo por encima de él).
    /// </summary>
    private static double ReachableCruise(AutopilotContext ctx, Vec2 target, double preferred)
    {
        var body = ctx.Body;
        var cruise = Math.Max(preferred, FlightEnvelope.HighestCenter(ctx.World, body, body.Position, 0.3));
        cruise = Math.Min(cruise, target.Y - body.Height * 0.25);
        var ceiling = FlightEnvelope.HighestCenter(ctx.World, body, target);
        return double.IsNegativeInfinity(ceiling) ? cruise : Math.Max(cruise, ceiling);
    }

    private ThrustInput Takeoff(AutopilotContext ctx)
    {
        if (_hopPause > 0)
        {
            // Entre saltito y saltito: las patas se encogen un instante antes de volver a saltar.
            _hopPause -= ctx.Dt;
            ctx.Visual.Crouch = 1;
            return ThrustInput.None;
        }

        if (_spool && _flightTime < SpoolSeconds)
        {
            ctx.Visual.Crouch = 1;
            return new ThrustInput(Throttle: 0.3, Stabilize: true, LandOn: ThrustInput.GroundOnly);
        }

        var boost = _boostTakeoff;
        _boostTakeoff = false;
        return new ThrustInput(Throttle: 1, Stabilize: true, Boost: boost, LandOn: ThrustInput.GroundOnly);
    }

    private ThrustInput UpdateCruise(AutopilotContext ctx)
    {
        var body = ctx.Body;
        _flightTime += ctx.Dt;
        _totalFlightTime += ctx.Dt;
        _waggleCooldown -= ctx.Dt;

        if (body.IsFlying)
        {
            _airborneSeen = true;
            _airTime += ctx.Dt;
        }
        else if (body.IsLanded)
        {
            if (_airborneSeen)
            {
                if (!TryNextHop(ctx))
                {
                    OnArrived(ctx);
                }

                return ThrustInput.None;
            }

            return Takeoff(ctx);
        }

        if (_flightTime > (_purpose == FlightPurpose.Sightsee ? MaxFlightSeconds * 1.8 : MaxFlightSeconds) && _purpose != FlightPurpose.Recover)
        {
            _waypoint = null;
            Retarget(ctx, DestinationPicker.PickBelow(ctx.World, body, body.Position, BrakingDistance(body, ctx.Physics)), FlightPurpose.Recover);
        }
        else if (_totalFlightTime > MaxTotalFlightSeconds && _purpose == FlightPurpose.Recover)
        {
            // Algo impide posarse donde quería: mejor la base, que siempre tiene sitio.
            _totalFlightTime = 0;
            Retarget(ctx, DestinationPicker.PickBase(ctx.World, body, random), FlightPurpose.ReturnToBase);
            State = AutopilotState.ReturningToBase;
        }

        if (!TryResolveTarget(ctx, out var target))
        {
            // Nada a la vista: descender despacio y estable.
            return Steer(ctx, new Vec2(0, 40 * body.Scale));
        }

        if (CheckFuel(ctx, target) && !TryResolveTarget(ctx, out target))
        {
            return Steer(ctx, new Vec2(0, 40 * body.Scale));
        }

        if (_waypoint is { } waypoint)
        {
            return FlyToWaypoint(ctx, waypoint);
        }

        if (Rethink(ctx, target) is { } hesitating)
        {
            return hesitating;
        }

        if (_phase == FlightPhase.Flip || TryStartFlip(ctx))
        {
            return UpdateFlip(ctx);
        }

        if (_tour.Count > 0)
        {
            return FlyTour(ctx, target);
        }

        ctx.Visual.LookTarget = target;
        WaggleAtCursor(ctx);
        if (IsShortHop(ctx, target))
        {
            return UpdateHop(ctx, target);
        }

        return _style switch
        {
            FlightStyle.Arc => UpdateArc(ctx, target),
            FlightStyle.Ballistic => UpdateBallistic(ctx, target),
            FlightStyle.Swoop => UpdateSwoop(ctx, target),
            _ => UpdateTransit(ctx, target),
        };
    }

    // Cambiar de idea --------------------------------------------------------------------------------------------

    /// <summary>
    /// Al despegar decide si, a medio camino, se lo va a pensar mejor: solo cuando va a explorar, de tarde en tarde (como
    /// mucho una vez cada pocos minutos) y más si está curioso.
    /// </summary>
    private void PlanRethink(FlightPurpose purpose)
    {
        _rethinkTo = null;
        _hesitation = 0;
        _rethinkAt = purpose is FlightPurpose.Explore or FlightPurpose.Investigate && _rethinkCooldown <= 0
            && random.Chance(0.12 + 0.25 * Personality.Curiosity)
            ? random.Range(0.9, 2.6)
            : double.NaN;
    }

    /// <summary>
    /// Cambia de idea sobre la marcha: frena un instante, mira el sitio nuevo (con su icono en el bocadillo) y va hacia
    /// allí. Devuelve los mandos mientras duda; null si sigue con lo suyo.
    /// </summary>
    private ThrustInput? Rethink(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        if (_rethinkTo is { } next)
        {
            _hesitation -= ctx.Dt;
            if (!next.TryResolve(ctx.World, body, out var center, out _))
            {
                _rethinkTo = null;
                return null;
            }

            ctx.Visual.LookTarget = center;
            if (_hesitation > 0)
            {
                return Steer(ctx, new Vec2(body.Velocity.X * 0.25, Math.Min(0, body.Velocity.Y) * 0.25));
            }

            _rethinkTo = null;
            Retarget(ctx, next, FlightPurpose.Explore);
            if (random.Chance(0.4) && Math.Abs(center.X - body.Position.X) > body.Height * 5)
            {
                _style = FlightStyle.Brisk;
            }

            return null;
        }

        if (double.IsNaN(_rethinkAt) || _airTime < _rethinkAt)
        {
            return null;
        }

        _rethinkAt = double.NaN;
        if (State != AutopilotState.Flying || _phase != FlightPhase.Transit || _tour.Count > 0 || _destination is null
            || _purpose is not (FlightPurpose.Explore or FlightPurpose.Investigate)
            || _style is FlightStyle.Ballistic or FlightStyle.Swoop or FlightStyle.Hopper or FlightStyle.HoverSlam
            || Vec2.Distance(body.Position, target) < body.Height * 5 || !CanExplore(ctx))
        {
            return null;
        }

        // Otro sitio de verdad: ni la ventana a la que iba ni uno pegado a ella.
        var query = BuildQuery(ctx, _destination.OwnerId);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (DestinationPicker.PickExploration(ctx.World, body, query, random) is not { } other
                || !other.TryResolve(ctx.World, body, out var center, out _)
                || Vec2.Distance(center, target) < body.Height * 4 || !IsReachable(ctx, other))
            {
                continue;
            }

            _rethinkTo = other;
            _hesitation = random.Range(0.55, 0.9);
            _rethinkCooldown = random.Range(150, 320);
            var direction = Math.Atan2(center.Y - body.Position.Y, center.X - body.Position.X);
            ctx.Visual.ShowBubble(DestinationBubble(other, _hesitation + 1.2, direction));
            ctx.Visual.Flash(Expression.Curious, _hesitation + 0.8);
            ctx.Visual.LookTarget = center;
            ctx.Signals.Add(GameEventKind.ChangedMind);
            return Steer(ctx, new Vec2(body.Velocity.X * 0.25, Math.Min(0, body.Velocity.Y) * 0.25));
        }

        return null;
    }

    private bool TryResolveTarget(AutopilotContext ctx, out Vec2 target)
    {
        if (_destination is not null && _destination.TryResolve(ctx.World, ctx.Body, out target, out _))
        {
            return true;
        }

        Replan(ctx);
        target = default;
        return _destination is not null && _destination.TryResolve(ctx.World, ctx.Body, out target, out _);
    }

    private readonly record struct Profile(double Speed, double Tilt, double Braking);

    private static Profile ProfileOf(FlightStyle style) => style switch
    {
        FlightStyle.Brisk => new Profile(330, 0.72, 105),
        FlightStyle.Lazy => new Profile(170, 0.4, 45),
        FlightStyle.Arc => new Profile(260, 0.65, 75),
        FlightStyle.HoverSlam => new Profile(240, 0.6, 75),
        FlightStyle.Contour => new Profile(250, 0.66, 85),
        FlightStyle.Precision => new Profile(150, 0, 60),
        FlightStyle.Dolphin => new Profile(240, 0.62, 75),
        _ => new Profile(230, FlightGuidance.DefaultMaxTilt, 70),
    };

    /// <summary>
    /// Desplazamiento hasta quedar sobre el destino: a la altura de crucero, siguiendo el relieve, ondulando o
    /// nivelado con los propulsores de traslación, según el estilo.
    /// </summary>
    private ThrustInput UpdateTransit(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        var height = body.Height;
        var profile = ProfileOf(_style);
        var dx = target.X - body.Position.X;

        if (_style == FlightStyle.HoverSlam)
        {
            if (Math.Abs(dx) < height * 0.35 && Math.Abs(body.Velocity.X) < 22 * scale
                && Math.Abs(body.Position.Y - _cruiseY) < height * 0.5 && Math.Abs(body.Velocity.Y) < 35 * scale)
            {
                State = AutopilotState.Landing;
                _stateTime = 0;
                _phase = FlightPhase.Poise;
                ctx.Visual.Flash(Expression.Focused, 3);
                return UpdateLanding(ctx);
            }
        }
        else if (Math.Abs(dx) < height * 0.5 && Math.Abs(body.Velocity.X) < 45 * scale && body.Position.Y <= target.Y + height * 0.2)
        {
            BeginApproach(ctx, target);
            return UpdateLanding(ctx);
        }

        var energyFactor = 0.55 + 0.45 * Personality.Energy;
        var cruiseSpeed = (_purpose == FlightPurpose.Dodge ? 260 : profile.Speed) * scale * energyFactor;
        var braking = profile.Braking * scale * (_mistake == PilotMistake.Overshoot ? 1.5 + 1.5 * _mistakeSeverity : 1);
        var vx = Math.Sign(dx) * Math.Min(cruiseSpeed, MathUtil.ApproachSpeed(dx, braking, body.Velocity.X));
        if (_airTime < 0.45 && _purpose is not (FlightPurpose.Recover or FlightPurpose.Dodge))
        {
            vx = 0;
        }

        var altitude = _style switch
        {
            FlightStyle.Contour => ContourAltitude(ctx, target),
            FlightStyle.Dolphin => DolphinAltitude(ctx, target),
            _ => _cruiseY,
        };
        var dy = altitude - body.Position.Y;
        var climbRate = _style == FlightStyle.Contour ? 200 : 170;
        var vy = Math.Sign(dy) * Math.Min(climbRate * scale, MathUtil.ApproachSpeed(dy, (_style == FlightStyle.Contour ? 140 : 90) * scale, body.Velocity.Y, 0.1));
        if (_style == FlightStyle.Dolphin)
        {
            vy += DolphinSlope(ctx, target) * body.Velocity.X;
        }

        var retractLegs = _airTime > 0.6 && body.Position.Y < target.Y - height * 4 && Math.Abs(dx) > height * 3;
        return _style == FlightStyle.Precision
            ? SteerLevel(ctx, new Vec2(vx, vy))
            : Steer(ctx, new Vec2(vx, vy), retractLegs, profile.Tilt);
    }

    // Rasante siguiendo el relieve ---------------------------------------------------------------------------

    /// <summary>
    /// Altura para pasar a algo más de una altura del lander por encima de lo que tiene delante (ventanas o
    /// suelo), mirando más lejos cuanto más deprisa va. Cerca del destino, la de crucero normal.
    /// </summary>
    private double ContourAltitude(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var height = body.Height;
        var x = body.Position.X;
        var dx = target.X - x;
        if (Math.Abs(dx) < height * 3)
        {
            return _cruiseY;
        }

        var ahead = Math.Sign(dx) * (Math.Abs(body.Velocity.X) * 1.1 + height * 2.5);
        var from = Math.Min(x, x + ahead) - body.HalfWidth;
        var to = Math.Max(x, x + ahead) + body.HalfWidth;
        var surface = double.MaxValue;
        foreach (var platform in ctx.World.Platforms)
        {
            if (platform.Right >= from && platform.Left <= to && platform.Y > body.Position.Y - height * 8)
            {
                surface = Math.Min(surface, platform.Y);
            }
        }

        if (surface == double.MaxValue)
        {
            return _cruiseY;
        }

        // Nunca más alto que el techo de lo que tiene delante (entre monitores, el techo puede bajar).
        var altitude = Math.Min(surface - body.HalfHeight - height * 1.2, target.Y - height * 0.6);
        for (var i = 0; i <= 4; i++)
        {
            var highest = FlightEnvelope.HighestCenter(ctx.World, body, new Vec2(x + ahead * i / 4, body.Position.Y), 0.3);
            if (!double.IsNegativeInfinity(highest))
            {
                altitude = Math.Max(altitude, highest);
            }
        }

        return altitude;
    }

    // Vuelo ondulante -----------------------------------------------------------------------------------------

    private static double DolphinAmplitude(double height) => height * 0.8;

    /// <summary>Entre la altura de crucero y el suelo hay sitio para toda la onda (y algo más).</summary>
    private bool HasRoomToUndulate(AutopilotContext ctx, FlightPurpose purpose, Vec2 target)
    {
        var body = ctx.Body;
        var cruise = PlanAltitude(ctx, FlightStyle.Dolphin, purpose, target);
        var lowest = cruise + DolphinAmplitude(body.Height) + body.HalfHeight + body.Height * 1.3;
        return lowest < GroundBelow(ctx, body.Position) && lowest < GroundBelow(ctx, target);
    }

    private static double DolphinWavelength(double height) => height * 8;

    /// <summary>Cuánto de la onda queda: crece al empezar y se apaga al acercarse al destino.</summary>
    private double DolphinEnvelope(AutopilotContext ctx, Vec2 target)
    {
        var height = ctx.Body.Height;
        var travelled = Math.Abs(ctx.Body.Position.X - _travelStartX);
        var remaining = Math.Abs(target.X - ctx.Body.Position.X);
        return Math.Clamp(travelled / (height * 2), 0, 1) * Math.Clamp((remaining - height * 1.5) / (height * 3), 0, 1);
    }

    private double DolphinAltitude(AutopilotContext ctx, Vec2 target)
    {
        var height = ctx.Body.Height;
        var phase = (ctx.Body.Position.X - _travelStartX) / DolphinWavelength(height) * Math.Tau;
        var altitude = _cruiseY - DolphinAmplitude(height) * DolphinEnvelope(ctx, target) * Math.Sin(phase);

        // Ni contra el techo ni cerca del suelo: con poco sitio, la onda se queda más plana.
        altitude = Math.Min(altitude, GroundBelow(ctx, ctx.Body.Position) - ctx.Body.HalfHeight - height * 1.3);
        var highest = FlightEnvelope.HighestCenter(ctx.World, ctx.Body, ctx.Body.Position, 0.3);
        return double.IsNegativeInfinity(highest) ? altitude : Math.Max(altitude, highest);
    }

    /// <summary>Pendiente de la onda (dy/dx), para anticipar la subida o bajada con la velocidad lateral.</summary>
    private double DolphinSlope(AutopilotContext ctx, Vec2 target)
    {
        var height = ctx.Body.Height;
        var wavelength = DolphinWavelength(height);
        var phase = (ctx.Body.Position.X - _travelStartX) / wavelength * Math.Tau;
        return -DolphinAmplitude(height) * DolphinEnvelope(ctx, target) * Math.Cos(phase) * Math.Tau / wavelength;
    }

    // Mandos, combustible y replanificación ----------------------------------------------------------------

    /// <summary>
    /// Pide al controlador la velocidad deseada, recortada para poder frenar siempre antes del techo y de
    /// las paredes. Solo se posa en su destino y únicamente en la fase de aterrizaje: en crucero pasa por
    /// delante de todas las ventanas, aunque el destino se le cruce.
    /// </summary>
    private ThrustInput Steer(AutopilotContext ctx, Vec2 velocity, bool retractLegs = false, double maxTilt = FlightGuidance.DefaultMaxTilt)
    {
        var body = ctx.Body;
        var landOn = LandOn(ctx);
        var limited = FlightEnvelope.Limit(ctx.World, body, ctx.Physics, velocity, GroundGuard(landOn));
        var input = FlightGuidance.TrackVelocity(body, ctx.Physics, limited, retractLegs, maxTilt) with { LandOn = landOn };

        // Cerca de una pared, si va demasiado deprisa hacia ella, los propulsores de traslación ayudan a frenar.
        var excess = body.Velocity.X - limited.X;
        if (Math.Abs(limited.X) < Math.Abs(velocity.X) - 1 && Math.Sign(excess) == Math.Sign(body.Velocity.X) && Math.Abs(excess) > 8 * body.Scale)
        {
            input = input with { Lateral = Math.Clamp(-excess * 2.4 / (ctx.Physics.LateralThrust * body.Scale), -1, 1) };
        }

        return input;
    }

    /// <summary>Como <see cref="Steer"/>, pero nivelado: se desplaza con los propulsores de traslación.</summary>
    private ThrustInput SteerLevel(AutopilotContext ctx, Vec2 velocity)
    {
        var landOn = LandOn(ctx);
        var limited = FlightEnvelope.Limit(ctx.World, ctx.Body, ctx.Physics, velocity, GroundGuard(landOn));
        return FlightGuidance.TrackVelocityLevel(ctx.Body, ctx.Physics, limited) with { LandOn = landOn };
    }

    /// <summary>Solo se acepta el destino aterrizando y con la nave casi recta: inclinada, mejor pasar de largo y volver.</summary>
    private long LandOn(AutopilotContext ctx) =>
        State == AutopilotState.Landing && _destination is not null && (Math.Abs(ctx.Body.Angle) < 0.12 || _mistake != PilotMistake.None)
            ? _destination.OwnerId
            : ThrustInput.GroundOnly;

    /// <summary>Si se ha despistado con la altura, no hace caso al aviso de suelo (es justo su error).</summary>
    private long GroundGuard(long landOn) =>
        _mistake == PilotMistake.FastTouchdown && State == AutopilotState.Landing ? FlightEnvelope.NoGroundGuard : landOn;

    /// <summary>
    /// Vigila el combustible en vuelo. Si ya no llega al destino, se desvía a la base o, si tampoco llega,
    /// aterriza en lo primero que tenga debajo. Devuelve true si ha cambiado el destino.
    /// </summary>
    private bool CheckFuel(AutopilotContext ctx, Vec2 target)
    {
        if (ctx.Time < _nextFuelCheck || _phase != FlightPhase.Transit)
        {
            return false;
        }

        _nextFuelCheck = ctx.Time + 0.25;
        var body = ctx.Body;
        var config = ctx.Physics;
        var hover = FuelPlanner.HoverBurn(body, config);
        var critical = FuelPlanner.ToStop(body, config) + hover * 4;
        if (body.Fuel < critical && (_purpose != FlightPurpose.Recover || _flightTime > 12))
        {
            EmergencyLanding(ctx);
            return true;
        }

        if (_purpose is FlightPurpose.ReturnToBase or FlightPurpose.Recover or FlightPurpose.EdgeDrop or FlightPurpose.Hop or FlightPurpose.Stroll or FlightPurpose.Dodge)
        {
            return false;
        }

        var needed = FuelPlanner.Estimate(body, config, body.Position, target) * 0.8;
        if (body.Fuel >= needed + FuelPlanner.Reserve(body) * 0.5)
        {
            return false;
        }

        var home = FuelPlanner.ToBase(ctx.World, body, config, body.Position);
        if (body.Fuel >= home + FuelPlanner.Reserve(body) * 0.25 && DestinationPicker.PickBase(ctx.World, body, random) is { } baseDestination)
        {
            ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Fuel, 2));
            Retarget(ctx, baseDestination, FlightPurpose.ReturnToBase);
            State = AutopilotState.ReturningToBase;
        }
        else
        {
            EmergencyLanding(ctx);
        }

        return true;
    }

    /// <summary>Aterriza ya en lo que tenga debajo, cayendo y frenando al final para gastar lo mínimo.</summary>
    private void EmergencyLanding(AutopilotContext ctx)
    {
        var body = ctx.Body;
        ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Fuel, 2));
        ctx.Visual.Flash(Expression.Scared, 1.5);
        Retarget(ctx, DestinationPicker.PickBelow(ctx.World, body, body.Position, BrakingDistance(body, ctx.Physics)), FlightPurpose.Recover);
        if (_destination is not null && _destination.TryResolve(ctx.World, body, out var center, out _)
            && center.Y - body.Position.Y > body.Height * 5)
        {
            _style = FlightStyle.HoverSlam;
            _cruiseY = body.Position.Y;
            _poiseDuration = 0.1;
        }
    }

    private void Retarget(AutopilotContext ctx, Destination? destination, FlightPurpose purpose)
    {
        var body = ctx.Body;
        _destination = destination;
        _purpose = purpose;
        _flightTime = 0;
        ResetManeuvers();
        _flipPlanned = false;
        _tour.Clear();
        _style = purpose == FlightPurpose.Recover ? FlightStyle.Direct : FlightStyle.Cruise;
        if (destination is not null && destination.TryResolve(ctx.World, body, out var center, out _))
        {
            _cruiseY = ReachableCruise(ctx, center, Math.Min(body.Position.Y, center.Y) - body.Height * 0.5);
        }
    }

    /// <summary>Vuelve a dejar todas las maniobras como al empezar un vuelo.</summary>
    private void ResetManeuvers()
    {
        _phase = FlightPhase.Transit;
        _arcStarted = false;
        _flipDone = false;
        _flipStage = 0;
        _leaf = false;
        _leafTime = 0;
        _hopApex = false;
        _hopPause = 0;
        _hopChain.Clear();
        _finalDestination = null;
        _coastFlipping = false;
        _diving = false;
        _surveyTime = 0;
    }

    /// <summary>Hay sitio de sobra por encima para salir disparado con el impulso sin acercarse al techo.</summary>
    private static bool HasRoomToBoost(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var highest = FlightEnvelope.HighestCenter(ctx.World, body, body.Position, 0.5);
        if (double.IsNegativeInfinity(highest))
        {
            return true;
        }

        var impulse = ctx.Physics.BoostImpulse * body.Scale;
        var rise = impulse * impulse / (2 * ctx.Physics.Gravity * body.Scale);
        return body.Position.Y - rise - body.Height > highest;
    }

    /// <summary>Si pasa volando junto al cursor (y el usuario lo está moviendo), lo saluda balanceando la nave.</summary>
    private void WaggleAtCursor(AutopilotContext ctx)
    {
        var body = ctx.Body;
        if (_waggleCooldown > 0 || _phase != FlightPhase.Transit || ctx.Sensors.Cursor is not { } cursor
            || ctx.Sensors.CursorVelocity.Length < 25 * body.Scale || Vec2.Distance(cursor, body.Position) > body.Height * 3)
        {
            return;
        }

        _waggleCooldown = 40;
        ctx.Visual.StartWave();
        ctx.Visual.Flash(Expression.Happy, 1.8);
        ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Hello, 1.6));
        ctx.Signals.Add(GameEventKind.Greeted);
    }

    /// <summary>Y del suelo (base) bajo un punto, o muy abajo si no hay.</summary>
    private static double GroundBelow(AutopilotContext ctx, Vec2 point)
    {
        var monitor = ctx.World.MonitorAt(point) ?? ctx.World.NearestMonitor(point);
        var ground = (monitor is null ? null : ctx.World.GroundOf(monitor.Index)) ?? ctx.World.NearestGround(point);
        return ground?.Y ?? point.Y + 10_000;
    }
}
