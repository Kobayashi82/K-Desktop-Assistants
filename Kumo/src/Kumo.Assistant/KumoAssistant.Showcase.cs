using KDesktop.Assistant.Sdk;
using Kumo.Assistant.Audio;
using Kumo.Core.Cloud;
using Kumo.Core.Geometry;

namespace Kumo.Assistant;

/// <summary>
/// Sus cosas propias para el escaparate de K-Desktop (SDK 1.18, pestaña «Probar» de su ficha y clips del catálogo): lo que hace
/// por su cuenta, cómo se mueve, sus juegos (arcoíris, llovizna, bailar, la siesta) y sus caras. Lo que pide K-Desktop a todos
/// (entradas, estados, gestos…) lo lanza el host.
/// </summary>
public sealed partial class KumoAssistant
{
    internal static readonly IReadOnlyList<ShowcaseItem> ShowcaseItems =
    [
        Item("kumo.drop", ShowcaseGroup.Idle, "Hacerse gota", "Turn into a drop", "Devenir une goutte", "Zum Tropfen werden", "Diventare una goccia", "Virar uma gota"),
        Item("kumo.puffs", ShowcaseGroup.Idle, "Soltar nubecitas", "Let out little clouds", "Lâcher des petits nuages", "Wölkchen ausstoßen", "Liberare nuvolette", "Soltar nuvenzinhas"),
        Item("kumo.wiggle", ShowcaseGroup.Idle, "Retorcerse", "Wiggle", "Se tortiller", "Sich winden", "Dimenarsi", "Se contorcer"),
        Item("kumo.travel", ShowcaseGroup.Moving, "Viajar a otro sitio", "Fly somewhere else", "S’envoler ailleurs", "Woandershin fliegen", "Volare altrove", "Voar para outro lugar"),
        Item("kumo.condense", ShowcaseGroup.Moving, "Condensarse", "Condense", "Se condenser", "Sich verdichten", "Condensarsi", "Condensar-se"),
        Item("kumo.dissolve", ShowcaseGroup.Moving, "Deshacerse en niebla", "Dissolve into mist", "Se dissoudre en brume", "Sich in Nebel auflösen", "Sciogliersi in nebbia", "Desfazer-se em névoa"),
        Item("kumo.rainbow", ShowcaseGroup.Playing, "Hacer un arcoíris", "Make a rainbow", "Faire un arc-en-ciel", "Einen Regenbogen machen", "Fare un arcobaleno", "Fazer um arco-íris"),
        Item("kumo.drizzle", ShowcaseGroup.Playing, "Llovizna", "Drizzle", "Bruine", "Nieselregen", "Pioggerellina", "Garoa"),
        Item("kumo.dance", ShowcaseGroup.Playing, "Bailar", "Dance", "Danser", "Tanzen", "Ballare", "Dançar"),
        Item("kumo.nap", ShowcaseGroup.Playing, "Siesta con almohada", "Nap on a pillow", "Sieste sur un oreiller", "Nickerchen auf dem Kissen", "Pisolino sul cuscino", "Soneca no travesseiro"),
        Item("kumo.wave", ShowcaseGroup.Feelings, "Saludar", "Wave", "Saluer", "Winken", "Salutare", "Acenar"),
        Item("kumo.happy", ShowcaseGroup.Feelings, "Contento", "Happy", "Content", "Fröhlich", "Contento", "Contente"),
        Item("kumo.surprise", ShowcaseGroup.Feelings, "Sorpresa", "Surprise", "Surprise", "Überraschung", "Sorpresa", "Surpresa"),
        Item("kumo.shrug", ShowcaseGroup.Feelings, "Ni idea", "No idea", "Aucune idée", "Keine Ahnung", "Non ne ho idea", "Sei lá"),
        Item("kumo.tickle", ShowcaseGroup.Feelings, "Cosquillas", "Tickles", "Chatouilles", "Kitzeln", "Solletico", "Cócegas"),
    ];

    public IReadOnlyList<ShowcaseItem> Showcase => ShowcaseItems;

