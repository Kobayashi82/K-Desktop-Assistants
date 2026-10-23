using KDesktop.Assistant.Sdk;
using Lander.Assistant.Audio;
using Lander.Assistant.Rendering;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;

namespace Lander.Assistant;

/// <summary>
/// Sus animaciones propias para el escaparate de K-Desktop (SDK 1.18, pestaña «Probar» de su ficha): lo que hace por su
/// cuenta (saludar, bostezar, estirarse, la siesta), cómo vuela (un saltito, el vuelo panorámico), sus trucos (voltereta,
/// fallo de motor, confeti) y sus caras. Lo que pide K-Desktop a todos (entradas, salidas, estados, reacciones, gestos) lo
/// lanza el host.
/// </summary>
public sealed partial class LanderAssistant
{
    internal static readonly IReadOnlyList<ShowcaseItem> ShowcaseItems =
    [
        Item("lander.wave", ShowcaseGroup.Idle, "Saludar", "Wave", "Saluer", "Winken", "Salutare", "Acenar"),
        Item("lander.yawn", ShowcaseGroup.Idle, "Bostezar", "Yawn", "Bâiller", "Gähnen", "Sbadigliare", "Bocejar"),
        Item("lander.stretch", ShowcaseGroup.Idle, "Estirarse", "Stretch", "S’étirer", "Sich strecken", "Stiracchiarsi", "Espreguiçar"),
        Item("lander.nap", ShowcaseGroup.Idle, "Echar una siesta", "Take a nap", "Faire la sieste", "Ein Nickerchen", "Fare un pisolino", "Tirar um cochilo"),
        Item("lander.hop", ShowcaseGroup.Moving, "Un saltito", "A little hop", "Un petit saut", "Ein Hüpfer", "Un saltello", "Um pulinho"),
        Item("lander.sightsee", ShowcaseGroup.Moving, "Vuelo panorámico", "Sightseeing flight", "Vol panoramique", "Rundflug", "Volo panoramico", "Voo panorâmico"),
        Item("lander.loop", ShowcaseGroup.Tricks, "Voltereta", "Loop", "Looping", "Looping", "Giro della morte", "Cambalhota"),
        Item("lander.stall", ShowcaseGroup.Tricks, "Fallo de motor", "Engine stall", "Panne de moteur", "Motoraussetzer", "Motore in stallo", "Falha no motor"),
        Item("lander.confetti", ShowcaseGroup.Tricks, "Confeti", "Confetti", "Confettis", "Konfetti", "Coriandoli", "Confete"),
        Item("lander.love", ShowcaseGroup.Feelings, "Enamorado", "In love", "Amoureux", "Verliebt", "Innamorato", "Apaixonado"),
        Item("lander.curious", ShowcaseGroup.Feelings, "Curioso", "Curious", "Curieux", "Neugierig", "Curioso", "Curioso"),
        Item("lander.scared", ShowcaseGroup.Feelings, "Asustado", "Scared", "Effrayé", "Erschrocken", "Spaventato", "Assustado"),
    ];

    public IReadOnlyList<ShowcaseItem> Showcase => ShowcaseItems;

