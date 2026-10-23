using KDesktop.Assistant.Sdk;
using Kobii.Core.Creature;

namespace Kobii.Assistant;

/// <summary>
/// Gestos que pide el módulo y lo que hace mientras te atiende: <see cref="Gesture.Communicate"/> y
/// <see cref="Gesture.React"/>, y los de las órdenes (ver <see cref="ManoeuvreAsync"/>). Mover ventanas aún no: lo hace el
/// módulo y lo cuenta en el bocadillo.
/// </summary>
public sealed partial class KobiiAssistant
{
    private const double ReactDuration = 1.2;

    public bool CanPerform(Gesture gesture)
    {
        switch (gesture.Kind)
        {
            case GestureKind.Communicate or GestureKind.React:
                return _game.IsInitialized && !_hidden && _exitStart is null;
            case var kind when IsManoeuvre(kind):
                var can = CanManoeuvre && !OnFullscreen(gesture);
                if (!can)
                {
                    _host.Log.Debug($"Maniobra {kind}: ahora no ({(_hidden ? "escondido" : _dragging || _game.Body.IsHeld ? "cogido" : "pantalla completa")})");
                }

                return can;
            default:
                return false;
        }
    }

    public async Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct)
    {
        if (IsManoeuvre(gesture.Kind))
        {
            return await ManoeuvreAsync(gesture, ctx, ct);
        }

        switch (gesture)
        {
            case Gesture.Communicate:
                // El módulo enseña el bocadillo (apuntando a BubbleAnchor) y decide cuánto dura: mientras, te mira y habla.
                _game.Communicate(true);
                _host.RequestTick();
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
                    _game.Communicate(false);
                    _host.RequestTick();
                }

                return GestureOutcome.Committed;

            case Gesture.React react:
                _game.React(react.Reaction switch
                {
                    Reaction.Success => ReactionKind.Success,
                    Reaction.Warning => ReactionKind.Warning,
                    Reaction.Failure => ReactionKind.Failure,
                    _ => ReactionKind.Celebration,
                });
                DrainGameEvents();
                _host.RequestTick();
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

    /// <summary>
    /// Lo que hace el módulo: escuchándote se queda atento con el brote encendido; pensando, se lleva la mano a la
    /// barbilla y el brote late; hablando, te mira y mueve la boca con su voz.
    /// </summary>
    public void SetState(AssistantState state)
    {
        _host.Log.Debug($"Estado: {state}");
        _game.SetAttention(state switch
        {
            AssistantState.Listening => Attention.Listening,
            AssistantState.Thinking => Attention.Thinking,
            AssistantState.Speaking => Attention.Speaking,
            AssistantState.AwaitingConfirmation => Attention.Waiting,
            AssistantState.Busy => Attention.Busy,
            _ => Attention.None,
        });
        DrainGameEvents();
        _host.RequestTick();
    }

    public void OnVoiceLevel(double level)
    {
        _game.SetVoiceLevel(level);
        _host.RequestTick();
    }
}
