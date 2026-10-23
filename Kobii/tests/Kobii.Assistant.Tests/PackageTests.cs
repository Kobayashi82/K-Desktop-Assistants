using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Sdk;
using Kobii.Core.Creature;

namespace Kobii.Assistant.Tests;

/// <summary>
/// El paquete y los ajustes: K-Desktop dibuja los ajustes del settings.json y devuelve los valores con esas claves, que
/// Kobii lee con <see cref="KobiiSettings"/>. Si no coinciden, un ajuste no haría nada.
/// </summary>
public sealed class PackageTests
{
    internal static readonly string Package = FindPackage();

    [Fact]
    public void Every_setting_key_is_read_with_the_same_default()
    {
        var defaults = new KobiiSettings();
        var expected = new Dictionary<string, object>
        {
            ["color"] = defaults.Color,
            ["followCursor"] = defaults.FollowCursor,
        };

        var items = SettingItems().Where(i => i["key"] is not null).ToList();
        Assert.Equal(expected.Keys.Order(), items.Select(i => i["key"]!.GetValue<string>()).Order());
        foreach (var item in items)
        {
            var key = item["key"]!.GetValue<string>();
            Assert.Equal(JsonSerializer.Serialize(expected[key]), item["default"]!.ToJsonString());
        }
    }

    [Fact]
    public void Values_saved_by_KDesktop_are_read()
    {
        // Lo que quedó guardado de antes (tamaño, movido, cogerlo) ya no cuenta: manda lo común de K-Desktop (SDK 1.9).
        var settings = KobiiSettings.Read(JsonDocument.Parse("""
            { "color": "lilac", "size": 1.4, "liveliness": 0.5, "followCursor": false, "allowDragging": true }
            """).RootElement, new CommonSettings(Size: 1.25, Liveliness: 1.5, Dragging: false));
        Assert.Equal("lilac", settings.Color);
        Assert.Equal(1.25, settings.Size);
        Assert.Equal(1.5, settings.Liveliness);
        Assert.False(settings.FollowCursor);
        Assert.False(settings.AllowDragging);
    }

    [Fact]
    public void Missing_or_broken_values_fall_back_to_the_defaults()
    {
        var broken = KobiiSettings.Read(JsonDocument.Parse("""{ "color": "rainbow", "followCursor": 3 }""").RootElement, CommonSettings.Default);
        Assert.Equal("mint", broken.Color);
        Assert.True(broken.FollowCursor);
        Assert.Equal(1, broken.Size);
        // Al 0 % lo menos movido que puede ser.
        Assert.Equal(0.2, KobiiSettings.Read(default, new CommonSettings(Liveliness: 0)).Liveliness);
    }

    [Fact]
    public void Every_colour_is_in_the_gallery_with_its_image()
    {
        var gallery = SettingItems().Single(i => i["key"]?.GetValue<string>() == "color");
        var ids = gallery["items"]!.AsArray().Select(i => i!["id"]!.GetValue<string>()).ToList();
        Assert.Equal(Species.Palettes.Select(p => p.Id), ids);
        foreach (var item in gallery["items"]!.AsArray())
        {
            Assert.True(File.Exists(Path.Combine(Package, item!["image"]!.GetValue<string>())), $"Falta la imagen de «{item["id"]}»");
        }

        Assert.True(File.Exists(Path.Combine(Package, "icon.png")));
    }

    [Fact]
    public void Texts_of_the_package_are_in_the_six_languages()
    {
        string[] languages = ["es", "en", "fr", "de", "it", "pt"];
        foreach (var file in new[] { "assistant.json", "settings.json" })
        {
            foreach (var text in LocalizedTexts(JsonNode.Parse(File.ReadAllText(Path.Combine(Package, file)))!))
            {
                Assert.All(languages, l => Assert.True(text.ContainsKey(l), $"{file}: falta «{l}» en {text.ToJsonString()[..Math.Min(80, text.ToJsonString().Length)]}"));
            }
        }

        foreach (var language in languages)
        {
            var persona = JsonNode.Parse(File.ReadAllText(Path.Combine(Package, "persona", language + ".json")))!;
            Assert.False(string.IsNullOrWhiteSpace(persona["prompt"]?.GetValue<string>()));
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

    private static string FindPackage()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var package = Path.Combine(dir.FullName, "src", "Kobii.Assistant", "Package");
            if (Directory.Exists(package))
            {
                return package;
            }
        }

        throw new DirectoryNotFoundException("No encuentro src/Kobii.Assistant/Package");
    }
}
