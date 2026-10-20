using Lander.Core.Vision;

namespace Lander.Core.Tests;

/// <summary>
/// Imágenes sintéticas que reproducen lo visto en capturas reales (Wikipedia, GitHub, MDN, BBC, VS Code y un
/// vídeo): separadores, títulos subrayados, tablas, fotos, texto y temas oscuros.
/// </summary>
public class ContentEdgeDetectorTests
{
    private static readonly ContentEdgeOptions Options = new();

    [Fact]
    public void Separator_line_on_plain_background_is_an_edge()
    {
        var image = Canvas(800, 400, 255);
        FillRect(image, 100, 200, 500, 1, 200);

        var edge = Assert.Single(Detect(image));

        Assert.Equal(200, edge.Y);
        Assert.Equal(100, edge.Left);
        Assert.Equal(600, edge.Right);
        Assert.False(edge.Rising);
        Assert.Equal(55, edge.Contrast);
    }

    [Fact]
    public void Only_the_top_of_a_thin_line_counts()
    {
        // La cara de abajo de la línea tiene la propia línea encima: no es fondo liso.
        var image = Canvas(800, 400, 30);
        FillRect(image, 100, 200, 500, 3, 120);

        var edge = Assert.Single(Detect(image));

        Assert.Equal(200, edge.Y);
        Assert.True(edge.Rising);
    }

    [Fact]
    public void Heading_rule_is_free_only_where_the_title_is_not_above()
    {
        // Como en Wikipedia: título grande y una línea gris debajo que llega hasta el margen derecho.
        var image = Canvas(1000, 300, 255);
        DrawText(image, 50, 120, 300, 30, seed: 1);
        FillRect(image, 50, 160, 900, 1, 162);

        var edge = Assert.Single(Detect(image));

        Assert.Equal(160, edge.Y);
        Assert.InRange(edge.Left, 320, 352);
        Assert.Equal(950, edge.Right);
    }

    [Fact]
    public void Table_rows_with_text_give_no_edges_but_the_free_top_does()
    {
        // Tabla con borde, filas de texto y separadores cada 28 px (como la lista de archivos de GitHub).
        var image = Canvas(900, 600, 250);
        FillRect(image, 100, 150, 700, 1, 200);
        for (var row = 0; row < 12; row++)
        {
            var top = 151 + row * 28;
            DrawText(image, 120, top + 8, 240, 12, seed: row);
            DrawText(image, 450, top + 8, 300, 12, seed: 100 + row);
            FillRect(image, 100, top + 27, 700, 1, 215);
        }

        var edge = Assert.Single(Detect(image));

        Assert.Equal(150, edge.Y);
        Assert.Equal(700, edge.Length);
    }

    [Fact]
    public void Paragraph_text_gives_no_edges_even_with_space_above()
    {
        var image = Canvas(1200, 500, 255);
        for (var line = 0; line < 6; line++)
        {
            DrawText(image, 60, 150 + line * 26, 1000, 16, seed: line);
        }

        // Un titular grande también: entre letra y letra siempre queda fondo.
        DrawText(image, 60, 80, 700, 40, seed: 77);

        Assert.Empty(Detect(image));
    }

    [Fact]
    public void Photo_top_edge_counts_but_nothing_inside_the_photo()
    {
        var image = Canvas(900, 700, 245);
        DrawPhoto(image, 200, 250, 500, 350, seed: 3);

        var edges = Detect(image);

        var edge = Assert.Single(edges);
        Assert.Equal(250, edge.Y);
        Assert.InRange(edge.Length, 450, 500);
    }

    [Fact]
    public void Short_or_faint_lines_are_ignored()
    {
        var image = Canvas(800, 400, 255);
        FillRect(image, 100, 100, Options.MinLength - 10, 1, 120);
        FillRect(image, 100, 250, 600, 1, 255 - Options.MinContrast + 4);

        Assert.Empty(Detect(image));
    }

    [Fact]
    public void Dark_theme_borders_are_found()
    {
        // Marco de un bloque de código en VS Code oscuro: #181818 sobre #303030 (salto de 24).
        var image = Canvas(800, 400, 24);
        FillRect(image, 150, 220, 450, 100, 48);
        FillRect(image, 151, 221, 448, 98, 24);

        var edges = Detect(image);

        Assert.Contains(edges, e => e.Y == 220 && e.Left == 150 && e.Right == 600 && e.Rising);
    }

