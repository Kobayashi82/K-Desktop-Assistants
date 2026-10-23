using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Assistant.Sdk;
using Lander.Assistant.Rendering;
using Lander.Core.Game;

namespace Lander.Assistant.Tests;

/// <summary>
/// Lo de la fase 15 de K-Desktop: el escaparate (sus animaciones, con nombre en los seis idiomas), los iconos de lo que hace
/// mientras atiende una orden y que todos los iconos de los bocadillos se dibujan.
/// </summary>
public sealed class MomentsTests
{
    private static readonly string[] Languages = ["es", "en", "fr", "de", "it", "pt"];

    [Fact]
    public void Showcase_items_have_unique_ids_and_a_name_in_every_language()
    {
        var items = LanderAssistant.ShowcaseItems;
        Assert.NotEmpty(items);
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
        foreach (var item in items)
        {
            Assert.StartsWith("lander.", item.Id, StringComparison.Ordinal);
            foreach (var language in Languages)
            {
                Assert.True(item.Name.TryGetValue(language, out var name) && !string.IsNullOrWhiteSpace(name), $"{item.Id} sin nombre en {language}");
            }
        }

        // Al menos una de cada grupo que tiene sentido para él.
        foreach (var group in new[] { ShowcaseGroup.Idle, ShowcaseGroup.Moving, ShowcaseGroup.Tricks, ShowcaseGroup.Feelings })
        {
            Assert.Contains(items, i => i.Group == group);
        }
    }

    [Fact]
    public void Busy_with_something_shows_what_and_speaking_or_idle_show_nothing()
    {
        Assert.Equal(BubbleIcon.Globe, LanderAssistant.ActivityIcon(AssistantState.Busy, new StateDetail(AssistantActivity.SearchingWeb)));
        Assert.Equal(BubbleIcon.Clock, LanderAssistant.ActivityIcon(AssistantState.Busy, new StateDetail(AssistantActivity.Timer)));
        Assert.Equal(BubbleIcon.Pencil, LanderAssistant.ActivityIcon(AssistantState.Listening, new StateDetail(AssistantActivity.Dictation)));
        Assert.Equal(BubbleIcon.Loading, LanderAssistant.ActivityIcon(AssistantState.LoadingBrain, StateDetail.None));
        Assert.Equal(BubbleIcon.Gears, LanderAssistant.ActivityIcon(AssistantState.ThinkingHard, StateDetail.None));
        Assert.Null(LanderAssistant.ActivityIcon(AssistantState.Thinking, StateDetail.None));
        Assert.Null(LanderAssistant.ActivityIcon(AssistantState.Listening, StateDetail.None));
        Assert.Null(LanderAssistant.ActivityIcon(AssistantState.Speaking, new StateDetail(AssistantActivity.SearchingWeb)));
        Assert.Null(LanderAssistant.ActivityIcon(AssistantState.Idle, StateDetail.None));

        // Las actividades que lleguen y no conozca, también con algo (las de herramientas propias o de MCP).
        foreach (var activity in Enum.GetValues<AssistantActivity>().Where(a => a != AssistantActivity.None))
        {
            Assert.NotNull(LanderAssistant.ActivityIcon(AssistantState.Busy, new StateDetail(activity)));
        }
    }

    [Fact]
    public void Every_bubble_icon_draws_something()
    {
        const int size = 32;
        var sheet = Environment.GetEnvironmentVariable("LANDER_ICONS_PNG");
        var icons = Enum.GetValues<BubbleIcon>();
        var empty = new List<BubbleIcon>();
        RunSta(() =>
        {
            var all = new DrawingVisual();
            using (var page = all.RenderOpen())
            {
                for (var i = 0; i < icons.Length; i++)
                {
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                    {
                        dc.PushTransform(new TranslateTransform(size / 2, size / 2));
                        dc.PushTransform(new ScaleTransform(1.8, 1.8));
                        Icons.Draw(dc, icons[i], 0, null, 0.4, dark: false);
                        dc.Pop();
                        dc.Pop();
                    }

                    var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    var pixels = new byte[size * size * 4];
                    bitmap.CopyPixels(pixels, size * 4, 0);
                    if (pixels.Where((_, n) => n % 4 == 3).All(a => a == 0))
                    {
                        empty.Add(icons[i]);
                    }

                    page.DrawRectangle(Brushes.White, null, new Rect(i % 8 * 40, i / 8 * 40, 40, 40));
                    page.DrawImage(bitmap, new Rect(i % 8 * 40 + 4, i / 8 * 40 + 4, size, size));
                }
            }

            if (sheet is { Length: > 0 })
            {
                var bitmap = new RenderTargetBitmap(320, (icons.Length + 7) / 8 * 40, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(all);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(sheet);
                encoder.Save(file);
            }
        });

        // App sin icono de la aplicación pinta un recuadro: todos dibujan algo.
        Assert.Empty(empty);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("No se pudo dibujar", failure);
        }
    }
}
