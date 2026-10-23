using System.Reflection;
using KDesktop.Assistant.Sdk;
using Kobii.Core.Animation;

namespace Kobii.Assistant.Tests;

/// <summary>
/// Lo de la fase 15 de K-Desktop: el escaparate (con nombre en los seis idiomas, cada elemento con su animación o su rutina)
/// y que las animaciones con las que reacciona, se despide o enseña lo que hace existen de verdad en su catálogo.
/// </summary>
public sealed class MomentsTests
{
    private static readonly string[] Languages = ["es", "en", "fr", "de", "it", "pt"];

    [Fact]
    public void Showcase_items_have_unique_ids_and_a_name_in_every_language()
    {
        var items = KobiiAssistant.ShowcaseItems;
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
        foreach (var item in items)
        {
            foreach (var language in Languages)
            {
                Assert.True(item.Name.TryGetValue(language, out var name) && !string.IsNullOrWhiteSpace(name), $"{item.Id} sin nombre en {language}");
            }
        }

        foreach (var group in new[] { ShowcaseGroup.Idle, ShowcaseGroup.Moving, ShowcaseGroup.Playing, ShowcaseGroup.Tricks, ShowcaseGroup.Feelings })
        {
            Assert.Contains(items, i => i.Group == group);
        }
    }

    [Fact]
    public void Every_clip_it_reacts_says_goodbye_or_works_with_is_in_the_catalog()
    {
        var clips = new List<string>();
        var kobii = new KobiiAssistant();
        foreach (var field in typeof(KobiiAssistant).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            switch (field.GetValue(kobii))
            {
                case Variety<string> variety:
                    clips.AddRange(variety.Items);
                    break;
                case Variety<(string Clip, double Seconds)> goodbyes:
                    clips.AddRange(goodbyes.Items.Select(g => g.Clip));
                    break;
            }
        }

        clips.AddRange(Enum.GetValues<AssistantActivity>().Select(KobiiAssistant.BusyClip).OfType<string>());
        clips.AddRange(KobiiAssistant.ShowcaseClips.Values);
        clips.AddRange(["enfurrunarse", "voltereta", "saltitos", "suspirar", "saludar-dos", "cucu-tras", "brazos-arriba", "encogerse"]);
        Assert.True(clips.Count > 20);
        foreach (var clip in clips.Distinct())
        {
            Assert.True(AnimationCatalog.Find(clip) is not null, $"«{clip}» no está en el catálogo");
        }
    }

    [Fact]
    public void Reading_and_music_show_their_own_animation_and_orders_leave_it_to_the_gestures()
    {
        Assert.Equal("leer-de-pie", KobiiAssistant.BusyClip(AssistantActivity.ReadingWeb));
        Assert.Equal("canturrear", KobiiAssistant.BusyClip(AssistantActivity.Music));
        Assert.Equal("hoja-paraguas", KobiiAssistant.BusyClip(AssistantActivity.Weather));
        Assert.Null(KobiiAssistant.BusyClip(AssistantActivity.Command));
        Assert.Null(KobiiAssistant.BusyClip(AssistantActivity.Timer));
        Assert.Null(KobiiAssistant.BusyClip(AssistantActivity.None));
    }
}
