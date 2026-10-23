using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;
using Kobii.Core.Geometry;
using SdkRect = KDesktop.Assistant.Sdk.PixelRect;

namespace Kobii.Assistant.Rendering;

internal enum EffectKind
{
    /// <summary>La pelota en vuelo, en arco de <see cref="ManoeuvreEffect.From"/> a <see cref="ManoeuvreEffect.To"/>.</summary>
    Ball,

    /// <summary>La pelota que rebota en el botón y cae desvaneciéndose (sale de To con la velocidad <see cref="ManoeuvreEffect.Velocity"/>).</summary>
    BallFall,

    /// <summary>El golpe en el botón: un aro que se abre y unas estrellitas.</summary>
    Impact,

    /// <summary>Ondas en un punto (llamar a una ventana para traerla delante).</summary>
    Ping,

    /// <summary>Una línea de puntitos de la mano a un punto, con un aro que late allí (señalar).</summary>
    Pointer,

    /// <summary>La banderita en vuelo, en arco de From a To.</summary>
    FlagFlight,

    /// <summary>La banderita clavada en To ondeando, con su texto debajo (si lo hay).</summary>
    Flag,

    /// <summary>Un haz suave desde las gafas (From) hasta la línea que baja por la zona (leer la pantalla).</summary>
    Scan,

    /// <summary>El flash de la foto sobre una zona.</summary>
    Flash,
}

/// <param name="From">Origen; lo mueve quien lo creó (la mano, las gafas) mientras dura.</param>
/// <param name="Area">Para el escáner y el flash.</param>
/// <param name="Peak">Altura del arco por encima del punto más alto (pelota y banderita).</param>
/// <param name="Velocity">Para la pelota que cae.</param>
/// <param name="Text">Para la banderita.</param>
internal sealed record ManoeuvreEffect(EffectKind Kind, double Start, double Duration, Vec2 From, Vec2 To, uint Color, double Scale,
    Rect Area = default, double Peak = 0, Vec2 Velocity = default, string? Text = null)
{
    public Vec2 From { get; set; } = From;

    /// <summary>Dónde va la pelota (o la banderita) en el momento <paramref name="t"/> (0–1) de su vuelo.</summary>
    public Vec2 ArcAt(double t)
    {
        var linear = Vec2.Lerp(From, To, t);
        return linear - new Vec2(0, 4 * Peak * t * (1 - t));
    }
}

/// <summary>
/// Los efectos de las maniobras para K-Desktop (la pelota, el golpe, las ondas, la banderita, el escáner…): lejos de
/// Kobii, así que van en una superficie propia por delante de todo, del tamaño justo de lo que hay que dibujar, y solo
/// mientras dura.
/// </summary>
internal sealed class ManoeuvreLayer(IRendering rendering) : IDisposable
{
    /// <summary>Gravedad de la pelota que cae tras el golpe (píxeles por segundo², a escala 1).</summary>
    private const double Gravity = 2200;

    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly List<ManoeuvreEffect> _effects = [];
    private ISurface? _surface;

    public bool IsActive => _effects.Count > 0;

    public ManoeuvreEffect Add(ManoeuvreEffect effect)
    {
        _effects.Add(effect);
        return effect;
    }

    public void Clear()
    {
        _effects.Clear();
        _surface?.Hide();
    }

    public void Render(double now, double pixelsPerDip)
    {
        _effects.RemoveAll(e => now - e.Start >= e.Duration);
        if (_effects.Count == 0)
        {
            if (_surface is { IsVisible: true })
            {
                _surface.Hide();
            }

            return;
        }

        var bounds = Rect.Empty;
        foreach (var effect in _effects)
        {
            bounds.Union(Extent(effect, now - effect.Start));
        }

        var x = (int)Math.Floor(bounds.X);
        var y = (int)Math.Floor(bounds.Y);
        var width = Math.Clamp((int)Math.Ceiling(bounds.Right) - x, 2, 8192);
        var height = Math.Clamp((int)Math.Ceiling(bounds.Bottom) - y, 2, 8192);
        _surface ??= rendering.CreateSurface(SurfaceLayer.Front);
        _surface.Present(SdkRect.FromSize(x, y, width, height), dc =>
        {
            foreach (var effect in _effects)
            {
                Draw(dc, effect, now - effect.Start, pixelsPerDip);
            }
        });
    }

