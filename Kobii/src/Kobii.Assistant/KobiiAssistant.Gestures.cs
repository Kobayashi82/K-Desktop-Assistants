using KDesktop.Assistant.Sdk;
using Kobii.Assistant.Audio;
using Kobii.Assistant.Rendering;
using Kobii.Core.Animation;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using AnimGesture = Kobii.Core.Animation.Gesture;
using SdkGesture = KDesktop.Assistant.Sdk.Gesture;

namespace Kobii.Assistant;

/// <summary>
/// Gestos que pide el módulo y lo que hace mientras te atiende: <see cref="SdkGesture.Communicate"/>,
/// <see cref="SdkGesture.React"/> (con variantes y según el contexto), <see cref="SdkGesture.Announce"/> (ver
/// <see cref="AnnounceAsync"/>) y los de las órdenes (ver <see cref="ManoeuvreAsync"/>; mover ventanas, en
/// <see cref="DragWindowAsync"/>).
/// </summary>
public sealed partial class KobiiAssistant
{
    private const double ReactDuration = 1.2;

    private readonly Variety<string> _successes = new Variety<string>().Add("asentir", 1.2).Add("aplaudir").Add("guino", 0.8).Add("saltitos", 0.6, cooldown: 30);
    private readonly Variety<string> _warnings = new Variety<string>().Add("senalar").Add("rascarse", 0.8);
    private readonly Variety<string> _failures = new Variety<string>().Add("encogerse", 1.2).Add("rascarse").Add("negar").Add("suspirar", 0.5);
    private readonly Variety<string> _embarrassed = new Variety<string>().Add("rascarse").Add("suspirar", 0.6);
    private readonly Variety<string> _celebrations = new Variety<string>().Add("brazos-arriba", 1.2).Add("saltitos").Add("aplaudir", 0.8).Add("voltereta", 0.6, cooldown: 60);
    private readonly Variety<string> _thanks = new Variety<string>().Add("beso").Add("menearse").Add("guino", 0.6);

    public bool CanPerform(SdkGesture gesture)
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

