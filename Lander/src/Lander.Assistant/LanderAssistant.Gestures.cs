using System.Globalization;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;
using Lander.Assistant.Audio;
using Lander.Assistant.Rendering;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;

namespace Lander.Assistant;

/// <summary>
/// Gestos que pide el módulo: los dos que sabe todo asistente (<see cref="Gesture.Communicate"/> y
/// <see cref="Gesture.React"/>), llamar la atención antes de decir algo por su cuenta (<see cref="Gesture.Announce"/>) y
/// las maniobras de las órdenes (ver <c>LanderAssistant.Manoeuvres.cs</c>); mover ventanas, con el gancho
/// (<c>LanderAssistant.Hook.cs</c>). Las reacciones varían (<see cref="Variety{T}"/>) y tienen en cuenta de dónde vienen,
/// la racha y si son las primeras del día.
/// </summary>
public sealed partial class LanderAssistant
{
    private const double ReactDuration = 0.8;

    private enum Cheer { Bounce, Sparkle, Nod, Flip }

    private enum Oops { Smoke, Shiver, Droop }

    private enum Party { Confetti, Stars, Hearts }

    private readonly Variety<Cheer> _cheers = new Variety<Cheer>().Add(Cheer.Bounce, 1.2).Add(Cheer.Sparkle).Add(Cheer.Nod).Add(Cheer.Flip, 0.8, cooldown: 60);
    private readonly Variety<Oops> _oopses = new Variety<Oops>().Add(Oops.Smoke).Add(Oops.Shiver).Add(Oops.Droop);
    private readonly Variety<Party> _parties = new Variety<Party>().Add(Party.Confetti).Add(Party.Stars).Add(Party.Hearts, 0.7);

    private (Color Color, double Start)? _reaction;

    public bool CanPerform(Gesture gesture) => gesture.Kind switch
    {
        GestureKind.Communicate or GestureKind.React => _game.IsInitialized && !_hidden && _exitStart is null,
        GestureKind.Announce => _game.IsInitialized && !_hidden && _exitStart is null && !_dragging && _game.Body.State != BodyState.Destroyed,
        var kind when IsManoeuvre(kind) => CanManoeuvre && !OnFullscreen(gesture),
        _ => false,
    };

