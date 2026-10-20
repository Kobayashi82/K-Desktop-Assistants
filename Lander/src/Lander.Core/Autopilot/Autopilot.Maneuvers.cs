using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Terrain;

namespace Lander.Core.Autopilot;

/// <summary>
/// Maniobras del vuelo: saltos cortos y saltitos encadenados, arcos, puntos en el aire (celebraciones y
/// vuelos panorámicos) y volteretas.
/// </summary>
public sealed partial class Autopilot
{
    private const int ArcSamples = 32;

    private readonly Vec2[] _arcPoints = new Vec2[ArcSamples + 1];
    private readonly double[] _arcLengths = new double[ArcSamples + 1];
    private readonly List<Vec2> _tour = [];
    private readonly List<Vec2> _plannedTour = [];
    private readonly List<double> _hopChain = [];
    private readonly List<double> _plannedHops = [];

    private bool _hopApex;
    private double _hopPause;
    private Destination? _finalDestination;

    private bool _arcStarted;
    private Vec2 _arcStart;
    private double _arcApex;
    private int _arcIndex;

    private bool _flipPlanned;
    private bool _flipDone;
    private int _flipStage;
    private double _flipTurned;
    private double _flipDirection;
    private double _flipPop;
    private double _flipTime;

    // Saltos cortos ------------------------------------------------------------------------------------------

    /// <summary>Saltitos (paseos, sustos, esquivas y saltos encadenados): cerca y casi a la misma altura.</summary>
    private bool IsShortHop(AutopilotContext ctx, Vec2 target) =>
        (_style == FlightStyle.Hopper || (_style == FlightStyle.Direct && _purpose is FlightPurpose.Stroll or FlightPurpose.Hop or FlightPurpose.Dodge))
        && Math.Abs(target.X - ctx.Body.Position.X) < ctx.Body.Height * 8
        && Math.Abs(target.Y - ctx.Body.Position.Y) < ctx.Body.Height * 2.5;

    /// <summary>
    /// Un salto corto y ágil: sube hasta la altura del salto, se desplaza y baja en cuanto está encima,
    /// sin la parada en estacionario de una aproximación normal.
    /// </summary>
    private ThrustInput UpdateHop(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        var height = body.Height;
        var dx = target.X - body.Position.X;
        var above = target.Y - body.Position.Y;
        if (!_hopApex && (body.Position.Y <= _cruiseY + height * 0.15 || _airTime > 1.1))
        {
            _hopApex = true;
        }

        var vx = Math.Sign(dx) * Math.Min(170 * scale, MathUtil.ApproachSpeed(dx, 70 * scale, body.Velocity.X, 0.45));
        if (!_hopApex || Math.Abs(dx) > height * 0.6 || Math.Abs(body.Velocity.X) > 45 * scale)
        {
            var dy = _cruiseY - body.Position.Y;
            var vy = Math.Sign(dy) * Math.Min(150 * scale, MathUtil.ApproachSpeed(dy, 120 * scale, body.Velocity.Y, 0.1));
            return Steer(ctx, new Vec2(_hopApex ? vx : vx * 0.3, vy), retractLegs: false, maxTilt: 0.6);
        }

        // Ya encima: a por el destino, terminando de centrarse mientras baja.
        State = AutopilotState.Landing;
        if (above > height * 0.6)
        {
            var descent = Math.Clamp(MathUtil.ApproachSpeed(above - height * 0.2, 70 * scale, body.Velocity.Y, 0.1), 32 * scale, 60 * scale);
            return Steer(ctx, new Vec2(Math.Clamp(vx, -35 * scale, 35 * scale), descent), retractLegs: false, maxTilt: 0.2);
        }

        // Los últimos centímetros, recto: el contacto tiene que ser con la nave nivelada.
        var settle = Math.Abs(body.Angle) > 0.09 || Math.Abs(dx) > height * 0.25 ? 0 : ContactSpeed(36, scale);
        return Steer(ctx, new Vec2(Math.Clamp(dx * 1.5, -20 * scale, 20 * scale), settle), retractLegs: false, maxTilt: 0.06);
    }

    // Saltitos encadenados ----------------------------------------------------------------------------------

