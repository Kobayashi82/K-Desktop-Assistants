using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;
using Kumo.Core.Geometry;

namespace Kumo.Assistant.Rendering;

/// <summary>
/// Lo que está haciendo para ti (SDK 1.13, <see cref="StateDetail.Activity"/>), en una burbujita de nube junto a su cabeza: un
/// icono de Segoe Fluent Icons (o Segoe MDL2 Assets en Windows 10) con dos puntitos hacia él, como un bocadillo de pensar.
/// Aparece con un rebote, cambia de icono con un saltito y se encoge al acabar.
/// </summary>
internal sealed class ActivityBadge
{
    private const double PopIn = 0.32;
    private const double PopOut = 0.18;
    private const double SwapTime = 0.25;

    /// <summary>Radio de la burbuja (unidades de Kumo).</summary>
    public const double Radius = 0.36;

    private static readonly Typeface? Icons = FindIcons();
    private static readonly Dictionary<char, Geometry> Glyphs = [];

    private AssistantActivity _activity = AssistantActivity.None;
    private double _since = double.NegativeInfinity;
    private double _swappedAt = double.NegativeInfinity;
    private double _hiddenAt = double.NaN;

    /// <summary>Lo que enseña ahora (None: nada, o encogiéndose).</summary>
    public AssistantActivity Activity => _activity;

    /// <summary>Pone o quita lo que hace (None la quita).</summary>
    public void Set(AssistantActivity activity, double now)
    {
        if (activity == AssistantActivity.None || Icons is null)
        {
            if (_activity != AssistantActivity.None && double.IsNaN(_hiddenAt))
            {
                _hiddenAt = now;
            }

            return;
        }

        if (!double.IsNaN(_hiddenAt) || _activity == AssistantActivity.None)
        {
            // Sale de nuevo (si se estaba encogiendo, desde donde iba).
            _since = double.IsNaN(_hiddenAt) ? now : now - PopIn * Math.Clamp(1 - (now - _hiddenAt) / PopOut, 0, 1) * 0.5;
            _hiddenAt = double.NaN;
        }
        else if (activity != _activity)
        {
            _swappedAt = now;
        }

        _activity = activity;
    }

    /// <summary>Lo que mide ahora (0 = no se ve); acaba de encogerse del todo, se olvida.</summary>
    public double Scale(double now)
    {
        if (_activity == AssistantActivity.None)
        {
            return 0;
        }

        if (!double.IsNaN(_hiddenAt))
        {
            var f = (now - _hiddenAt) / PopOut;
            if (f >= 1)
            {
                _activity = AssistantActivity.None;
                _hiddenAt = double.NaN;
                return 0;
            }

            return 1 - f * f;
        }

        var t = (now - _since) / PopIn;
        var grow = t >= 1 ? 1 : Math.Max(0, 1 - Math.Exp(-5 * t) * Math.Cos(8 * t));
        var s = now - _swappedAt;
        var hop = s < SwapTime ? 1 + 0.18 * Math.Sin(s / SwapTime * Math.PI) : 1;
        return grow * hop;
    }

    /// <summary>Se ve moverse (sale, cambia o se va): a cada fotograma.</summary>
    public bool IsAnimating(double now) =>
        _activity != AssistantActivity.None && (now - _since < PopIn || now - _swappedAt < SwapTime || !double.IsNaN(_hiddenAt));

    /// <summary>Dónde va su centro (píxeles), arriba a la derecha del cuerpo (o a la izquierda si mira hacia allí).</summary>
    public static Vec2 Place(Vec2 at, (double Left, double Top, double Right, double Bottom) body, double unit, bool left, double time)
    {
        var x = left ? body.Left * 0.72 - 0.34 : body.Right * 0.72 + 0.34;
        var y = body.Top - 0.36 + 0.03 * Math.Sin(time * 2.1);
        return new Vec2(at.X + x * unit, at.Y + y * unit);
    }

    /// <summary>Lo que ocupa (píxeles) con el centro en <paramref name="center"/> y el cuerpo en <paramref name="from"/>.</summary>
    public static Rect Bounds(Vec2 center, Vec2 from, double unit)
    {
        var r = Radius * 1.25 * unit;
        var box = new Rect(center.X - r, center.Y - r, r * 2, r * 2);
        box.Union(new Point(from.X, from.Y));
        return box;
    }

