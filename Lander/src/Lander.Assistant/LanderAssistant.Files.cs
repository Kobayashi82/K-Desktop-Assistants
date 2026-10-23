using KDesktop.Assistant.Sdk;
using Lander.Assistant.Audio;
using Lander.Assistant.Rendering;
using Lander.Core.Game;
using Lander.Core.Geometry;

namespace Lander.Assistant;

/// <summary>
/// Archivos que le sueltan encima: con ellos encima enseña el icono de paquete y sujeta su imagen con el rayo tractor bajo
/// la nave (K-Desktop esconde la de Windows, que lo tapaba; SDK 1.17); si te los llevas, la suelta con una interrogación,
/// y al soltarlos la sube a la nave y lo celebra como una orden que sale bien. Lo que se hace con ellos lo decide K-Desktop.
/// </summary>
public sealed partial class LanderAssistant
{
    /// <summary>Lo que tarda la imagen en subir a la nave al soltarla.</summary>
    private const double CatchSeconds = 0.4;

    private ManoeuvreEffect? _carry;

    private bool OnFiles(PointerEvent e)
    {
        var visual = _game.Visual;
        switch (e.Kind)
        {
            case PointerEventKind.FilesOver:
                visual.Flash(Expression.Curious, 30);
                visual.ShowBubble(new Bubble(BubbleIcon.Package, 30));
                Play(Sound.Pop, 0.4);
                if (e.Preview is { } preview)
                {
                    DropCarry();
                    _carry = _effects.Add(new ManoeuvreEffect(EffectKind.Carry, _now, double.MaxValue, _game.Body.Position, _game.Body.Position,
                        ShotColor, _game.Body.Scale) { Picture = preview });
                    PlaceCarry(_now);
                    Play(Sound.Charge, 0.3);
                }

                break;

            case PointerEventKind.FilesAway:
                DropCarry();
                visual.Flash(Expression.Neutral, 0.1);
                visual.ShowBubble(new Bubble(BubbleIcon.Question, 1.2));
                break;

            case PointerEventKind.FilesDropped:
                _host.Log.Info($"Le sueltan {e.Files?.Count ?? 0} archivo(s) encima.");
                if (_carry is { } carry)
                {
                    carry.Caught = _now;
                }

                visual.ShowBubble(new Bubble(BubbleIcon.Heart, 1.4));
                React(Reaction.Success);
                break;

            default:
                return false;
        }

        _host.RequestTick();
        return true;
    }

    /// <summary>La imagen sigue a la nave, colgada debajo; al cogerla sube encogiéndose y se va.</summary>
    private void PlaceCarry(double now)
    {
        if (_carry is not { Picture: { } picture } carry)
        {
            return;
        }

        var shrink = 1.0;
        if (carry.Caught is { } caught)
        {
            var t = (now - caught) / CatchSeconds;
            if (t >= 1)
            {
                DropCarry();
                return;
            }

            shrink = 1 - t * t;
        }

        var body = _game.Body;
        var h = body.Height;
        var side = 2.2 * h;
        var fit = Math.Min(side / Math.Max(1, picture.Width), side / Math.Max(1, picture.Height));
        var w = picture.Width * fit * shrink;
        var ph = picture.Height * fit * shrink;
        var sway = carry.Caught is null ? Math.Sin((now - carry.Start) * 2.2) * 0.12 * h : 0;
        var hang = new Vec2(body.Position.X + sway, body.Position.Y + 1.2 * h + side / 2);
        var center = Vec2.Lerp(body.Position, hang, shrink);
        carry.From = body.Position;
        carry.Area = new System.Windows.Rect(center.X - w / 2, center.Y - ph / 2, Math.Max(1, w), Math.Max(1, ph));
    }

    private void DropCarry()
    {
        if (_carry is { } carry)
        {
            _effects.Remove(carry);
            _carry = null;
        }
    }
}
