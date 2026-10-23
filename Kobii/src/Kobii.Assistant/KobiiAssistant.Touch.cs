using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Assistant.Sdk;
using Kobii.Assistant.Audio;
using Kobii.Core.Creature;

namespace Kobii.Assistant;

/// <summary>
/// Los juegos con el ratón que se piden desde el menú, el panel o los comandos: chocar los cinco, darse la mano y
/// cepillarle (el cursor se vuelve un cepillo sobre él hasta que lo guardas o pasan dos minutos sin usarlo). Las
/// cosquillas no se piden: basta con frotarle la barriga deprisa.
/// </summary>
public sealed partial class KobiiAssistant
{
    private const string HighFiveExtra = "kobii.highfive";
    private const string HandshakeExtra = "kobii.handshake";
    private const string BrushExtra = "kobii.brush";
    private const string BrushStopExtra = "kobii.brush.stop";

    /// <summary>Sin cepillarle este rato, el cepillo se guarda solo.</summary>
    private const double BrushIdleSeconds = 120;

    /// <summary>Lado del cursor de cepillo a 100 % (los de Windows miden 32).</summary>
    private const double BrushCursorDip = 40;

    private double _brushUntil;
    private double _brushCursorScale;

    /// <summary>Atiende los extras de los juegos con el ratón. False si no es uno de ellos.</summary>
    private bool OnTouchExtra(string extraId)
    {
        switch (extraId)
        {
            case HighFiveExtra:
                _game.OfferHighFive();
                return true;
            case HandshakeExtra:
                _game.OfferHandshake();
                return true;
            case BrushExtra:
                SetBrushMode(true);
                return true;
            case BrushStopExtra:
                SetBrushMode(false);
                return true;
            default:
                return false;
        }
    }

    private void SetBrushMode(bool on)
    {
        _brushUntil = on ? _now + BrushIdleSeconds : 0;
        if (_game.BrushMode == on)
        {
            return;
        }

        _game.BrushMode = on;
        _brushCursorScale = 0;
        if (!on)
        {
            _hit.SetCursorImage(null);
        }

        UpdateBrushCursor();
        UpdateTouchExtraStates();
        _host.Log.Debug(on ? "Kobii: cepillo en la mano" : "Kobii: cepillo guardado");
    }

    /// <summary>El cepillo se guarda solo tras un rato sin usarlo; el cursor se dibuja a la escala del monitor en el que está.</summary>
    private void UpdateBrushMode()
    {
        if (_game.BrushMode && _now > _brushUntil)
        {
            SetBrushMode(false);
            return;
        }

        UpdateBrushCursor();
    }

    private void UpdateBrushCursor()
    {
        if (!_game.BrushMode)
        {
            return;
        }

        var scale = (_world.MonitorAt(_game.Body.Center) ?? _world.NearestMonitor(_game.Body.Center))?.DpiScale ?? 1;
        if (Math.Abs(scale - _brushCursorScale) < 0.01)
        {
            return;
        }

        _brushCursorScale = scale;
        var (image, hotspot) = BrushCursor(scale);
        _hit.SetCursorImage(image, hotspot);
    }

    /// <summary>
    /// El cepillo del cursor (de Kobii AI): cabeza ovalada de madera con las cerdas hacia abajo a la izquierda y el mango
    /// hacia arriba a la derecha. La punta que cepilla es el centro de las cerdas.
    /// </summary>
    private static (BitmapSource Image, Point Hotspot) BrushCursor(double scale)
    {
        var size = (int)Math.Ceiling(BrushCursorDip * scale);
        var s = size / 48.0;
        var center = new Point(19 * s, 27 * s);
        var turn = new RotateTransform(-0.75 * 180 / Math.PI, center.X, center.Y);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(turn);
            var outline = Math.Max(1, 1.4 * s);
            var dark = Brush(0x6D4C41);
            var wood = Brush(0xC08552);

            // Mango.
            dc.DrawLine(RoundPen(dark, 7 * s + outline * 2), center + new Vector(0, -6 * s), center + new Vector(0, -24 * s));
            dc.DrawLine(RoundPen(wood, 7 * s), center + new Vector(0, -6 * s), center + new Vector(0, -24 * s));
            dc.DrawEllipse(dark, null, center + new Vector(0, -21 * s), 1.4 * s, 1.4 * s);

            // Cerdas: un cepillo de pelo blandito.
            for (var i = -4; i <= 4; i++)
            {
                var x = i * 1.9 * s;
                dc.DrawLine(RoundPen(Brush(0x8D6E63), Math.Max(1, 1.9 * s)), center + new Vector(x, 2 * s), center + new Vector(x * 1.08, 10.5 * s));
                dc.DrawLine(RoundPen(Brush(0xFFF3E0), Math.Max(1, 1.2 * s)), center + new Vector(x, 2 * s), center + new Vector(x * 1.08, 10 * s));
            }

            // Cabeza de madera con un brillo.
            dc.DrawEllipse(dark, null, center, 10.5 * s + outline, 5.5 * s + outline);
            dc.DrawEllipse(wood, null, center, 10.5 * s, 5.5 * s);
            dc.DrawEllipse(Brush(0xE8B98A, 0.9), null, center + new Vector(-3 * s, -2 * s), 4.5 * s, 1.6 * s);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return (bitmap, turn.Transform(center + new Vector(0, 9 * s)));

        static SolidColorBrush Brush(uint rgb, double alpha = 1) =>
            new(Color.FromArgb((byte)(alpha * 255), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

        static Pen RoundPen(Brush brush, double width) => new(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
    }

    private void UpdateTouchExtraStates()
    {
        SetExtra(BrushExtra, _game.BrushMode ? ExtraState.Hidden : ExtraState.Shown);
        SetExtra(BrushStopExtra, _game.BrushMode ? ExtraState.Shown : ExtraState.Hidden);
    }

    /// <summary>Lo que suena de los juegos con el ratón.</summary>
    private void HandleTouchEvent(GameEvent gameEvent)
    {
        switch (gameEvent.Kind)
        {
            case GameEventKind.Giggled:
                Play(Sound.Giggle, 0.35 + 0.15 * gameEvent.Magnitude, gameEvent.Position.X);
                break;
            case GameEventKind.Brushed:
                _brushUntil = _now + BrushIdleSeconds;
                Play(Sound.Swish, 0.12, gameEvent.Position.X);
                break;
            case GameEventKind.Shook:
                Play(Sound.Boing, 0.2, gameEvent.Position.X);
                break;
            case GameEventKind.HighFive:
                Play(Sound.Clap, 0.8, gameEvent.Position.X);
                Play(Sound.Chime, 0.35, gameEvent.Position.X);
                _host.Log.Debug("Kobii: ¡choca esos cinco!");
                break;
            case GameEventKind.Handshake:
                Play(Sound.Chime, 0.4, gameEvent.Position.X);
                _host.Log.Debug("Kobii: os habéis dado la mano");
                break;
        }
    }
}
