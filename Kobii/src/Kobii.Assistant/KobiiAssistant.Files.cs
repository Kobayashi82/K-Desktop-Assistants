using Rect = System.Windows.Rect;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;
using Kobii.Assistant.Audio;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;

namespace Kobii.Assistant;

/// <summary>
/// Archivos que le sueltan encima: con ellos encima levanta los brazos y sostiene su imagen sobre las manos (K-Desktop
/// esconde la de Windows, que lo tapaba; SDK 1.17); si te los llevas se queda chafado, y al soltarlos la imagen se le mete
/// dentro y salta de alegría. Lo que se hace con los archivos lo decide K-Desktop.
/// </summary>
public sealed partial class KobiiAssistant
{
    private const string HoldClip = "sostener-archivos";
    private const double CatchSeconds = 0.4;

    /// <summary>La imagen de los archivos, desde cuándo y cuándo la soltaron (desde ahí se le mete dentro).</summary>
    private (ImageSource Image, double Since, double? Caught)? _filesHeld;

    private bool OnFiles(PointerEvent e)
    {
        switch (e.Kind)
        {
            case PointerEventKind.FilesOver:
                _game.Visual.SetExpression(Expression.Curious, 30);
                _game.Visual.AddEmote(EmoteKind.Exclamation, _game.HeadPoint, _game.Body.Scale);
                _filesHeld = e.Preview is { } preview ? (preview, _now, null) : null;
                _game.PlayClip(HoldClip);
                Play(Sound.Pop, 0.25, e.Position.X);
                break;

            case PointerEventKind.FilesAway:
                _filesHeld = null;
                _game.StopClip(HoldClip);
                _game.Visual.SetExpression(Expression.Sad, 1.2);
                break;

            case PointerEventKind.FilesDropped:
                _host.Log.Info($"Le sueltan {e.Files?.Count ?? 0} archivo(s) encima.");
                if (_filesHeld is { } carried)
                {
                    _filesHeld = (carried.Image, carried.Since, _now);
                }

                _game.StopClip(HoldClip);
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

    /// <summary>
    /// Dónde va la imagen: sobre sus manos levantadas (o sobre la cabeza si no las tiene arriba), del tamaño de su cuerpo
    /// como mucho; al soltarla se encoge hacia su barriga. Null si no hay (o ya se le metió dentro).
    /// </summary>
    private (ImageSource Image, Rect Rect, double Opacity)? CarriedPicture()
    {
        if (_filesHeld is not { } carried)
        {
            return null;
        }

        var shrink = 1.0;
        if (carried.Caught is { } caught)
        {
            var t = (_now - caught) / CatchSeconds;
            if (t >= 1)
            {
                _filesHeld = null;
                return null;
            }

            shrink = 1 - t * t;
        }

        var body = _game.Body;
        var h = body.Height;
        var side = 1.1 * h;
        var image = carried.Image;
        var fit = Math.Min(side / Math.Max(1, image.Width), side / Math.Max(1, image.Height));
        var w = image.Width * fit;
        var ph = image.Height * fit;
        Vec2 bottom;
        if (_game.Animator.Clip?.Id == HoldClip)
        {
            var rig = _game.Animator.Rig;
            var hands = (_game.WorldPoint(rig.Hand(1, Species.Form)) + _game.WorldPoint(rig.Hand(-1, Species.Form))) / 2;
            bottom = hands + new Vec2(0, 0.08 * h);
        }
        else
        {
            bottom = _game.HeadPoint - new Vec2(0, 0.15 * h);
        }

        var bob = carried.Caught is null ? Math.Sin((_now - carried.Since) * 3) * 0.03 * h : 0;
        var center = new Vec2(bottom.X, bottom.Y - ph / 2 + bob);
        center = Vec2.Lerp(body.Center, center, shrink);
        w *= shrink;
        ph *= shrink;
        return (image, new Rect(center.X - w / 2, center.Y - ph / 2, Math.Max(1, w), Math.Max(1, ph)), 0.5 + 0.5 * shrink);
    }
}
