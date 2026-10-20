using Lander.Core.Vision;
using static Lander.Core.Tests.ContentEdgeDetectorTests;

namespace Lander.Core.Tests;

public class ContentEdgeTrackerTests
{
    private static readonly ContentEdgeOptions Options = new();

    [Fact]
    public void Edge_is_confirmed_after_two_identical_scans_and_keeps_its_id()
    {
        var tracker = new ContentEdgeTracker();
        var image = Page();

        Scan(tracker, image);
        var first = Assert.Single(tracker.Edges);
        Assert.Empty(tracker.Confirmed);

        Scan(tracker, image);
        var confirmed = Assert.Single(tracker.Confirmed);
        Assert.Equal(first.Id, confirmed.Id);
        Assert.Equal(300, confirmed.Y);
    }

    [Fact]
    public void Edge_over_changing_content_is_never_confirmed()
    {
        // Como un vídeo: el borde está, pero lo que hay debajo cambia en cada escaneo.
        var tracker = new ContentEdgeTracker();
        for (var frame = 0; frame < 4; frame++)
        {
            var image = Canvas(900, 600, 250);
            DrawPhoto(image, 100, 300, 700, 250, seed: frame);
            Scan(tracker, image);
        }

        Assert.Single(tracker.Edges);
        Assert.Empty(tracker.Confirmed);
    }

    [Fact]
    public void Small_changes_like_a_caret_do_not_break_the_edge()
    {
        var tracker = new ContentEdgeTracker();
        var image = Page();
        Scan(tracker, image);
        FillRect(image, 400, 304, 1, 10, 0);
        Scan(tracker, image);

        Assert.Single(tracker.Confirmed);
    }

    [Fact]
    public void Unchanged_screen_confirms_without_rescanning_and_demote_undoes_it()
    {
        var tracker = new ContentEdgeTracker();
        Scan(tracker, Page());

        tracker.ConfirmUnchanged();
        var edge = Assert.Single(tracker.Confirmed);

        tracker.Demote(edge.Id);
        Assert.Empty(tracker.Confirmed);
    }

    [Fact]
    public void Text_changing_below_the_edge_does_not_change_its_identity()
    {
        // Como la hora de la fila de debajo de un separador («ahora», «2 min»).
        var tracker = new ContentEdgeTracker();
        Scan(tracker, Page());
        var id = tracker.Edges[0].Id;

        var changed = Canvas(900, 600, 250);
        FillRect(changed, 100, 300, 700, 2, 180);
        DrawText(changed, 120, 310, 600, 12, seed: 99);
        Scan(tracker, changed);

        var edge = Assert.Single(tracker.Confirmed);
        Assert.Equal(id, edge.Id);
    }

    [Fact]
    public void Edge_that_disappears_is_forgotten()
    {
        var tracker = new ContentEdgeTracker();
        Scan(tracker, Page());
        Scan(tracker, Page());
        Scan(tracker, Canvas(900, 600, 250));

        Assert.Empty(tracker.Edges);
    }

    [Fact]
    public void Edges_are_local_to_the_window_whatever_part_was_captured()
    {
        // Segundo escaneo con la ventana medio fuera de la pantalla: se captura desde x = 150.
        var tracker = new ContentEdgeTracker();
        var full = Page();
        Scan(tracker, full);

        var cropped = Canvas(750, 600, 250);
        for (var y = 0; y < 600; y++)
        {
            full.Row(y)[150..].CopyTo(cropped.Row(y));
        }

        tracker.Update(cropped, 150, 0, new ContentEdgeDetector().Detect(cropped, Options));

        var edge = Assert.Single(tracker.Confirmed);
        Assert.Equal(150, edge.Left);
        Assert.Equal(800, edge.Right);
    }

    [Fact]
    public void Shift_follows_scrolled_content()
    {
        var tracker = new ContentEdgeTracker();
        Scan(tracker, Page(300));
        var id = tracker.Edges[0].Id;

        tracker.Shift(-120);
        Scan(tracker, Page(180));

        var edge = Assert.Single(tracker.Confirmed);
        Assert.Equal(id, edge.Id);
        Assert.Equal(180, edge.Y);
    }

    [Fact]
    public void Edge_that_scrolled_between_scans_keeps_its_id()
    {
        // La página se ha movido 140 px entre dos escaneos sin que nadie midiera el scroll.
        var tracker = new ContentEdgeTracker();
        Scan(tracker, Page(300));
        Scan(tracker, Page(300));
        var id = tracker.Edges[0].Id;

        Scan(tracker, Page(160));

        var edge = Assert.Single(tracker.Confirmed);
        Assert.Equal(id, edge.Id);
        Assert.Equal(160, edge.Y);
    }

    [Fact]
    public void Repetitive_page_that_jumps_keeps_identities_in_order()
    {
        // Secciones idénticas cada 208 px y un salto de 260: lo prudente es suponer que todo se ha movido
        // 52 px (al vecino), nunca cruzar identificadores.
        var tracker = new ContentEdgeTracker();
        Scan(tracker, Sections(0));
        Scan(tracker, Sections(0));
        var before = tracker.Edges.OrderBy(e => e.Y).Select(e => e.Id).ToList();

        Scan(tracker, Sections(260));

        var after = tracker.Edges.Where(e => before.Contains(e.Id)).OrderBy(e => e.Y).Select(e => e.Id).ToList();
        Assert.NotEmpty(after);
        Assert.Equal(after.OrderBy(id => before.IndexOf(id)), after);
    }

    [Fact]
    public void Band_difference_finds_the_scroll_offset()
    {
        var tracker = new ContentEdgeTracker();
        Scan(tracker, Page(300));
        var edge = tracker.Edges[0];
        var scrolled = Page(260);

        var best = Enumerable.Range(-80, 161).MinBy(dy => ContentEdgeTracker.BandDifference(edge, scrolled, 0, 0, dy));

        Assert.Equal(-40, best);
        Assert.Equal(0, ContentEdgeTracker.BandDifference(edge, scrolled, 0, 0, -40));
    }

    /// <summary>Página clara con un separador en <paramref name="lineY"/> y un párrafo debajo.</summary>
    private static GrayImage Page(int lineY = 300)
    {
        var image = Canvas(900, 600, 250);
        FillRect(image, 100, lineY, 700, 2, 180);
        DrawText(image, 120, lineY + 10, 600, 12, seed: 5);
        return image;
    }

    /// <summary>Página repetitiva: secciones iguales (título, regla, párrafo) cada 208 px.</summary>
    private static GrayImage Sections(int shift)
    {
        var image = Canvas(900, 1400, 250);
        for (var top = -2000 + shift; top < 1400; top += 208)
        {
            if (top - 60 < 0 || top + 60 > 1400)
            {
                continue;
            }

            DrawText(image, 100, top - 40, 160, 20, seed: 1);
            FillRect(image, 100, top, 700, 2, 128);
            DrawText(image, 100, top + 16, 650, 12, seed: 2);
            DrawText(image, 100, top + 36, 650, 12, seed: 3);
        }

        return image;
    }

    private static void Scan(ContentEdgeTracker tracker, GrayImage image) =>
        tracker.Update(image, 0, 0, new ContentEdgeDetector().Detect(image, Options));
}
