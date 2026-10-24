using System.Windows.Media;
using KDesktop.Assistant.Sdk;
using Kumo.Assistant.Audio;
using Kumo.Assistant.Rendering;
using Kumo.Core.Cloud;
using Kumo.Core.Geometry;
using Rect = System.Windows.Rect;

namespace Kumo.Assistant;

/// <summary>
/// Archivos que le sueltan encima (SDK 1.16–1.17): con ellos encima se queda mirándolos con la imagen de Windows flotando sobre
/// él; si te los llevas se desinfla un poco, y al soltarlos la imagen se le mete dentro y se alegra. Lo que se hace con los
/// archivos lo decide K-Desktop (te pregunta).
/// </summary>
public sealed partial class KumoAssistant
{
    /// <summary>Lo que tarda la imagen en metérsele dentro.</summary>
    private const double SwallowSeconds = 0.45;

    /// <summary>La imagen de los archivos (si la hay), desde cuándo y cuándo la soltaron.</summary>
    private (ImageSource? Image, double Since, double? Caught)? _filesHeld;

    private bool OnFiles(PointerEvent e)
    {
        switch (e.Kind)
        {
            case PointerEventKind.FilesOver:
                WakeUp(quiet: true);
                _filesHeld = (e.Preview, _now, null);
                _cloud.Burst(ParticleKind.Exclamation, new Vec2(_cloud.RenderPosition.X, _cloud.RenderPosition.Y - _unit));
                Act(CloudPose.Wait, 60);
                Play(Sound.Pop, 0.3);
                break;

            case PointerEventKind.FilesAway:
                _filesHeld = null;
                Act(CloudPose.Shrug, 1.2);
                Play(Sound.Sigh, 0.2);
                break;

            case PointerEventKind.FilesDropped:
                _host.Log.Info($"Le sueltan {e.Files?.Count ?? 0} archivo(s) encima.");
                _filesHeld = _filesHeld is { Image: not null } held ? held with { Caught = _now } : null;
                Act(CloudPose.Happy, 1.6);
                _cloud.Burst(ParticleKind.Sparkle, new Vec2(_cloud.RenderPosition.X, _cloud.RenderPosition.Y - _unit * 0.4), 10);
                Play(Sound.Chime, 0.35);
                break;

            default:
                return false;
        }

        _host.RequestTick();
        return true;
    }

    /// <summary>
    /// La imagen y dónde va: flotando sobre su cabeza, como mucho del ancho de su cuerpo; al soltarla, encogiéndose hacia su centro.
    /// Null si no hay (o ya se la ha tragado).
    /// </summary>
    private (ImageSource Image, Rect Rect, double Opacity)? CarriedPicture()
    {
        if (_filesHeld is not { Image: { } image } held)
        {
            return null;
        }

        var (left, top, right, _) = _cloud.BodyExtent();
        var u = _cloud.Unit;
        var at = _cloud.RenderPosition;
        var rect = ExtrasPainter.PictureRect(image, new Vec2(at.X, at.Y + top * u - 0.1 * u), Math.Min(2.2 * u, (right - left) * u));

        // Sube un poco al aparecer.
        var rise = KumoCloud.SmoothStep(0, 0.3, _now - held.Since);
        rect.Offset(0, (1 - rise) * 0.3 * u);
        if (held.Caught is not { } caught)
        {
            return (image, rect, rise);
        }

        var t = (_now - caught) / SwallowSeconds;
        if (t >= 1)
        {
            _filesHeld = null;
            return null;
        }

        // Se encoge hacia el centro de la nube.
        var shrink = 1 - t * t;
        var center = new System.Windows.Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var to = new System.Windows.Point(at.X, at.Y);
        center = new System.Windows.Point(center.X + (to.X - center.X) * t, center.Y + (to.Y - center.Y) * t);
        var w = rect.Width * shrink;
        var h = rect.Height * shrink;
        return (image, new Rect(center.X - w / 2, center.Y - h / 2, w, h), shrink);
    }
}
