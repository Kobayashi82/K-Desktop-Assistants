using KDesktop.Assistant.Sdk;
using Kumo.Assistant.Rendering;
using Kumo.Core.Cloud;
using Kumo.Core.Geometry;
using SdkRect = KDesktop.Assistant.Sdk.PixelRect;

namespace Kumo.Assistant;

/// <summary>
/// Dibujo de Kumo en la capa del host, en vectorial con la API de WPF (<see cref="ISurface.Present"/>; el host lo pasa a la GPU
/// con Direct2D). La nube (con sus nubecitas, que van en el mismo campo) en su superficie; las partículas en otra, por delante y
/// solo mientras las hay.
/// </summary>
public sealed partial class KumoAssistant
{
    /// <summary>Lo que tarda en aparecer al volver (tras esconderse o con una pantalla completa).</summary>
    private const double FadeInDuration = 0.35;

    private readonly KumoFrame _frame = new();
    private readonly KumoPainter _painter = new();
    private readonly ActivityBadge _badge = new();
    private ISurface _surface = null!;
    private ISurface _effects = null!;
    private IHitArea _hit = null!;
    private bool _effectsShown;
    private (int X, int Y, int W, int H) _hitKey = (int.MinValue, 0, 0, 0);
    private bool _hitShown;
    private double _fadeStart = double.NegativeInfinity;

    private void AttachRendering()
    {
        _surface = _host.Rendering.CreateSurface();
        _effects = _host.Rendering.CreateSurface(SurfaceLayer.Front);
        _hit = _host.Pointer.CreateHitArea();
        _hit.Cursor = PointerCursor.Hand;
    }

    private void Render()
    {
        if (_hidden)
        {
            return;
        }

        var fieldClock = System.Diagnostics.Stopwatch.GetTimestamp();
        _frame.Dark = _host.World.System.DarkMode;
        _frame.Update(_cloud, Opacity());
        _statFieldMs += System.Diagnostics.Stopwatch.GetElapsedTime(fieldClock).TotalMilliseconds;
        var (left, top, width, height) = _frame.PixelBounds();
        var badge = BadgePlace();
        if (badge is var (center, from))
        {
            var box = ActivityBadge.Bounds(center, from, _cloud.Unit);
            var right = Math.Max(left + width, (int)Math.Ceiling(box.Right) + 2);
            var bottom = Math.Max(top + height, (int)Math.Ceiling(box.Bottom) + 2);
            left = Math.Min(left, (int)Math.Floor(box.Left) - 2);
            top = Math.Min(top, (int)Math.Floor(box.Top) - 2);
            width = Math.Clamp(right - left, 1, 4096);
            height = Math.Clamp(bottom - top, 1, 4096);
        }

        _surface.Present(SdkRect.FromSize(left, top, width, height), dc =>
        {
            var drawClock = System.Diagnostics.Stopwatch.GetTimestamp();
            _painter.Draw(dc, _frame);
            if (badge is var (c, f))
            {
                _badge.Draw(dc, c, f, _cloud.Unit, _now, _frame.Dark, _frame.Opacity);
            }

            DrawSpeech(dc);
            _statDrawMs += System.Diagnostics.Stopwatch.GetElapsedTime(drawClock).TotalMilliseconds;
        });
        _statFrames++;
        RenderEffects();
    }

    /// <summary>Las partículas, en su superficie del tamaño de lo que ocupan.</summary>
    private void RenderEffects()
    {
        if (_cloud.Particles.Bounds() is not { } bounds)
        {
            if (_effectsShown)
            {
                _effectsShown = false;
                _effects.Hide();
            }

            return;
        }

        var left = (int)Math.Floor(bounds.Left);
        var top = (int)Math.Floor(bounds.Top);
        var width = Math.Clamp((int)Math.Ceiling(bounds.Right) - left, 1, 4096);
        var height = Math.Clamp((int)Math.Ceiling(bounds.Bottom) - top, 1, 4096);
        _effectsShown = true;
        var opacity = Opacity();
        _effects.Present(SdkRect.FromSize(left, top, width, height), dc =>
        {
            if (opacity < 0.998)
            {
                dc.PushOpacity(opacity);
            }

            ParticlePainter.Draw(dc, _cloud.Particles.Items, _cloud.Unit);
            if (opacity < 0.998)
            {
                dc.Pop();
            }
        });
    }

    /// <summary>
    /// Dónde va la burbujita de lo que hace (píxeles) y desde dónde salen sus puntitos; null si no hay nada que enseñar. Va hacia
    /// el lado del monitor con más sitio, y no mientras es bocadillo o lleva una ventana.
    /// </summary>
    private (Vec2 Center, Vec2 From)? BadgePlace()
    {
        if (_badge.Scale(_now) <= 0.01 || _cloud.Pose is CloudPose.Bubble or CloudPose.Hug)
        {
            return null;
        }

        var at = _cloud.RenderPosition;
        var body = _cloud.BodyExtent();
        var monitor = _host.World.Current.MonitorAt(new System.Windows.Point(at.X, at.Y));
        var left = monitor is not null && at.X > monitor.Bounds.Center.X;
        var center = ActivityBadge.Place(at, body, _cloud.Unit, left, _now);
        var from = new Vec2(at.X + (left ? body.Left : body.Right) * 0.38 * _cloud.Unit, at.Y + body.Top * 0.62 * _cloud.Unit);
        return (center, from);
    }

    /// <summary>Aparece al entrar o volver y se desvanece al salir.</summary>
    private double Opacity()
    {
        var fadeIn = Math.Clamp((_now - _fadeStart) / FadeInDuration, 0, 1);
        return fadeIn * FadeOutAt(_now);
    }

    /// <summary>La zona de clic: una elipse sobre el cuerpo (en el bocadillo, sobre el bocadillo).</summary>
    private void UpdateHitArea()
    {
        if (_hidden || _exitStart is not null || _cloud.Alpha < 0.3)
        {
            if (_hitShown)
            {
                _hitShown = false;
                _hitKey = (int.MinValue, 0, 0, 0);
                _hit.Hide();
            }

            return;
        }

        var (l, t, r, b) = _cloud.BodyExtent();
        var u = _cloud.Unit;
        var at = _cloud.RenderPosition;
        var key = ((int)Math.Round(at.X + l * u * 0.92), (int)Math.Round(at.Y + t * u * 0.9), (int)Math.Round((r - l) * u * 0.92), (int)Math.Round((b - t) * u * 0.9));

        // Cambia de forma sin parar: solo se mueve la zona si se ha ido más de un par de píxeles.
        if (_hitShown && Math.Abs(key.Item1 - _hitKey.X) <= 2 && Math.Abs(key.Item2 - _hitKey.Y) <= 2 && Math.Abs(key.Item3 - _hitKey.W) <= 3 && Math.Abs(key.Item4 - _hitKey.H) <= 3)
        {
            return;
        }

        _hitKey = key;
        _hitShown = true;
        var shape = SdkRect.FromSize(key.Item1, key.Item2, Math.Max(1, key.Item3), Math.Max(1, key.Item4));
        _hit.Place(_cloud.Pose is Kumo.Core.Cloud.CloudPose.Bubble ? new HitShape.Rectangle(shape) : new HitShape.Ellipse(shape));
    }

    private void HideAll()
    {
        _surface.Hide();
        _effects.Hide();
        _effectsShown = false;
        _hit.Hide();
        _hitShown = false;
        _hitKey = (int.MinValue, 0, 0, 0);
    }

    private void DisposeRendering()
    {
        _hit.Dispose();
        _effects.Dispose();
        _surface.Dispose();
    }
}
