using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;

namespace Lander.Core.Autopilot;

/// <summary>
/// Las dos maniobras más «de cohete»: el salto balístico (un empujón inclinado, la parábola con el motor
/// apagado y una frenada final en dos dimensiones) y el picado (baja en diagonal muy deprisa y se encabrita
/// para frenar de golpe). Los dos prevén con <see cref="FlightPredictor"/> el último instante en que aún
/// pueden frenar.
/// </summary>
public sealed partial class Autopilot
{
    /// <summary>Inclinación máxima en la frenada de un salto balístico.</summary>
    private const double BrakeTilt = 0.85;

    /// <summary>Inclinación máxima al encabritarse al final de un picado.</summary>
    private const double FlareTilt = 0.95;

    private readonly List<Vec2> _coastPath = [];

    private double _apexY;
    private double _coastCheck;
    private double _coastSeconds;
    private bool _coastFlipPlanned;
    private bool _coastFlipping;
    private bool _diving;

    // Salto balístico -----------------------------------------------------------------------------------------

    /// <summary>
    /// Punto por el que pasa la parábola al bajar: sobre el destino, con sitio para frenar. Con el destino cerca
    /// del techo apunta más bajo, para dejar sitio a la cima por encima.
    /// </summary>
    private static Vec2 BallisticAim(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var height = body.Height;
        var highest = FlightEnvelope.HighestCenter(ctx.World, body, target, 0.6);
        var above = double.IsNegativeInfinity(highest) ? 2.2 : Math.Clamp((target.Y - highest) / height - 1.6, 0.9, 2.2);
        return target - new Vec2(0, height * above);
    }

    /// <summary>Donde termina la frenada: un poco por encima del destino, para posarse con calma.</summary>
    private static Vec2 BrakePoint(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var point = target - new Vec2(0, body.Height * 1.3);
        var highest = FlightEnvelope.HighestCenter(ctx.World, body, target, 0.1);
        return double.IsNegativeInfinity(highest) ? point : point.WithY(Math.Min(target.Y, Math.Max(point.Y, highest)));
    }

    /// <summary>
    /// Comprueba que hay sitio para una parábola de verdad (con su cima bajo el techo de todo el recorrido) y
    /// que esa parábola no se sale de los monitores ni pasa rozando un suelo.
    /// </summary>
    private bool TryPlanBallistic(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var world = ctx.World;
        var height = body.Height;
        var from = body.Position - new Vec2(0, height * 0.4);
        var aim = BallisticAim(ctx, target);
        var higher = Math.Min(from.Y, aim.Y);

        var apex = higher - height * random.Range(2.5, 5);
        for (var i = 0; i <= 8; i++)
        {
            var limit = FlightEnvelope.HighestCenter(world, body, new Vec2(MathUtil.Lerp(from.X, aim.X, i / 8.0), higher), 0.6);
            if (!double.IsNegativeInfinity(limit))
            {
                apex = Math.Max(apex, limit);
            }
        }

        // El limitador de subida solo deja aprovechar el 70 % del sitio que hay sobre el despegue.
        var here = FlightEnvelope.HighestCenter(world, body, from);
        if (!double.IsNegativeInfinity(here))
        {
            apex = Math.Max(apex, from.Y - 0.62 * (from.Y - here));
        }

        if (apex > higher - height * 1.2)
        {
            return false;
        }

        var gravity = ctx.Physics.Gravity * body.Scale;
        var launch = FlightPredictor.LaunchVelocity(from, aim, apex, gravity, ctx.Physics);
        if (Math.Abs(launch.X) > 650 * body.Scale)
        {
            return false;
        }

        if (!CoastIsClear(ctx, from, launch, aim.Y, target))
        {
            return false;
        }

        _apexY = apex;
        return true;
    }

    /// <summary>La parábola desde aquí (o sus próximos <paramref name="seconds"/>) no se sale de los monitores ni roza un suelo.</summary>
    private bool CoastIsClear(AutopilotContext ctx, Vec2 from, Vec2 velocity, double endY, Vec2 target, double seconds = 12)
    {
        var body = ctx.Body;
        _coastPath.Clear();
        FlightPredictor.Coast(from, velocity, endY, ctx.Physics.Gravity * body.Scale, ctx.Physics, _coastPath, seconds);
        return FlightEnvelope.PathIsClear(ctx.World, body, _coastPath, target.Y + body.HalfHeight);
    }