    public async Task<bool> PlayShowcaseAsync(string id, CancellationToken ct)
    {
        if (!_started || _hidden || _exitStart is not null || _dragging || ManoeuvreActive || ShowcaseItems.All(i => i.Id != id))
        {
            return false;
        }

        _host.Log.Info($"Escaparate: {id}.");
        WakeUp(quiet: true);
        var seconds = 2.0;
        switch (id)
        {
            case "kumo.drop":
                Act(CloudPose.Drop, 3);
                Play(Sound.Puff, 0.35);
                seconds = 3.2;
                break;
            case "kumo.puffs":
                _cloud.PuffBurst(6, 1.1);
                Play(Sound.Pop, 0.3);
                seconds = 1.8;
                break;
            case "kumo.wiggle":
                Act(CloudPose.Tickle, 1.2);
                seconds = 1.4;
                break;
            case "kumo.travel" when PickSpot() is { } spot:
                Travel(spot);
                seconds = 6;
                break;
            case "kumo.travel":
                return false;
            case "kumo.condense":
                _cloud.Condense();
                Play(Sound.Puff, 0.35);
                seconds = 2.4;
                break;
            case "kumo.dissolve":
                Act(CloudPose.Dissolve, 2.4);
                Play(Sound.Puff, 0.3);
                seconds = 2.8;
                break;
            case "kumo.rainbow":
                Rainbow();
                seconds = RainbowSeconds + 0.2;
                break;
            case "kumo.drizzle":
                Drizzle(4, rainbowAfter: false);
                seconds = 4.6;
                break;
            case "kumo.dance":
                Dance(6);
                seconds = 6.2;
                break;
            case "kumo.nap":
                Nap(6);
                seconds = 6.5;
                break;
            case "kumo.wave":
                _cloud.Aim = _cloud.Cursor ?? _cloud.Position + new Vec2(-_unit * 4, -_unit);
                Act(CloudPose.Wave, 1.4);
                Play(Sound.Hello, 0.45);
                seconds = 1.6;
                break;
            case "kumo.happy":
                Act(CloudPose.Happy, 1.6);
                _cloud.Burst(ParticleKind.Sparkle, new Vec2(_cloud.Position.X, _cloud.Position.Y - _unit * 0.4), 8);
                Play(Sound.Chime, 0.35);
                seconds = 1.8;
                break;
            case "kumo.surprise":
                Act(CloudPose.Surprise, 0.9);
                Play(Sound.Pop, 0.35);
                seconds = 1.2;
                break;
            case "kumo.shrug":
                Act(CloudPose.Shrug, 1.6);
                Play(Sound.Sigh, 0.25);
                seconds = 1.8;
                break;
            case "kumo.tickle":
                Act(CloudPose.Tickle, 1.1);
                _cloud.PuffBurst(3, 1);
                Play(Sound.Giggle, 0.5);
                seconds = 1.4;
                break;
        }

        _host.RequestTick();
        try
        {
            // Volando, hasta que llega (o se acaba el tiempo).
            var until = _now + seconds;
            await DelayAsync(0.3, ct);
            while (_now < until && !(id == "kumo.travel" && _cloud.Motion != Motion.Fly))
            {
                await DelayAsync(0.1, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Lo han parado o han pedido otra: lo deja como estaba.
            StopShowcase();
        }

        return true;
    }

    /// <summary>Corta lo que estuviera enseñando.</summary>
    private void StopShowcase()
    {
        _rainbowStart = double.NaN;
        _drizzleUntil = double.NaN;
        _danceUntil = double.NaN;
        _napUntil = double.NaN;
        WakeUp(quiet: true);
        if (_action is not null)
        {
            EndAction();
        }
    }

    private static ShowcaseItem Item(string id, ShowcaseGroup group, string es, string en, string fr, string de, string it, string pt) =>
        new(id, new Dictionary<string, string> { ["es"] = es, ["en"] = en, ["fr"] = fr, ["de"] = de, ["it"] = it, ["pt"] = pt }, group);
}