    [Fact]
    public void Soft_edge_over_two_rows_gives_a_single_edge()
    {
        var image = Canvas(800, 400, 255);
        FillRect(image, 100, 200, 500, 1, 200);
        FillRect(image, 100, 201, 500, 199, 128);

        var edge = Assert.Single(Detect(image));

        Assert.Equal(200, edge.Y);
    }

    [Fact]
    public void Tiny_gaps_are_bridged_but_dashes_are_not()
    {
        var image = Canvas(900, 500, 255);
        DrawDashed(image, 50, 150, 800, dash: 12, gap: Options.MaxGap, value: 90);
        DrawDashed(image, 50, 350, 800, dash: 12, gap: 6, value: 90);

        var edge = Assert.Single(Detect(image));

        Assert.Equal(150, edge.Y);
        Assert.InRange(edge.Length, 790, 800);
    }

    [Fact]
    public void Edge_needs_the_full_clearance_inside_the_image()
    {
        var image = Canvas(800, 300, 255);
        FillRect(image, 100, Options.Clearance - 1, 500, 1, 100);
        FillRect(image, 100, Options.Clearance + 100, 500, 1, 100);

        var edge = Assert.Single(Detect(image));

        Assert.Equal(Options.Clearance + 100, edge.Y);
    }

    [Fact]
    public void Edge_reaching_the_last_column_is_complete()
    {
        // Ancho que no es múltiplo de 16: el final de la fila va por la vía escalar.
        var image = Canvas(333, 200, 255);
        FillRect(image, 0, 120, 333, 1, 0);

        var edge = Assert.Single(Detect(image));

        Assert.Equal(0, edge.Left);
        Assert.Equal(333, edge.Right);
    }

    [Fact]
    public void Keeps_the_longest_and_strongest_edges()
    {
        var image = Canvas(1000, 1200, 255);
        for (var i = 0; i < 8; i++)
        {
            FillRect(image, 50, 100 + i * 120, 200 + i * 80, 1, 60);
        }

        var edges = Detect(image, Options with { MaxEdges = 3 });

        Assert.Equal(3, edges.Count);
        Assert.Equal([700, 820, 940], edges.Select(e => e.Y));
    }

    [Fact]
    public void Reuses_buffers_between_images_of_different_size()
    {
        var detector = new ContentEdgeDetector();
        var big = Canvas(1600, 900, 255);
        FillRect(big, 100, 500, 1200, 1, 0);
        var small = Canvas(400, 200, 255);
        FillRect(small, 20, 100, 300, 1, 0);

        Assert.Single(detector.Detect(big, Options));
        var edge = Assert.Single(detector.Detect(small, Options));
        Assert.Equal(100, edge.Y);
        Assert.Single(detector.Detect(big, Options));
    }

    [Fact]
    public void Unknown_zone_like_the_lander_itself_does_not_break_the_edge_below_it()
    {
        // El lander dibujado encima del borde: tapa la línea y llena de «ruido» la zona de encima.
        var image = Canvas(900, 400, 250);
        FillRect(image, 100, 250, 700, 1, 150);
        DrawPhoto(image, 420, 196, 50, 60, seed: 9);
        var lander = new Geometry.PixelRect(410, 190, 480, 262);

        Assert.DoesNotContain(Detect(image), e => e.Left < 420 && e.Right > 470);

        var edge = Assert.Single(new ContentEdgeDetector().Detect(image, Options, [lander]));
        Assert.Equal(250, edge.Y);
        Assert.Equal((100, 800), (edge.Left, edge.Right));
        Assert.Equal(100, edge.Contrast);
    }

    [Fact]
    public void Unknown_zone_alone_is_never_an_edge()
    {
        var image = Canvas(900, 400, 250);
        DrawText(image, 100, 300, 700, 14, seed: 4);
        var unknown = new Geometry.PixelRect(300, 100, 300 + Options.MinLength + 40, 400);

        Assert.Empty(new ContentEdgeDetector().Detect(image, Options, [unknown]));
    }

