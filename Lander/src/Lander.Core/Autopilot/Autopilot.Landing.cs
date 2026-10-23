using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Terrain;

namespace Lander.Core.Autopilot;

/// <summary>
/// El aterrizaje: aproximación prudente (a veces con un reconocimiento previo de la zona, y nivelada en el
/// vuelo de precisión), bajada balanceándose como una hoja o caída libre con frenada en el último instante.
/// </summary>
public sealed partial class Autopilot
{
    private bool _leaf;
    private double _leafTime;
    private double _poiseDuration;
    private bool _finalDescent;
    private double _surveyTime;
    private double _surveyClock;

    private void BeginApproach(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        State = AutopilotState.Landing;
        _stateTime = 0;
        _phase = FlightPhase.Approach;
        var drop = target.Y - body.Position.Y;
        _leaf = _style == FlightStyle.Lazy && drop > body.Height * 4 && drop < body.Height * 12
            && body.Fuel > FuelPlanner.HoverBurn(body, ctx.Physics) * (drop / (55 * body.Scale) + 6) + FuelPlanner.Reserve(body);
        _leafTime = 0;
        _finalDescent = false;

        // Antes de posarse en un sitio nuevo, a veces se queda un momento encima mirando a un lado y a otro.
        var curiosity = _purpose switch
        {
            FlightPurpose.Investigate or FlightPurpose.Perch => 0.55,
            FlightPurpose.Explore when _destination?.Kind == PlatformKind.Content => 0.5,
            FlightPurpose.Explore => 0.12 + 0.15 * Personality.Curiosity,
            _ => 0,
        };
        _surveyTime = !_leaf && body.FuelFraction > 0.35 && random.Chance(curiosity) ? random.Range(1.6, 2.6) : 0;
        _surveyClock = 0;
    }

    private ThrustInput UpdateLanding(AutopilotContext ctx)
    {
        var body = ctx.Body;
        if (body.IsLanded)
        {
            if (_airborneSeen)
            {
                if (!TryNextHop(ctx))
                {
                    OnArrived(ctx);
                }

                return ThrustInput.None;
            }

            _flightTime += ctx.Dt;
            return Takeoff(ctx);
        }

        _airborneSeen = true;
        _airTime += ctx.Dt;
        _flightTime += ctx.Dt;
        _totalFlightTime += ctx.Dt;
        if (_destination is null || !_destination.TryResolve(ctx.World, body, out var target, out _))
        {
            Replan(ctx);
            State = _purpose == FlightPurpose.ReturnToBase ? AutopilotState.ReturningToBase : AutopilotState.Flying;
            return Steer(ctx, Vec2.Zero);
        }

        // Una aproximación que no termina (algo raro en el terreno): mejor posarse en lo que haya debajo.
        if (_stateTime > 25 && _purpose != FlightPurpose.Recover)
        {
            Retarget(ctx, DestinationPicker.PickBelow(ctx.World, body, body.Position, BrakingDistance(body, ctx.Physics)), FlightPurpose.Recover);
            State = AutopilotState.Flying;
            return Steer(ctx, Vec2.Zero);
        }

        // Mientras llegaba, otro asistente se ha quedado justo ahí: al lado (o a otra parte si no cabe).
        if (_purpose != FlightPurpose.Recover && _destination.TryResolve(ctx.World, body, out _, out var segment)
            && DestinationPicker.IsTaken(body, segment.Y, target.X, ctx.Occupied))
        {
            if (DestinationPicker.FreeX(body, segment, target.X, ctx.Occupied) is { } free)
            {
                _destination = Destination.On(segment, free);
                target = new Vec2(free, target.Y);
            }
            else
            {
                Replan(ctx);
                State = _purpose == FlightPurpose.ReturnToBase ? AutopilotState.ReturningToBase : AutopilotState.Flying;
                return Steer(ctx, Vec2.Zero);
            }
        }

        ctx.Visual.LookTarget = target + new Vec2(0, body.Height);
        if (_phase == FlightPhase.Transit && IsShortHop(ctx, target))
        {
            return UpdateHop(ctx, target);
        }

        return _phase switch
        {
            FlightPhase.Poise => UpdatePoise(ctx, target),
            FlightPhase.Drop => UpdateDrop(ctx, target),
            FlightPhase.Burn => UpdateBurn(ctx, target),
            FlightPhase.Coast => UpdateCoast(ctx, target),
            FlightPhase.Brake => UpdateBrake(ctx, target),
            FlightPhase.Flare => UpdateFlare(ctx, target),
            _ => UpdateApproach(ctx, target),
        };
    }

