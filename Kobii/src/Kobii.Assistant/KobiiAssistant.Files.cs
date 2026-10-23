using KDesktop.Assistant.Sdk;
using Kobii.Assistant.Audio;
using Kobii.Core.Creature;

namespace Kobii.Assistant;

/// <summary>
/// Archivos que le sueltan encima (SDK 1.16): con ellos encima los mira con curiosidad, si te los llevas se queda
/// chafado y al soltarlos los coge de un salto. Lo que se hace con los archivos lo decide K-Desktop.
/// </summary>
public sealed partial class KobiiAssistant
{
    private bool OnFiles(PointerEvent e)
    {
        switch (e.Kind)
        {
            case PointerEventKind.FilesOver:
                _game.Visual.SetExpression(Expression.Curious, 30);
                _game.Visual.AddEmote(EmoteKind.Exclamation, _game.HeadPoint, _game.Body.Scale);
                Play(Sound.Pop, 0.25, e.Position.X);
                break;

            case PointerEventKind.FilesAway:
                _game.Visual.SetExpression(Expression.Sad, 1.2);
                break;

            case PointerEventKind.FilesDropped:
                _host.Log.Info($"Le sueltan {e.Files?.Count ?? 0} archivo(s) encima.");
                if (!_game.PlayClip("brazos-arriba"))
                {
                    _game.Visual.SetExpression(Expression.Joy, 1.8);
                }

                _game.Visual.AddEmote(EmoteKind.Sparkle, _game.HeadPoint, _game.Body.Scale, 12);
                Play(Sound.Chime, 0.35, e.Position.X);
                break;

            default:
                return false;
        }

        _host.RequestTick();
        return true;
    }
}
