using System.Numerics;
using Kobii.Core.Animation;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Kobii.Assistant.Rendering;

/// <summary>
/// Dibuja con Direct2D lo que va encima de la criatura: lo que lleva en la mano y las partículas (los bocadillos
/// los pone el host). Todo en coordenadas del mundo; <c>origin</c> es la esquina del lienzo en el mundo.
/// </summary>
internal sealed partial class ScenePainter : IDisposable
{
    private const string FontFamily = "Segoe UI Variable Text";
    private const string FallbackFont = "Segoe UI";

    private readonly Gpu _gpu;
    private readonly ID2D1SolidColorBrush _brush;
    private readonly ID2D1PathGeometry1 _heart;
    private readonly ID2D1PathGeometry1 _star;
    private readonly ID2D1PathGeometry1 _shard;
    private readonly ID2D1PathGeometry1 _wave;
    private readonly ID2D1PathGeometry1 _vein;
    private readonly ID2D1StrokeStyle1 _round;
    private readonly Dictionary<int, IDWriteTextFormat> _formats = [];
    private string? _fontFamily;

    public ScenePainter(Gpu gpu)
    {
        _gpu = gpu;
        _brush = gpu.D2D.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _heart = CreateHeart(gpu.D2DFactory);
        _star = CreateStar(gpu.D2DFactory);
        _shard = CreateShard(gpu.D2DFactory);
        _wave = CreateWave(gpu.D2DFactory);
        _vein = CreateVein(gpu.D2DFactory);
        _round = gpu.D2DFactory.CreateStrokeStyle(new StrokeStyleProperties1 { StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round });
    }

    /// <summary>Lo que lleva en la mano o puesto, por delante del cuerpo y detrás de las manos.</summary>
    public void DrawHeld(ID2D1DeviceContext dc, Vec2 origin, KobiiFrame frame)
    {
        if (frame.Props.Prop != Prop.None || frame.Props.Wear != Wear.None)
        {
            DrawProps(dc, origin, frame.Props);
        }

        dc.Transform = Matrix3x2.Identity;
    }

    /// <summary>Las partículas (en su propia superficie, que empieza en <paramref name="origin"/>).</summary>
    public void DrawParticles(ID2D1DeviceContext dc, Vec2 origin, IReadOnlyList<Particle> particles)
    {
        foreach (var particle in particles)
        {
            DrawParticle(dc, origin, particle);
        }

        dc.Transform = Matrix3x2.Identity;
    }

    public void Dispose()
    {
        foreach (var format in _formats.Values)
        {
            format.Dispose();
        }

        _round.Dispose();
        _wave.Dispose();
        _vein.Dispose();
        _shard.Dispose();
        _star.Dispose();
        _heart.Dispose();
        _brush.Dispose();
    }

    // Partículas -----------------------------------------------------------------------------------------

