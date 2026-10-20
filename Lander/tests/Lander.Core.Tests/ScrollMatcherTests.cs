using Lander.Core.Vision;
using static Lander.Core.Tests.ContentEdgeDetectorTests;

namespace Lander.Core.Tests;

public class ScrollMatcherTests
{
    private static readonly ContentEdgeOptions Options = new();

    [Theory]
    [InlineData(-37)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(118)]
    public void Finds_how_far_the_page_scrolled(int shift)
    {
        var edge = TrackedOn(Article(0));

        var match = ScrollMatcher.Find(edge, Article(shift), 0, 0, maxShift: 120);

        Assert.True(match.Found);
        Assert.Equal(shift, match.Shift);
        Assert.Equal(0, match.Difference);
    }

    [Fact]
    public void Reports_nothing_when_the_edge_left_the_search_range()
    {
        var edge = TrackedOn(Article(0));

        var match = ScrollMatcher.Find(edge, Article(-300), 0, 0, maxShift: 120);

        Assert.False(match.Found);
    }

    [Fact]
    public void Reports_nothing_when_the_page_changed()
    {
        var edge = TrackedOn(Article(0));
        var other = Canvas(900, 900, 250);
        DrawPhoto(other, 0, 0, 900, 900, seed: 2);

        Assert.False(ScrollMatcher.Find(edge, other, 0, 0, maxShift: 120).Found);
    }

    [Fact]
    public void Repeated_rows_prefer_the_predicted_scroll()
    {
        // Filas idénticas cada 40 px: 40, 0 y -40 encajan igual de bien; manda la velocidad prevista.
        var edge = TrackedOn(Rows(0));
        var scrolled = Rows(-40);

        Assert.Equal(-40, ScrollMatcher.Find(edge, scrolled, 0, 0, maxShift: 120, predicted: -36).Shift);
        Assert.Equal(0, ScrollMatcher.Find(edge, scrolled, 0, 0, maxShift: 120, predicted: 0).Shift);
    }

    [Fact]
    public void Works_on_a_strip_captured_around_the_edge()
    {
        // Solo se lee una franja de la ventana (desde la fila 300), no la ventana entera.
        var edge = TrackedOn(Article(0));
        var full = Article(-60);
        var strip = Canvas(900, 300, 0);
        for (var y = 0; y < 300; y++)
        {
            full.Row(300 + y).CopyTo(strip.Row(y));
        }

        var match = ScrollMatcher.Find(edge, strip, 0, 300, maxShift: 120);

        Assert.True(match.Found);
        Assert.Equal(-60, match.Shift);
    }

    /// <summary>Página con un separador en 450 + <paramref name="shift"/> y texto alrededor.</summary>
    private static GrayImage Article(int shift)
    {
        var image = Canvas(900, 900, 250);
        DrawText(image, 80, 330 + shift, 700, 14, seed: 1);
        FillRect(image, 60, 450 + shift, 780, 2, 170);
        DrawText(image, 80, 462 + shift, 600, 14, seed: 2);
        DrawText(image, 80, 490 + shift, 650, 14, seed: 3);
        return image;
    }

    /// <summary>Filas iguales cada 40 px con el primer separador libre por encima.</summary>
    private static GrayImage Rows(int shift)
    {
        var image = Canvas(900, 900, 250);
        for (var row = -2; row < 14; row++)
        {
            var top = 400 + shift + row * 40;
            if (top < 1 || top > 860)
            {
                continue;
            }

            FillRect(image, 60, top, 780, 1, 170);
            FillRect(image, 100, top + 14, 500, 10, 120);
        }

        return image;
    }

    private static TrackedEdge TrackedOn(GrayImage image)
    {
        var tracker = new ContentEdgeTracker();
        tracker.Update(image, 0, 0, new ContentEdgeDetector().Detect(image, Options));
        return tracker.Edges.MaxBy(e => e.Length)!;
    }
}