    public async Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct)
    {
        if (IsManoeuvre(gesture.Kind))
        {
            return await ManoeuvreAsync(gesture, ctx, ct).ConfigureAwait(true);
        }

        switch (gesture)
        {
            case Gesture.Communicate:
                // El módulo enseña el bocadillo (apuntando a BubbleAnchor) y decide cuánto dura.
                _game.Visual.Flash(Expression.Curious, 1.5);
                _host.RequestTick();
                try
                {
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    // Fin del bocadillo.
                }

                return GestureOutcome.Committed;

            case Gesture.React react:
                React(react.Reaction, react.Context);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(ReactDuration), ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    // El módulo tiene prisa.
                }

                return GestureOutcome.Committed;

            case Gesture.Announce announce:
                return await AnnounceAsync(announce, ctx, ct).ConfigureAwait(true);

            default:
                return GestureOutcome.NotPerformed;
        }
    }

    /// <summary>
    /// Va a decir algo por su cuenta (un temporizador, un aviso, «Asistente: decir»): si el cursor queda lejos y le da
    /// tiempo, vuela a su lado; allí saluda con el icono del motivo. Si llevas un rato sin tocar nada, además destellos y
    /// un tono más alto, por si estás lejos. Luego habla el host (y se queda un momento en el aire antes de posarse).
    /// </summary>
    private async Task<GestureOutcome> AnnounceAsync(Gesture.Announce announce, IGestureContext ctx, CancellationToken ct)
    {
        _manoeuvres++;
        _lingerUntil = double.NaN;
        var pace = PaceOf(ctx.Speed);
        var body = _game.Body;
        try
        {
            if (announce.Cursor is { } cursor && CanManoeuvre && pace.Flight > 0)
            {
                var target = ToVec(cursor);
                var monitor = _world.MonitorAt(target);
                if (monitor is not null && !_sensors.FullscreenMonitors.Contains(monitor.Index) && Vec2.Distance(body.Position, target) > body.Height * 5)
                {
                    var side = target.X > body.Position.X ? -1 : 1;
                    var point = new Vec2(target.X + side * body.Height * 2.6, target.Y - body.Height * 1.6);
                    await ApproachAsync(point, target, pace, ctx, 0.5, ct, range: 4);
                }

                _game.Visual.LookTarget = target;
            }

            var icon = announce.Reason switch
            {
                AnnounceReason.Timer => BubbleIcon.Clock,
                AnnounceReason.Watch => BubbleIcon.Eye,
                AnnounceReason.Message => BubbleIcon.Exclamation,
                _ => BubbleIcon.Hello,
            };
            _game.Visual.Flash(Expression.Happy, 2);
            _game.Visual.StartWave();
            _game.Visual.ShowBubble(new Bubble(icon, 2.4));
            Play(Sound.Pop, 0.6);
            if (announce.UserAway > 60)
            {
                _particles.Ring(body.Position, body.Scale, 1);
                _particles.Sparkles(body.Position, 34 * body.Scale, Gfx.Rgb(0xFFD23F), 18, body.Scale);
                Play(Sound.PerfectChime, 0.8);
            }
            else
            {
                Play(Sound.Crystal, 0.35);
            }

            _host.Log.Debug($"Llama la atención ({announce.Reason}, {announce.UserAway:0} s sin tocar nada).");
            var left = ctx.TimeLeft.TotalSeconds - 0.1;
            if (left > 0.05)
            {
                await DelayAsync(Math.Min(0.6, left), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Se acabó su tiempo: habla igual.
        }
        finally
        {
            _manoeuvres--;
            if (_manoeuvres == 0)
            {
                _lingerUntil = _now + LingerSeconds;
                _host.RequestTick();
            }
        }

        return GestureOutcome.Committed;
    }

    /// <summary>
    /// Un destello del color de la reacción, la cara que toca y una de sus variantes: rebote, destellos, asentir o, volando
    /// para una orden, voltereta al celebrar; humo, escalofrío o desánimo cuando algo sale mal (con un fallo de motor si
    /// vuela). El contexto la cambia: «eso está mal» le da vergüenza en vez de humo, la tercera seguida es más grande, la
    /// primera del día lleva estrella o trofeo, y un «gracias» corazones.
    /// </summary>
    private void React(Reaction reaction, ReactionContext? context = null)
    {
        context ??= ReactionContext.Unknown;
        var body = _game.Body;
        var visual = _game.Visual;
        var scale = body.Scale;
        var alive = body.State != BodyState.Destroyed;
        var top = body.Position - new Vec2(0, 20 * scale);
        var errand = _game.Autopilot.OnErrand;
        Color color;
        switch (reaction)
        {
            case Reaction.Success:
            {
                color = Gfx.Rgb(0x6BE675);
                var cheer = _cheers.Pick(c => c != Cheer.Flip || errand);
                visual.Flash(Expression.Happy, 1.2);
                switch (cheer)
                {
                    case Cheer.Sparkle:
                        if (alive)
                        {
                            _particles.Sparkles(body.Position, 30 * scale, Gfx.Rgb(0x7DE3FF), 12, scale);
                        }

                        visual.ShowBubble(new Bubble(BubbleIcon.Star, 1.1));
                        Play(Sound.Crystal, 0.45);
                        break;

                    case Cheer.Nod:
                        visual.Impact(0.9);
                        LookAtCursor();
                        Play(Sound.LegClick, 0.6);
                        Play(Sound.PerfectChime, 0.35);
                        break;

                    case Cheer.Flip:
                        _game.Autopilot.ErrandFlip();
                        Play(Sound.PerfectChime, 0.5);
                        break;

                    default:
                        visual.StartBounce();
                        Play(Sound.PerfectChime, 0.5);
                        break;
                }

                if (context.Source == ReactionSource.Notice)
                {
                    visual.ShowBubble(new Bubble(context.What == "watch" ? BubbleIcon.Eye : BubbleIcon.Clock, 1.4));
                }

                if (context.Streak >= 3 && alive)
                {
                    visual.AddPopup("×" + context.Streak.ToString(CultureInfo.InvariantCulture), top - new Vec2(0, 10 * scale), PopupStyle.Bonus);
                    _particles.Sparkles(body.Position, 34 * scale, Gfx.Rgb(0xFFD23F), 10, scale);
                }

                if (context.FirstToday)
                {
                    visual.ShowBubble(new Bubble(BubbleIcon.Star, 1.6));
                    Play(Sound.Fanfare, 0.35);
                }

                break;
            }

            case Reaction.Warning:
                color = Gfx.Rgb(0xFFC83D);
                if (context.Streak >= 2)
                {
                    // Otra vez algo a medias: se rasca la cabeza.
                    visual.Flash(Expression.Tired, 1.4);
                    visual.ShowBubble(new Bubble(BubbleIcon.Wrench, 1.4));
                }
                else
                {
                    visual.Flash(Expression.Curious, 1.2);
                    visual.ShowBubble(new Bubble(BubbleIcon.Exclamation, 1.4));
                }

                Play(Sound.Pop, 0.7);
                break;

            case Reaction.Failure:
                color = Gfx.Rgb(0xFF5A5A);
                if (context.Source == ReactionSource.User)
                {
                    // «Eso está mal»: no es una avería, es vergüenza.
                    visual.Flash(Expression.Tired, 2);
                    visual.StartWiggle();
                    visual.ShowBubble(new Bubble(BubbleIcon.Question, 1.6));
                    Play(Sound.Pop, 0.4);
                    break;
                }

                var oops = _oopses.Pick();
                switch (oops)
                {
                    case Oops.Shiver:
                        visual.Flash(Expression.Scared, 1.4);
                        visual.StartWiggle();
                        if (alive)
                        {
                            _particles.Sparks(body.Position, new Vec2(0, -1), 5, scale);
                        }

                        Play(Sound.Scrape, 0.5);
                        break;

                    case Oops.Droop:
                        visual.Flash(Expression.Tired, 1.8);
                        visual.ShowBubble(new Bubble(BubbleIcon.Question, 1.4));
                        visual.Impact(0.6);
                        if (alive)
                        {
                            _particles.DustBurst(body.Position + new Vec2(0, body.HalfHeight), 0.4, scale);
                        }

                        Play(Sound.Puff, 0.6);
                        break;

                    default:
                        visual.Flash(Expression.Scared, 1.2);
                        visual.StartWiggle();
                        Play(Sound.HardThud, 0.4);
                        if (alive)
                        {
                            _particles.DustBurst(body.Position, 0.7, scale);
                            _particles.EnginePuff(body.Position, new Vec2(0, -1), body.Velocity, scale, "smoke", _now);
                        }

                        break;
                }

                if (errand)
                {
                    _game.Autopilot.Stall();
                    Play(Sound.Puff, 0.8);
                }

                if (context.Streak >= 3)
                {
                    // Tercera seguida: le cuesta hasta hablar.
                    VoiceMood("error", 8);
                    if (alive)
                    {
                        _particles.Sparks(body.Position, new Vec2(0, -1), 8, scale);
                    }
                }

                break;

            default:
            {
                color = Gfx.Rgb(0xFF7BD5);
                var praised = context.Source == ReactionSource.User;
                var party = _parties.Pick(p => !praised || p != Party.Confetti);
                visual.Flash(Expression.Happy, 1.6);
                switch (party)
                {
                    case Party.Stars:
                        if (alive)
                        {
                            _particles.Sparkles(top, 40 * scale, Gfx.Rgb(0xFFD23F), 22, scale);
                        }

                        visual.ShowBubble(new Bubble(BubbleIcon.Star, 1.4));
                        Play(Sound.Crystal, 0.5);
                        break;

                    case Party.Hearts:
                        if (alive)
                        {
                            _particles.Hearts(top, scale, 6);
                        }

                        visual.ShowBubble(new Bubble(BubbleIcon.Heart, 1.4));
                        visual.StartWiggle();
                        break;

                    default:
                        if (alive)
                        {
                            _particles.Confetti(top, scale, 30);
                        }

                        break;
                }

                if (errand)
                {
                    _game.Autopilot.ErrandFlip();
                }

                if (context.FirstToday)
                {
                    visual.ShowBubble(new Bubble(BubbleIcon.Trophy, 1.8));
                    Play(Sound.Fanfare, 0.5);
                }
                else
                {
                    Play(Sound.PerfectChime, 0.7);
                }

                break;
            }
        }

        _host.Log.Debug($"Reacciona: {reaction} ({context.Source}, racha {context.Streak}{(context.FirstToday ? ", primera del día" : "")}).");
        _reaction = (color, _now);
        _host.RequestTick();
    }

    private void LookAtCursor()
    {
        if (_sensors.Cursor is { } cursor)
        {
            _game.Visual.LookTarget = cursor;
        }
    }

    /// <summary>Color y fuerza del destello de la reacción en curso (null cuando ya ha pasado).</summary>
    private (Color Color, double Strength)? ReactionGlow(double now)
    {
        if (_reaction is not { } reaction)
        {
            return null;
        }

        var t = (now - reaction.Start) / ReactDuration;
        if (t >= 1)
        {
            _reaction = null;
            return null;
        }

        return (reaction.Color, 0.55 * Math.Sin(Math.PI * t));
    }

    /// <summary>Lo que hace el módulo ahora (escuchar, pensar, hablar…): se ve en la luz de la antena.</summary>
    private AssistantState _moduleState;

    private StateDetail _stateDetail = StateDetail.None;

    /// <summary>El icono de lo que está haciendo que enseña ahora en su bocadillo (null si ninguno).</summary>
    private BubbleIcon? _activityIcon;

    public void SetState(AssistantState state) => SetState(state, StateDetail.None);

    /// <summary>
    /// Lo que hace el módulo (escuchar, pensar, hablar…) y con qué (SDK 1.13): la luz de la antena lo enseña, y mientras
    /// trabaja en una orden, un bocadillo con lo que hace (internet, archivos, la música…). Pensando a fondo pone cara de
    /// concentración; sin cerebro, se encoge de hombros.
    /// </summary>
    public void SetState(AssistantState state, StateDetail detail)
    {
        var previous = _moduleState;
        _host.Log.Debug(detail.Activity == AssistantActivity.None ? $"Estado: {state}" : $"Estado: {state} ({detail.Activity}{(detail.Tool is null ? "" : ", " + detail.Tool)})");
        _moduleState = state;
        _stateDetail = detail;
        if (_game.IsInitialized)
        {
            var visual = _game.Visual;
            if (state == AssistantState.ThinkingHard)
            {
                visual.Flash(Expression.Focused, 60);
            }
            else if (previous == AssistantState.ThinkingHard)
            {
                visual.ClearFlash();
            }

            if (state == AssistantState.NoBrain && previous != AssistantState.NoBrain)
            {
                visual.Flash(Expression.Tired, 2.5);
                visual.StartWiggle();
                Play(Sound.Pop, 0.4);
            }

            UpdateActivityBubble();
        }

        _host.RequestTick();
    }

    /// <summary>El bocadillo con lo que hace mientras atiende una orden; fuera en cuanto habla o termina.</summary>
    private void UpdateActivityBubble()
    {
        var wanted = ActivityIcon(_moduleState, _stateDetail);
        var visual = _game.Visual;
        if (wanted == _activityIcon && (wanted is null || visual.Bubble?.Icon == wanted))
        {
            return;
        }

        if (_activityIcon is { } shown && visual.Bubble?.Icon == shown)
        {
            visual.ClearBubble();
        }

        _activityIcon = wanted;
        if (wanted is { } icon)
        {
            visual.ShowBubble(new Bubble(icon, 45));
        }
    }

    internal static BubbleIcon? ActivityIcon(AssistantState state, StateDetail detail) => state switch
    {
        AssistantState.LoadingBrain => BubbleIcon.Loading,
        AssistantState.NoBrain => BubbleIcon.Question,
        AssistantState.Busy or AssistantState.Thinking or AssistantState.ThinkingHard or AssistantState.Listening => detail.Activity switch
        {
            AssistantActivity.SearchingWeb or AssistantActivity.OpeningWeb => BubbleIcon.Globe,
            AssistantActivity.ReadingWeb => BubbleIcon.Page,
            AssistantActivity.Files => BubbleIcon.Folder,
            AssistantActivity.Notes => BubbleIcon.Note,
            AssistantActivity.Memory => BubbleIcon.Crystal,
            AssistantActivity.Weather => BubbleIcon.Weather,
            AssistantActivity.Music => BubbleIcon.Music,
            AssistantActivity.Timer => BubbleIcon.Clock,
            AssistantActivity.Watching or AssistantActivity.Screen => BubbleIcon.Eye,
            AssistantActivity.Writing or AssistantActivity.Dictation => BubbleIcon.Pencil,
            AssistantActivity.Calculating => BubbleIcon.Sums,
            AssistantActivity.Command => BubbleIcon.Wrench,
            AssistantActivity.Apps or AssistantActivity.System or AssistantActivity.Other => BubbleIcon.Gears,
            _ => state == AssistantState.ThinkingHard ? BubbleIcon.Gears : null,
        },
        _ => null,
    };

    /// <summary>
    /// La luz de la antena según lo que hace el módulo: cian y latiendo al escucharte, ámbar parpadeando mientras piensa,
    /// violeta lento pensando a fondo, azul que se va llenando mientras carga su cerebro, naranja a golpes haciendo algo,
    /// verde al hablar, amarilla esperando un sí o un no y roja parpadeando sin cerebro. Dormido, apagada (encendida gastaría
    /// un fotograma tras otro).
    /// </summary>
    private (Color Color, double Level) SignalTarget(double now) => _moduleState switch
    {
        AssistantState.Listening => (Gfx.Rgb(0x38E1FF), 0.8 + 0.2 * Math.Sin(now * 7)),
        AssistantState.Thinking => (Gfx.Rgb(0xFFB547), 0.45 + 0.55 * Math.Max(0, Math.Sin(now * 7.5))),
        AssistantState.ThinkingHard => (Gfx.Rgb(0xB98CFF), 0.35 + 0.65 * Math.Abs(Math.Sin(now * 2.4))),
        AssistantState.LoadingBrain => (Gfx.Rgb(0x5AA9FF), 0.15 + 0.85 * (now * 1.2 % 1)),
        AssistantState.Busy => (Gfx.Rgb(0xFF9A3D), now * 2.5 % 1 is < 0.12 or (> 0.25 and < 0.37) ? 1 : 0.4),
        AssistantState.NoBrain => (Gfx.Rgb(0xFF5A5A), Math.Sin(now * 12) > 0 ? 0.9 : 0.2),
        // Al hablar, sigue la voz (si no llega nivel, late como si hablara).
        AssistantState.Speaking => (Gfx.Rgb(0x6DFFA8), _voiceLevel > 0 ? 0.3 + 0.7 * _voiceLevel : 0.7 + 0.3 * Math.Sin(now * 13)),
        AssistantState.AwaitingConfirmation => (Gfx.Rgb(0xFFE15A), 0.75 + 0.25 * Math.Sin(now * 4)),
        _ => (_signalColor, 0),
    };

    private double _signal;
    private Color _signalColor = Gfx.Rgb(0x38E1FF);
    private double _signalClock;

    /// <summary>La luz sigue encendida o apagándose: hay que seguir dibujando.</summary>
    private bool SignalShowing => _signal > 0 || _moduleState is not (AssistantState.Idle or AssistantState.Sleeping);

    /// <summary>La luz de este instante, con suavidad al encender y apagar.</summary>
    private void UpdateSignal(double now)
    {
        var dt = Math.Clamp(now - _signalClock, 0, 0.1);
        _signalClock = now;
        var (color, level) = SignalTarget(now);
        _signalColor = color;
        _signal = level > 0 ? MathUtil.Damp(_signal, level, 14, dt) : MathUtil.Damp(_signal, 0, 5, dt);
        if (level <= 0 && _signal < 0.02)
        {
            _signal = 0;
        }

        _scene.Pose = _scene.Pose with { Signal = _signal, SignalColor = _signalColor };
    }

    /// <summary>Lo fuerte que suena su voz ahora (0…1): la luz de la antena lo sigue.</summary>
    private double _voiceLevel;

    public void OnVoiceLevel(double level)
    {
        _voiceLevel = Math.Clamp(level, 0, 1);
        _host.RequestTick();
    }
}
