using KDesktop.Assistant.Sdk;
using Kobii.Assistant.Audio;
using Kobii.Core.Animation;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using AnimGesture = Kobii.Core.Animation.Gesture;
using SdkGesture = KDesktop.Assistant.Sdk.Gesture;
using SdkRect = KDesktop.Assistant.Sdk.PixelRect;

namespace Kobii.Assistant;

/// <summary>
/// Mover una ventana a lo Kobii (<see cref="SdkGesture.MoveWindow"/>): sube a su barra de título si no está ya en ella, se
/// agacha, se agarra con las dos manos, da dos tirones y la arrastra hasta su sitio subido encima, fotograma a fotograma con
/// <see cref="IWindowServices.TryMove"/>; al final la deja exacta (K-Desktop la comprueba igual) y se sacude las manos. Si no
/// llega a tiempo a la ventana, o está tapada, señala adónde va y la mueve K-Desktop.
/// </summary>
public sealed partial class KobiiAssistant
{
    /// <summary>La ventana que lleva arrastrando: el terreno la pone donde la ha dejado él (el mundo del host va detrás).</summary>
    private (long Handle, SdkRect Bounds, double Until)? _carried;

    /// <summary>Lo que se aparta de los bordes de la barra para agarrarse (en alturas del cuerpo).</summary>
    private const double GripMargin = 1.2;

    /// <summary>Lo que avanza la ventana en cada tirón (a escala 1).</summary>
    private const double TugPixels = 7;

    private async Task<GestureOutcome> DragWindowAsync(SdkGesture.MoveWindow move, Pace pace, IGestureContext ctx, Action commit, CancellationToken ct)
    {
        var handle = move.Window.Handle;
        var destination = move.Destination;
        var bounds = WindowBounds(handle) ?? move.Window.Bounds;
        var h = _game.Body.Height;
        if (pace.Walk <= 0 || destination.IsEmpty || bounds.IsEmpty || !_game.CanRunErrands || Math.Min(bounds.Width, destination.Width) < 2 * GripMargin * h + h)
        {
            // Rápido, o una ventana tan estrecha que no cabe encima: que la mueva K-Desktop ya.
            commit();
            return GestureOutcome.Committed;
        }

        if (_host.World.Current.MonitorAt(destination.Center) is { } there && _host.World.System.FullscreenMonitors.Contains(there.Index))
        {
            // Va a un monitor con algo a pantalla completa (allí no se le ve): que la mueva K-Desktop.
            commit();
            return GestureOutcome.Committed;
        }

        var deadline = _now + ctx.TimeLeft.TotalSeconds - 0.4;
        _host.World.WatchClosely(ctx.TimeLeft.TotalSeconds);
        var drag = DragSeconds(bounds, destination, pace);

        if (_game.Body.SupportId != handle)
        {
            // A la barra de título, cerca de donde está él (si se ve: si no, treparía por lo que hay delante).
            var budget = Math.Min(pace.Walk * 4, deadline - _now - Gestures.HaulTugSeconds - drag - 0.4);
            var x = GripX(bounds, destination);
            if (budget < 0.8 || !Seen(handle, new Vec2(x, bounds.Top + 4)) || !_game.GoToWindow(handle, x, run: pace.Run, errand: true))
            {
                _host.Log.Debug($"Maniobra: no sube a «{move.Window.Title}» ({(budget < 0.8 ? "sin tiempo" : _game.TravelProblem ?? "tapada")}); la mueve K-Desktop");
                return await LetModuleMoveAsync(destination, pace, commit, ct);
            }

            await TravelAsync(budget, ct);
            if (_game.Body.SupportId != handle || !_game.CanRunErrands)
            {
                _host.Log.Debug("Maniobra: no ha llegado a tiempo a la ventana; la mueve K-Desktop (y él sigue hacia ella)");
                return await LetModuleMoveAsync(destination, pace, commit, ct);
            }

            bounds = WindowBounds(handle) ?? bounds;
        }

        // Donde agarrarse: un trozo de la barra que esté tanto antes como después (si cambia de tamaño).
        var grip = GripX(bounds, destination);
        if (Math.Abs(grip - _game.Body.Feet.X) > h * 0.3 && _game.WalkTo(grip, pace.Run))
        {
            await WalkAsync(Math.Min(1.2, deadline - _now - drag - Gestures.HaulTugSeconds), ct);
        }

        if (!_game.CanRunErrands || _game.Body.SupportId != handle || !Seen(handle, new Vec2(_game.Body.Feet.X, bounds.Top + 4)))
        {
            return await LetModuleMoveAsync(destination, pace, commit, ct);
        }

        return await HaulAsync(handle, bounds, destination, drag, deadline, ct) ? GestureOutcome.PerformedSelf : GestureOutcome.NotPerformed;
    }