    private void DrawParticle(ID2D1DeviceContext dc, Vec2 origin, Particle p)
    {
        var position = ToVector(p.Position - origin);
        var alpha = (float)p.Alpha;
        var size = (float)p.Size;
        switch (p.Kind)
        {
            case EmoteKind.Heart:
                FillGeometry(dc, _heart, position, size * (float)(0.8 + 0.2 * Math.Min(1, p.Age * 6)), 0, p.Color, alpha);
                break;
            case EmoteKind.Sparkle:
                FillGeometry(dc, _star, position, size * (float)(0.6 + 0.4 * Math.Sin(p.Age * 18)), (float)p.Rotation, p.Color, alpha);
                break;
            case EmoteKind.Shell:
                FillGeometry(dc, _shard, position, size, (float)p.Rotation, p.Color, alpha);
                break;
            case EmoteKind.Sleep:
                DrawLabel(dc, "z", position, size * (float)(0.7 + 0.6 * p.T), p.Color, alpha, bold: true);
                break;
            case EmoteKind.Note:
                DrawLabel(dc, "♪", position, size, p.Color, alpha, bold: true);
                break;
            case EmoteKind.Question:
                DrawLabel(dc, "?", position, size * (float)Math.Min(1, 0.6 + p.Age * 4), p.Color, alpha, bold: true);
                break;
            case EmoteKind.Exclamation:
                DrawLabel(dc, "!", position, size * (float)Math.Min(1, 0.6 + p.Age * 4), p.Color, alpha, bold: true);
                break;
            case EmoteKind.Dust:
                dc.Transform = Matrix3x2.Identity;
                Fill(dc, new Ellipse(position, size * (float)(1 + p.T), size * (float)(0.7 + p.T * 0.6)), p.Color, alpha * 0.5f);
                break;
            case EmoteKind.Warmth:
                dc.Transform = Matrix3x2.Identity;
                Fill(dc, new Ellipse(position, size * 1.8f, size * 1.8f), p.Color, alpha * 0.2f);
                Fill(dc, new Ellipse(position, size, size), p.Color, alpha * 0.6f);
                break;
            case EmoteKind.Bubble:
                dc.Transform = Matrix3x2.Identity;
                Fill(dc, new Ellipse(position, size, size), 0xFFFFFF, alpha * 0.3f);
                Stroke(dc, new Ellipse(position, size, size), p.Color, Math.Max(1, size * 0.15f), alpha * 0.9f);
                Fill(dc, new Ellipse(position - new Vector2(size * 0.35f, size * 0.35f), size * 0.25f, size * 0.25f), 0xFFFFFF, alpha * 0.9f);
                break;
            case EmoteKind.Drop:
                dc.Transform = Matrix3x2.Identity;
                Fill(dc, new Ellipse(position, size * 0.7f, size), p.Color, alpha * 0.85f);
                Fill(dc, new Ellipse(position - new Vector2(size * 0.2f, size * 0.3f), size * 0.2f, size * 0.3f), 0xFFFFFF, alpha * 0.7f);
                break;
            case EmoteKind.Stink:
                dc.Transform = Matrix3x2.CreateScale(size) * Matrix3x2.CreateTranslation(position);
                _brush.Color = ToColor(p.Color, alpha * 0.8f);
                dc.DrawGeometry(_wave, _brush, 0.22f);
                dc.Transform = Matrix3x2.CreateScale(size * 0.8f) * Matrix3x2.CreateTranslation(position + new Vector2(size * 0.9f, size * 0.4f));
                dc.DrawGeometry(_wave, _brush, 0.25f);
                dc.Transform = Matrix3x2.Identity;
                break;
            case EmoteKind.Fly:
                dc.Transform = Matrix3x2.Identity;
                var flap = (float)(0.6 + 0.4 * Math.Sin(p.Age * 70));
                Fill(dc, new Ellipse(position + new Vector2(-size * 0.7f, -size * 0.9f), size * 0.8f, size * 0.55f * flap), 0xFFFFFF, alpha * 0.75f);
                Fill(dc, new Ellipse(position + new Vector2(size * 0.7f, -size * 0.9f), size * 0.8f, size * 0.55f * flap), 0xFFFFFF, alpha * 0.75f);
                Fill(dc, new Ellipse(position, size, size * 0.85f), p.Color, alpha);
                break;
            case EmoteKind.Flower:
                dc.Transform = Matrix3x2.Identity;
                Line(dc, position, position + new Vector2(0, size * 1.6f), 0x7CB342, size * 0.3f);
                for (var k = 0; k < 5; k++)
                {
                    var a = p.Rotation + k * Math.Tau / 5;
                    Fill(dc, new Ellipse(position + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * size * 0.55f, size * 0.42f, size * 0.42f), p.Color, alpha);
                }

                Fill(dc, new Ellipse(position, size * 0.32f, size * 0.32f), 0xFFD54F, alpha);
                break;
            case EmoteKind.Paint:
                dc.Transform = Matrix3x2.Identity;
                Fill(dc, new Ellipse(position, size, size * 1.15f), p.Color, alpha);
                break;
            case EmoteKind.Leaf:
                dc.Transform = Matrix3x2.CreateRotation((float)p.Rotation, position);
                Fill(dc, new Ellipse(position, size, size * 0.5f), p.Color, alpha);
                _brush.Color = ToColor(0x8D4A21, alpha * 0.8f);
                dc.DrawLine(position - new Vector2(size * 1.3f, 0), position + new Vector2(size * 0.8f, 0), _brush, Math.Max(1, size * 0.15f));
                dc.Transform = Matrix3x2.Identity;
                break;
            case EmoteKind.Fluff:
                dc.Transform = Matrix3x2.Identity;
                Fill(dc, new Ellipse(position, size, size * 0.8f), p.Color, alpha * 0.9f);
                Fill(dc, new Ellipse(position + new Vector2(size * 0.6f, -size * 0.3f), size * 0.6f, size * 0.5f), p.Color, alpha * 0.8f);
                Fill(dc, new Ellipse(position - new Vector2(size * 0.5f, size * 0.2f), size * 0.5f, size * 0.45f), p.Color, alpha * 0.8f);
                break;

            case EmoteKind.Burst:
            {
                // Estrella de ocho puntas que crece de golpe, con el centro blanco.
                var grow = (float)(0.55 + 0.6 * Easing.Apply(Ease.Out, Math.Min(1, p.Age / 0.12)));
                var fade = (float)Math.Clamp(1 - (p.Age - 0.12) / 0.26, 0, 1);
                FillGeometry(dc, _star, position, size * grow, (float)p.Rotation, p.Color, fade);
                FillGeometry(dc, _star, position, size * grow * 0.8f, (float)p.Rotation + MathF.PI / 4, 0xFFB300, fade);
                Fill(dc, new Ellipse(position, size * grow * 0.32f, size * grow * 0.32f), 0xFFFFFF, fade);
                break;
            }

            case EmoteKind.Confetti:
            {
                // Un papelito que gira en el aire: se estrecha al ponerse de canto y por detrás se ve más apagado.
                var flip = (float)Math.Cos(p.Age * (7 + Math.Abs(p.Spin)) + p.Anchor.Y);
                var width = size * (0.25f + 0.75f * Math.Abs(flip));
                dc.Transform = Matrix3x2.CreateRotation((float)p.Rotation, position);
                _brush.Color = ToColor(p.Color, alpha * (flip >= 0 ? 1 : 0.72f));
                dc.FillRectangle(new RawRectF(position.X - width / 2, position.Y - size * 0.32f, position.X + width / 2, position.Y + size * 0.32f), _brush);
                dc.Transform = Matrix3x2.Identity;
                break;
            }

            case EmoteKind.Anger:
            {
                // 💢: aparece de golpe y late (cada vez menos), con un borde blanco para que se lea sobre cualquier fondo.
                var pop = (float)(Easing.Apply(Ease.Out, Math.Min(1, p.Age / 0.1)) * (1 + 0.16 * Math.Sin(p.Age * 15) * Math.Exp(-p.Age * 1.5)));
                for (var pass = 0; pass < 2; pass++)
                {
                    _brush.Color = ToColor(pass == 0 ? 0xFFFFFFu : p.Color, alpha * (pass == 0 ? 0.85f : 1));
                    for (var k = 0; k < 4; k++)
                    {
                        dc.Transform = Matrix3x2.CreateScale(size * pop) * Matrix3x2.CreateRotation((float)p.Rotation + k * MathF.PI / 2) * Matrix3x2.CreateTranslation(position);
                        dc.DrawGeometry(_vein, _brush, pass == 0 ? 0.5f : 0.27f, _round);
                    }
                }

                dc.Transform = Matrix3x2.Identity;
                break;
            }

            default:
                dc.Transform = Matrix3x2.Identity;
                Fill(dc, new Ellipse(position, size, size), p.Color, alpha);
                break;
        }
    }