    /// <summary>Dónde está la pelota que cae <paramref name="age"/> segundos después del golpe.</summary>
    private static Vec2 FallAt(ManoeuvreEffect e, double age) =>
        e.To + e.Velocity * age + new Vec2(0, 0.5 * Gravity * e.Scale * age * age);

    /// <summary>Lo que ocupa un efecto <paramref name="age"/> segundos después de empezar (la superficie mide la unión).</summary>
    internal static Rect Extent(ManoeuvreEffect e, double age)
    {
        var t = Math.Clamp(age / e.Duration, 0, 1);
        var s = e.Scale;
        switch (e.Kind)
        {
            case EffectKind.Ball:
                return Around(e.ArcAt(t), 40 * s);
            case EffectKind.FlagFlight:
                // Gira en el aire alrededor de su centro (20 px por encima del pie): el mástil y la tela llegan a 50.
                return Around(e.ArcAt(t) - new Vec2(0, 20 * s), 56 * s);
            case EffectKind.BallFall:
                return Around(FallAt(e, age), 40 * s);
            case EffectKind.Impact:
                return Around(e.To, 70 * s);
            case EffectKind.Ping:
                return Around(e.To, 90 * s);
            case EffectKind.Pointer:
            {
                var r = new Rect(new Point(e.From.X, e.From.Y), new Point(e.To.X, e.To.Y));
                r.Inflate(40 * s, 40 * s);
                return r;
            }

            case EffectKind.Flag:
            {
                var r = new Rect(e.To.X - 30 * s, e.To.Y - 60 * s, 60 * s, 70 * s);
                if (!string.IsNullOrEmpty(e.Text))
                {
                    r.Union(new Rect(e.To.X - 160 * s, e.To.Y, 320 * s, 34 * s));
                }

                return r;
            }

            case EffectKind.Scan:
            {
                var lineY = e.Area.Top + e.Area.Height * Math.Min(1, t * 1.05);
                var band = new Rect(e.Area.Left, lineY - 60 * s, e.Area.Width, 66 * s);
                band.Union(Around(e.From, 16 * s));
                return band;
            }

            case EffectKind.Flash:
                return e.Area;

            default:
                return Around(e.To, 40 * s);
        }
    }

    private static Rect Around(Vec2 p, double r) => new(p.X - r, p.Y - r, 2 * r, 2 * r);