    /// <summary>
    /// Agarrado a la barra: dos tirones (la ventana avanza un poquito con cada uno) y luego la lleva hasta su sitio, con la
    /// salida y la llegada suaves, mientras él va encima. False si la ventana no se deja mover.
    /// </summary>
    private async Task<bool> HaulAsync(long handle, SdkRect from, SdkRect destination, double drag, double deadline, CancellationToken ct)
    {
        var shift = new Vec2(destination.Left + destination.Width / 2.0 - (from.Left + from.Width / 2.0), destination.Top - from.Top);
        var direction = shift.LengthSquared < 1 ? Vec2.Zero : shift.Normalized();
        var side = Math.Abs(shift.X) > 2 ? Math.Sign(shift.X) : _game.Animator.Side;
        _game.LookAt(_game.Body.Center + new Vec2(side * _game.Body.Height * 3, direction.Y * _game.Body.Height * 2));
        _game.PlayGesture(AnimGesture.Haul, Gestures.HaulTugSeconds + drag + 0.2, side);
        _game.Visual.SetExpression(Expression.Pain, Gestures.HaulTugSeconds);
        _host.Log.Debug(FormattableString.Invariant($"Maniobra: arrastra de {from} a {destination} en {drag:0.00} s"));

        try
        {
            return await PullAsync(handle, from, destination, direction, drag, deadline, ct);
        }
        finally
        {
            if (_carried is { Until: double.PositiveInfinity } stuck)
            {
                // Cancelado a medias: el terreno vuelve enseguida a lo que ve el host.
                _carried = stuck with { Until = _now + 0.5 };
            }
        }
    }

    private async Task<bool> PullAsync(long handle, SdkRect from, SdkRect destination, Vec2 direction, double drag, double deadline, CancellationToken ct)
    {
        var scale = _game.Body.Scale;
        var start = _now;
        var tugs = 0;
        var slid = false;
        var jumped = false;
        var tugged = from;
        var ceiling = Ceiling(destination);
        while (true)
        {
            await NextFrameAsync(ct);
            var t = _now - start;
            SdkRect rect;
            if (t < Gestures.HaulTugSeconds)
            {
                // Los tirones: la ventana da un saltito hacia donde va con cada uno.
                var pulled = 0.0;
                foreach (var at in Gestures.HaulTugs)
                {
                    pulled += Easing.Segment(t, at, 0.08, Ease.Out);
                }

                if (tugs < Gestures.HaulTugs.Length && t >= Gestures.HaulTugs[tugs])
                {
                    tugs++;
                    Play(Sound.Tug, 0.6);
                    _game.Visual.AddEmote(EmoteKind.Drop, _game.HeadPoint, scale);
                }

                var offset = direction * (TugPixels * scale * pulled);
                rect = tugged = from.Offset((int)Math.Round(offset.X), (int)Math.Round(offset.Y));
            }
            else
            {
                if (!slid)
                {
                    slid = true;
                    Play(Sound.Slide, 0.5);
                    _game.Visual.SetExpression(Expression.Happy, drag);
                    _game.Visual.AddEmote(EmoteKind.Dust, _game.Body.Feet, scale, 2);
                }

                var p = Math.Min(1, (t - Gestures.HaulTugSeconds) / drag);
                rect = Lerp(tugged, destination, Easing.Apply(Ease.InOut, p));
                if (ceiling is { } top && !jumped && _game.Body.SupportId == handle && rect.Top < top + _game.Body.Height * 0.6)
                {
                    // Va a quedar pegada arriba, donde no cabe: se suelta y salta hacia donde iba.
                    jumped = true;
                    _game.JumpOff(new Vec2(Math.Sign(direction.X == 0 ? _game.Animator.Side : direction.X) * 260, -520) * scale);
                    Play(Sound.Boing, 0.5);
                }

                if (p >= 1)
                {
                    break;
                }
            }

            if (_now > deadline)
            {
                break;
            }

            if (!_host.Windows.TryMove(handle, rect))
            {
                _host.Log.Debug("Maniobra: la ventana no se deja mover");
                _carried = null;
                return false;
            }

            _carried = (handle, rect, double.PositiveInfinity);
            _terrainDirty = true;
        }

        // En su sitio, al píxel; el terreno la deja ahí hasta que el mundo del host la vea igual.
        var placed = _host.Windows.TryMove(handle, destination);
        _carried = (handle, destination, _now + 1.5);
        _terrainDirty = true;
        if (!placed)
        {
            return false;
        }

        Play(Sound.Tap, 0.7, destination.Left + destination.Width / 2.0);
        _ = DustOffAsync();
        return true;
    }

