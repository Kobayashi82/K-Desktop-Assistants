using KDesktop.Assistant.Sdk;
using Kobii.Assistant.Rendering;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using SdkRect = KDesktop.Assistant.Sdk.PixelRect;

namespace Kobii.Assistant;

/// <summary>
/// Dibujo de Kobii en la capa del host. El cuerpo va en su superficie con el shader de Kobii AI (<see cref="ISurface.PresentGpu"/>)
/// y lo que lleva en la mano encima, con Direct2D; las partículas (corazones, notas, confeti) van en otra superficie, por
/// delante y solo mientras las hay, para que el lienzo del cuerpo (lo que cuesta, porque el shader trabaja en cada píxel)
/// sea pequeño. Solo se presenta un fotograma cuando cambia algo visible (se compara una huella del estado).
/// </summary>
public sealed partial class KobiiAssistant
{
    /// <summary>Lienzo del cuerpo, en alturas de Kobii: sitio para los brazos, tumbarse, rodar y el globo.</summary>
    private const double CanvasWidth = 4.4;

    private const double CanvasHeight = 4.0;

    /// <summary>Lo que el lienzo baja por debajo de los pies (la sombra al saltar).</summary>
    private const double CanvasBelow = 0.55;

    private const double BlinkPeriod = 4.6;
    private const double BlinkLength = 0.16;

    private readonly KobiiFrame _frame = new();
    private ISurface _surface = null!;
    private ISurface _effects = null!;
    private IHitArea _hit = null!;
    private Gpu? _gpu;
    private bool _forceRender = true;
    private bool _shaderFailed;
    private int _lastSignature;
    private bool _effectsShown;
    private (int X, int Y, int W, int H) _hitKey = (int.MinValue, 0, 0, 0);
    private bool _hitShown;

    private void Render(double now)
    {
        if (_hidden || !ShaderReady())
        {
            return;
        }

        var body = _game.Body;
        var alpha = Math.Clamp(_accumulator / KobiiGame.TimeStep, 0, 1);
        var jumped = (body.Feet - _previousFeet).LengthSquared > body.Height * body.Height * 4;
        var feet = body.IsHeld || jumped ? body.Feet : Vec2.Lerp(_previousFeet, body.Feet, alpha);

        // En reposo y sin nadie delante, el tiempo de las animaciones va a saltos de un segundo para no redibujar sin necesidad.
        var lively = IsActive(now);
        var away = !lively && IsUserAway();
        var time = away ? Math.Floor(now) : now;
        var ground = body.IsGrounded ? feet.Y : _world.PlatformBelow(feet.X, feet.Y)?.Y;
        var opacity = Opacity(now);

        _frame.Dark = _host.World.System.DarkMode;
        _frame.Palette = Species.Palette(_settings.Color);
        _frame.Update(_game, feet, time, BlinkAt(now), ground, body.IsHeld ? 1 : alpha, opacity);

        var h = body.Height;
        var width = (int)Math.Ceiling(h * CanvasWidth);
        var height = (int)Math.Ceiling(h * CanvasHeight);
        var left = (int)Math.Floor(feet.X) - width / 2;
        var top = (int)Math.Floor(feet.Y + h * CanvasBelow) - height;
        _frame.SetCanvas(new Vec2(left, top), width, height);

        var signature = Signature(left, top);
        var needsFrame = _surface.NeedsFrame || _forceRender;
        if (needsFrame || signature != _lastSignature)
        {
            _forceRender = false;
            _lastSignature = signature;
            var origin = new Vec2(left, top);
            var presentClock = System.Diagnostics.Stopwatch.GetTimestamp();
            _surface.PresentGpu(SdkRect.FromSize(left, top, width, height), target =>
            {
                var drawClock = System.Diagnostics.Stopwatch.GetTimestamp();
                DrawBody(target, origin, width, height);
                _statDrawMs += System.Diagnostics.Stopwatch.GetElapsedTime(drawClock).TotalMilliseconds;
            });
            _statPresentMs += System.Diagnostics.Stopwatch.GetElapsedTime(presentClock).TotalMilliseconds;
            _statFrames++;
        }

        RenderEffects();
    }

    private void DrawBody(GpuTarget target, Vec2 origin, int width, int height)
    {
        var gpu = Devices(target);
        using var view = Gpu.TargetView(target);
        using var bitmap = Gpu.TargetBitmap(target);
        FramePainter.DrawBody(gpu, view, bitmap, _frame, origin, width, height);
    }