    /// <summary>Dibuja un efecto <paramref name="age"/> segundos después de empezar, en píxeles de pantalla.</summary>
    internal static void Draw(DrawingContext dc, ManoeuvreEffect e, double age, double pixelsPerDip)
    {
        var t = Math.Clamp(age / e.Duration, 0, 1);
        var s = e.Scale;
        switch (e.Kind)
        {
            case EffectKind.Ball:
            {
                // Una estela de pelotitas cada vez más pálidas detrás de la de verdad.
                for (var i = 3; i >= 1; i--)
                {
                    var back = Math.Max(0, t - i * 0.05);
                    var at = e.ArcAt(back);
                    dc.DrawEllipse(Fill(0xFFFFFF, 0.18 / i), null, P(at), 14 * s, 14 * s);
                }

                ScenePainter.DrawBallAt(dc, P(e.ArcAt(t)), 17 * s, age * 14);
                break;
            }

            case EffectKind.BallFall:
                ScenePainter.DrawBallAt(dc, P(FallAt(e, age)), 17 * s, age * 9 + 2, 1 - t * t);
                break;

            case EffectKind.Impact:
            {
                var ring = 10 * s + 52 * s * Ease(t);
                dc.DrawEllipse(null, Stroke(e.Color, 5 * s * (1 - t), 0.9 * (1 - t)), P(e.To), ring, ring);
                dc.DrawEllipse(Fill(0xFFFFFF, 0.8 * (1 - t) * (1 - t)), null, P(e.To), 14 * s * (1 - t), 14 * s * (1 - t));
                for (var i = 0; i < 6; i++)
                {
                    var angle = i * Math.Tau / 6 + 0.3;
                    var at = e.To + new Vec2(Math.Cos(angle), Math.Sin(angle)) * (16 * s + 46 * s * Ease(t));
                    Star(dc, at, 7 * s * (1 - t), angle * 2 + t * 3, e.Color, 1 - t);
                }

                break;
            }

            case EffectKind.Ping:
                for (var i = 0; i < 3; i++)
                {
                    var u = t * 1.4 - i * 0.2;
                    if (u is <= 0 or >= 1)
                    {
                        continue;
                    }

                    var r = 12 * s + 70 * s * u;
                    dc.DrawEllipse(null, Stroke(e.Color, 4 * s * (1 - u), 0.85 * (1 - u)), P(e.To), r, r * 0.55);
                }

                break;

            case EffectKind.Pointer:
            {
                // Puntitos de la mano al sitio, apareciendo uno tras otro, y un aro que late allí.
                var appear = Math.Min(1, t * 4);
                var fade = t > 0.8 ? (1 - t) / 0.2 : 1;
                var length = (e.To - e.From).Length;
                var dots = Math.Clamp((int)(length / (16 * s)), 2, 60);
                for (var i = 1; i < dots; i++)
                {
                    var u = (double)i / dots;
                    if (u > appear)
                    {
                        break;
                    }

                    var at = Vec2.Lerp(e.From, e.To, u) - new Vec2(0, Math.Sin(Math.PI * u) * Math.Min(60 * s, length * 0.15));
                    dc.DrawEllipse(Fill(e.Color, 0.8 * fade), null, P(at), 3.2 * s, 3.2 * s);
                }

                if (appear >= 1)
                {
                    var pulse = 0.5 + 0.5 * Math.Sin(age * 9);
                    dc.DrawEllipse(null, Stroke(e.Color, 3.5 * s, 0.9 * fade), P(e.To), 16 * s + 6 * s * pulse, 16 * s + 6 * s * pulse);
                    Star(dc, e.To, 8 * s, age * 2, e.Color, fade);
                }

                break;
            }

            case EffectKind.FlagFlight:
                DrawFlag(dc, e.ArcAt(t), s, age, e.Color, 1, spin: t * Math.Tau);
                break;

            case EffectKind.Flag:
            {
                var appear = Math.Min(1, age / 0.18);
                var fade = Math.Min(1, (e.Duration - age) / 0.35);
                DrawFlag(dc, e.To, s * (0.6 + 0.4 * appear), age, e.Color, fade, spin: 0);
                if (!string.IsNullOrEmpty(e.Text) && fade > 0)
                {
                    var text = new FormattedText(e.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFace, 13 * s, Fill(0xFFFFFF), pixelsPerDip)
                    {
                        MaxTextWidth = 300 * s,
                        MaxLineCount = 1,
                        Trimming = TextTrimming.CharacterEllipsis,
                    };
                    var box = new Rect(e.To.X - text.Width / 2 - 8 * s, e.To.Y + 6 * s, text.Width + 16 * s, text.Height + 6 * s);
                    dc.PushOpacity(fade);
                    dc.DrawRoundedRectangle(Fill(0x37474F, 0.88), null, box, 8 * s, 8 * s);
                    dc.DrawText(text, new Point(box.Left + 8 * s, box.Top + 3 * s));
                    dc.Pop();
                }

                break;
            }

            case EffectKind.Scan:
            {
                // De las gafas sale un haz suave que abre hasta la línea; la línea baja por la zona dejando un brillo.
                var fade = Math.Min(1, Math.Min(age / 0.15, (e.Duration - age) / 0.15));
                var lineY = e.Area.Top + e.Area.Height * Math.Min(1, t * 1.05);
                var a = new Point(e.Area.Left, lineY);
                var b = new Point(e.Area.Right, lineY);
                var cone = new StreamGeometry();
                using (var g = cone.Open())
                {
                    g.BeginFigure(P(e.From), true, true);
                    g.LineTo(a, false, false);
                    g.LineTo(b, false, false);
                }

                cone.Freeze();
                dc.DrawGeometry(Fill(e.Color, 0.10 * fade), null, cone);
                var glow = new LinearGradientBrush(Color.FromArgb(0, R(e.Color), G(e.Color), B(e.Color)), Color.FromArgb((byte)(90 * fade), R(e.Color), G(e.Color), B(e.Color)), 90);
                glow.Freeze();
                dc.DrawRectangle(glow, null, new Rect(e.Area.Left, lineY - 54 * s, e.Area.Width, 54 * s));
                dc.DrawLine(Stroke(e.Color, 2.5 * s, 0.95 * fade), a, b);
                dc.DrawEllipse(Fill(0xFFFFFF, 0.9 * fade), null, P(e.From), 4 * s, 4 * s);
                break;
            }

            case EffectKind.Flash:
                dc.DrawRectangle(Fill(0xFFFFFF, 0.55 * (1 - t) * (1 - t)), null, e.Area);
                break;
        }
    }