    [Fact]
    public void Bgra_is_converted_to_luma_respecting_the_pitch()
    {
        const int width = 37;
        const int pitch = width * 4 + 12;
        var bgra = new byte[pitch * 3];
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * pitch + x * 4;
                bgra[i] = (byte)(x * 7);
                bgra[i + 1] = (byte)(y * 90);
                bgra[i + 2] = (byte)(255 - x * 5);
                bgra[i + 3] = 255;
            }

            bgra.AsSpan(y * pitch + width * 4, 12).Fill(0xAB);
        }

        var image = new GrayImage(1, 1);
        image.LoadBgra(bgra, width, 3, pitch);

        Assert.Equal(width, image.Width);
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Assert.Equal(GrayImage.Luma((byte)(255 - x * 5), (byte)(y * 90), (byte)(x * 7)), image[x, y]);
            }
        }

        Assert.Equal(255, GrayImage.Luma(255, 255, 255));
        Assert.Equal(0, GrayImage.Luma(0, 0, 0));
    }

    [Fact]
    public void Bit_scans_find_runs_across_words()
    {
        ulong[] mask = [0xFFFF_0000_0000_0000UL, 0x0000_0000_0000_00FFUL, 0];

        Assert.Equal(48, ContentEdgeDetector.NextSet(mask, 0, 192));
        Assert.Equal(72, ContentEdgeDetector.NextClear(mask, 48, 192));
        Assert.Equal(192, ContentEdgeDetector.NextSet(mask, 72, 192));
        Assert.Equal(100, ContentEdgeDetector.NextSet(mask, 72, 100));
        Assert.Equal(10, ContentEdgeDetector.NextClear(mask, 10, 192));
    }

    internal static List<ContentEdge> Detect(GrayImage image, ContentEdgeOptions? options = null) =>
        [.. new ContentEdgeDetector().Detect(image, options ?? Options)];

    internal static GrayImage Canvas(int width, int height, byte value)
    {
        var image = new GrayImage(width, height);
        image.Pixels.Fill(value);
        return image;
    }

    internal static void FillRect(GrayImage image, int x, int y, int width, int height, int value)
    {
        for (var row = y; row < y + height; row++)
        {
            image.Row(row).Slice(x, width).Fill((byte)value);
        }
    }

    /// <summary>Renglón de «texto»: palabras de letras con trazos, huecos entre letras y antialias.</summary>
    internal static void DrawText(GrayImage image, int x, int y, int width, int height, int seed)
    {
        var random = new Random(seed);
        var cursor = x;
        while (cursor < x + width)
        {
            var letters = random.Next(2, 9);
            for (var i = 0; i < letters && cursor < x + width; i++)
            {
                var letterWidth = Math.Max(3, height / 2 + random.Next(-2, 3));
                var top = y + (random.Next(3) == 0 ? 0 : height / 3);
                for (var col = 0; col < letterWidth && cursor + col < x + width; col++)
                {
                    // Trazos verticales en los bordes de la letra y uno horizontal arriba o abajo.
                    var stem = col == 0 || col == letterWidth - 1 || (letterWidth > 5 && col == letterWidth / 2 && random.Next(2) == 0);
                    for (var row = top; row < y + height; row++)
                    {
                        var bar = row == top || row == y + height - 1;
                        if (stem || (bar && random.Next(4) != 0))
                        {
                            image[cursor + col, row] = (byte)random.Next(20, 90);
                        }
                    }
                }

                cursor += letterWidth + random.Next(2, 4);
            }

            cursor += height / 2 + random.Next(2, 6);
        }
    }

    /// <summary>«Foto»: textura con gradientes y ruido, sin bordes rectos dentro.</summary>
    internal static void DrawPhoto(GrayImage image, int x, int y, int width, int height, int seed)
    {
        var random = new Random(seed);
        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                var value = 110 + 50 * Math.Sin(col * 0.031 + row * 0.017) + 35 * Math.Cos(row * 0.052 - col * 0.011) + random.Next(-25, 26);
                image[x + col, y + row] = (byte)Math.Clamp(value, 0, 255);
            }
        }
    }

    internal static void DrawDashed(GrayImage image, int x, int y, int length, int dash, int gap, int value)
    {
        for (var start = x; start < x + length; start += dash + gap)
        {
            FillRect(image, start, y, Math.Min(dash, x + length - start), 1, value);
        }
    }
}
