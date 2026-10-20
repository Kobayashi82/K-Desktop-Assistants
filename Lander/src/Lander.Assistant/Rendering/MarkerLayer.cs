using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;
using Lander.Core.Game;
using Lander.Core.Geometry;

namespace Lander.Assistant.Rendering;

internal enum MarkerKind
{
    /// <summary>Cristal flotante que hay que recoger.</summary>
    Crystal,

    /// <summary>Baliza que señala una plataforma de destino.</summary>
    Beacon,
}

/// <param name="Position">Cristal: su centro. Baliza: el punto de la plataforma que señala.</param>
/// <param name="Label">Texto corto en lugar del icono (el número de una baliza de carrera).</param>
/// <param name="Alpha">Opacidad: las balizas que vienen después se ven más tenues.</param>
internal readonly record struct MarkerSpec(MarkerKind Kind, Vec2 Position, double Scale, BubbleIcon Icon = BubbleIcon.Star, ImageSource? AppIcon = null, string? Label = null, double Alpha = 1);

/// <summary>
/// Marcadores de misión y de carrera: cada uno en su propia superficie del host, por detrás del lander (pueden estar
/// lejos de él, en otra ventana o en otro monitor).
/// </summary>
internal sealed class MarkerLayer(IRendering rendering) : IDisposable
{
    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private readonly List<ISurface> _pool = [];
    private int _visible;

    public bool HasVisible => _visible > 0;

    public void Render(IReadOnlyList<MarkerSpec> markers, double time, bool dark)
    {
        for (var i = 0; i < markers.Count; i++)
        {
            if (i >= _pool.Count)
            {
                _pool.Add(rendering.CreateSurface(SurfaceLayer.Back));
            }

            var marker = markers[i];
            var phase = i * 1.7;
            if (marker.Kind == MarkerKind.Crystal)
            {
                var size = Even(64 * marker.Scale);
                var x = (int)Math.Round(marker.Position.X) - size / 2;
                var y = (int)Math.Round(marker.Position.Y) - size / 2;
                Present(_pool[i], x, y, size, size, dc => DrawCrystal(dc, size, marker.Scale, time + phase));
            }
            else
            {
                var width = Even(56 * marker.Scale);
                var height = Even(110 * marker.Scale);
                var x = (int)Math.Round(marker.Position.X) - width / 2;
                var y = (int)Math.Round(marker.Position.Y) - height + (int)(10 * marker.Scale);
                Present(_pool[i], x, y, width, height, dc => DrawBeacon(dc, width, height, marker, time + phase, dark));
            }
        }

        for (var i = markers.Count; i < _visible; i++)
        {
            _pool[i].Hide();
        }

        _visible = markers.Count;
    }

    public void HideAll()
    {
        for (var i = 0; i < _visible; i++)
        {
            _pool[i].Hide();
        }

        _visible = 0;
    }

    public void Dispose()
    {
        foreach (var surface in _pool)
        {
            surface.Dispose();
        }

        _pool.Clear();
    }

    /// <summary>Los marcadores se dibujan en coordenadas propias (origen en su esquina); el host pide píxeles de pantalla.</summary>
    private static void Present(ISurface surface, int x, int y, int width, int height, Action<DrawingContext> draw) =>
        surface.Present(KDesktop.Assistant.Sdk.PixelRect.FromSize(x, y, width, height), dc =>
        {
            dc.PushTransform(new TranslateTransform(x, y));
            draw(dc);
            dc.Pop();
        });

    internal static void DrawCrystal(DrawingContext dc, int size, double scale, double time)
    {
        var center = new Point(size / 2.0, size / 2.0 + Math.Sin(time * 2.4) * 3 * scale);
        var pulse = 0.55 + 0.25 * Math.Sin(time * 3.1);
        dc.DrawEllipse(Skins.LanderParts.Glow(Gfx.Rgb(0x5CF2FF), pulse), null, center, 22 * scale, 22 * scale);

        dc.PushTransform(new MatrixTransform(1.6 * scale, 0, 0, 1.6 * scale, center.X, center.Y));
        dc.PushTransform(new RotateTransform(Math.Sin(time * 1.3) * 10));
        Icons.DrawCrystal(dc, 1);
        dc.Pop();
        dc.Pop();

        for (var i = 0; i < 3; i++)
        {
            var twinkle = Math.Max(0, Math.Sin(time * 4 + i * 2.1));
            var angle = i * 2.1 + time * 0.4;
            var point = new Point(center.X + Math.Cos(angle) * 15 * scale, center.Y + Math.Sin(angle) * 12 * scale);
            ParticleSystem.DrawSparkle(dc, point, 3.2 * scale * twinkle, Colors.White.WithAlpha(twinkle));
        }
    }

    internal static void DrawBeacon(DrawingContext dc, int width, int height, MarkerSpec marker, double time, bool dark)
    {
        var scale = marker.Scale;
        var ground = new Point(width / 2.0, height - 10 * scale);
        var accent = Icons.Accent(dark);
        var faded = marker.Alpha < 0.99;
        if (faded)
        {
            dc.PushOpacity(marker.Alpha);
        }

        // Onda que se expande sobre la plataforma.
        var wave = time * 0.8 % 1;
        dc.DrawEllipse(null, Gfx.Pen(accent.WithAlpha(1 - wave), 1.6 * scale), ground, (6 + 20 * wave) * scale, (2 + 5 * wave) * scale);

        // Flecha que bota hacia abajo.
        var bounce = Math.Abs(Math.Sin(time * 3)) * 6 * scale;
        var tip = new Point(ground.X, ground.Y - 10 * scale - bounce);
        var chevron = Gfx.Polygon(
            tip.X, tip.Y,
            tip.X - 9 * scale, tip.Y - 9 * scale,
            tip.X - 4 * scale, tip.Y - 9 * scale,
            tip.X, tip.Y - 5 * scale,
            tip.X + 4 * scale, tip.Y - 9 * scale,
            tip.X + 9 * scale, tip.Y - 9 * scale);
        dc.DrawGeometry(Gfx.Brush(accent), Gfx.Pen(Colors.White.WithAlpha(0.8), 1 * scale), chevron);

        // Icono del objetivo en un círculo.
        var iconCenter = new Point(ground.X, tip.Y - 26 * scale);
        dc.DrawEllipse(Gfx.Brush(Colors.Black.WithAlpha(0.2)), null, new Point(iconCenter.X, iconCenter.Y + 1.5 * scale), 14 * scale, 14 * scale);
        dc.DrawEllipse(Gfx.Brush(dark ? Gfx.Rgb(0x2B2B2B) : Colors.White), Gfx.Pen(accent, 1.6 * scale), iconCenter, 13 * scale, 13 * scale);
        if (marker.Label is { } label)
        {
            var ink = dark ? Gfx.Rgb(0xF2F2F2) : Gfx.Rgb(0x1F2328);
            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 14 * scale, Gfx.Brush(ink), 1);
            dc.DrawText(text, new Point(iconCenter.X - text.Width / 2, iconCenter.Y - text.Height / 2));
        }
        else
        {
            dc.PushTransform(new MatrixTransform(1.2 * scale, 0, 0, 1.2 * scale, iconCenter.X, iconCenter.Y));
            Icons.Draw(dc, marker.Icon, 0, marker.AppIcon, time, dark);
            dc.Pop();
        }

        if (faded)
        {
            dc.Pop();
        }
    }

    private static int Even(double value) => Math.Max(8, (int)Math.Ceiling(value / 2) * 2);
}
