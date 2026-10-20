using System.Text.Json.Nodes;
using Lander.Core.Progression;
using Lander.Core.Racing;

namespace Lander.Assistant;

/// <summary>
/// Lo que el lander publica para K-Desktop: los datos de su panel.json (nivel, estadísticas, carreras, logros) y las
/// galerías de modelos y estelas de sus ajustes. K-Desktop fusiona lo publicado, así que basta con mandarlo todo de
/// nuevo cuando cambia algo (el host lo manda como mucho cuatro veces por segundo).
/// </summary>
public sealed partial class LanderAssistant
{
    /// <summary>Carreras terminadas en esta sesión, la última primero.</summary>
    private const int SessionRacesShown = 8;

    private readonly List<string> _sessionRaces = [];
    private int _publishedLevel = -1;

    /// <summary>Publica todo el panel; las galerías solo cuando cambia el nivel (lo que bloquean depende de él).</summary>
    private void PublishPanel()
    {
        var data = new JsonObject
        {
            ["pilot"] = new JsonObject
            {
                ["level"] = _progress.Level,
                ["points"] = _progress.TotalPoints - Levels.PointsForLevel(_progress.Level),
                ["nextLevel"] = _progress.Level >= Levels.MaxLevel
                    ? Math.Max(1, _progress.TotalPoints - Levels.PointsForLevel(_progress.Level))
                    : Levels.PointsForLevel(_progress.Level + 1) - Levels.PointsForLevel(_progress.Level),
            },
            ["stats"] = new JsonObject
            {
                ["perfect"] = _progress.PerfectLandings,
                ["bestStreak"] = _progress.BestStreak,
                ["missions"] = _progress.MissionsCompleted,
                ["crystals"] = _progress.CrystalsCollected,
                ["races"] = _progress.RacesCompleted,
                ["gold"] = _progress.GoldMedals,
                ["autoLandings"] = _progress.AutoLandings,
                ["crashes"] = _progress.Crashes,
                ["flightTime"] = FlightTime(_progress.FlightSeconds),
            },
            ["races"] = new JsonObject { ["session"] = new JsonArray([.. _sessionRaces.Select(r => JsonValue.Create(r))]) },
            ["achievements"] = new JsonArray([.. Achievements.All.Select(a => (JsonNode)new JsonObject
            {
                ["id"] = a.Id,
                ["name"] = Texts.Get("ach." + a.Id, a.Name),
                ["description"] = Texts.Get("ach." + a.Id + ".d", a.Description),
                ["unlocked"] = a.IsEarned(_progress),
                ["progress"] = Math.Round(a.ProgressOf(_progress), 3),
            })]),
        };

        if (_progress.Level != _publishedLevel)
        {
            _publishedLevel = _progress.Level;
            data["skins"] = Gallery(UnlockKind.Skin, "skins");
            data["trails"] = Gallery(UnlockKind.Trail, "trails");
        }

        _host.PublishPanel(data);
    }

    /// <summary>Una carrera terminada pasa a la lista de la sesión (los récords no sobreviven a un reinicio).</summary>
    private void RecordRace(Race race)
    {
        var entry = Texts.Format("race.entry", race.Checkpoints.Count, Race.FormatTime(race.Elapsed), Texts.Medal(race.Medal))
            + (race.IsNewRecord ? Texts.Get("race.record") : string.Empty);
        _sessionRaces.Insert(0, entry);
        if (_sessionRaces.Count > SessionRacesShown)
        {
            _sessionRaces.RemoveAt(_sessionRaces.Count - 1);
        }
    }

    /// <summary>Galería de los ajustes: cada modelo o estela con su imagen del paquete y, si falta nivel, bloqueado.</summary>
    private JsonArray Gallery(UnlockKind kind, string folder)
    {
        var items = new JsonArray();
        foreach (var unlock in Unlocks.OfKind(kind))
        {
            var item = new JsonObject
            {
                ["id"] = unlock.Id,
                ["name"] = (JsonNode?)AllLanguages("unlock." + unlock.Id) ?? JsonValue.Create(unlock.Name),
                ["image"] = $"assets/{folder}/{unlock.Id}.png",
            };
            if (unlock.Level > _progress.Level)
            {
                item["locked"] = true;
                item["lockText"] = AllLanguages("lockedAt", unlock.Level);
            }

            items.Add(item);
        }

        return items;
    }

    /// <summary>Un texto en los seis idiomas (K-Desktop elige el suyo), o null si no está.</summary>
    private static JsonObject? AllLanguages(string key, params object[] args)
    {
        var text = new JsonObject();
        foreach (var (language, value) in Texts.All(key))
        {
            text[language] = args.Length == 0 ? value : string.Format(System.Globalization.CultureInfo.GetCultureInfo(language), value, args);
        }

        return text.Count == 0 ? null : text;
    }

    private static string FlightTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours} h {time.Minutes} min" : $"{time.Minutes} min {time.Seconds} s";
    }
}