    private ThrustInput UpdateBallistic(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        switch (_phase)
        {
            case FlightPhase.Coast:
                return UpdateCoast(ctx, target);

            case FlightPhase.Brake:
                return UpdateBrake(ctx, target);

            case FlightPhase.Boost:
                break;

            default:
                // Despegue recto, y en cuanto está en el aire, el empujón.
                if (_airTime < 0.3)
                {
                    return Steer(ctx, new Vec2(0, -120 * scale));
                }

                _phase = FlightPhase.Boost;
                _stateTime = 0;
                _travelStartX = body.Position.X;
                _coastCheck = 0;
                ctx.Visual.Flash(Expression.Focused, 1.6);
                break;
        }

        // Empujón: hasta llevar la velocidad que dibuja la parábola desde donde está ahora.
        var gravity = ctx.Physics.Gravity * scale;
        var aim = BallisticAim(ctx, target);
        var apex = Math.Min(_apexY, Math.Min(body.Position.Y, aim.Y) - body.Height * 0.5);
        var wanted = FlightPredictor.LaunchVelocity(body.Position, aim, apex, gravity, ctx.Physics);

        // La parábola real no es exactamente la planeada: si la de ahora se sale de los monitores o roza un
        // suelo, mejor un crucero normal.
        _coastCheck -= ctx.Dt;
        if (_coastCheck <= 0)
        {
            _coastCheck = 0.1;
            if (!CoastIsClear(ctx, body.Position, wanted, aim.Y, target))
            {
                _style = FlightStyle.Cruise;
                _phase = FlightPhase.Transit;
                _cruiseY = PlanAltitude(ctx, FlightStyle.Cruise, _purpose, target);
                return UpdateTransit(ctx, target);
            }
        }

        var error = (body.Velocity - wanted).Length;
        if (error < Math.Max(14 * scale, wanted.Length * 0.06) || _stateTime > 2.5)
        {
            _phase = FlightPhase.Coast;
            _stateTime = 0;
            _coastCheck = 0;
            _coastSeconds = FlightPredictor.Coast(body.Position, body.Velocity, aim.Y, gravity, ctx.Physics).Seconds;
            _coastFlipPlanned = _flipPlanned && _coastSeconds > 3;
            _flipTurned = 0;
            _flipDirection = body.Velocity.X < 0 ? -1 : 1;
            ctx.Signals.Add(GameEventKind.EngineCut);
            ctx.Visual.Flash(Expression.Happy, Math.Min(3, _coastSeconds));
            return UpdateCoast(ctx, target);
        }

        return Steer(ctx, wanted, retractLegs: _airTime > 0.5, maxTilt: 0.85);
    }

    /// <summary>
    /// La parábola con el motor apagado: endereza la nave (o da una voltereta si hay tiempo) y, cada poco,
    /// prevé dónde se quedaría quieto si frenara ya. Enciende en el último instante en que la frenada aún
    /// termina sobre el destino.
    /// </summary>
    private ThrustInput UpdateCoast(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var config = ctx.Physics;
        var height = body.Height;
        ctx.Visual.LookTarget = target;

        _coastCheck -= ctx.Dt;
        if (_coastCheck <= 0)
        {
            _coastCheck = 0.05;
            var brakePoint = BrakePoint(ctx, target);
            var stop = FlightPredictor.StopPoint(FlightPredictor.CopyInto(_probe, body), config, BrakeTilt);
            var direction = Math.Sign(target.X - _travelStartX);
            var reached = direction * (stop.X - brakePoint.X) >= -height * 0.3;
            var tooLow = body.Velocity.Y > 0 && stop.Y >= brakePoint.Y - height * 0.2;
            var blocked = !CoastIsClear(ctx, body.Position, body.Velocity, BallisticAim(ctx, target).Y, target, 1.2);
            if (reached || tooLow || blocked || _stateTime > _coastSeconds + 1.5 || body.Fuel <= 0)
            {
                _phase = FlightPhase.Brake;
                _stateTime = 0;
                _coastFlipping = false;
                State = AutopilotState.Landing;
                ctx.Signals.Add(GameEventKind.Ignition);
                ctx.Visual.Flash(Expression.Focused, 2.5);
                return UpdateBrake(ctx, target);
            }
        }

        // Voltereta en plena parábola: con el motor apagado no hay nada más que hacer.
        if (_coastFlipPlanned && _stateTime > 0.25)
        {
            _flipTurned += body.AngularVelocity * ctx.Dt * _flipDirection;
            var omega = Math.Abs(body.AngularVelocity);
            var stopping = omega * omega / (2 * config.RotationAcceleration * 0.85);
            if (_flipTurned < Math.Tau - stopping - 0.05)
            {
                _coastFlipping = true;
                return new ThrustInput(Rotate: _flipDirection, RetractLegs: true, LandOn: ThrustInput.GroundOnly);
            }

            _coastFlipPlanned = false;
            _coastFlipping = false;
            _flipDone = true;
            ctx.Signals.Add(GameEventKind.Flipped);
        }

        return new ThrustInput(Rotate: FlightGuidance.RotateToward(body, config, 0), RetractLegs: true, LandOn: ThrustInput.GroundOnly);
    }