    public async Task<GestureOutcome> PerformAsync(SdkGesture gesture, IGestureContext ctx, CancellationToken ct)
    {
        if (IsManoeuvre(gesture.Kind))
        {
            return await ManoeuvreAsync(gesture, ctx, ct);
        }

        switch (gesture)
        {
            case SdkGesture.Communicate:
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

            case SdkGesture.React react:
                React(react.Reaction, react.Context);
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
    /// Una reacción, distinta cada vez (asentir, aplaudir, guiñar, saltitos; señalar o rascarse; encogerse, negar o
    /// suspirar; brazos arriba, voltereta…), según el contexto: un «eso está mal» le da vergüenza, la tercera seguida que
    /// sale mal se enfurruña y la tercera que sale bien da saltitos entre destellos; un «gracias», un beso o un meneo; y la
    /// primera del día, voltereta con confeti.
    /// </summary>
    private void React(Reaction reaction, ReactionContext context)
    {
        var scale = _game.Body.Scale;
        string clip;
        EmoteKind? emote = null;
        var emotes = 0;
        switch (reaction)
        {
            case Reaction.Success:
                clip = context.Streak >= 3 ? "saltitos" : _successes.Pick();
                if (context.Streak >= 3)
                {
                    emotes = 6;
                }

                if (context.FirstToday)
                {
                    (clip, emote, emotes) = ("voltereta", EmoteKind.Confetti, 20);
                }

                break;

            case Reaction.Warning:
                clip = _warnings.Pick();
                break;

            case Reaction.Failure when context.Source == ReactionSource.User:
                clip = _embarrassed.Pick();
                emote = EmoteKind.Question;
                break;

            case Reaction.Failure:
                clip = context.Streak >= 3 ? "enfurrunarse" : _failures.Pick();
                break;

            default:
                if (context.Source == ReactionSource.User)
                {
                    clip = _thanks.Pick();
                    (emote, emotes) = (EmoteKind.Heart, 4);
                }
                else
                {
                    clip = context.FirstToday ? "voltereta" : _celebrations.Pick();
                }

                break;
        }

        _game.React(reaction switch
        {
            Reaction.Success => ReactionKind.Success,
            Reaction.Warning => ReactionKind.Warning,
            Reaction.Failure => ReactionKind.Failure,
            _ => ReactionKind.Celebration,
        }, clip, emote, emotes);
        if (context.Streak >= 3 && reaction == Reaction.Success)
        {
            _game.Visual.AddEmote(EmoteKind.Sparkle, _game.HeadPoint, scale, 6);
        }

        _host.Log.Debug($"Kobii reacciona: {reaction} con «{clip}» ({context.Source}, racha {context.Streak}{(context.FirstToday ? ", primera del día" : "")}).");
    }

    /// <summary>
    /// Va a decir algo por su cuenta: se acerca andando (o corriendo) hacia el cursor si está en su suelo, te mira y saluda
    /// (o te llama con la mano si es un mensaje); si llevas un rato sin tocar nada, con las dos manos, un brinco y destellos.
    /// </summary>
    private async Task AnnounceAsync(SdkGesture.Announce announce, Pace pace, IGestureContext ctx, CancellationToken ct)
    {
        var body = _game.Body;
        var scale = body.Scale;
        var away = announce.UserAway > 60;
        var side = 1.0;
        if (announce.Cursor is { } cursor)
        {
            var target = ToVec(cursor);
            side = target.X < body.Feet.X ? -1 : 1;
            if (pace.Walk > 0 && body.Support is { } support && target.Y < support.Y + body.Height && target.Y > support.Y - body.Height * 8)
            {
                await ApproachAsync(new Vec2(target.X, support.Y), pace, ctx, 0.7, ct);
                _game.StopWalking();
            }

            _game.LookAt(target);
        }

        var gesture = away ? AnimGesture.WaveBoth : announce.Reason == AnnounceReason.Message ? AnimGesture.Beckon : AnimGesture.Wave;
        _game.PlayGesture(gesture, 1.6, side);
        _game.Visual.SetExpression(Expression.Joy, 2);
        _game.Visual.AddEmote(EmoteKind.Exclamation, _game.HeadPoint, scale);
        if (away)
        {
            _game.Hop();
            _game.Visual.AddEmote(EmoteKind.Sparkle, _game.HeadPoint, scale, 6);
            Play(Sound.Whistle, 0.6);
        }
        else
        {
            Play(Sound.Chime, 0.45);
        }

        _host.Log.Debug($"Kobii llama la atención ({announce.Reason}, {announce.UserAway:0} s sin tocar nada).");
        var left = ctx.TimeLeft.TotalSeconds - 0.1;
        if (left > 0.05)
        {
            await DelayAsync(Math.Min(0.8, left), ct);
        }
    }

    private SdkStateTracker _state;

    /// <summary>Lo que hacía el módulo la última vez (para hacer cada cosa una sola vez al cambiar).</summary>
    private readonly record struct SdkStateTracker(AssistantState State, AssistantActivity Activity);

    public void SetState(AssistantState state) => SetState(state, StateDetail.None);

    /// <summary>
    /// Lo que hace el módulo (SDK 1.13): escuchándote se queda atento con el brote encendido; pensando, se lleva la mano a la
    /// barbilla y el brote late; pensando a fondo, además con las gafas puestas; mientras carga su cerebro se frota los ojos;
    /// sin cerebro se encoge de hombros; hablando, te mira y mueve la boca con su voz. Mientras hace algo para ti (y no hay
    /// una maniobra que lo enseñe), una animación de lo que hace: lee un libro (una web, archivos, notas), mira alrededor con
    /// las gafas (internet, la pantalla), se tapa con la hoja (el tiempo), canturrea (la música) o piensa (las cuentas).
    /// </summary>
    public void SetState(AssistantState state, StateDetail detail)
    {
        var previous = _state;
        _state = new SdkStateTracker(state, detail.Activity);
        _host.Log.Debug(detail.Activity == AssistantActivity.None ? $"Estado: {state}" : $"Estado: {state} ({detail.Activity}{(detail.Tool is null ? "" : ", " + detail.Tool)})");
        _game.SetAttention(state switch
        {
            AssistantState.Listening => Attention.Listening,
            AssistantState.Thinking or AssistantState.ThinkingHard or AssistantState.LoadingBrain => Attention.Thinking,
            AssistantState.Speaking => Attention.Speaking,
            AssistantState.AwaitingConfirmation => Attention.Waiting,
            AssistantState.Busy => Attention.Busy,
            _ => Attention.None,
        });

        if (_manoeuvres == 0)
        {
            _game.HeldWear = state == AssistantState.ThinkingHard ? Wear.Glasses : Wear.None;
        }

        if (state != previous.State || detail.Activity != previous.Activity)
        {
            switch (state)
            {
                case AssistantState.LoadingBrain when previous.State != AssistantState.LoadingBrain:
                    _game.PlayGesture(AnimGesture.RubEyes, 2, _game.Animator.Side);
                    break;

                case AssistantState.ThinkingHard when previous.State != AssistantState.ThinkingHard:
                    _game.Visual.AddEmote(EmoteKind.Question, _game.HeadPoint, _game.Body.Scale, 2);
                    break;

                case AssistantState.NoBrain:
                    _game.React(ReactionKind.Failure, "encogerse");
                    break;

                case AssistantState.Busy when _manoeuvres == 0 && BusyClip(detail.Activity) is { } clip:
                    _game.PlayClip(clip);
                    break;
            }
        }

        DrainGameEvents();
        _host.RequestTick();
    }

    /// <summary>La animación que enseña lo que hace para ti (null: nada propio; lo enseñan los gestos de la orden).</summary>
    internal static string? BusyClip(AssistantActivity activity) => activity switch
    {
        AssistantActivity.ReadingWeb or AssistantActivity.Files or AssistantActivity.Notes or AssistantActivity.Memory or AssistantActivity.Writing => "leer-de-pie",
        AssistantActivity.SearchingWeb => "mirar-alrededor",
        AssistantActivity.Weather => "hoja-paraguas",
        AssistantActivity.Music => "canturrear",
        AssistantActivity.Calculating => "pensar",
        _ => null,
    };

    public void OnVoiceLevel(double level)
    {
        _game.SetVoiceLevel(level);
        _host.RequestTick();
    }
}
