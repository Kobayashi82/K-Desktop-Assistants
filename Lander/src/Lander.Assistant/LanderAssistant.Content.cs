using System.Windows;
using Lander.Assistant.Vision;
using Lander.Core.Game;
using CoreRect = Lander.Core.Geometry.PixelRect;
using Sdk = KDesktop.Assistant.Sdk;

namespace Lander.Assistant;

/// <summary>
/// Posarse en el contenido de las ventanas (opción experimental, apagada por defecto): <see cref="ContentVision"/> mira
/// la pantalla solo cuando el juego lo pide y sus bordes confirmados entran en el terreno. Mientras está posado en uno,
/// pide al host cada giro de la rueda para seguir el scroll de la página al instante.
/// </summary>
public sealed partial class LanderAssistant
{
    private readonly Dictionary<long, string> _titles = [];
    private ContentVision _content = null!;
    private bool _contentFailed;

    private void CreateContentVision()
    {
        _content = new ContentVision
        {
            TitleOf = handle => _titles.GetValueOrDefault(handle, string.Empty),
            // Cada scroll medido va al registro de depuración (solo sale con un host compilado en Debug).
            Verbose = true,
            UserExcludedApps = _settings.ContentExcludedApps,
            LanderSizeDip = _settings.LanderSize,
            Enabled = _settings.LandOnContent,
        };
    }

    /// <summary>
    /// Mira el contenido si hace falta. Devuelve true si han cambiado los bordes confirmados (hay que rehacer el terreno).
    /// </summary>
    private bool UpdateContent(double now)
    {
        if (!_content.Enabled)
        {
            return false;
        }

        var body = _game.Body;
        var landerRect = DrawnArea();
        try
        {
            var changed = _content.Update(now, _game.ContentInterest, _windows, _monitors, body.Position, landerRect, _sensors.ActiveWindow);
            if (changed)
            {
                Log.Stats($"Contenido: {_content.Surfaces.Count} bordes confirmados.");
            }

            return changed;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Es una función experimental: si falla, se apaga (hasta volver a cargar el asistente) y el lander sigue volando.
            Log.Error("Contenido de las ventanas: error inesperado; se desactiva hasta reiniciar el asistente.", ex);
            _content.Enabled = false;
            _contentFailed = true;
            return true;
        }
    }

    /// <summary>
    /// Zona donde hay algo dibujado por el lander. La capa del host sale en la captura (sacarla de ella quitaría a todos
    /// los asistentes de las capturas y grabaciones del usuario), así que esa zona no se analiza. Tiene que ser justa: el
    /// detector no cuenta lo tapado para la longitud mínima de un borde, y con una zona amplia (la del Lander
    /// independiente con --show-in-captures, 2,5 alturas a cada lado) el borde desaparecía justo al bajar a posarse.
    /// </summary>
    private CoreRect DrawnArea()
    {
        var body = _game.Body;
        var visual = _game.Visual;
        var h = body.Height;
        var at = body.Position;
        // El cuerpo con las patas abiertas y la antena, girado como esté (su caja), con un margen para el brillo.
        var area = new Rect(at.X - h * 0.7, at.Y - h * 0.8, h * 1.4, h * 1.4);
        if (visual.MainFlame > 0.03 || visual.BoostFlash > 0)
        {
            area.Union(new Rect(at.X - h * 0.8, at.Y, h * 1.6, h * 1.4));
        }

        if (visual.Bubble is not null)
        {
            area.Union(new Rect(at.X - h * 1.2, at.Y - h * 2.6, h * (_scene.BubbleHint is null ? 2.4 : 3.6), h * 2));
        }

        // La sombra y el resplandor del motor sobre la superficie de debajo, y de noche el faro: caen justo sobre el borde
        // al que va a posarse y, sin taparlos, el borde parecería otro (cambia su franja) y perdería el destino.
        var s = body.Scale;
        if (_scene.ShadowY is { } surface)
        {
            area.Union(new Rect(at.X - 36 * s, surface - 7 * s, 72 * s, 14 * s));
        }

        if (_scene.LandingLight > 0 && _scene.LightSurfaceY > at.Y)
        {
            var spread = (7 * s + (_scene.LightSurfaceY - at.Y) * 0.42) * 1.2;
            area.Union(new Rect(at.X - spread, at.Y, spread * 2, _scene.LightSurfaceY - at.Y + 6 * s));
        }

        // Pilotando a mano, los indicadores van a los lados; los puntos suben desde donde se posa.
        if (visual.ShowHud || visual.Popups.Count > 0)
        {
            area.Union(new Rect(at.X - h * 2.5, at.Y - h * 2.5, h * 5, h * 3.5));
        }

        var particles = _particles.Bounds(_frame);
        if (!particles.IsEmpty)
        {
            area.Union(particles);
        }

        return new CoreRect((int)Math.Floor(area.Left), (int)Math.Floor(area.Top), (int)Math.Ceiling(area.Right), (int)Math.Ceiling(area.Bottom));
    }

    /// <summary>
    /// La rueda solo interesa posado sobre el contenido de una ventana o volviendo a él (el viento llega con el scroll
    /// fuerte).
    /// </summary>
    private void UpdateWheelWatch()
    {
        var wanted = _content.Enabled && !_hidden && _exitStart is null && _game.ContentInterest.Need is ContentNeed.Landed or ContentNeed.Destination;
        if (_host.Pointer.WatchWheel != wanted)
        {
            _host.Pointer.WatchWheel = wanted;
        }
    }

    /// <summary>
    /// Scroll en la ventana sobre cuyo contenido está posado: la página va a moverse. Ir pegado a ella se veía con retraso
    /// (la imagen de la pantalla llega después que la rueda), así que se eleva un poco y espera en el aire a que pare,
    /// mientras la vista sigue el borde a cada fotograma; luego vuelve a posarse.
    /// </summary>
    private void OnWheel(Sdk.SystemEvent.WheelTurned wheel)
    {
        var interest = _game.ContentInterest;
        if (interest.Need is ContentNeed.Landed or ContentNeed.Destination && interest.Window == wheel.Window)
        {
            _game.OnContentScrolling();
            DrainGameEvents();
        }

        if (_content.NotifyWheel(_now, wheel.Window))
        {
            _host.RequestTick();
        }
    }

    private void ApplyContentSettings(Core.Settings.LanderSettings previous, Core.Settings.LanderSettings settings)
    {
        if (Math.Abs(previous.LanderSize - settings.LanderSize) > 0.01)
        {
            _content.OnLanderSizeChanged(settings.LanderSize);
        }

        _content.UserExcludedApps = settings.ContentExcludedApps;
        if (_content.Enabled != settings.LandOnContent && !_contentFailed)
        {
            _content.Enabled = settings.LandOnContent;
            _terrainDirty = true;
        }
    }
}
