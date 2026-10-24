using KDesktop.Assistant.Sdk;
using Kumo.Assistant.Audio;
using Kumo.Core.Cloud;
using Kumo.Core.Geometry;

namespace Kumo.Assistant;

/// <summary>
/// El ratón: clic = cosquillas, doble clic = se condensa en gota, y arrastrar = llevarlo (se estira al tirar y se queda donde lo
/// sueltes). El clic derecho es del host (abre el menú del asistente) y el clic central, escribirle.
/// </summary>
public sealed partial class KumoAssistant
{
    /// <summary>
    /// Lo más deprisa que sale lanzado (unidades por segundo; el ratón llega a 15 000 px/s). Con el freno de la nube recorre
    /// hasta unas 19 unidades.
    /// </summary>
    private const double FlingSpeed = 60;

    private bool _pressed;
    private bool _dragging;
    private Vec2 _grabOffset;

    public void OnPointer(PointerEvent e)
    {
        if (!_started || _hidden || _exitStart is not null)
        {
            return;
        }

        if (OnFiles(e))
        {
            return;
        }

        var at = new Vec2(e.Position.X, e.Position.Y);
        switch (e.Kind)
        {
            case PointerEventKind.Down:
                _pressed = true;
                break;

            case PointerEventKind.DragStart when _settings.AllowDragging && !ManoeuvreActive:
                _dragging = true;
                _grabOffset = _cloud.Position - at;
                _cloud.Follow(at + _grabOffset);
                _hit.Cursor = PointerCursor.Grabbing;
                Play(Sound.Pop, 0.3);
                break;

            case PointerEventKind.DragMove when _dragging:
                _cloud.Follow(at + _grabOffset);
                break;

            case PointerEventKind.DragEnd:
                EndPress();
                break;

            case PointerEventKind.Click when _asleep:
                // Un clic lo despierta de la siesta.
                _pressed = false;
                _napUntil = double.NaN;
                WakeUp(quiet: false);
                break;

            case PointerEventKind.Click:
                _pressed = false;
                Act(CloudPose.Tickle, 1.1);
                _cloud.PuffBurst(3, 1);
                Play(Sound.Giggle, 0.5);
                break;

            case PointerEventKind.DoubleClick:
                Act(CloudPose.Drop, 2.2);
                Play(Sound.Puff, 0.35);
                break;

            case PointerEventKind.Up when !_dragging:
                _pressed = false;
                break;
        }

        _host.RequestTick();
    }

    private void EndPress()
    {
        if (_dragging)
        {
            // Si lo lanzas, sigue con el impulso y se queda por donde le lleva (no vuelve al ratón); si no, donde lo sueltas.
            // KeepOnScreen lo mete en la pantalla.
            var fling = _cloud.Velocity;
            var most = _unit * FlingSpeed;
            if (fling.Length > most)
            {
                fling *= most / fling.Length;
            }

            if (fling.Length > _unit * 2)
            {
                _cloud.Glide(fling);
            }
            else
            {
                _cloud.Hover(_cloud.Position);
            }
            _savedSpot = _cloud.Home;
            _hit.Cursor = PointerCursor.Hand;
        }

        _dragging = false;
        _pressed = false;
    }
}
