using KDesktop.Assistant.Sdk;
using Kumo.Assistant.Audio;
using Kumo.Assistant.Rendering;
using Kumo.Core.Cloud;
using Kumo.Core.Geometry;
using SdkGesture = KDesktop.Assistant.Sdk.Gesture;

namespace Kumo.Assistant;

/// <summary>
/// Gestos que pide el módulo y lo que hace mientras te atiende. De momento: <see cref="SdkGesture.Communicate"/> (habla latiendo
/// con la voz), <see cref="SdkGesture.React"/> (contento, sorpresa, gris con lluvia, estrellas) y <see cref="SdkGesture.Announce"/>
/// (vuela junto al cursor y saluda). Las maniobras de las órdenes están en <see cref="ManoeuvreAsync"/>.
/// </summary>
public sealed partial class KumoAssistant
{
    private readonly List<TaskCompletionSource> _frames = [];
    private AssistantState _moduleState = AssistantState.Idle;
    private int _manoeuvres;

    /// <summary>La ventana que lleva abrazada (<see cref="CarryWindowAsync"/>).</summary>
    private long? Carrying { get; set; }

    /// <summary>Hay una maniobra en marcha (o algo esperando al siguiente fotograma).</summary>
    private bool ManoeuvreActive => _manoeuvres > 0 || _frames.Count > 0;

    public bool CanPerform(SdkGesture gesture) =>
        (gesture.Kind is GestureKind.Communicate or GestureKind.React or GestureKind.Announce || IsManoeuvre(gesture.Kind) && !OnFullscreen(gesture))
        && _started && !_hidden && _exitStart is null && !_dragging;

    public async Task<GestureOutcome> PerformAsync(SdkGesture gesture, IGestureContext ctx, CancellationToken ct)
    {
        switch (gesture)
        {
            case SdkGesture.Communicate:
                Act(CloudPose.Talk, 30);
                try
                {
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    // Fin del bocadillo.
                }
                finally
                {
                    EndAction();
                }

                return GestureOutcome.Committed;

            case SdkGesture.React react:
                React(react.Reaction, react.Context);
                await SafeDelayAsync(1.2, ct);
                return GestureOutcome.Committed;

            case SdkGesture.Announce announce:
                _manoeuvres++;
                try
                {
                    if (announce.Cursor is { } c && _host.World.Current.MonitorAt(c) is { } monitor)
                    {
                        var at = new Vec2(c.X, c.Y);
                        Travel(Clamp(at + new Vec2(at.X > monitor.Bounds.Center.X ? -2.2 * _unit : 2.2 * _unit, -0.4 * _unit), monitor), 700);
                    }

                    _cloud.Aim = _cloud.Cursor ?? _cloud.Position;
                    _cloud.SetPose(CloudPose.Wave);
                    Play(Sound.Bell, 0.45);
                    await SafeDelayAsync(Math.Min(1.2, Math.Max(0.2, ctx.TimeLeft.TotalSeconds - 0.1)), ct);
                }
                finally
                {
                    _manoeuvres--;
                    _cloud.Hover(_cloud.Home);
                    _cloud.SetPose(_basePose);
                }

                return GestureOutcome.Committed;

            case var _ when IsManoeuvre(gesture.Kind):
                return await ManoeuvreAsync(gesture, ctx, ct);

            default:
                return GestureOutcome.NotPerformed;
        }
    }

    private void React(Reaction reaction, ReactionContext context)
    {
        var top = new Vec2(_cloud.Position.X, _cloud.Position.Y - _unit * 0.5);
        switch (reaction)
        {
            case Reaction.Success:
                Act(CloudPose.Happy, context.FirstToday ? 2 : 1.3);
                Play(Sound.Chime, 0.4);
                if (context.Streak >= 3 || context.FirstToday)
                {
                    _cloud.Burst(ParticleKind.Sparkle, top, 8);
                }

                break;
            case Reaction.Warning:
                Act(CloudPose.Surprise, 0.8);
                _cloud.Burst(ParticleKind.Exclamation, new Vec2(top.X, top.Y - _unit * 0.6));
                break;
            case Reaction.Failure:
                Act(CloudPose.Error, context.Streak >= 3 ? 3 : 2);
                Play(Sound.Drizzle, 0.4);
                break;
            default:
                Act(CloudPose.Happy, 2);
                _cloud.Burst(context.Source == ReactionSource.User ? ParticleKind.Heart : ParticleKind.Star, top, context.Source == ReactionSource.User ? 3 : 12);
                Play(Sound.Sparkle, 0.45);
                break;
        }

        _host.Log.Debug($"Kumo reacciona: {reaction} ({context.Source}, racha {context.Streak}{(context.FirstToday ? ", primera del día" : "")}).");
    }

    public void SetState(AssistantState state) => SetState(state, StateDetail.None);

    /// <summary>
    /// Lo que hace el módulo (SDK 1.13): cada estado tiene su forma; mientras trabaja para ti, lo que hace va en una burbujita junto a
    /// su cabeza (<see cref="ActivityBadge"/>). Cargando el cerebro, junta fuerzas (<see cref="CloudPose.Gather"/>).
    /// </summary>
    public void SetState(AssistantState state, StateDetail detail)
    {
        var previous = _moduleState;
        _moduleState = state;
        _host.Log.Debug(detail.Activity == AssistantActivity.None ? $"Estado: {state}" : $"Estado: {state} ({detail.Activity})");
        _basePose = state switch
        {
            AssistantState.Listening => CloudPose.Listen,
            AssistantState.Thinking => CloudPose.Think,
            AssistantState.ThinkingHard => CloudPose.ThinkHard,
            AssistantState.LoadingBrain => CloudPose.Gather,
            AssistantState.NoBrain => CloudPose.Shrug,
            AssistantState.Speaking => CloudPose.Talk,
            AssistantState.AwaitingConfirmation => CloudPose.Wait,
            AssistantState.Busy => CloudPose.Busy,
            AssistantState.Sleeping => CloudPose.Sleep,
            _ => _asleep ? CloudPose.Sleep : CloudPose.Idle,
        };

        _badge.Set(state is AssistantState.Busy or AssistantState.Thinking or AssistantState.ThinkingHard or AssistantState.AwaitingConfirmation
            ? detail.Activity : AssistantActivity.None, _now);
        if (state == AssistantState.LoadingBrain && previous != AssistantState.LoadingBrain)
        {
            Play(Sound.Whoosh, 0.2);
        }

        if (state == AssistantState.NoBrain && previous != AssistantState.NoBrain)
        {
            _cloud.Burst(ParticleKind.Question, new Vec2(_cloud.Position.X, _cloud.Position.Y - _unit * 1.1));
        }

        if (_action is null && !ManoeuvreActive && _speech is null)
        {
            _cloud.SetPose(_basePose);
        }

        _host.RequestTick();
    }

    public void OnVoiceLevel(double level)
    {
        _cloud.VoiceLevel = level;
        _host.RequestTick();
    }

    /// <summary>Suelta las esperas de fotograma al empezar cada ciclo.</summary>
    private void ReleaseFrames()
    {
        if (_frames.Count == 0)
        {
            return;
        }

        var waiting = _frames.ToArray();
        _frames.Clear();
        foreach (var frame in waiting)
        {
            frame.TrySetResult();
        }
    }

    private static Task DelayAsync(double seconds, CancellationToken ct) =>
        Task.Delay(TimeSpan.FromSeconds(Math.Max(0, seconds)), ct);

    private static async Task SafeDelayAsync(double seconds, CancellationToken ct)
    {
        try
        {
            await DelayAsync(seconds, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // El módulo tiene prisa.
        }
    }
}
