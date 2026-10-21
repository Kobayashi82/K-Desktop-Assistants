using System.Windows;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;
using Lander.Assistant.Rendering.Skins;
using Lander.Core.Geometry;
using SdkRect = KDesktop.Assistant.Sdk.PixelRect;

namespace Lander.Assistant.Rendering;

internal enum EffectKind
{
    /// <summary>El proyectil, de <see cref="ManoeuvreEffect.From"/> a <see cref="ManoeuvreEffect.To"/>.</summary>
    Bolt,

    /// <summary>El impacto en el botón: destello, onda y chispas.</summary>
    Impact,

    /// <summary>El foco de luz del lander a un punto (señalar).</summary>
    Spotlight,

    /// <summary>Ondas en un punto (llamar a una ventana para traerla delante).</summary>
    Ping,

    /// <summary>Una línea que barre una zona de arriba abajo (escanear).</summary>
    Scan,

    /// <summary>El flash de la cámara sobre una zona.</summary>
    Flash,

    /// <summary>La carga del disparo fuerte, alrededor del lander.</summary>
    Charge,
}

/// <param name="From">Origen (proyectil, foco); lo mueve el lander mientras vuela.</param>
/// <param name="Area">Para el escáner y el flash.</param>
/// <param name="Size">1 = normal; más, el disparo cargado.</param>
internal sealed record ManoeuvreEffect(EffectKind Kind, double Start, double Duration, Vec2 From, Vec2 To, Color Color, double Scale,
    Rect Area = default, double Size = 1)
{
    public Vec2 From { get; set; } = From;

    public Vec2 To { get; set; } = To;
}

/// <summary>
/// Los efectos de las maniobras para K-Desktop (disparos, impactos, foco, escáner, flash): lejos del lander, así que van
/// en una superficie propia por delante de todo, del tamaño justo de lo que hay que dibujar, y solo mientras dura.
/// </summary>
internal sealed class ManoeuvreLayer(IRendering rendering) : IDisposable
{
    private readonly List<ManoeuvreEffect> _effects = [];
    private ISurface? _surface;

    public bool IsActive => _effects.Count > 0;

    public ManoeuvreEffect Add(ManoeuvreEffect effect)
    {
        _effects.Add(effect);
        return effect;
    }

    public void Remove(ManoeuvreEffect effect) => _effects.Remove(effect);

    public void Clear()
    {
        _effects.Clear();
        _surface?.Hide();
    }

