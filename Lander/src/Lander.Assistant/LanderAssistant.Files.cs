using KDesktop.Assistant.Sdk;
using Lander.Assistant.Audio;
using Lander.Core.Game;

namespace Lander.Assistant;

/// <summary>
/// Archivos que le sueltan encima (SDK 1.16): con ellos encima enseña el icono de paquete, si te los llevas una
/// interrogación y al soltarlos lo celebra como una orden que sale bien. Lo que se hace con ellos lo decide K-Desktop.
/// </summary>
public sealed partial class LanderAssistant
{
    private bool OnFiles(PointerEvent e)
    {
        var visual = _game.Visual;
        switch (e.Kind)
        {
            case PointerEventKind.FilesOver:
                visual.Flash(Expression.Curious, 30);
                visual.ShowBubble(new Bubble(BubbleIcon.Package, 30));
                Play(Sound.Pop, 0.4);
                break;

            case PointerEventKind.FilesAway:
                visual.Flash(Expression.Neutral, 0.1);
                visual.ShowBubble(new Bubble(BubbleIcon.Question, 1.2));
                break;

            case PointerEventKind.FilesDropped:
                _host.Log.Info($"Le sueltan {e.Files?.Count ?? 0} archivo(s) encima.");
                visual.ShowBubble(new Bubble(BubbleIcon.Heart, 1.4));
                React(Reaction.Success);
                break;

            default:
                return false;
        }

        _host.RequestTick();
        return true;
    }
}
