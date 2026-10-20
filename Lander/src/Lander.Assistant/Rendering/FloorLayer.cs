using System.Windows;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;
using Lander.Core.Terrain;

namespace Lander.Assistant.Rendering;

/// <summary>
/// Pilotando a mano, los bordes del contenido de las ventanas donde se puede posar: una línea discontinua y tenue sobre
/// cada uno, en su propia superficie por detrás del lander. Sin ella no se sabe dónde hay suelo dentro de una página.
/// </summary>
internal sealed class FloorLayer(IRendering rendering) : IDisposable
{
    /// <summary>Alto de cada superficie: la línea y un poco de brillo alrededor (lo justo: tapa el borde en la captura).</summary>
    private const int Thickness = 6;

    private readonly List<ISurface> _pool = [];
    private readonly List<(int X, int Y, int Width)> _shown = [];

    public bool HasVisible => _shown.Count > 0;

    /// <summary>Lo que ocupa cada línea en pantalla (la vista del contenido no lo analiza).</summary>
    public IReadOnlyList<Lander.Core.Geometry.PixelRect> Drawn { get; private set; } = [];

    /// <summary>Dibuja <paramref name="floors"/> (solo si han cambiado).</summary>
    public void Render(IReadOnlyList<Platform> floors, double scale, bool dark)
    {
        var wanted = floors.Select(f => (X: (int)Math.Round(f.Left), Y: (int)Math.Round(f.Y), Width: Math.Max(1, (int)Math.Round(f.Width)))).ToList();
        if (wanted.SequenceEqual(_shown))
        {
            return;
        }

        var accent = Icons.Accent(dark);
        var line = new Pen(new SolidColorBrush(accent.WithAlpha(0.75)), Math.Max(1.5, 1.6 * scale)) { DashStyle = new DashStyle([4, 3], 0), DashCap = PenLineCap.Round };
        line.Freeze();
        var glow = new Pen(new SolidColorBrush(accent.WithAlpha(0.18)), Thickness - 1);
        glow.Freeze();
        for (var i = 0; i < wanted.Count; i++)
        {
            if (i >= _pool.Count)
            {
                _pool.Add(rendering.CreateSurface(SurfaceLayer.Back));
            }

            var (x, y, width) = wanted[i];
            var top = y - Thickness / 2;
            _pool[i].Present(KDesktop.Assistant.Sdk.PixelRect.FromSize(x, top, width, Thickness), dc =>
            {
                var from = new Point(x + 2, y);
                var to = new Point(x + width - 2, y);
                dc.DrawLine(glow, from, to);
                dc.DrawLine(line, from, to);
            });
        }

        for (var i = wanted.Count; i < _shown.Count; i++)
        {
            _pool[i].Hide();
        }

        _shown.Clear();
        _shown.AddRange(wanted);
        Drawn = [.. wanted.Select(w => new Lander.Core.Geometry.PixelRect(w.X, w.Y - Thickness / 2, w.X + w.Width, w.Y - Thickness / 2 + Thickness))];
    }

    public void HideAll()
    {
        for (var i = 0; i < _shown.Count; i++)
        {
            _pool[i].Hide();
        }

        _shown.Clear();
        Drawn = [];
    }

    public void Dispose()
    {
        foreach (var surface in _pool)
        {
            surface.Dispose();
        }

        _pool.Clear();
        _shown.Clear();
    }
}