    /// <summary>Las partículas, en su superficie del tamaño de lo que ocupan.</summary>
    private void RenderEffects()
    {
        if (_particles.Bounds() is not { } bounds)
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
        var origin = new Vec2(left, top);
        _effectsShown = true;
        _effects.PresentGpu(SdkRect.FromSize(left, top, width, height), target =>
        {
            var gpu = Devices(target);
            using var bitmap = Gpu.TargetBitmap(target);
            if (bitmap is not null)
            {
                FramePainter.DrawParticles(gpu, bitmap, origin, _particles.Items);
            }
        });
    }

    /// <summary>Los dispositivos del host y lo que dibuja con ellos (se rehacen si el host los rehízo).</summary>
    private Gpu Devices(GpuTarget target)
    {
        var gpu = Gpu.For(ref _gpu, target);
        gpu.Renderer ??= new CreatureRenderer(gpu.Device, gpu.Context, _shader!.Result);
        gpu.Painter ??= new ScenePainter(gpu);
        return gpu;
    }

    /// <summary>El shader ya está compilado (si falló, se dice una vez en el registro y Kobii no se ve).</summary>
    private bool ShaderReady()
    {
        if (_shader is not { IsCompleted: true } shader)
        {
            return false;
        }

        if (shader.IsCompletedSuccessfully)
        {
            return true;
        }

        if (!_shaderFailed)
        {
            _shaderFailed = true;
            _host.Log.Error("No se pudo compilar el shader de Kobii.", shader.Exception?.InnerException);
        }

        return false;
    }

    /// <summary>Aparece al entrar o volver y se desvanece al salir.</summary>
    private double Opacity(double now)
    {
        var fadeIn = Math.Clamp((now - _fadeStart) / FadeInDuration, 0, 1);
        var fadeOut = _exitStart is { } start ? 1 - Math.Clamp((now - start) / ExitDuration, 0, 1) : 1;
        return fadeIn * fadeOut;
    }

    /// <summary>Huella de todo lo que se ve en un fotograma (para no repetir uno idéntico).</summary>
    private int Signature(int left, int top)
    {
        var hash = new HashCode();
        var constants = _frame.Constants;
        hash.AddBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<CreatureConstants>(in constants)));
        hash.Add(left);
        hash.Add(top);
        hash.Add(_frame.Props);
        return hash.ToHashCode();
    }

    /// <summary>La zona de clic sigue al cuerpo (una elipse como la de <see cref="KobiiGame.HitTest"/>).</summary>
    private void UpdateHitArea()
    {
        if (_hidden || _exitStart is not null || !ShaderReady())
        {
            if (_hitShown)
            {
                _hitShown = false;
                _hitKey = (int.MinValue, 0, 0, 0);
                _hit.Hide();
            }

            return;
        }

        var body = _game.Body;
        var center = body.Center;
        var rx = body.HalfWidth * 0.95;
        var ry = body.Height * 0.52;
        var key = ((int)Math.Round(center.X - rx), (int)Math.Round(center.Y - ry), (int)Math.Round(rx * 2), (int)Math.Round(ry * 2));
        if (key == _hitKey)
        {
            return;
        }

        _hitKey = key;
        _hitShown = true;
        _hit.Place(new HitShape.Ellipse(SdkRect.FromSize(key.Item1, key.Item2, Math.Max(1, key.Item3), Math.Max(1, key.Item4))));
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

    /// <summary>Parpadea cada unos segundos, con un desfase al azar en cada ciclo (el de Kobii AI).</summary>
    private static double BlinkAt(double time)
    {
        var cycle = Math.Floor(time / BlinkPeriod);
        var phase = time - cycle * BlinkPeriod - BlinkOffset(cycle);
        return phase is >= 0 and < BlinkLength ? 1 : 0;
    }

    /// <summary>El próximo momento en que empieza o acaba un parpadeo.</summary>
    private static double NextBlinkEdge(double time)
    {
        var cycle = Math.Floor(time / BlinkPeriod);
        for (var i = 0; i < 3; i++, cycle++)
        {
            var start = cycle * BlinkPeriod + BlinkOffset(cycle);
            if (start > time)
            {
                return start;
            }

            if (start + BlinkLength > time)
            {
                return start + BlinkLength;
            }
        }

        return time + BlinkPeriod;
    }

    private static double BlinkOffset(double cycle) => (Math.Sin(cycle * 12.9898) * 43758.5453 % 1 + 1) % 1 * 2.5;
}
