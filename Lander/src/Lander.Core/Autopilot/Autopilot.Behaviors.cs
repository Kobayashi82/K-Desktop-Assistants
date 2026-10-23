using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Terrain;

namespace Lander.Core.Autopilot;

/// <summary>
/// Lo que hace el lander por su cuenta mientras reposa: pasear por la barra de título, asomarse al borde
/// y tirarse, echar una siesta, dejarse acariciar, saludar, acompañar a la ventana activa o visitar el
/// reloj a las horas en punto.
/// </summary>
public sealed partial class Autopilot
{
    private Destination? _pendingDrop;
    private int _strollStreak;
    private Vec2 _peekAt;
    private bool _napping;
    private double _napBubbleTimer;
    private double _petTime;
    private double _petCooldown;
    private bool _userWasAway;
    private double _greetAt;
    private double _greetCooldown;
    private long? _perchCandidate;
    private double _perchAt;
    private double _perchCooldown = 30;
    private long _clockVisited = -1;
    private double _idleTimer = 9;
    private bool _riding;

    /// <summary>Echando una siesta fuera de la base.</summary>
    public bool IsNapping => _napping;

    private void UpdateCooldowns(double dt)
    {
        _petCooldown -= dt;
        _greetCooldown -= dt;
        _perchCooldown -= dt;
    }

    // Siesta ------------------------------------------------------------------------------------------

