using System.Windows.Input;
using KDesktop.Assistant.Sdk;
using Lander.Assistant.Audio;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Racing;

namespace Lander.Assistant;

/// <summary>
/// Ratón (coger, lanzar, tocar y doble clic para pilotar), teclado del modo manual y los extras del manifiesto. El
/// clic derecho es del host (abre el menú del asistente).
/// </summary>
public sealed partial class LanderAssistant
{
    /// <summary>
    /// Teclas del modo manual. K-Desktop se las traga mientras se pilota (solo sin Ctrl, Alt ni Win, así que los atajos
    /// siguen funcionando) para que no lleguen también a la ventana de delante.
    /// </summary>
    private static readonly Key[] GameKeys = [Key.Up, Key.Down, Key.Left, Key.Right, Key.Space, Key.Escape, Key.A, Key.D, Key.G];

    private bool _pressed;
    private bool _dragging;
    private Vec2 _pressPoint;
    private Vec2? _dragCursor;
    private bool _gameKeys;

    /// <summary>Un extra que llegó antes de que el juego arrancara.</summary>
    private string? _pendingExtra;

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
                _game.Poke();
                Play(Sound.Pop, 0.5);
                break;

            case PointerEventKind.DoubleClick:
                ToggleMode();
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

    /// <summary>Alterna entre automático y manual; tomar el control saca al lander de la pausa.</summary>
    private void ToggleMode()
    {
        if (_hidden)
        {
            return;
        }

        _game.ToggleMode();
        DrainGameEvents();
        _host.RequestTick();
    }

    private ManualControls ReadControls()
    {
        if (_hidden)
        {
            return default;
        }

        var keys = _host.Keyboard;
        return new ManualControls(
            keys.IsDown(Key.Up), keys.IsDown(Key.Down), keys.IsDown(Key.Left), keys.IsDown(Key.Right), keys.IsDown(Key.Space),
            SlideLeft: keys.IsDown(Key.A), SlideRight: keys.IsDown(Key.D), Legs: keys.IsDown(Key.G));
    }

    /// <summary>Captura las teclas del juego mientras se pilota a mano (y no está escondido ni saliendo).</summary>
    private void UpdateGameKeys()
    {
        var wanted = _game.Mode == ControlMode.Manual && !_hidden && _exitStart is null;
        if (wanted == _gameKeys)
        {
            return;
        }

        _gameKeys = wanted;
        if (wanted)
        {
            _host.Keyboard.Capture(GameKeys);
        }
        else
        {
            _host.Keyboard.Release();
        }
    }

    private void ReleaseGameKeys()
    {
        if (_gameKeys)
        {
            _gameKeys = false;
            _host.Keyboard.Release();
        }
    }

    private void OnKeyChanged(Key key, bool down)
    {
        if (down && key == Key.Escape && _game.Mode == ControlMode.Manual)
        {
            _game.SetMode(ControlMode.Auto, ModeChangeReason.User);
            DrainGameEvents();
        }

        // Las demás se leen en cada paso de la física; basta con no esperar al siguiente ciclo.
        _host.RequestTick();
    }

    public Task OnExtraAsync(string extraId, CancellationToken ct)
    {
        if (_exitStart is not null)
        {
            return Task.CompletedTask;
        }

        if (!_game.IsInitialized)
        {
            // Recién cargado (K-Desktop lo saca para esto, p. ej. con Ctrl+Alt+L) y aún sin mundo: lo hace al arrancar.
            _host.Log.Info($"Extra en espera de arrancar: {extraId}");
            _pendingExtra = extraId;
            _host.RequestTick();
            return Task.CompletedTask;
        }

        RunExtra(extraId);
        return Task.CompletedTask;
    }

    private void RunExtra(string extraId)
    {
        _host.Log.Info($"Extra: {extraId}");
        switch (extraId)
        {
            case ManualExtra:
                ToggleMode();
                break;

            case AcceptExtra:
                AcceptMission();
                break;

            case MissionExtra:
                if (!_game.OfferMission())
                {
                    _host.Notify(Texts.Get("noMission.title"), Texts.Get("noMission.text"));
                }

                break;

            case RaceExtra:
                BeginRace(repeat: false);
                break;

            case RepeatExtra:
                BeginRace(repeat: _game.Races.CanRepeat);
                break;

            case AbandonExtra:
                _game.AbandonRace();
                break;

            default:
                _host.Log.Warn($"Extra desconocido: {extraId}");
                break;
        }

        DrainGameEvents();
        _host.RequestTick();
    }

    /// <summary>Empieza una carrera por un circuito nuevo o repite el último para batir el récord.</summary>
    private void BeginRace(bool repeat)
    {
        if (_hidden)
        {
            _host.Notify(Texts.Get("notNow.title"), Texts.Get("notNow.fullscreen"));
            return;
        }

        var result = _game.StartRace(repeat);
        DrainGameEvents();
        if (result != RaceStartResult.Started)
        {
            _host.Log.Info($"No se puede empezar la carrera: {result}.");
        }

        switch (result)
        {
            case RaceStartResult.NoCircuit:
                _host.Notify(Texts.Get("noCircuit.title"), Texts.Get("noCircuit.text"));
                break;
            case RaceStartResult.CircuitGone:
                _host.Notify(Texts.Get("circuitGone.title"), Texts.Get("circuitGone.text"));
                break;
            case RaceStartResult.NotReady:
                _host.Notify(Texts.Get("notNow.title"), Texts.Get("notNow.busy"));
                break;
        }
    }
}