    // Utilidades ----------------------------------------------------------------------------------------

    private IDWriteTextFormat Format(float size, bool bold)
    {
        var key = (int)(size * 10) * 2 + (bold ? 1 : 0);
        if (_formats.TryGetValue(key, out var format))
        {
            return format;
        }

        _fontFamily ??= FontExists(FontFamily) ? FontFamily : FallbackFont;
        format = _gpu.DWrite.CreateTextFormat(_fontFamily, bold ? FontWeight.Bold : FontWeight.Normal, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, size);
        format.WordWrapping = WordWrapping.Wrap;
        _formats[key] = format;
        return format;
    }

    private bool FontExists(string family)
    {
        using var collection = _gpu.DWrite.GetSystemFontCollection(false);
        return collection.FindFamilyName(family, out _);
    }

    private void DrawLabel(ID2D1DeviceContext dc, string text, Vector2 center, float size, uint color, float alpha, bool bold)
    {
        dc.Transform = Matrix3x2.Identity;
        using var layout = _gpu.DWrite.CreateTextLayout(text, Format(size, bold), 200, 200);
        var metrics = layout.Metrics;
        var position = center - new Vector2(metrics.Width / 2, metrics.Height / 2);

        // Contorno claro para que se lea sobre cualquier fondo.
        _brush.Color = ToColor(0xFFFFFF, alpha * 0.8f);
        foreach (var offset in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) })
        {
            dc.DrawTextLayout(position + offset * Math.Max(1, size / 12), layout, _brush, DrawTextOptions.None);
        }

        _brush.Color = ToColor(color, alpha);
        dc.DrawTextLayout(position, layout, _brush, DrawTextOptions.None);
    }

    private void FillGeometry(ID2D1DeviceContext dc, ID2D1Geometry geometry, Vector2 position, float size, float rotation, uint color, float alpha)
    {
        dc.Transform = Matrix3x2.CreateRotation(rotation) * Matrix3x2.CreateScale(size) * Matrix3x2.CreateTranslation(position);
        _brush.Color = ToColor(color, alpha);
        dc.FillGeometry(geometry, _brush);
        dc.Transform = Matrix3x2.Identity;
    }

    private void FillGeometry(ID2D1DeviceContext dc, ID2D1Geometry geometry, uint color, float alpha)
    {
        _brush.Color = ToColor(color, alpha);
        dc.FillGeometry(geometry, _brush);
    }

    private void DrawGeometry(ID2D1DeviceContext dc, ID2D1Geometry geometry, uint color, float width, float alpha)
    {
        _brush.Color = ToColor(color, alpha);
        dc.DrawGeometry(geometry, _brush, width);
    }

    private void Fill(ID2D1DeviceContext dc, Ellipse ellipse, uint color, float alpha)
    {
        _brush.Color = ToColor(color, alpha);
        dc.FillEllipse(ellipse, _brush);
    }

    private void Fill(ID2D1DeviceContext dc, RoundedRectangle rect, uint color, float alpha)
    {
        _brush.Color = ToColor(color, alpha);
        dc.FillRoundedRectangle(rect, _brush);
    }

    private void Stroke(ID2D1DeviceContext dc, Ellipse ellipse, uint color, float width, float alpha = 1)
    {
        _brush.Color = ToColor(color, alpha);
        dc.DrawEllipse(ellipse, _brush, width);
    }

    private void Line(ID2D1DeviceContext dc, Vector2 from, Vector2 to, uint color, float width)
    {
        _brush.Color = ToColor(color, 1);
        dc.DrawLine(from, to, _brush, width);
    }

    private static Color4 ToColor(uint rgb, float alpha) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    private static Vector2 ToVector(Vec2 v) => new((float)v.X, (float)v.Y);

    /// <summary>Corazón de 2×2 unidades centrado en el origen.</summary>
    private static ID2D1PathGeometry1 CreateHeart(ID2D1Factory1 factory)
    {
        var geometry = factory.CreatePathGeometry();
        using var sink = geometry.Open();
        sink.BeginFigure(new Vector2(0, 0.9f), FigureBegin.Filled);
        sink.AddBezier(new BezierSegment { Point1 = new Vector2(-1.3f, 0.0f), Point2 = new Vector2(-1.0f, -1.2f), Point3 = new Vector2(0, -0.5f) });
        sink.AddBezier(new BezierSegment { Point1 = new Vector2(1.0f, -1.2f), Point2 = new Vector2(1.3f, 0.0f), Point3 = new Vector2(0, 0.9f) });
        sink.EndFigure(FigureEnd.Closed);
        sink.Close();
        return geometry;
    }

    /// <summary>Destello de cuatro puntas.</summary>
    private static ID2D1PathGeometry1 CreateStar(ID2D1Factory1 factory)
    {
        var geometry = factory.CreatePathGeometry();
        using var sink = geometry.Open();
        sink.BeginFigure(new Vector2(0, -1), FigureBegin.Filled);
        sink.AddBezier(new BezierSegment { Point1 = new Vector2(0.1f, -0.1f), Point2 = new Vector2(0.1f, -0.1f), Point3 = new Vector2(1, 0) });
        sink.AddBezier(new BezierSegment { Point1 = new Vector2(0.1f, 0.1f), Point2 = new Vector2(0.1f, 0.1f), Point3 = new Vector2(0, 1) });
        sink.AddBezier(new BezierSegment { Point1 = new Vector2(-0.1f, 0.1f), Point2 = new Vector2(-0.1f, 0.1f), Point3 = new Vector2(-1, 0) });
        sink.AddBezier(new BezierSegment { Point1 = new Vector2(-0.1f, -0.1f), Point2 = new Vector2(-0.1f, -0.1f), Point3 = new Vector2(0, -1) });
        sink.EndFigure(FigureEnd.Closed);
        sink.Close();
        return geometry;
    }

    /// <summary>Onda vertical (mal olor) de 1 × 2 unidades, de abajo arriba.</summary>
    private static ID2D1PathGeometry1 CreateWave(ID2D1Factory1 factory)
    {
        var geometry = factory.CreatePathGeometry();
        using var sink = geometry.Open();
        sink.BeginFigure(new Vector2(0, 1), FigureBegin.Hollow);
        sink.AddBezier(new BezierSegment { Point1 = new Vector2(0.6f, 0.6f), Point2 = new Vector2(-0.6f, 0.2f), Point3 = new Vector2(0, -0.2f) });
        sink.AddBezier(new BezierSegment { Point1 = new Vector2(0.6f, -0.5f), Point2 = new Vector2(-0.4f, -0.8f), Point3 = new Vector2(0.1f, -1) });
        sink.EndFigure(FigureEnd.Open);
        sink.Close();
        return geometry;
    }

    /// <summary>
    /// Un trazo de la vena de enfado (💢): la esquina de arriba a la derecha de un cuadrado de 2×2, curvada hacia
    /// el centro. Girado cuatro veces forma el símbolo entero.
    /// </summary>
    private static ID2D1PathGeometry1 CreateVein(ID2D1Factory1 factory)
    {
        var geometry = factory.CreatePathGeometry();
        using var sink = geometry.Open();
        sink.BeginFigure(new Vector2(0.18f, -0.95f), FigureBegin.Hollow);
        sink.AddBezier(new BezierSegment { Point1 = new Vector2(0.22f, -0.42f), Point2 = new Vector2(0.42f, -0.22f), Point3 = new Vector2(0.95f, -0.18f) });
        sink.EndFigure(FigureEnd.Open);
        sink.Close();
        return geometry;
    }

    /// <summary>Trozo de cáscara.</summary>
    private static ID2D1PathGeometry1 CreateShard(ID2D1Factory1 factory)
    {
        var geometry = factory.CreatePathGeometry();
        using var sink = geometry.Open();
        sink.BeginFigure(new Vector2(-1, -0.4f), FigureBegin.Filled);
        sink.AddLine(new Vector2(0.2f, -1));
        sink.AddLine(new Vector2(1, 0.3f));
        sink.AddLine(new Vector2(-0.3f, 0.8f));
        sink.EndFigure(FigureEnd.Closed);
        sink.Close();
        return geometry;
    }
}