    /// <summary>Aproximación prudente: centrarse, estabilizar en estacionario y bajar despacio.</summary>
    private ThrustInput UpdateApproach(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        var height = body.Height;
        var dx = target.X - body.Position.X;
        var heightAbove = target.Y - body.Position.Y;

        // Con el despiste de altura cree que el destino está más abajo de lo que está y frena tarde.
        var perceived = heightAbove + (_mistake == PilotMistake.FastTouchdown ? height * (0.6 + 4 * _mistakeSeverity) : 0);

        // La plataforma ha subido por encima: volver a ganar altura.
        if (heightAbove < -height * 0.3)
        {
            _cruiseY = ReachableCruise(ctx, target, target.Y - height * 1.5);
            _phase = FlightPhase.Transit;
            _style = _style is FlightStyle.HoverSlam or FlightStyle.Arc or FlightStyle.Ballistic or FlightStyle.Swoop or FlightStyle.Hopper
                ? FlightStyle.Cruise
                : _style;
            State = _purpose == FlightPurpose.ReturnToBase ? AutopilotState.ReturningToBase : AutopilotState.Flying;
            return Steer(ctx, new Vec2(Math.Clamp(dx, -40 * scale, 40 * scale), -80 * scale));
        }

        var vxNow = Math.Abs(body.Velocity.X);
        double vx;
        double vy;
        double maxTilt;
        if (_leaf && heightAbove > height * 1.5)
        {
            // Como una hoja: balanceándose de lado a lado mientras baja.
            _leafTime += ctx.Dt;
            var amplitude = height * 1.1 * Math.Clamp((heightAbove - height * 1.5) / (height * 3), 0, 1);
            var offset = amplitude * Math.Sin(_leafTime * 2.1);
            vx = Math.Clamp((dx + offset) * 2.2, -75 * scale, 75 * scale);
            vy = 55 * scale;
            maxTilt = 0.4;
        }
        else if (_surveyTime > 0 && Math.Abs(dx) < height * 0.6 && heightAbove > height * 0.9 && heightAbove < height * 2.6)
        {
            // Reconocimiento: quieto un poco por encima, mirando la zona de un lado a otro.
            _surveyTime -= ctx.Dt;
            _surveyClock += ctx.Dt;
            var sweep = Math.Sin(_surveyClock * 2.3) * height * 2.6;
            ctx.Visual.LookTarget = new Vec2(target.X + sweep, target.Y + height);
            ctx.Visual.Flash(Expression.Curious, 0.4);
            vx = Math.Clamp(dx * 1.5, -25 * scale, 25 * scale);
            vy = Math.Clamp((target.Y - height * 1.7 - body.Position.Y) * 2, -45 * scale, 45 * scale);
            maxTilt = 0.2;
        }
        else if (Math.Abs(dx) > height * 0.9)
        {
            vx = Math.Clamp(dx * 1.2, -60 * scale, 60 * scale);
            vy = 0;
            maxTilt = 0.35;
        }
        else if (perceived > height * 1.5)
        {
            vx = Math.Clamp(dx, -40 * scale, 40 * scale);
            var hurry = _mistake == PilotMistake.FastTouchdown ? 1 + 2 * _mistakeSeverity : 1;
            vy = Math.Clamp(MathUtil.BrakingSpeed(perceived - height * 1.2, 55 * scale * hurry), 28 * scale, 160 * scale * hurry);
            maxTilt = 0.3;
        }
        else if ((Math.Abs(dx) > height * 0.15 || vxNow > 22 * scale || Math.Abs(body.Angle) > 0.12) && !(_finalDescent && _mistake == PilotMistake.Wobble))
        {
            vx = Math.Clamp(dx * 1.5, -30 * scale, 30 * scale);
            vy = heightAbove < height * 0.4 ? -25 * scale : 0;
            maxTilt = 0.22;
        }
        else
        {
            // Contacto a unos 38 px/s: suave y muy por debajo del límite de un aterrizaje perfecto (70).
            vx = Math.Clamp(dx, -12 * scale, 12 * scale);
            vy = ContactSpeed(38, scale);
            _finalDescent = true;
            maxTilt = 0.08;
            if (_mistake == PilotMistake.Wobble)
            {
                // Se tambalea: corrige de más de un lado a otro justo antes de tocar.
                vx += Math.Sin(_stateTime * 7) * 95 * _mistakeSeverity * scale;
                maxTilt += 0.55 * _mistakeSeverity;
            }
        }

        // En el vuelo de precisión, la aproximación también es nivelada (salvo si se tambalea).
        return _style == FlightStyle.Precision && !(_mistake == PilotMistake.Wobble && _finalDescent)
            ? SteerLevel(ctx, new Vec2(vx, vy))
            : Steer(ctx, new Vec2(vx, vy), retractLegs: false, maxTilt);
    }