    public async Task<bool> PlayShowcaseAsync(string id, CancellationToken ct)
    {
        if (!_game.IsInitialized || _hidden || _exitStart is not null || _game.Body.State == BodyState.Destroyed)
        {
            return false;
        }

        var flight = id is "lander.hop" or "lander.sightsee" or "lander.loop" or "lander.stall";
        if (flight && !(CanManoeuvre && _game.CanRunErrands))
        {
            return false;
        }

        if (ShowcaseItems.All(item => item.Id != id))
        {
            return false;
        }

        _host.Log.Info($"Escaparate: {id}.");
        var visual = _game.Visual;
        var body = _game.Body;
        var scale = body.Scale;
        _manoeuvres++;
        _lingerUntil = double.NaN;
        try
        {
            switch (id)
            {
                case "lander.wave":
                    visual.Flash(Expression.Happy, 2);
                    visual.StartWave();
                    visual.ShowBubble(new Bubble(BubbleIcon.Hello, 1.8));
                    Play(Sound.Pop, 0.5);
                    await DelayAsync(LanderVisual.WaveDuration + 0.2, ct);
                    break;

                case "lander.yawn":
                    visual.Flash(Expression.Tired, LanderVisual.YawnDuration);
                    visual.StartYawn();
                    await DelayAsync(LanderVisual.YawnDuration + 0.2, ct);
                    break;

                case "lander.stretch":
                    visual.Flash(Expression.Tired, LanderVisual.StretchDuration);
                    visual.StartStretch();
                    await DelayAsync(LanderVisual.StretchDuration + 0.2, ct);
                    break;

                case "lander.nap":
                    visual.Flash(Expression.Asleep, 4);
                    visual.ShowBubble(new Bubble(BubbleIcon.Sleep, 4));
                    await DelayAsync(4, ct);
                    visual.ClearBubble();
                    visual.Flash(Expression.Tired, LanderVisual.StretchDuration);
                    visual.StartStretch();
                    await DelayAsync(LanderVisual.StretchDuration, ct);
                    break;

                case "lander.hop":
                {
                    var side = SideWithRoom();
                    await FlyToAsync(body.Position + new Vec2(side * body.Height * 3.5, -body.Height * 2), 1.2, 2.5, ct);
                    await LandAsync(4, ct);
                    break;
                }

                case "lander.sightsee":
                {
                    var monitor = _world.MonitorAt(body.Position) ?? _world.NearestMonitor(body.Position);
                    if (monitor is null)
                    {
                        return false;
                    }

                    var area = monitor.WorkArea;
                    var x = body.Position.X < (area.Left + area.Right) / 2 ? area.Right - body.Height * 4 : area.Left + body.Height * 4;
                    await FlyToAsync(new Vec2((body.Position.X + x) / 2, area.Top + body.Height * 4), 1.1, 4, ct);
                    await FlyToAsync(new Vec2(x, area.Top + (area.Bottom - area.Top) * 0.45), 1.1, 4, ct);
                    await LandAsync(6, ct);
                    break;
                }

                case "lander.loop":
                    await FlyToAsync(OpenSky(6), 1.2, 3.5, ct);
                    _game.Autopilot.ErrandFlip();
                    Play(Sound.Boost, 0.5);
                    await DelayAsync(3, ct);
                    await LandAsync(5, ct);
                    break;

                case "lander.stall":
                    await FlyToAsync(OpenSky(4), 1.1, 3, ct);
                    visual.Flash(Expression.Scared, 1.5);
                    _game.Autopilot.Stall();
                    _particles.EnginePuff(body.Position, new Vec2(0, -1), body.Velocity, scale, "smoke", _now);
                    Play(Sound.Puff, 0.8);
                    await DelayAsync(1.6, ct);
                    await LandAsync(5, ct);
                    break;

                case "lander.confetti":
                    visual.Flash(Expression.Happy, 2);
                    visual.StartBounce();
                    _particles.Confetti(body.Position - new Vec2(0, 20 * scale), scale, 40);
                    Play(Sound.Fanfare, 0.5);
                    await DelayAsync(1.6, ct);
                    break;

                case "lander.love":
                    visual.Flash(Expression.Happy, 2.4);
                    visual.ShowBubble(new Bubble(BubbleIcon.Heart, 2.2));
                    visual.StartBounce();
                    _particles.Hearts(body.Position - new Vec2(0, 20 * scale), scale, 7);
                    await DelayAsync(2.2, ct);
                    break;

                case "lander.curious":
                    visual.Flash(Expression.Curious, 2.6);
                    visual.ShowBubble(new Bubble(BubbleIcon.Question, 2.2));
                    visual.LookTarget = body.Position + new Vec2(-body.Height * 4, -body.Height);
                    await DelayAsync(1.1, ct);
                    visual.LookTarget = body.Position + new Vec2(body.Height * 4, -body.Height);
                    await DelayAsync(1.1, ct);
                    visual.LookTarget = null;
                    break;

                case "lander.scared":
                    visual.Flash(Expression.Scared, 1.8);
                    visual.StartWiggle();
                    visual.ShowBubble(new Bubble(BubbleIcon.Exclamation, 1.6));
                    visual.Impact(1.2);
                    Play(Sound.Pop, 0.7);
                    await DelayAsync(1.6, ct);
                    break;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            // Lo han parado o han pedido otra.
            return true;
        }
        finally
        {
            _manoeuvres--;
            if (_manoeuvres == 0)
            {
                // Si se quedó en el aire, se posa enseguida.
                _lingerUntil = _now + 0.3;
                _host.RequestTick();
            }
        }
    }

    /// <summary>Vuela a un punto (encargo) y espera a llegar, como mucho <paramref name="seconds"/>.</summary>
    private async Task FlyToAsync(Vec2 point, double speed, double seconds, CancellationToken ct)
    {
        _game.BeginErrand(point, speed);
        var until = _now + seconds;
        while (_now < until && !_game.Autopilot.ErrandArrived)
        {
            await NextFrameAsync(ct);
        }
    }

    /// <summary>Termina el encargo y espera a que se pose, como mucho <paramref name="seconds"/>.</summary>
    private async Task LandAsync(double seconds, CancellationToken ct)
    {
        _game.EndErrand();
        var until = _now + seconds;
        while (_now < until && !_game.Body.IsLanded)
        {
            await NextFrameAsync(ct);
        }
    }

    /// <summary>Un punto en el aire encima de él, con sitio (sin pasarse del techo del monitor).</summary>
    private Vec2 OpenSky(double heights)
    {
        var body = _game.Body;
        var top = TopOfMonitor() + body.Height * 2.5;
        return new Vec2(body.Position.X, Math.Max(top, body.Position.Y - body.Height * heights));
    }

    /// <summary>El lado hacia el que hay más sitio en su monitor (−1 izquierda, 1 derecha).</summary>
    private int SideWithRoom()
    {
        var body = _game.Body;
        var monitor = _world.MonitorAt(body.Position) ?? _world.NearestMonitor(body.Position);
        return monitor is null || body.Position.X < (monitor.Bounds.Left + monitor.Bounds.Right) / 2 ? 1 : -1;
    }

    private static ShowcaseItem Item(string id, ShowcaseGroup group, string es, string en, string fr, string de, string it, string pt) =>
        new(id, new Dictionary<string, string> { ["es"] = es, ["en"] = en, ["fr"] = fr, ["de"] = de, ["it"] = it, ["pt"] = pt }, group);
}