    public void Draw(DrawingContext dc, Vec2 center, Vec2 from, double unit, double now, bool dark, double opacity)
    {
        var scale = Scale(now);
        if (scale <= 0.01 || Icons is null || opacity <= 0.01)
        {
            return;
        }

        var popped = opacity < 0.998;
        if (popped)
        {
            dc.PushOpacity(opacity);
        }

        var fill = dark ? DarkFill : LightFill;
        var ink = dark ? DarkInk : LightInk;

        // Los dos puntitos que llevan de la cabeza a la burbuja.
        for (var i = 0; i < 2; i++)
        {
            var f = 0.38 + i * 0.26;
            var p = new Vec2(from.X + (center.X - from.X) * f, from.Y + (center.Y - from.Y) * f);
            var dr = (0.05 + i * 0.03) * unit * Math.Min(1, scale * 1.6);
            dc.DrawEllipse(fill, Rim, new Point(p.X, p.Y), dr, dr);
        }

        var r = Radius * unit * scale;
        dc.DrawEllipse(Shadow, null, new Point(center.X + 0.03 * unit, center.Y + 0.06 * unit), r * 1.04, r * 1.04);
        dc.DrawEllipse(fill, Rim, new Point(center.X, center.Y), r, r);

        var glyph = GlyphOf(_activity);
        if (GeometryOf(glyph) is { } geometry)
        {
            var size = r * 1.05;
            var bounds = geometry.Bounds;
            var k = size / Math.Max(bounds.Width, bounds.Height);
            dc.PushTransform(new MatrixTransform(k, 0, 0, k, center.X - (bounds.X + bounds.Width / 2) * k, center.Y - (bounds.Y + bounds.Height / 2) * k));
            dc.DrawGeometry(ink, null, geometry);
            dc.Pop();
        }

        if (popped)
        {
            dc.Pop();
        }
    }

    /// <summary>El icono de cada actividad (códigos comunes a Segoe Fluent Icons y Segoe MDL2 Assets).</summary>
    public static char GlyphOf(AssistantActivity activity) => activity switch
    {
        AssistantActivity.Command => '',      // rayo
        AssistantActivity.Apps => '',         // todas las aplicaciones
        AssistantActivity.SearchingWeb => '', // lupa
        AssistantActivity.ReadingWeb => '',   // lectura
        AssistantActivity.OpeningWeb => '',   // globo
        AssistantActivity.Files => '',        // carpeta
        AssistantActivity.Notes => '',        // nota rápida
        AssistantActivity.Memory => '',       // biblioteca
        AssistantActivity.Weather => '',      // sol
        AssistantActivity.Music => '',        // nota musical
        AssistantActivity.Timer => '',        // cronómetro
        AssistantActivity.Watching => '',     // ojo
        AssistantActivity.Dictation => '',    // micrófono
        AssistantActivity.Screen => '',       // monitor
        AssistantActivity.Writing => '',      // lápiz
        AssistantActivity.Calculating => '',  // calculadora
        AssistantActivity.System => '',       // diagnóstico
        _ => '',                              // engranaje
    };

    private static Geometry? GeometryOf(char glyph)
    {
        if (Glyphs.TryGetValue(glyph, out var cached))
        {
            return cached;
        }

        if (Icons is null)
        {
            return null;
        }

        var text = new FormattedText(glyph.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Icons, 100, Brushes.Black, 1);
        var geometry = text.BuildGeometry(new Point(0, 0));
        geometry.Freeze();
        Glyphs[glyph] = geometry;
        return geometry;
    }

    /// <summary>La fuente de iconos de Windows: Segoe Fluent Icons (Windows 11) o Segoe MDL2 Assets (Windows 10).</summary>
    private static Typeface? FindIcons()
    {
        foreach (var name in (ReadOnlySpan<string>)["Segoe Fluent Icons", "Segoe MDL2 Assets"])
        {
            var typeface = new Typeface(new FontFamily(name), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            if (typeface.TryGetGlyphTypeface(out var glyphs) && glyphs.CharacterToGlyphMap.ContainsKey(0xE713))
            {
                return typeface;
            }
        }

        return null;
    }

    private static readonly Brush LightFill = Frozen(new SolidColorBrush(Color.FromRgb(255, 248, 232)));
    private static readonly Brush DarkFill = Frozen(new SolidColorBrush(Color.FromRgb(255, 241, 214)));
    private static readonly Brush LightInk = Frozen(new SolidColorBrush(Color.FromRgb(176, 98, 18)));
    private static readonly Brush DarkInk = Frozen(new SolidColorBrush(Color.FromRgb(168, 88, 12)));
    private static readonly Brush Shadow = Frozen(new SolidColorBrush(Color.FromArgb(40, 60, 30, 0)));
    private static readonly Pen Rim = FrozenPen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(150, 236, 170, 70))), 1.2));

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}
