using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Sdk;
using Lander.Core.Progression;
using Lander.Core.Settings;

namespace Lander.Assistant.Tests;

/// <summary>
/// El paquete y los ajustes: K-Desktop dibuja los ajustes del settings.json y devuelve los valores con esas claves, que
/// el lander lee con el modelo de ajustes del Lander independiente. Si no coinciden, un ajuste no haría nada.
/// </summary>
public sealed class PackageTests
{
    private static readonly string Package = FindPackage();

    [Fact]
    public void Every_setting_key_exists_in_LanderSettings_with_the_same_default()
    {
        var model = JsonSerializer.SerializeToNode(new LanderSettings(), LanderJsonContext.Default.LanderSettings)!.AsObject();
        var items = SettingItems().Where(i => i["key"] is not null).ToList();
        Assert.NotEmpty(items);
        foreach (var item in items)
        {
            var key = item["key"]!.GetValue<string>();
            Assert.True(model.ContainsKey(key), $"«{key}» no es un ajuste del Lander");

            var expected = model[key];
            var actual = item["default"];
            Assert.True(SameValue(expected, actual), $"«{key}»: por defecto {actual?.ToJsonString()} en settings.json y {expected?.ToJsonString()} en el Lander");
        }
    }

    [Fact]
    public void Choices_offer_the_value_of_their_default()
    {
        foreach (var item in SettingItems().Where(i => i["type"]?.GetValue<string>() == "choice"))
        {
            var values = item["options"]!.AsArray().Select(o => o!["value"]).ToList();
            Assert.Contains(values, v => SameValue(v, item["default"]));
        }
    }

    [Fact]
    public void Values_saved_by_KDesktop_are_read()
    {
        var json = JsonDocument.Parse("""
            { "missionFrequency": "high", "pilotErrors": 0.25, "acrobatics": 1, "landOnContent": true,
              "contentExcludedApps": ["Slack.exe"], "skin": "apollo" }
            """).RootElement;
        var settings = LanderAssistant.ReadSettings(json);
        Assert.Equal(Core.Game.MissionFrequency.High, settings.MissionFrequency);
        Assert.Equal(0.25, settings.PilotErrors);
        Assert.Equal(1, settings.Acrobatics);
        Assert.True(settings.LandOnContent);
        Assert.Equal(["slack"], settings.ContentExcludedApps);
        Assert.Equal("apollo", settings.Skin);
    }

    /// <summary>
    /// Lo que K-Desktop pone igual para todos (SDK 1.9) manda sobre lo que quede guardado de antes: tamaño en %, lo movido
    /// que es y cogerlo con el ratón; el sonido siempre encendido y el volumen fijo (el host aplica el suyo encima).
    /// </summary>
    [Fact]
    public void The_common_settings_of_KDesktop_win()
    {
        var old = JsonDocument.Parse("""{ "landerSize": 80, "activity": 2.5, "allowDragging": true, "soundEnabled": false, "volume": 0.9 }""").RootElement;
        var settings = LanderAssistant.ReadSettings(old, new CommonSettings(Size: 1.5, Liveliness: 0, Dragging: false));
        Assert.Equal(72, settings.LanderSize);
        Assert.Equal(0.25, settings.Activity);
        Assert.False(settings.AllowDragging);
        Assert.True(settings.SoundEnabled);
        Assert.Equal(0.5, settings.Volume);

        Assert.Equal(LanderSettings.MinSize, LanderAssistant.ReadSettings(old, new CommonSettings(Size: 0.5)).LanderSize);
        Assert.Equal(LanderSettings.MaxSize, LanderAssistant.ReadSettings(old, new CommonSettings(Size: 2)).LanderSize);
        Assert.Equal(LanderAssistant.BaseSize, LanderAssistant.ReadSettings(old).LanderSize);
    }

    [Fact]
    public void Missing_or_broken_values_fall_back_to_the_defaults()
    {
        Assert.True(LanderAssistant.ReadSettings(JsonDocument.Parse("{}").RootElement).SoundEnabled);
        var broken = LanderAssistant.ReadSettings(JsonDocument.Parse("""{ "pilotErrors": "mucho", "skin": "nope" }""").RootElement);
        Assert.Equal(0.5, broken.PilotErrors);
        Assert.Equal(Unlocks.DefaultSkin, broken.Skin);
    }

    [Fact]
    public void Every_skin_and_trail_has_its_gallery_image()
    {
        foreach (var (kind, folder) in new[] { (UnlockKind.Skin, "skins"), (UnlockKind.Trail, "trails") })
        {
            foreach (var unlock in Unlocks.OfKind(kind))
            {
                Assert.True(File.Exists(Path.Combine(Package, "assets", folder, unlock.Id + ".png")), $"Falta la imagen de «{unlock.Id}»");
            }
        }
    }

    [Fact]
    public void Texts_of_the_package_are_in_the_six_languages()
    {
        string[] languages = ["es", "en", "fr", "de", "it", "pt"];
        foreach (var file in new[] { "assistant.json", "settings.json", "panel.json" })
        {
            foreach (var text in LocalizedTexts(JsonNode.Parse(File.ReadAllText(Path.Combine(Package, file)))!))
            {
                Assert.All(languages, l => Assert.True(text.ContainsKey(l), $"{file}: falta «{l}» en {text.ToJsonString()[..Math.Min(80, text.ToJsonString().Length)]}"));
            }
        }
    }

    private static IEnumerable<JsonObject> SettingItems()
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(Package, "settings.json")))!;
        return root["pages"]!.AsArray()
            .SelectMany(p => p!["sections"]!.AsArray())
            .SelectMany(s => s!["items"]!.AsArray())
            .Select(i => i!.AsObject());
    }

    /// <summary>Los objetos idioma → texto que hay en un JSON (los que tienen la clave «es»).</summary>
    private static IEnumerable<JsonObject> LocalizedTexts(JsonNode node)
    {
        switch (node)
        {
            case JsonObject o when o.ContainsKey("es") && o.All(p => p.Value is JsonValue):
                yield return o;
                break;
            case JsonObject o:
                foreach (var child in o.Select(p => p.Value).OfType<JsonNode>().SelectMany(LocalizedTexts))
                {
                    yield return child;
                }

                break;
            case JsonArray a:
                foreach (var child in a.OfType<JsonNode>().SelectMany(LocalizedTexts))
                {
                    yield return child;
                }

                break;
        }
    }

    private static bool SameValue(JsonNode? a, JsonNode? b)
    {
        if (a is JsonValue va && b is JsonValue vb && va.TryGetValue<double>(out var da) && vb.TryGetValue<double>(out var db))
        {
            return Math.Abs(da - db) < 1e-9;
        }

        if (a is JsonValue sa && b is JsonValue sb && sa.TryGetValue<string>(out var ta) && sb.TryGetValue<string>(out var tb))
        {
            return string.Equals(ta, tb, StringComparison.OrdinalIgnoreCase);
        }

        return JsonNode.DeepEquals(a, b);
    }

    private static string FindPackage()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var package = Path.Combine(dir.FullName, "src", "Lander.Assistant", "Package");
            if (Directory.Exists(package))
            {
                return package;
            }
        }

        throw new DirectoryNotFoundException("No encuentro src/Lander.Assistant/Package");
    }
}