    /// <summary>Una banderita de colores en un palito; el pie en <paramref name="foot"/>.</summary>
    private static void DrawFlag(DrawingContext dc, Vec2 foot, double s, double age, uint color, double alpha, double spin)
    {
        if (alpha <= 0)
        {
            return;
        }

        dc.PushOpacity(alpha);
        if (spin != 0)
        {
            dc.PushTransform(new RotateTransform(spin * 180 / Math.PI, foot.X, foot.Y - 20 * s));
        }

        var top = foot - new Vec2(0, 46 * s);
        dc.DrawLine(Stroke(0x8D6E63, 3 * s), P(foot), P(top));
        var wave = Math.Sin(age * 7) * 4 * s;
        var flag = new StreamGeometry();
        using (var g = flag.Open())
        {
            g.BeginFigure(P(top), true, true);
            g.BezierTo(P(top + new Vec2(10 * s, -4 * s + wave * 0.4)), P(top + new Vec2(20 * s, 4 * s - wave * 0.4)), P(top + new Vec2(30 * s, 2 * s + wave)), true, true);
            g.LineTo(P(top + new Vec2(28 * s, 22 * s + wave)), true, true);
            g.BezierTo(P(top + new Vec2(18 * s, 24 * s - wave * 0.4)), P(top + new Vec2(8 * s, 16 * s + wave * 0.4)), P(top + new Vec2(0, 20 * s)), true, true);
        }

        flag.Freeze();
        dc.DrawGeometry(Fill(color), Stroke(0xFFFFFF, 1.5 * s, 0.8), flag);
        dc.DrawEllipse(Fill(0xFFD54F), null, P(top), 3.5 * s, 3.5 * s);
        if (spin != 0)
        {
            dc.Pop();
        }

        dc.Pop();
    }

    private static void Star(DrawingContext dc, Vec2 at, double r, double rotation, uint color, double alpha)
    {
        if (r <= 0.3 || alpha <= 0)
        {
            return;
        }

        var star = new StreamGeometry();
        using (var g = star.Open())
        {
            for (var i = 0; i < 8; i++)
            {
                var angle = rotation + i * Math.PI / 4;
                var radius = i % 2 == 0 ? r : r * 0.38;
                var p = new Point(at.X + Math.Cos(angle) * radius, at.Y + Math.Sin(angle) * radius);
                if (i == 0)
                {
                    g.BeginFigure(p, true, true);
                }
                else
                {
                    g.LineTo(p, false, false);
                }
            }
        }

        star.Freeze();
        dc.DrawGeometry(Fill(color, alpha), null, star);
    }

    private static double Ease(double t) => 1 - (1 - t) * (1 - t);

    private static Point P(Vec2 v) => new(v.X, v.Y);

    private static byte R(uint rgb) => (byte)(rgb >> 16);

    private static byte G(uint rgb) => (byte)(rgb >> 8);

    private static byte B(uint rgb) => (byte)rgb;

    private static SolidColorBrush Fill(uint rgb, double alpha = 1)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), R(rgb), G(rgb), B(rgb)));
        brush.Freeze();
        return brush;
    }

    private static Pen Stroke(uint rgb, double width, double alpha = 1)
    {
        var pen = new Pen(Fill(rgb, alpha), Math.Max(0.5, width)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }

    public void Dispose() => _surface?.Dispose();
}