    /// <summary>Frenada final: inclinado contra el movimiento y a fondo, hasta quedar quieto sobre el destino.</summary>
    private ThrustInput UpdateBrake(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        var stopAt = BrakePoint(ctx, target);
        var delta = stopAt - body.Position;
        var distance = delta.Length;
        var speed = body.Velocity.Length;
        if ((distance < body.Height * 0.9 && speed < 45 * scale) || speed < 22 * scale || _stateTime > 6)
        {
            BeginApproach(ctx, target);
            return UpdateApproach(ctx, target);
        }

        var desired = distance > 1 ? delta / distance * Math.Min(MathUtil.BrakingSpeed(distance, 105 * scale), speed) : Vec2.Zero;
        return Steer(ctx, desired, retractLegs: false, maxTilt: BrakeTilt);
    }

    // Picado ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Sube a su altura de crucero y, cuando el destino queda bastante por debajo, se lanza en diagonal hacia un
    /// punto junto a él; en el último instante se encabrita (muy inclinado hacia atrás, motor a fondo) y frena.
    /// </summary>
    private ThrustInput UpdateSwoop(AutopilotContext ctx, Vec2 target)
    {
        if (_phase == FlightPhase.Flare)
        {
            return UpdateFlare(ctx, target);
        }

        var body = ctx.Body;
        var scale = body.Scale;
        var height = body.Height;
        if (_airTime < 0.45)
        {
            return Steer(ctx, new Vec2(0, -110 * scale));
        }

        var side = target.X < body.Position.X ? -1 : 1;
        var entry = BrakePoint(ctx, target) - new Vec2(side * height * 1.2, 0);
        var toEntry = entry - body.Position;
        if (!_diving)
        {
            // Hasta que el destino queda a unos 40° por debajo, crucero normal.
            if ((toEntry.Y < Math.Abs(toEntry.X) * 0.8 && Math.Abs(toEntry.X) > height * 2) || !LineIsClear(ctx, body.Position, entry, target))
            {
                return UpdateTransit(ctx, target);
            }

            _diving = true;
            _travelStartX = body.Position.X;
            _coastCheck = 0;
            ctx.Visual.Flash(Expression.Focused, 3);
        }

        _coastCheck -= ctx.Dt;
        if (_coastCheck <= 0)
        {
            _coastCheck = 0.05;
            var stop = FlightPredictor.StopPoint(FlightPredictor.CopyInto(_probe, body), ctx.Physics, FlareTilt);
            if (side * (stop.X - target.X) >= -height * 0.25 || stop.Y >= target.Y - height * 0.9)
            {
                _phase = FlightPhase.Flare;
                _stateTime = 0;
                State = AutopilotState.Landing;
                ctx.Signals.Add(GameEventKind.Ignition);
                return UpdateFlare(ctx, target);
            }
        }

        var distance = toEntry.Length;
        var speed = 310 * scale * (0.6 + 0.4 * Personality.Energy);
        return Steer(ctx, toEntry / Math.Max(1, distance) * speed, retractLegs: true, maxTilt: 0.75);
    }

    /// <summary>El tramo recto hasta <paramref name="to"/> no se sale de los monitores ni pasa rozando un suelo.</summary>
    private bool LineIsClear(AutopilotContext ctx, Vec2 from, Vec2 to, Vec2 target)
    {
        var body = ctx.Body;
        var steps = Math.Clamp((int)(Vec2.Distance(from, to) / (body.Height * 0.5)), 2, 80);
        _coastPath.Clear();
        for (var i = 1; i <= steps; i++)
        {
            _coastPath.Add(Vec2.Lerp(from, to, (double)i / steps));
        }

        return FlightEnvelope.PathIsClear(ctx.World, body, _coastPath, target.Y + body.HalfHeight);
    }

    /// <summary>Encabritado: se inclina hacia atrás a fondo hasta matar la velocidad, y luego aterriza.</summary>
    private ThrustInput UpdateFlare(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        var height = body.Height;
        if ((Math.Abs(body.Velocity.X) < 35 * scale && Math.Abs(body.Velocity.Y) < 60 * scale) || _stateTime > 5)
        {
            BeginApproach(ctx, target);
            return UpdateApproach(ctx, target);
        }

        var hold = BrakePoint(ctx, target);
        var desired = new Vec2(
            Math.Clamp((target.X - body.Position.X) * 1.4, -40 * scale, 40 * scale),
            Math.Clamp((hold.Y - body.Position.Y) * 1.5, -60 * scale, 60 * scale));
        return Steer(ctx, desired, retractLegs: false, maxTilt: FlareTilt);
    }
}
