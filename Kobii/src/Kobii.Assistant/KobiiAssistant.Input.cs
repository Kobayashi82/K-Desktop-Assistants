using KDesktop.Assistant.Sdk;
using Kobii.Core.Geometry;

namespace Kobii.Assistant;

/// <summary>
/// El ratón: clic = una caricia, frotarle (pasar el cursor por encima de un lado a otro) = mimos, y arrastrar = cogerlo
/// y lanzarlo. El clic derecho es del host (abre el menú del asistente) y el clic central, escribirle.
/// </summary>
public sealed partial class KobiiAssistant
{
    private bool _pressed;
    private bool _dragging;
    private Vec2 _pressPoint;
    private Vec2? _dragCursor;

    public void OnPointer(PointerEvent e)
    {
        if (!_game.IsInitialized || _hidden || _exitStart is not null)
        {
            return;
        }

        if (OnFiles(e))
        {
            return;
        }

        if (e.Area != _hit.Id)
        {
            OnToyPointer(e);
            return;
        }

        var at = new Vec2(e.Position.X, e.Position.Y);
        switch (e.Kind)
        {
            case PointerEventKind.Down:
                _pressed = true;
                _pressPoint = at;
                break;

            case PointerEventKind.DragStart when _settings.AllowDragging:
                _dragging = true;
                _dragCursor = at;
                _game.BeginDrag(_pressed ? _pressPoint : at);
                _hit.Cursor = PointerCursor.Grabbing;
                break;

            case PointerEventKind.DragMove:
                _dragCursor = at;
                break;

            case PointerEventKind.DragEnd:
                EndPress();
                break;

            case PointerEventKind.Click:
                _pressed = false;
                _game.Click();
                break;

            case PointerEventKind.Up when !_dragging:
                _pressed = false;
                break;
        }

        DrainGameEvents();
        _host.RequestTick();
    }

    private void EndPress()
    {
        if (_dragging)
        {
            _game.EndDrag();
            _hit.Cursor = PointerCursor.Hand;
        }

        _dragging = false;
        _pressed = false;
        _dragCursor = null;
    }
}
