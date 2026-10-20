using System.Globalization;
using KDesktop.Assistant.Sdk;
using Lander.Assistant.Rendering;
using Lander.Core.Game;
using Lander.Core.Missions;
using Lander.Core.Terrain;

namespace Lander.Assistant;

/// <summary>
/// Lo que el menú y el panel ofrecen según el momento (SDK 1.1: «Repetir la carrera» solo después de una carrera,
/// «Aceptar la misión» solo mientras se ofrece una), el aviso que explica cómo aceptar una misión (con el atajo real del
/// comando de K-Desktop) y, pilotando a mano, las líneas de los suelos de contenido.
/// </summary>
public sealed partial class LanderAssistant
{
    private const string ManualExtra = "lander.manual";
    private const string MissionExtra = "lander.mission";
    private const string AcceptExtra = "lander.mission.accept";
    private const string RaceExtra = "lander.race";
    private const string RepeatExtra = "lander.race.repeat";

    private readonly Dictionary<string, ExtraState> _extraStates = [];
    private FloorLayer _floors = null!;
    private double _lastFloorRender;

    /// <summary>Enseña, atenúa o esconde los extras según lo que se pueda hacer ahora (solo manda los cambios).</summary>
    private void UpdateExtraStates()
    {
        var mission = _game.Missions.Current;
        var offered = mission is { Status: MissionStatus.Offered };
        SetExtra(AcceptExtra, offered ? ExtraState.Shown : ExtraState.Hidden);
        SetExtra(MissionExtra, offered ? ExtraState.Hidden
            : mission is not null || _game.Races.IsActive ? ExtraState.Disabled : ExtraState.Shown);
        SetExtra(RepeatExtra, _game.Races.CanRepeat ? ExtraState.Shown : ExtraState.Hidden);
    }

    private void SetExtra(string id, ExtraState state)
    {
        if (_extraStates.TryGetValue(id, out var current) && current == state)
        {
            return;
        }

        _extraStates[id] = state;
        _host.SetExtraState(id, state);
    }

    /// <summary>Acepta la misión que se está ofreciendo: es tomar el control.</summary>
    private void AcceptMission()
    {
        if (_game.Missions.Current is not { Status: MissionStatus.Offered })
        {
            _host.Notify(Texts.Get("noOffer.title"), Texts.Get("noOffer.text"));
            return;
        }

        _game.SetMode(ControlMode.Manual, ModeChangeReason.User);
    }

    /// <summary>Se ofrece una misión: qué hay que hacer y cómo aceptarla (con el atajo del modo manual, si tiene).</summary>
    private void NotifyMissionOffer(Mission mission)
    {
        var what = mission.Kind == MissionKind.LandOnApp
            ? Texts.Format("mission.LandOnApp", AppName(mission.AppId))
            : Texts.Get("mission." + mission.Kind);
        var seconds = (int)Math.Round(mission.TimeLeft);
        var text = _host.ExtraHotkey(ManualExtra) is { } key
            ? Texts.Format("mission.accept.key", seconds, key)
            : Texts.Format("mission.accept.noKey", seconds);
        _host.Notify(Texts.Format("mission.offer.title", what), text);
        _host.Log.Info($"Misión ofrecida: {mission.Kind}{(mission.AppId is null ? string.Empty : " (" + mission.AppId + ")")}.");
    }

    /// <summary>«chrome» → «Chrome».</summary>
    private static string AppName(string? appId) =>
        string.IsNullOrEmpty(appId) ? "?" : char.ToUpper(appId[0], CultureInfo.InvariantCulture) + appId[1..];

    /// <summary>
    /// Junto al bocadillo de una misión ofrecida, la tecla para aceptarla (el atajo del modo manual, compacto:
    /// «CTRL+ALT+L»). Null si no hay oferta o el modo manual no tiene atajo.
    /// </summary>
    private string? MissionHint()
    {
        if (_game.Mode != ControlMode.Auto || _game.Missions.Current is not { Status: MissionStatus.Offered } offer
            || _game.Visual.Bubble is not { } bubble || bubble.Icon != offer.Icon)
        {
            return null;
        }

        return _host.ExtraHotkey(ManualExtra)?.Replace(" + ", "+", StringComparison.Ordinal);
    }

    /// <summary>
    /// Pilotando a mano (con la opción de posarse en el contenido), los bordes de contenido cercanos: dónde hay suelo
    /// dentro de las páginas. La vista del contenido no analiza lo que se dibuja ahí.
    /// </summary>
    private void UpdateFloors(double now)
    {
        var show = _content.Enabled && !_hidden && _exitStart is null && _game.Mode == ControlMode.Manual
            && _game.Body.State != Core.Physics.BodyState.Destroyed;
        if (!show)
        {
            if (_floors.HasVisible)
            {
                _floors.HideAll();
                _content.Masks = [];
            }

            return;
        }

        if (now - _lastFloorRender < 1.0 / 30)
        {
            return;
        }

        _lastFloorRender = now;
        var body = _game.Body;
        var reachX = body.Height * 25;
        var reachY = body.Height * 15;
        var floors = _world.Platforms
            .Where(p => p.Kind == PlatformKind.Content
                && p.Right > body.Position.X - reachX && p.Left < body.Position.X + reachX
                && Math.Abs(p.Y - body.Position.Y) < reachY)
            .OrderBy(p => Math.Abs(p.Y - body.Position.Y))
            .Take(16)
            .ToList();
        _floors.Render(floors, body.Scale, _sensors.DarkMode);
        _content.Masks = _floors.Drawn;
    }
}