    /// <summary>
    /// Planea ir a saltitos hasta un destino de la misma plataforma: tres o más saltos de unas tres o cuatro
    /// alturas del lander, todos sobre la misma superficie y con sitio por encima.
    /// </summary>
    private bool TryPlanHops(AutopilotContext ctx, Destination destination)
    {
        _plannedHops.Clear();
        var body = ctx.Body;
        if (body.Support is not { } support || support.OwnerId != destination.OwnerId
            || !destination.TryResolve(ctx.World, body, out var target, out var platform))
        {
            return false;
        }

        var height = body.Height;
        var distance = target.X - body.Position.X;
        if (Math.Abs(distance) < height * 6 || Math.Abs(target.Y - body.Position.Y) > height * 0.5)
        {
            return false;
        }

        var hops = Math.Max(2, (int)Math.Round(Math.Abs(distance) / (height * random.Range(3, 4.2))));
        var margin = DestinationPicker.LandingMargin(body);
        for (var i = 1; i < hops; i++)
        {
            var x = body.Position.X + distance * i / hops + random.Range(-0.3, 0.3) * height;
            if (ctx.World.FindSegment(platform.OwnerId, x) is not { } segment || !segment.ContainsX(x, margin)
                || Math.Abs(segment.Y - platform.Y) > 1 || !FlightEnvelope.HasHeadroom(ctx.World, body, new Vec2(x, target.Y - height * 0.9)))
            {
                _plannedHops.Clear();
                return false;
            }

            _plannedHops.Add(x);
        }

        return true;
    }

    private void StartHopChain(AutopilotContext ctx)
    {
        if (_plannedHops.Count == 0 || _destination is null || !_destination.TryResolve(ctx.World, ctx.Body, out _, out var platform))
        {
            _style = FlightStyle.Direct;
            return;
        }

        _hopChain.Clear();
        _hopChain.AddRange(_plannedHops);
        _finalDestination = _destination;
        _destination = Destination.On(ctx.World.FindSegment(platform.OwnerId, _hopChain[0]) ?? platform, _hopChain[0]);
        _hopChain.RemoveAt(0);
        _cruiseY = HopAltitude(ctx);
    }

    /// <summary>Al posarse en mitad de unos saltitos, prepara el siguiente. Devuelve false si ya ha llegado.</summary>
    private bool TryNextHop(AutopilotContext ctx)
    {
        if (_style != FlightStyle.Hopper || _finalDestination is null)
        {
            return false;
        }

        var body = ctx.Body;
        Destination? next = null;
        if (_hopChain.Count > 0)
        {
            var x = _hopChain[0];
            _hopChain.RemoveAt(0);
            if (ctx.World.FindSegment(_finalDestination.OwnerId, x) is { } segment)
            {
                next = Destination.On(segment, x);
            }
        }

        if (next is null)
        {
            next = _finalDestination;
            _finalDestination = null;
        }

        _destination = next;
        State = _purpose == FlightPurpose.ReturnToBase ? AutopilotState.ReturningToBase : AutopilotState.Flying;
        _stateTime = 0;
        _phase = FlightPhase.Transit;
        _airborneSeen = false;
        _airTime = 0;
        _hopApex = false;
        _spool = false;
        _hopPause = random.Range(0.12, 0.22);
        _cruiseY = HopAltitude(ctx);
        ctx.Visual.LookTarget = next.TryResolve(ctx.World, body, out var center, out _) ? center : null;
        return true;
    }

    private double HopAltitude(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var preferred = body.Position.Y - body.Height * random.Range(0.55, 0.95);
        var highest = FlightEnvelope.HighestCenter(ctx.World, body, body.Position, 0.3);
        return double.IsNegativeInfinity(highest) ? preferred : Math.Max(preferred, Math.Min(highest, body.Position.Y - body.Height * 0.3));
    }

    // Arco --------------------------------------------------------------------------------------------------

