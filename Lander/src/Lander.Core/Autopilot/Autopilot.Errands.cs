using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;

namespace Lander.Core.Autopilot;

/// <summary>
/// Encargos del asistente (las maniobras de K-Desktop): volar deprisa a un punto en el aire y quedarse ahí, mirando
/// lo que toque, hasta que se le suelte. Al soltarlo se posa en lo que le digan (la ventana que acaba de traer delante)
/// o en lo que tenga debajo. Mientras dura no explora, no esquiva ni se duerme: tiene prisa.
/// </summary>
public sealed partial class Autopilot
{
    /// <summary>Un encargo sin noticias durante este tiempo se da por abandonado (el asistente siempre lo suelta).</summary>
    private const double ErrandTimeout = 30;

    /// <summary>Lo que dura un fallo de motor fingido (la reacción de «algo ha ido mal»).</summary>
    private const double StallSeconds = 0.38;

    private Vec2 _errandPoint;
    private double _errandSpeed = 1;
    private double _errandRenewed;
    private double _stall;
    private bool _errandFlip;

    /// <summary>Haciendo un encargo: volando hacia su punto o esperando en él.</summary>
    public bool OnErrand => State == AutopilotState.Flying && _purpose == FlightPurpose.Errand;

    /// <summary>Ya está en su punto, casi quieto.</summary>
    public bool ErrandArrived { get; private set; }

    /// <summary>Adónde mira mientras hace el encargo (el botón al que apunta); null, al punto.</summary>
    public Vec2? ErrandLook { get; set; }

    /// <summary>
    /// Empieza un encargo (o cambia el punto del que lleva): despega si está posado y vuela a <paramref name="point"/>
    /// con la rapidez dada (1 = normal; más, más brusco).
    /// </summary>
    public void BeginErrand(AutopilotContext ctx, Vec2 point, double speed)
    {
        var body = ctx.Body;
        if (!OnErrand)
        {
            WakeFromNap(ctx);
            _pendingDrop = null;
            _sleepAfterLanding = false;
            _perchCandidate = null;
            StartFlight(ctx, DestinationPicker.PickBelow(ctx.World, body, point), FlightPurpose.Errand, AutopilotState.Flying);
            _style = FlightStyle.Cruise;
            _flipPlanned = false;
            _mistake = PilotMistake.None;
        }

        _errandPoint = ClampToScreen(ctx, point, body.Height * 1.2);
        _errandSpeed = Math.Clamp(speed, 0.3, 3);
        _errandRenewed = ctx.Time;
        ErrandArrived = false;
    }

    /// <summary>
    /// Termina el encargo: se posa en <paramref name="landOn"/> (si llega) o en lo que tenga debajo, con calma.
    /// </summary>
    public void EndErrand(AutopilotContext ctx, Destination? landOn = null)
    {
        ErrandLook = null;
        ErrandArrived = false;
        _errandFlip = false;
        _stall = 0;
        if (!OnErrand)
        {
            return;
        }

        if (_phase == FlightPhase.Flip)
        {
            EndFlip(ctx);
        }

        var body = ctx.Body;
        var target = landOn is not null && IsReachable(ctx, landOn) ? landOn : DestinationPicker.PickBelow(ctx.World, body, body.Position);
        Retarget(ctx, target, landOn is not null && target == landOn ? FlightPurpose.Perch : FlightPurpose.Recover);
        _airborneSeen = body.IsFlying;
        _celebrated = true;
    }

    /// <summary>Una voltereta en cuanto haya sitio (la celebración de un encargo bien hecho).</summary>
    public void ErrandFlip() => _errandFlip = true;

    /// <summary>El motor «se cala» un instante: cae un poco y lo recupera (la reacción de un fallo).</summary>
    public void Stall() => _stall = StallSeconds;

    /// <summary>Haciendo el encargo: fuera de sus manos van la voltereta y el fallo de motor; el resto, al punto.</summary>
    private ThrustInput UpdateErrand(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var scale = body.Scale;
        if (ctx.Time - _errandRenewed > ErrandTimeout)
        {
            EndErrand(ctx);
            return Steer(ctx, Vec2.Zero);
        }

        if (body.IsLanded)
        {
            // Despega ya, sin calentar el motor.
            ctx.Visual.Crouch = 0;
            return new ThrustInput(Throttle: 1, Stabilize: true, LandOn: ThrustInput.GroundOnly);
        }

        _airborneSeen = true;
        if (_stall > 0)
        {
            _stall -= ctx.Dt;
            return new ThrustInput(Rotate: FlightGuidance.RotateToward(body, ctx.Physics, 0.25 * Math.Sin(ctx.Time * 30)), LandOn: ThrustInput.GroundOnly);
        }

        if (_phase == FlightPhase.Flip)
        {
            return UpdateFlip(ctx);
        }

        if (_errandFlip)
        {
            _flipPlanned = true;
            _flipDone = false;
            _airTime = Math.Max(_airTime, 1.2);
            var started = TryStartFlip(ctx);
            _errandFlip = false;
            _flipPlanned = false;
            if (started)
            {
                return UpdateFlip(ctx);
            }
        }

        var point = _errandPoint;
        var delta = point - body.Position;
        ErrandArrived = delta.Length < body.Height && body.Velocity.Length < 90 * scale;
        ctx.Visual.LookTarget = ErrandLook ?? point;

        // Más deprisa que un paseo (es un recado), frenando a tiempo con lo que de verdad da la nave: de lado, lo que
        // empuja inclinada (y tarda en girar al otro lado); subiendo, solo la gravedad; bajando, el motor.
        var speed = _errandSpeed;
        var gravity = ctx.Physics.Gravity * scale;
        var up = delta.Y < 0;
        var vy = Math.Sign(delta.Y) * Math.Min(380 * scale * speed,
            Approach(delta.Y, up ? gravity * 0.7 : gravity * 0.9, body.Velocity.Y, 0.25));

        // Cerca, nivelado y con los propulsores de traslación, como al atracar: llega justo al punto sin bambolearse.
        if (Math.Abs(delta.X) < body.Height * 3.5 && Math.Abs(body.Velocity.X) < 160 * scale)
        {
            var near = Math.Sign(delta.X) * Approach(delta.X, ctx.Physics.LateralThrust * scale * 0.55, body.Velocity.X, 0.15);
            return SteerLevel(ctx, new Vec2(near, vy));
        }

        var vx = Math.Sign(delta.X) * Math.Min(560 * scale * speed,
            MathUtil.ApproachSpeed(Math.Max(0, Math.Abs(delta.X) - body.Height * 2.5), gravity * 0.55, body.Velocity.X, 0.6));
        return Steer(ctx, new Vec2(vx, vy));
    }

    /// <summary>Como <see cref="MathUtil.ApproachSpeed"/>, pero con una llegada final más decidida (tiene prisa).</summary>
    private static double Approach(double distance, double deceleration, double currentSpeed, double lag)
    {
        var usable = Math.Max(0, Math.Abs(distance) - Math.Abs(currentSpeed) * lag);
        return Math.Min(Math.Sqrt(2 * deceleration * usable), Math.Abs(distance) * 3.5);
    }
}
