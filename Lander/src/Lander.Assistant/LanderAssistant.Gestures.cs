using System.Windows.Media;
using KDesktop.Assistant.Sdk;
using Lander.Assistant.Audio;
using Lander.Assistant.Rendering;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;

namespace Lander.Assistant;

/// <summary>
/// Gestos que pide el módulo. De momento los dos que sabe todo asistente (<see cref="Gesture.Communicate"/> y
/// <see cref="Gesture.React"/>); los de las órdenes (señalar, pulsar botones, mover ventanas…) llegan en la fase 6.
/// </summary>
public sealed partial class LanderAssistant
{
    private const double ReactDuration = 0.8;

    private (Color Color, double Start)? _reaction;

    public bool CanPerform(Gesture gesture) =>
        gesture.Kind is GestureKind.Communicate or GestureKind.React && _game.IsInitialized && !_hidden && _exitStart is null;

    public async Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct)
    {
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
                React(react.Reaction);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(ReactDuration), ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    // El módulo tiene prisa.
                }

                return GestureOutcome.Committed;

            default:
                return GestureOutcome.NotPerformed;
        }
    }

    /// <summary>Un destello del color de la reacción, la cara que toca y su sonido.</summary>
    private void React(Reaction reaction)
    {
        var body = _game.Body;
        var visual = _game.Visual;
        var scale = body.Scale;
        Color color;
        switch (reaction)
        {
            case Reaction.Success:
                color = Gfx.Rgb(0x6BE675);
                visual.Flash(Expression.Happy, 1.2);
                visual.StartBounce();
                Play(Sound.PerfectChime, 0.5);
                break;

            case Reaction.Warning:
                color = Gfx.Rgb(0xFFC83D);
                visual.Flash(Expression.Curious, 1.2);
                visual.ShowBubble(new Bubble(BubbleIcon.Exclamation, 1.4));
                Play(Sound.Pop, 0.7);
                break;

            case Reaction.Failure:
                color = Gfx.Rgb(0xFF5A5A);
                visual.Flash(Expression.Scared, 1.2);
                visual.StartWiggle();
                Play(Sound.HardThud, 0.4);
                break;

            default:
                color = Gfx.Rgb(0xFF7BD5);
                visual.Flash(Expression.Happy, 1.6);
                if (body.State != BodyState.Destroyed)
                {
                    _particles.Confetti(body.Position - new Lander.Core.Geometry.Vec2(0, 20 * scale), scale, 30);
                }

                Play(Sound.PerfectChime, 0.7);
                break;
        }

        _reaction = (color, _now);
        _host.RequestTick();
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

    /// <summary>Lo que hace el módulo (escuchar, pensar, hablar…): la luz de la antena lo enseña.</summary>
    public void SetState(AssistantState state)
    {
        _host.Log.Debug($"Estado: {state}");
        _moduleState = state;
        _host.RequestTick();
    }

    /// <summary>
    /// La luz de la antena según lo que hace el módulo: cian y latiendo al escucharte, ámbar parpadeando mientras piensa,
    /// verde al hablar (y amarilla esperando un sí o un no). Apagada en reposo.
    /// </summary>
    private (Color Color, double Level) SignalTarget(double now) => _moduleState switch
    {
        AssistantState.Listening => (Gfx.Rgb(0x38E1FF), 0.8 + 0.2 * Math.Sin(now * 7)),
        AssistantState.Thinking or AssistantState.Busy => (Gfx.Rgb(0xFFB547), 0.45 + 0.55 * Math.Max(0, Math.Sin(now * 7.5))),
        AssistantState.Speaking => (Gfx.Rgb(0x6DFFA8), 0.7 + 0.3 * Math.Sin(now * 13)),
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

    public void OnVoiceLevel(double level)
    {
        // La voz llega en la fase 5.
    }
}