    /// <summary>Quieto sobre el destino, mirando abajo, antes de apagar el motor.</summary>
    private ThrustInput UpdatePoise(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        var height = body.Height;
        var dx = target.X - body.Position.X;
        if (target.Y - body.Position.Y < height * 5)
        {
            // El destino se ha acercado (la ventana ha subido): mejor una aproximación normal.
            _phase = FlightPhase.Approach;
            return UpdateApproach(ctx, target);
        }

        if (_stateTime >= _poiseDuration && Math.Abs(dx) < height * 0.3 && Math.Abs(body.Velocity.X) < 15 * scale
            && Math.Abs(body.Velocity.Y) < 25 * scale && Math.Abs(body.Angle) < 0.05)
        {
            _phase = FlightPhase.Drop;
            _stateTime = 0;
            ctx.Signals.Add(GameEventKind.EngineCut);
            return UpdateDrop(ctx, target);
        }

        var vx = Math.Clamp(dx * 1.6, -30 * scale, 30 * scale);
        var vy = Math.Clamp((_cruiseY - body.Position.Y) * 2, -50 * scale, 50 * scale);
        return Steer(ctx, new Vec2(vx, vy), retractLegs: false, maxTilt: 0.25);
    }

    /// <summary>Caída libre con el motor apagado hasta el último instante en que aún puede frenar.</summary>
    private ThrustInput UpdateDrop(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var height = body.Height;
        var remaining = target.Y - body.Position.Y;
        var required = RequiredDeceleration(body, remaining);
        var available = AvailableDeceleration(body, ctx.Physics);
        if (required >= available * IgnitionThreshold || remaining < height * 0.8 || Math.Abs(target.X - body.Position.X) > height * 1.2 || body.Fuel <= 0)
        {
            _phase = FlightPhase.Burn;
            _stateTime = 0;
            ctx.Signals.Add(GameEventKind.Ignition);
            ctx.Visual.Flash(Expression.Focused, 2);
            return UpdateBurn(ctx, target);
        }

        return new ThrustInput(Stabilize: true, LandOn: _destination!.OwnerId);
    }

    /// <summary>Frenada final: el empuje justo para llegar al destino a velocidad de contacto suave.</summary>
    private ThrustInput UpdateBurn(AutopilotContext ctx, Vec2 target)
    {
        var body = ctx.Body;
        var config = ctx.Physics;
        var scale = body.Scale;
        var height = body.Height;
        var gravity = config.Gravity * scale;
        var remaining = target.Y - body.Position.Y;
        var vy = body.Velocity.Y;
        var touchdown = SlamTouchdownSpeed * scale;

        if (vy < touchdown * 0.6 && remaining > height * 1.3)
        {
            // Ha frenado demasiado arriba: termina con una aproximación normal.
            _phase = FlightPhase.Approach;
            return UpdateApproach(ctx, target);
        }

        double accelerationUp;
        if (vy > touchdown)
        {
            accelerationUp = gravity + RequiredDeceleration(body, remaining) - config.LinearDrag * vy;
        }
        else
        {
            accelerationUp = gravity + (vy - touchdown) * 2.4 - config.LinearDrag * vy;
        }

        var dx = target.X - body.Position.X;
        var desiredVx = Math.Clamp(dx * 1.5, -25 * scale, 25 * scale);
        var lateral = (desiredVx - body.Velocity.X) * 2.4;
        var desiredAngle = Math.Clamp(Math.Atan2(lateral, Math.Max(accelerationUp, gravity * 0.2)), -0.12, 0.12);
        var maxThrust = config.MainThrust * scale * FuelPlanner.ThrustFactor(body, config) * Math.Max(0.3, Math.Cos(body.Angle));
        return new ThrustInput(
            Throttle: Math.Clamp(accelerationUp / maxThrust, 0, 1),
            Rotate: FlightGuidance.RotateToward(body, config, desiredAngle),
            LandOn: _destination!.OwnerId);
    }

    /// <summary>Deceleración necesaria para llegar a <paramref name="remaining"/> píxeles a velocidad de contacto.</summary>
    private static double RequiredDeceleration(LanderBody body, double remaining)
    {
        var vy = body.Velocity.Y;
        var touchdown = SlamTouchdownSpeed * body.Scale;
        if (vy <= touchdown)
        {
            return 0;
        }

        var distance = Math.Max(body.Height * 0.05, remaining - body.Height * 0.1);
        return (vy * vy - touchdown * touchdown) / (2 * distance);
    }

    /// <summary>Lo que puede frenar con el motor a tope (empuje menos gravedad).</summary>
    private static double AvailableDeceleration(LanderBody body, PhysicsConfig config) =>
        (config.MainThrust * FuelPlanner.ThrustFactor(body, config) * Math.Cos(body.Angle) - config.Gravity) * body.Scale;
}