    /// <summary>Un arco amplio (curva de Bézier) desde el despegue hasta un punto sobre el destino.</summary>
    private ThrustInput UpdateArc(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        var height = body.Height;
        if (!_arcStarted)
        {
            if (_airTime < 0.35)
            {
                return Steer(ctx, new Vec2(0, -110 * scale));
            }

            _arcStarted = true;
            _arcStart = body.Position;
            _arcIndex = 0;
            BuildArc(ctx, ArcEnd(ctx, target));
            if (!FlightEnvelope.PathIsClear(ctx.World, body, _arcPoints, target.Y + body.HalfHeight, (int)(ArcSamples * 0.85)))
            {
                // El arco se saldría de los monitores o rozaría un suelo por el camino: mejor un crucero.
                _style = FlightStyle.Cruise;
                _cruiseY = PlanAltitude(ctx, FlightStyle.Cruise, _purpose, target);
                return UpdateTransit(ctx, target);
            }
        }

        BuildArc(ctx, ArcEnd(ctx, target));

        var best = _arcIndex;
        var bestDistance = double.MaxValue;
        for (var i = _arcIndex; i <= Math.Min(ArcSamples, _arcIndex + 6); i++)
        {
            var distance = (_arcPoints[i] - body.Position).LengthSquared;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        _arcIndex = best;
        var total = _arcLengths[ArcSamples];
        var travelled = _arcLengths[best];
        var remaining = Math.Max(0, total - travelled) + Math.Sqrt(bestDistance) * 0.5;
        if (remaining < height * 0.8 || best == ArcSamples)
        {
            BeginApproach(ctx, target);
            return UpdateLanding(ctx);
        }

        var lookahead = Math.Max(height * 1.1, body.Velocity.Length * 0.35);
        var carrot = PointOnArc(travelled + lookahead);
        var profile = ProfileOf(FlightStyle.Arc);
        var speed = Math.Min(
            profile.Speed * scale * (0.55 + 0.45 * Personality.Energy),
            Math.Min(MathUtil.BrakingSpeed(remaining, profile.Braking * scale), 70 * scale + MathUtil.BrakingSpeed(travelled, 140 * scale)));
        var direction = (carrot - body.Position).Normalized();
        var retract = remaining > height * 4 && travelled > height * 2;
        return Steer(ctx, direction * speed, retract, profile.Tilt);
    }

    /// <summary>Final del arco: sobre el destino, pero nunca por encima del techo (ventanas pegadas arriba).</summary>
    private static Vec2 ArcEnd(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var end = target + new Vec2(0, -body.Height * 1.4);
        var highest = FlightEnvelope.HighestCenter(ctx.World, body, target, 0.1);
        return double.IsNegativeInfinity(highest) ? end : end.WithY(Math.Min(target.Y, Math.Max(end.Y, highest)));
    }

    private void BuildArc(AutopilotContext ctx, Vec2 end)
    {
        var body = ctx.Body;
        var start = _arcStart;
        var middle = Vec2.Lerp(start, end, 0.5);

        // Punto más alto de la curva: el elegido, pero siempre bajo el techo.
        var peak = Math.Min(start.Y, end.Y) - _arcApex;
        var limit = FlightEnvelope.HighestCenter(ctx.World, body, middle, 0.3);
        if (!double.IsNegativeInfinity(limit))
        {
            peak = Math.Max(peak, limit);
        }

        peak = Math.Min(peak, Math.Min(start.Y, end.Y));
        var control = new Vec2(middle.X, (peak - 0.25 * (start.Y + end.Y)) * 2);
        _arcLengths[0] = 0;
        for (var i = 0; i <= ArcSamples; i++)
        {
            var u = (double)i / ArcSamples;
            var a = (1 - u) * (1 - u);
            var b = 2 * u * (1 - u);
            var c = u * u;
            _arcPoints[i] = new Vec2(a * start.X + b * control.X + c * end.X, a * start.Y + b * control.Y + c * end.Y);
            if (i > 0)
            {
                _arcLengths[i] = _arcLengths[i - 1] + Vec2.Distance(_arcPoints[i], _arcPoints[i - 1]);
            }
        }
    }

    private Vec2 PointOnArc(double length)
    {
        if (length >= _arcLengths[ArcSamples])
        {
            return _arcPoints[ArcSamples];
        }

        for (var i = 1; i <= ArcSamples; i++)
        {
            if (_arcLengths[i] >= length)
            {
                var span = _arcLengths[i] - _arcLengths[i - 1];
                var t = span > 0 ? (length - _arcLengths[i - 1]) / span : 0;
                return Vec2.Lerp(_arcPoints[i - 1], _arcPoints[i], t);
            }
        }

        return _arcPoints[ArcSamples];
    }

    // Puntos en el aire -------------------------------------------------------------------------------------

    private ThrustInput FlyToWaypoint(AutopilotContext ctx, Vec2 waypoint)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        ctx.Visual.LookTarget = waypoint + new Vec2(0, body.Height);

        var delta = waypoint - body.Position;
        if (delta.Length < body.Height * 0.6 && body.Velocity.Length < 50 * scale)
        {
            if (!_celebrated)
            {
                _celebrated = true;
                ctx.Visual.CelebrateTime = 2;
                ctx.Visual.Flash(Expression.Happy, 2.5);
                ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Heart, 1.8));
                ctx.Signals.Add(GameEventKind.Celebrated);
            }

