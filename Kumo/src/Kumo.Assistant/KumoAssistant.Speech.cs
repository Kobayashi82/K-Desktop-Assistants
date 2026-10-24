using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;
using Kumo.Assistant.Audio;
using Kumo.Core.Cloud;
using Kumo.Core.Geometry;

namespace Kumo.Assistant;

/// <summary>
/// Texto propio (SDK 1.15, manifiesto <c>"speech": "own"</c>): la nube se hace bocadillo (<see cref="CloudPose.Bubble"/>) del
/// tamaño del texto, con la cara en una esquina y las palabras dentro. Crece con la respuesta mientras se escribe, cabe siempre
/// en el área de trabajo (se aparta del borde si hace falta y luego vuelve) y, si no puede (escondido, en plena maniobra, en
/// la mano o el texto no cabe en la pantalla), dice que no y sale el bocadillo de K-Desktop.
/// </summary>
public sealed partial class KumoAssistant
{
    /// <summary>Ancho máximo del texto (unidades).</summary>
    private const double SpeechMaxWidth = 7;

    /// <summary>Tamaño de la letra (unidades: unos 14 px a escala 1).</summary>
    private const double SpeechFontSize = 0.36;

    /// <summary>Líneas como mucho (lo demás, con «…»: entero está en el chat).</summary>
    private const int SpeechMaxLines = 7;

    /// <summary>Sitio para la cara a la izquierda y margen a la derecha (unidades).</summary>
    private const double SpeechFaceRoom = 1.0;

    private const double SpeechRightRoom = 0.32;

    private static readonly Typeface SpeechFont = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Brush SpeechInk = Frozen(new SolidColorBrush(Color.FromRgb(92, 46, 6)));

    /// <summary>Lo que enseña en su bocadillo (null: nada).</summary>
    private SpeechText? _speech;
    private FormattedText? _speechText;
    private double _speechUnit;
    private Vec2? _speechHome;

    public bool ShowSpeech(SpeechText speech)
    {
        if (!_started || _hidden || _exitStart is not null || ManoeuvreActive || _dragging || Carrying is not null)
        {
            return false;
        }

        var u = _unit;
        var text = new FormattedText(speech.Text.Trim(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, SpeechFont, SpeechFontSize * u, SpeechInk, 1)
        {
            MaxTextWidth = SpeechMaxWidth * u,
            MaxLineCount = SpeechMaxLines,
            Trimming = TextTrimming.WordEllipsis,
        };
        var hw = (SpeechFaceRoom + text.Width / u + SpeechRightRoom) / 2;
        var hh = Math.Max(0.55, (text.Height / u + 0.46) / 2);
        if (FitSpeech(hw, hh) is not { } spot)
        {
            _host.Log.Debug("Kumo: el texto no cabe en la pantalla; bocadillo de K-Desktop");
            HideSpeech();
            return false;
        }

        if (_speech is null)
        {
            _speechHome = _cloud.Motion == Motion.Hover ? _cloud.Home : _cloud.Position;
            Play(Sound.Pop, 0.25);
        }

        _speech = speech;
        _speechText = text;
        _speechUnit = u;
        _cloud.Bubble = (hw, hh);
        _cloud.Hold(spot);
        _cloud.SetPose(CloudPose.Bubble);
        _host.RequestTick();
        return true;
    }

    public void HideSpeech()
    {
        if (_speech is null)
        {
            return;
        }

        _speech = null;
        _speechText = null;
        if (!ManoeuvreActive && Carrying is null)
        {
            _cloud.Hover(_speechHome is { } home ? InRoom(home) : _cloud.Position);
            _cloud.SetPose(_action ?? _basePose);
        }

        _speechHome = null;
        _host.RequestTick();
    }

    /// <summary>
    /// Dónde poner su centro para que el bocadillo (± medio ancho y medio alto, y la colita de abajo) quepa en el área de trabajo
    /// del monitor donde está, lo más cerca posible de donde está; null si no cabe.
    /// </summary>
    private Vec2? FitSpeech(double hw, double hh)
    {
        var at = _cloud.Motion == Motion.Hover ? _cloud.Home : _cloud.Position;
        var monitor = _host.World.Current.MonitorAt(new Point(at.X, at.Y)) ?? Nearest(at);
        if (monitor is null)
        {
            return null;
        }

        var u = _unit;
        var w = monitor.WorkArea;
        var margin = 0.25 * u;
        double left = w.Left + hw * u + margin, right = w.Right - hw * u - margin;
        double top = w.Top + hh * u + margin, bottom = w.Bottom - (hh + 0.75) * u - margin;
        if (left > right || top > bottom)
        {
            return null;
        }

        return new Vec2(Math.Clamp(at.X, left, right), Math.Clamp(at.Y, top, bottom));
    }

    /// <summary>El texto dentro del bocadillo (aparece cuando la nube ya tiene casi su forma).</summary>
    private void DrawSpeech(DrawingContext dc)
    {
        if (_speechText is not { } text || _cloud.Pose != CloudPose.Bubble)
        {
            return;
        }

        var shown = Math.Clamp((_cloud.PoseTime - 0.32) / 0.2, 0, 1) * _frame.Opacity;
        if (shown <= 0.01)
        {
            return;
        }

        // Si ha cambiado de tamaño (otro monitor) se escala con él hasta que llegue el texto siguiente.
        var k = _speechUnit > 0 ? _cloud.Unit / _speechUnit : 1;
        var at = _cloud.RenderPosition;
        var (hw, _) = _cloud.Bubble;
        var origin = new Point(at.X + (-hw + SpeechFaceRoom) * _cloud.Unit, at.Y - text.Height * k / 2);
        dc.PushTransform(new MatrixTransform(k, 0, 0, k, origin.X, origin.Y));
        if (shown < 0.998)
        {
            dc.PushOpacity(shown);
        }

        dc.DrawText(text, new Point(0, 0));
        if (shown < 0.998)
        {
            dc.Pop();
        }

        dc.Pop();
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    public Task OnExtraAsync(string extraId, CancellationToken ct) => Task.CompletedTask;
}