    /// <summary>Al acabar se sacude las manos, tan contento.</summary>
    private async Task DustOffAsync()
    {
        await NextFrameAsync(CancellationToken.None);
        _game.PlayGesture(AnimGesture.Clap, 0.45, _game.Animator.Side);
        _game.Visual.SetExpression(Expression.Joy, 1.2);
        _game.Visual.AddEmote(EmoteKind.Sparkle, _game.HandPoint, _game.Body.Scale, 3);
        _host.RequestTick();
    }

    /// <summary>
    /// No puede (o no llega a tiempo): señala adónde va la ventana y la mueve K-Desktop cuando llega la línea. Si va trepando
    /// hacia ella, sigue (y se subirá donde quede).
    /// </summary>
    private async Task<GestureOutcome> LetModuleMoveAsync(SdkRect destination, Pace pace, Action commit, CancellationToken ct)
    {
        if (_game.IsTraveling || !_game.CanRunErrands)
        {
            commit();
            return GestureOutcome.Committed;
        }

        await PointAsync(new Place.Area(destination), pace with { Show = Math.Min(pace.Show, 0.6) }, commit, ct);
        return GestureOutcome.Committed;
    }

    /// <summary>Lo que tarda en llevarla: más lejos (o más cambio de tamaño), más rato, sin pasar de 1,9 s (2,6 en «Vistosas»).</summary>
    private static double DragSeconds(SdkRect from, SdkRect to, Pace pace)
    {
        var distance = Math.Sqrt(Math.Pow(to.Left - from.Left, 2) + Math.Pow(to.Top - from.Top, 2))
            + 0.5 * (Math.Abs(to.Width - from.Width) + Math.Abs(to.Height - from.Height));
        var showy = pace.Walk > 2;
        return Math.Clamp(0.5 + distance / (showy ? 1100 : 1500), 0.7, showy ? 2.6 : 1.9);
    }

    /// <summary>Dónde agarrarse en la barra: lo más cerca de él dentro del trozo que la ventana tiene antes y después.</summary>
    private double GripX(SdkRect bounds, SdkRect destination)
    {
        var margin = GripMargin * _game.Body.Height;
        var span = Math.Min(bounds.Width, destination.Width);
        return Math.Clamp(_game.Body.Feet.X, bounds.Left + margin, bounds.Left + Math.Max(margin, span - margin));
    }

    /// <summary>
    /// Si la ventana va a quedar tan arriba que no cabe de pie encima (encajada en una mitad, por ejemplo), lo más alto a lo
    /// que puede llegar su barra con él encima; null si cabe.
    /// </summary>
    private double? Ceiling(SdkRect destination)
    {
        var monitor = _host.World.Current.MonitorAt(destination.Center) ?? _host.World.Current.MonitorAt(new System.Windows.Point(destination.Left + 1, destination.Top + 1));
        if (monitor is null)
        {
            return null;
        }

        var work = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
        var top = work.Top + _game.Body.Height + 16 * monitor.DpiScale;
        return destination.Top < top + 2 ? top : null;
    }

    private SdkRect? WindowBounds(long handle) =>
        _host.World.Current.Windows.FirstOrDefault(w => w.Handle == handle) is { Bounds.IsEmpty: false } window ? window.Bounds : null;

    private static SdkRect Lerp(SdkRect a, SdkRect b, double t)
    {
        int L(int x, int y) => (int)Math.Round(x + (y - x) * t);
        return new SdkRect(L(a.Left, b.Left), L(a.Top, b.Top), L(a.Right, b.Right), L(a.Bottom, b.Bottom));
    }
}