    /// <summary>Muy cansado y sin nadie cerca: cierra los ojos un rato donde esté.</summary>
    private void UpdateNap(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var sensors = ctx.Sensors;
        var cursorNear = sensors.Cursor is { } cursor && Vec2.Distance(cursor, body.Position) < body.Height * 3;
        if (!_napping)
        {
            if (Personality.Energy < 0.22 && !body.IsOnBase && _stateTime > 8 && sensors.UserIdleSeconds > 20
                && !cursorNear && _pendingDrop is null)
            {
                _napping = true;
                _napBubbleTimer = 1;
                ctx.Visual.Flash(Expression.Tired, 1.5);
                ctx.Visual.StartYawn();
            }

            return;
        }

        ctx.Visual.SetBaseExpression(Expression.Asleep);
        ctx.Visual.LookTarget = null;
        _napBubbleTimer -= ctx.Dt;
        if (_napBubbleTimer <= 0)
        {
            ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Sleep, 3));
            _napBubbleTimer = random.Range(12, 20);
        }

        if (Personality.Energy > 0.55 || cursorNear)
        {
            WakeFromNap(ctx);
        }
    }

    private void WakeFromNap(AutopilotContext ctx)
    {
        if (!_napping)
        {
            return;
        }

        _napping = false;
        ctx.Visual.SetBaseExpression(Expression.Neutral);
        ctx.Visual.Flash(Expression.Tired, 1.8);
        ctx.Visual.StartStretch();
        if (ctx.Visual.Bubble is { Icon: BubbleIcon.Sleep })
        {
            ctx.Visual.ClearBubble();
        }

        _restDuration = Math.Max(_restDuration, _stateTime + random.Range(5, 15));
    }

    // Caricias, saludos y paseos en ventana ----------------------------------------------------------------

    /// <summary>Pasar el cursor despacio por encima del lander: le encanta.</summary>
    private void UpdatePetting(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var sensors = ctx.Sensors;
        var speed = sensors.CursorVelocity.Length;
        var over = sensors.Cursor is { } cursor && Vec2.Distance(cursor, body.Position) < body.Height * 0.75;
        if (over && speed > 15 * body.Scale && speed < 350 * body.Scale)
        {
            _petTime += ctx.Dt;
            _dodgeCooldown = Math.Max(_dodgeCooldown, 1);
        }
        else
        {
            _petTime = Math.Max(0, _petTime - ctx.Dt * 0.5);
        }

        if (_petTime > 0.9 && _petCooldown <= 0)
        {
            _petTime = 0;
            _petCooldown = 5;
            WakeFromNap(ctx);
            ctx.Visual.Flash(Expression.Happy, 2.2);
            ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Heart, 1.6));
            ctx.Visual.StartBounce();
            ctx.Signals.Add(GameEventKind.Petted);
            Personality.Energy = Math.Min(1, Personality.Energy + 0.05);
            _dodgeCooldown = Math.Max(_dodgeCooldown, 4);
        }
    }

    /// <summary>Cuando el usuario vuelve tras un rato fuera (o al despertarse), saluda.</summary>
    private void UpdateGreeting(AutopilotContext ctx)
    {
        var idle = ctx.Sensors.UserIdleSeconds;
        if (idle > 90)
        {
            _userWasAway = true;
            return;
        }

        var back = _userWasAway && idle < 1.5;
        var awake = _greetAt > 0 && ctx.Time >= _greetAt;
        if (!back && !awake)
        {
            return;
        }

        _userWasAway = false;
        _greetAt = 0;
        if (_greetCooldown > 0)
        {
            return;
        }

        _greetCooldown = 60;
        WakeFromNap(ctx);
        ctx.Visual.Flash(Expression.Happy, 2);
        ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Hello, 2));
        ctx.Visual.StartWave();
        ctx.Signals.Add(GameEventKind.Greeted);
    }

    /// <summary>Si la ventana sobre la que está se mueve deprisa, lo vive (o lo sufre) según su carácter.</summary>
    private void UpdateRide(AutopilotContext ctx)
    {
        if (ctx.Body.Support is not { } support)
        {
            _riding = false;
            return;
        }

        var speed = support.Velocity.Length / ctx.Body.Scale;
        if (!_riding && speed > 260)
        {
            _riding = true;
            WakeFromNap(ctx);
            ctx.Visual.Flash(Personality.Shyness < 0.6 ? Expression.Happy : Expression.Scared, 1.6);
        }
        else if (_riding && speed < 80)
        {
            _riding = false;
        }
    }

    /// <summary>Gestos sueltos mientras espera: bostezar si está cansado, estirarse, un meneo alegre.</summary>
    private void UpdateIdleAnimations(AutopilotContext ctx)
    {
        _idleTimer -= ctx.Dt;
        if (_idleTimer > 0)
        {
            return;
        }

        _idleTimer = random.Range(7, 18);
        if (_napping)
        {
            return;
        }

        var roll = random.NextDouble();
        if (Personality.Energy < 0.35 && roll < 0.45)
        {
            ctx.Visual.StartYawn();
        }
        else if (roll < 0.22)
        {
            ctx.Visual.StartStretch();
        }
        else if (roll < 0.36 && Personality.Energy > 0.5)
        {
            ctx.Visual.StartWiggle();
        }
    }

    // Ventana activa y reloj ---------------------------------------------------------------------------

    private void OnWindowActivated(long handle, AutopilotContext ctx)
    {
        if (State != AutopilotState.Resting || _perchCooldown > 0 || handle == ctx.Body.Support?.OwnerId || !ctx.World.HasOwner(handle))
        {
            return;
        }

        _perchCandidate = handle;
        _perchAt = ctx.Time + random.Range(1.2, 3);
    }

    /// <summary>A veces acompaña al usuario y se posa sobre la ventana que acaba de activar.</summary>
    private bool TryPerch(AutopilotContext ctx)
    {
        if (_perchCandidate is not { } handle || ctx.Time < _perchAt)
        {
            return false;
        }

        _perchCandidate = null;
        var body = ctx.Body;
        if (!CanExplore(ctx) || handle == body.Support?.OwnerId || !random.Chance(0.18 + 0.3 * Personality.Curiosity))
        {
            return false;
        }

        if (DestinationPicker.PickOnOwner(ctx.World, body, handle, random, ctx.Occupied) is not { } perch || !IsReachable(ctx, perch)
            || ctx.Sensors.FullscreenMonitors.Contains(perch.MonitorIndex))
        {
            return false;
        }

        _perchCooldown = 120;
        _pendingDrop = null;
        WakeFromNap(ctx);
        BeginChoosing(ctx, perch, FlightPurpose.Perch, BubbleIcon.Arrow);
        return true;
    }

    /// <summary>A las horas en punto visita el reloj de la barra de tareas (si hay alguien delante).</summary>
    private bool TryVisitClock(AutopilotContext ctx)
    {
        if (!ctx.VisitClock || ctx.Sensors.LocalTime is not { } now || now.Minute != 0)
        {
            return false;
        }

        var key = now.Ticks / TimeSpan.TicksPerHour;
        if (key == _clockVisited)
        {
            return false;
        }

        _clockVisited = key;
        var body = ctx.Body;
        if (!CanExplore(ctx) || ctx.Sensors.UserIdleSeconds > 120 || ctx.Sensors.QuietMode
            || ctx.World.PrimaryMonitor is not { } primary || ctx.World.PrimaryGround is not { } ground)
        {
            return false;
        }

        WakeFromNap(ctx);
        var margin = DestinationPicker.LandingMargin(body);
        var taskbarBelow = !primary.WorkArea.IsEmpty && primary.WorkArea.Bottom < primary.Bounds.Bottom;
        var x = ground.Right - margin - body.Height * 0.4;
        if (!taskbarBelow || (body.IsOnBase && body.Support?.OwnerId == ground.OwnerId && Math.Abs(body.Position.X - x) < body.Height * 2))
        {
            ChimeHere(ctx);
            return true;
        }

        var clock = Destination.On(ground, x);
        if (!IsReachable(ctx, clock))
        {
            ChimeHere(ctx);
            return true;
        }

        _pendingDrop = null;
        BeginChoosing(ctx, clock, FlightPurpose.Clock, BubbleIcon.Clock);
        return true;
    }

    private static void ChimeHere(AutopilotContext ctx)
    {
        ctx.Visual.Flash(Expression.Happy, 2.5);
        ctx.Visual.ShowBubble(new Bubble(BubbleIcon.Clock, 2.5));
        ctx.Visual.CelebrateTime = 1.5;
        ctx.Visual.StartWiggle();
        ctx.Signals.Add(GameEventKind.HourChime);
    }

    // Paseos y saltos al vacío --------------------------------------------------------------------------

    /// <summary>
    /// Al acabar el descanso, a veces no se va lejos: da un paseo por la misma barra de título o se asoma al
    /// borde para tirarse y frenar en el último instante.
    /// </summary>
    private bool TryIdleTrip(AutopilotContext ctx)
    {
        var body = ctx.Body;
        if (!CanExplore(ctx) || body.Support is not { } support)
        {
            _pendingDrop = null;
            return false;
        }

        if (_pendingDrop is { } drop)
        {
            _pendingDrop = null;
            if (IsReachable(ctx, drop))
            {
                ctx.Visual.Flash(Expression.Happy, 1.5);
                StartFlight(ctx, drop, FlightPurpose.EdgeDrop, AutopilotState.Flying);
                return true;
            }

            return false;
        }

        if (ctx.World.FindSegment(support.OwnerId, body.Position.X) is not { } platform)
        {
            return false;
        }

        var roll = random.NextDouble();
        if (platform.Kind == PlatformKind.Window && Personality.Energy > 0.45 && body.FuelFraction > 0.5 && roll < 0.16 * Math.Min(1.5, ctx.Acrobatics * 2)
            && TryPlanEdgeDrop(ctx, platform))
        {
            return true;
        }

        // Tras un paseo el descanso es corto, así que sin freno se encadenaban muchos seguidos: el segundo
        // es menos probable y después siempre toca ir a otro sitio.
        var height = body.Height;
        var strollChance = _strollStreak switch
        {
            0 => 0.38,
            1 => 0.15,
            _ => 0,
        };

        if (platform.Width > height * 8 && roll < strollChance)
        {
            var margin = DestinationPicker.LandingMargin(body);
            var hop = height * random.Range(2, 5) * random.Sign();
            var x = body.Position.X + hop;
            if (!platform.ContainsX(x, margin + body.Height * 0.4))
            {
                x = body.Position.X - hop;
            }

            if (platform.ContainsX(x, margin + body.Height * 0.4))
            {
                ctx.Visual.LookTarget = new Vec2(x, platform.Y);
                StartFlight(ctx, Destination.On(platform, x), FlightPurpose.Stroll, AutopilotState.Flying);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Vuelo panorámico: sube, recorre el monitor pasando por varios puntos en el aire y se posa en otro
    /// punto del suelo. Es lo que hace cuando no hay ventanas a las que ir (o, a veces, por gusto).
    /// </summary>
    private bool TrySightsee(AutopilotContext ctx)
    {
        var body = ctx.Body;
        var world = ctx.World;
        if (!CanExplore(ctx) || body.FuelFraction < 0.5 || body.Damage > 0.4
            || (world.MonitorAt(body.Position) ?? world.NearestMonitor(body.Position)) is not { } monitor
            || ctx.Sensors.FullscreenMonitors.Contains(monitor.Index) || world.GroundOf(monitor.Index) is not { } ground)
        {
            return false;
        }

        var height = body.Height;
        var area = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
        var margin = height * 2.6;
        var top = FlightEnvelope.HighestCenter(world, body, new Vec2(area.Center.X, area.Top + height), 1.1);
        var bottom = ground.Y - height * 3;
        if (double.IsNegativeInfinity(top) || bottom - top < height * 2 || area.Width < height * 10)
        {
            return false;
        }

        // A veces, en lugar de recorrer el monitor, un paseo a saltitos por la barra de tareas.
        var landingMargin = DestinationPicker.LandingMargin(body) + height * 0.4;
        if (body.IsOnBase && body.Support?.OwnerId == ground.OwnerId && random.Chance(0.3))
        {
            var side = random.Sign();
            for (var i = 0; i < 2; i++, side = -side)
            {
                var x = body.Position.X + side * height * random.Range(6.5, 13);
                if (ground.ContainsX(x, landingMargin) && Destination.On(ground, x) is { } along && IsReachable(ctx, along))
                {
                    _plannedTour.Clear();
                    BeginChoosing(ctx, along, FlightPurpose.Sightsee, BubbleIcon.Star);
                    return true;
                }
            }
        }

        // Destino: otro punto del suelo, bien lejos de donde está.
        double? landX = null;
        for (var i = 0; i < 6 && landX is null; i++)
        {
            var x = random.Range(ground.Left + landingMargin, ground.Right - landingMargin);
            if (Math.Abs(x - body.Position.X) > Math.Max(height * 6, ground.Width * 0.25))
            {
                landX = x;
            }
        }

        if (landX is not { } destinationX || Destination.On(ground, destinationX) is not { } destination || !IsReachable(ctx, destination))
        {
            return false;
        }

        // Dos a cuatro puntos en zigzag por el monitor, a distintas alturas.
        _plannedTour.Clear();
        var count = 2 + random.Next(3);
        var lastX = body.Position.X;
        var length = 0.0;
        var last = body.Position;
        for (var i = 0; i < count; i++)
        {
            var x = lastX;
            for (var attempt = 0; attempt < 6 && Math.Abs(x - lastX) < area.Width * 0.22; attempt++)
            {
                x = random.Range(area.Left + margin, area.Right - margin);
            }

            var point = new Vec2(x, random.Range(top, MathUtil.Lerp(top, bottom, 0.75)));
            length += Vec2.Distance(point, last);
            _plannedTour.Add(point);
            last = point;
            lastX = x;
        }

        // Combustible para todo el recorrido y para volver a la base después.
        var target = new Vec2(destinationX, ground.Y - body.HalfHeight);
        var seconds = 8 + (length + Vec2.Distance(last, target)) / (140 * body.Scale);
        var needed = seconds * FuelPlanner.HoverBurn(body, ctx.Physics) * 1.3;
        if (body.Fuel - needed < FuelPlanner.Reserve(body) * 2)
        {
            _plannedTour.Clear();
            return false;
        }

        BeginChoosing(ctx, destination, FlightPurpose.Sightsee, BubbleIcon.Star, _plannedTour[0]);
        return true;
    }

    /// <summary>Busca un borde de la plataforma con caída libre de sobra hasta algo donde posarse.</summary>
    private bool TryPlanEdgeDrop(AutopilotContext ctx, Platform platform)
    {
        var body = ctx.Body;
        var world = ctx.World;
        var height = body.Height;
        var margin = DestinationPicker.LandingMargin(body);
        var first = random.Sign();
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? first : -first;
            var edgeX = side < 0 ? platform.Left + margin + height * 0.35 : platform.Right - margin - height * 0.35;
            var beyond = (side < 0 ? platform.Left : platform.Right) + side * (body.HalfWidth + height * 0.9);
            if (world.MonitorAt(new Vec2(beyond, body.Position.Y)) is null)
            {
                continue;
            }

            var below = world.PlatformBelow(beyond, body.FeetY + 1, margin);
            if (below is null || below.Y - platform.Y < height * 6 || ctx.Sensors.FullscreenMonitors.Contains(below.MonitorIndex))
            {
                continue;
            }

            var target = Destination.On(below, beyond);
            if (!IsReachable(ctx, target))
            {
                continue;
            }

            _pendingDrop = target;
            _peekAt = new Vec2(beyond, below.Y);
            if (Math.Abs(body.Position.X - edgeX) > height * 1.5)
            {
                ctx.Visual.LookTarget = new Vec2(edgeX, platform.Y);
                StartFlight(ctx, Destination.On(platform, edgeX), FlightPurpose.Stroll, AutopilotState.Flying);
            }
            else
            {
                Peek(ctx);
            }

            return true;
        }

        return false;
    }

    /// <summary>Se asoma al borde: mira hacia abajo con curiosidad un momento antes de saltar.</summary>
    private void Peek(AutopilotContext ctx)
    {
        EnterResting(ctx, random.Range(1.6, 3));
        _lookOverride = _peekAt;
        _lookOverrideTime = 3;
        ctx.Visual.Flash(Expression.Curious, 3);
    }

    /// <summary>Lo que toca al posarse según el motivo del vuelo. Devuelve true si ya ha decidido el descanso.</summary>
    private bool OnBehaviorArrived(AutopilotContext ctx)
    {
        switch (_purpose)
        {
            case FlightPurpose.Stroll when _pendingDrop is not null:
                Peek(ctx);
                return true;

            case FlightPurpose.Stroll:
                EnterResting(ctx, random.Range(5, 12));
                return true;

            case FlightPurpose.Clock:
                ChimeHere(ctx);
                EnterResting(ctx, NewRestDuration(ctx));
                return true;

            case FlightPurpose.Perch:
                ctx.Visual.Flash(Expression.Curious, 1.5);
                return false;

            default:
                return false;
        }
    }
}