            _waypointHold -= ctx.Dt;
            if (_waypointHold <= 0)
            {
                _waypoint = null;
                if (_destination is not null && _destination.TryResolve(ctx.World, body, out var target, out _))
                {
                    _cruiseY = ReachableCruise(ctx, target, Math.Min(body.Position.Y, target.Y) - body.Height * 0.5);
                }
            }
        }

        var vx = Math.Sign(delta.X) * Math.Min(230 * scale, MathUtil.BrakingSpeed(delta.X, 70 * scale));
        var vy = Math.Sign(delta.Y) * Math.Min(170 * scale, MathUtil.BrakingSpeed(delta.Y, 90 * scale));
        return Steer(ctx, new Vec2(vx, vy));
    }

    /// <summary>
    /// Pasa por los puntos del recorrido sin pararse (siguiendo una «zanahoria» un poco por delante) y,
    /// al terminar, sigue hacia su destino con el estilo elegido.
    /// </summary>
    private ThrustInput FlyTour(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        var height = body.Height;
        var point = _tour[0];
        ctx.Visual.LookTarget = point;
        if (Vec2.Distance(point, body.Position) < height * (_tour.Count > 1 ? 1.4 : 0.9))
        {
            _tour.RemoveAt(0);
            if (_tour.Count == 0)
            {
                // Recorrido terminado: crucero (o caída libre) a la altura a la que va.
                _cruiseY = ReachableCruise(ctx, target, Math.Min(body.Position.Y, target.Y - height * 0.5));
                if (_style == FlightStyle.HoverSlam && target.Y - _cruiseY < height * 6)
                {
                    _style = FlightStyle.Cruise;
                }

                return UpdateTransit(ctx, target);
            }

            point = _tour[0];
        }

        if (_airTime < 0.45)
        {
            return Steer(ctx, new Vec2(0, -120 * scale));
        }

        var delta = point - body.Position;
        var distance = delta.Length;
        var cruise = 210 * scale * (0.6 + 0.4 * Personality.Energy);

        // Solo frena de verdad hacia el último punto; entre medias, curva sin pararse.
        var speed = _tour.Count == 1
            ? Math.Min(cruise, 60 * scale + MathUtil.ApproachSpeed(distance, 80 * scale, body.Velocity.Length))
            : cruise;
        return Steer(ctx, delta / Math.Max(1, distance) * speed, retractLegs: true, maxTilt: 0.62);
    }

    // Voltereta ---------------------------------------------------------------------------------------------

    private void PlanFlip(AutopilotContext ctx, FlightPurpose purpose)
    {
        var stunts = Math.Clamp(ctx.Acrobatics, 0, 1) * 2;
        _flipPlanned = stunts > 0
            && (_style is FlightStyle.Cruise or FlightStyle.Arc or FlightStyle.Brisk or FlightStyle.Ballistic || purpose == FlightPurpose.Sightsee)
            && purpose is FlightPurpose.Explore or FlightPurpose.Investigate or FlightPurpose.Follow or FlightPurpose.Celebrate or FlightPurpose.Sightsee
            && Personality.Energy > 0.55
            && random.Chance(((purpose == FlightPurpose.Sightsee || _style == FlightStyle.Ballistic ? 0.25 : 0.08) + 0.17 * Personality.Energy) * Math.Min(1.6, stunts));
    }

    /// <summary>
    /// Una voltereta cuando hay sitio de sobra: un empujón hacia arriba, motor apagado, una vuelta completa
    /// con los propulsores de giro y recuperación. El empujón se calcula para no llegar al techo.
    /// </summary>
    private bool TryStartFlip(AutopilotContext ctx)
    {
        var body = ctx.Body;
        if (!_flipPlanned || _flipDone || _phase != FlightPhase.Transit || _airTime < 1.2 || _waypoint is not null
            || _style is FlightStyle.Ballistic or FlightStyle.Swoop)
        {
            return false;
        }

        var scale = body.Scale;
        var height = body.Height;
        if (body.Velocity.Length > 150 * scale || Math.Abs(body.Angle) > 0.15 || body.FuelFraction < 0.35)
        {
            return false;
        }

        // El techo de todo el tramo que va a recorrer mientras da la vuelta (entre monitores puede bajar).
        var highest = double.NegativeInfinity;
        for (var i = 0; i <= 4; i++)
        {
            highest = Math.Max(highest, FlightEnvelope.HighestCenter(ctx.World, body, body.Position + new Vec2(body.Velocity.X * 0.75 * i, 0), 0.2));
        }

        var room = double.IsNegativeInfinity(highest) ? height * 20 : body.Position.Y - highest;
        var gravity = ctx.Physics.Gravity * scale;
        var pop = Math.Min(250 * scale, Math.Sqrt(2 * gravity * Math.Max(0, room - height * 0.4)) * 0.92);
        var groundGap = GroundBelow(ctx, body.Position) - body.FeetY;
        if (pop < 200 * scale || groundGap < height * 4)
        {
            return false;
        }

        _phase = FlightPhase.Flip;
        _flipStage = 0;
        _flipPop = pop;
        _flipTurned = 0;
        _flipTime = 0;
        _flipDirection = body.Velocity.X > 5 * scale ? 1 : body.Velocity.X < -5 * scale ? -1 : random.Sign();
        ctx.Visual.Flash(Expression.Happy, 3.5);
        return true;
    }

    private ThrustInput UpdateFlip(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var config = ctx.Physics;
        var dt = ctx.Dt;
        _flipTime += dt;
        if (_flipTime > 5 || body.Fuel <= 0)
        {
            EndFlip(ctx);
            return Steer(ctx, Vec2.Zero);
        }

        if (_flipStage == 0)
        {
            // Empujón hacia arriba, sin pasarse: lo que suba ahora es lo que subirá sin motor.
            var highest = FlightEnvelope.HighestCenterAhead(ctx.World, body, config) + body.Height * 0.2;
            if (body.Velocity.Y > -_flipPop && FlightEnvelope.CoastApex(body, config) > highest)
            {
                return new ThrustInput(Throttle: 1, Rotate: FlightGuidance.RotateToward(body, config, 0), LandOn: ThrustInput.GroundOnly);
            }

            if (body.Velocity.Y > -_flipPop * 0.7)
            {
                // No ha podido coger impulso (el techo baja por delante): mejor sin voltereta.
                EndFlip(ctx);
                return Steer(ctx, Vec2.Zero);
            }

            _flipStage = 1;
        }

        // Si el suelo se acerca demasiado, se endereza ya aunque no haya completado la vuelta.
        if (_flipStage == 1 && GroundBelow(ctx, body.Position) - body.FeetY < body.Height * 2.5)
        {
            _flipStage = 2;
        }

        _flipTurned += body.AngularVelocity * dt * _flipDirection;
        if (_flipStage == 1)
        {
            var omega = Math.Abs(body.AngularVelocity);
            var stopping = omega * omega / (2 * config.RotationAcceleration * 0.85);
            if (_flipTurned < Math.Tau - stopping - 0.05)
            {
                return new ThrustInput(Rotate: _flipDirection, LandOn: ThrustInput.GroundOnly);
            }

            _flipStage = 2;
        }

        if (_flipStage == 2 && Math.Abs(body.Angle) < 0.3 && Math.Abs(body.AngularVelocity) < 1.2)
        {
            EndFlip(ctx);
            if (_flipTurned > Math.PI)
            {
                ctx.Signals.Add(GameEventKind.Flipped);
            }

            return TryResolveTarget(ctx, out var target) ? UpdateTransit(ctx, target) : Steer(ctx, Vec2.Zero);
        }

        return new ThrustInput(Rotate: FlightGuidance.RotateToward(body, config, 0), LandOn: ThrustInput.GroundOnly);
    }

    private void EndFlip(AutopilotContext ctx)
    {
        _phase = FlightPhase.Transit;
        _flipDone = true;
        var body = ctx.Body;

        // Tras la voltereta se sigue a la altura que haya quedado (sin volver a subir).
        _cruiseY = Math.Max(_cruiseY, Math.Min(body.Position.Y, _cruiseY + body.Height * 2));
        if (TryResolveTarget(ctx, out var target))
        {
            _cruiseY = ReachableCruise(ctx, target, _cruiseY);
        }
    }
}