    public void Render(double now)
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
            bounds.Union(Extent(effect, Math.Clamp((now - effect.Start) / effect.Duration, 0, 1)));
        }

        var x = (int)Math.Floor(bounds.X);
        var y = (int)Math.Floor(bounds.Y);
        var width = Math.Max(2, (int)Math.Ceiling(bounds.Right) - x);
        var height = Math.Max(2, (int)Math.Ceiling(bounds.Bottom) - y);
        _surface ??= rendering.CreateSurface(SurfaceLayer.Front);
        _surface.Present(SdkRect.FromSize(x, y, width, height), dc =>
        {
            foreach (var effect in _effects)
            {
                var t = Math.Clamp((now - effect.Start) / effect.Duration, 0, 1);
                Draw(dc, effect, t, now);
            }
        });
    }

    private static Rect Extent(ManoeuvreEffect effect, double t)
    {
        var pad = 40 * effect.Scale * effect.Size;
        switch (effect.Kind)
        {
            case EffectKind.Scan:
                // Solo la franja de la línea (la zona puede ser un monitor entero).
                var lineY = effect.Area.Top + effect.Area.Height * t;
                return new Rect(effect.Area.Left, lineY - 50 * effect.Scale, effect.Area.Width, 54 * effect.Scale);

            case EffectKind.Flash:
                return effect.Area;

            case EffectKind.Bolt:
            case EffectKind.Spotlight:
                var r = new Rect(new Point(effect.From.X, effect.From.Y), new Point(effect.To.X, effect.To.Y));
                r.Inflate(pad, pad);
                return r;

            default:
                return new Rect(effect.To.X - pad * 2, effect.To.Y - pad * 2, pad * 4, pad * 4);
        }
    }

    private static void Draw(DrawingContext dc, ManoeuvreEffect e, double t, double now)
    {
        var s = e.Scale;
        var to = new Point(e.To.X, e.To.Y);
        switch (e.Kind)
        {
            case EffectKind.Bolt:
            {
                // La cabeza avanza en línea recta; la estela se alarga hasta su largo y se queda detrás.
                var from = e.From;
                var head = Vec2.Lerp(from, e.To, t);
                var travelled = Vec2.Distance(from, head);
                var direction = travelled > 0.01 ? (head - from) / travelled : new Vec2(1, 0);
                var tail = head - direction * Math.Min(travelled, 70 * s * e.Size);
                var h = new Point(head.X, head.Y);
                var tl = new Point(tail.X, tail.Y);
                dc.DrawLine(Gfx.Pen(e.Color.WithAlpha(0.35), 9 * s * e.Size), tl, h);
                dc.DrawLine(Gfx.Pen(e.Color.WithAlpha(0.85), 4 * s * e.Size), tl, h);
                dc.DrawLine(Gfx.Pen(Colors.White.WithAlpha(0.95), 1.8 * s * e.Size), new Point((tl.X + h.X * 2) / 3, (tl.Y + h.Y * 2) / 3), h);
                dc.DrawEllipse(LanderParts.Glow(e.Color, 0.9), null, h, 13 * s * e.Size, 13 * s * e.Size);
                dc.DrawEllipse(Gfx.Brush(Colors.White), null, h, 2.6 * s * e.Size, 2.6 * s * e.Size);
                break;
            }

            case EffectKind.Impact:
            {
                var fade = 1 - t;
                dc.DrawEllipse(LanderParts.Glow(e.Color, fade), null, to, (18 + 26 * t) * s * e.Size, (18 + 26 * t) * s * e.Size);
                dc.DrawEllipse(null, Gfx.Pen(e.Color.WithAlpha(fade), 2.4 * s), to, (6 + 34 * t) * s * e.Size, (6 + 34 * t) * s * e.Size);
                if (t < 0.25)
                {
                    dc.DrawEllipse(Gfx.Brush(Colors.White.WithAlpha(1 - t * 4)), null, to, 7 * s * e.Size, 7 * s * e.Size);
                }

                // Chispas en estrella, que salen y se apagan.
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * Math.PI / 4 + 0.4;
                    var inner = (6 + 22 * t) * s * e.Size;
                    var outer = inner + 10 * s * (1 - t) * e.Size;
                    var c = Math.Cos(angle);
                    var n = Math.Sin(angle);
                    dc.DrawLine(Gfx.Pen(Colors.White.WithAlpha(fade), 1.6 * s), new Point(to.X + c * inner, to.Y + n * inner), new Point(to.X + c * outer, to.Y + n * outer));
                }

                break;
            }

            case EffectKind.Spotlight:
            {
                // Entra y sale con suavidad; un cono translúcido y un círculo de luz que respira sobre el punto.
                var level = Math.Min(1, Math.Min(t / 0.15, (1 - t) / 0.2));
                var from = new Point(e.From.X, e.From.Y);
                var axis = e.To - e.From;
                var length = axis.Length;
                if (length < 1)
                {
                    break;
                }

                var normal = new Vec2(-axis.Y, axis.X) / length;
                var radius = (20 + 3 * Math.Sin(now * 6)) * s;
                var a = e.To + normal * radius;
                var b = e.To - normal * radius;
                var near = normal * 4 * s;
                var cone = Gfx.Polygon(from.X + near.X, from.Y + near.Y, a.X, a.Y, b.X, b.Y, from.X - near.X, from.Y - near.Y);
                dc.DrawGeometry(Gfx.Gradient(from, to, (0, e.Color.WithAlpha(0.45 * level)), (1, e.Color.WithAlpha(0.12 * level))), null, cone);
                dc.DrawEllipse(LanderParts.Glow(e.Color, 0.7 * level), null, to, radius * 1.6, radius * 1.6);
                dc.DrawEllipse(null, Gfx.Pen(e.Color.WithAlpha(0.8 * level), 2 * s), to, radius, radius);
                break;
            }

            case EffectKind.Ping:
            {
                for (var i = 0; i < 2; i++)
                {
                    var k = t * 1.4 - i * 0.4;
                    if (k is <= 0 or >= 1)
                    {
                        continue;
                    }

                    dc.DrawEllipse(null, Gfx.Pen(e.Color.WithAlpha(1 - k), 2.6 * s), to, (8 + 46 * k) * s, (8 + 46 * k) * s * 0.6);
                }

                break;
            }

            case EffectKind.Scan:
            {
                var area = e.Area;
                var lineY = area.Top + area.Height * t;
                var band = 46 * s;
                var top = Math.Max(area.Top, lineY - band);
                if (lineY - top > 1)
                {
                    dc.DrawRectangle(Gfx.Vertical((0, e.Color.WithAlpha(0)), (1, e.Color.WithAlpha(0.28))), null,
                        new Rect(area.Left, top, area.Width, lineY - top));
                }

                dc.DrawLine(Gfx.Pen(e.Color.WithAlpha(0.95), 2.4 * s, round: false), new Point(area.Left, lineY), new Point(area.Right, lineY));
                break;
            }

            case EffectKind.Flash:
                dc.DrawRectangle(Gfx.Brush(Colors.White.WithAlpha(0.42 * (1 - t) * (1 - t))), null, e.Area);
                break;

            case EffectKind.Charge:
            {
                // Puntos de luz que se juntan en el lander y un brillo que crece.
                dc.DrawEllipse(LanderParts.Glow(e.Color, 0.25 + 0.6 * t), null, to, (14 + 14 * t) * s, (14 + 14 * t) * s);
                for (var i = 0; i < 6; i++)
                {
                    var angle = i * Math.PI / 3 + now * 5;
                    var distance = (36 * (1 - t) + 6) * s;
                    var p = new Point(to.X + Math.Cos(angle) * distance, to.Y + Math.Sin(angle) * distance);
                    dc.DrawEllipse(Gfx.Brush(e.Color.WithAlpha(0.4 + 0.6 * t)), null, p, 2.2 * s, 2.2 * s);
                }

                break;
            }
        }
    }

    public void Dispose()
    {
        _surface?.Dispose();
        _surface = null;
    }
}
