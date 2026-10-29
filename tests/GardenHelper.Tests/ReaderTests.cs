using GardenHelper.Core.Library;
using GardenHelper.Core.Library.Reading;
using Xunit;

namespace GardenHelper.Tests;

public class ReaderTests
{
    [Fact]
    public void Headings_paragraphs_and_emphasis()
    {
        var b = HtmlBlocks.Parse("<html><head><title>Hidden</title><style>p{}</style></head><body><h2>Soil</h2><p>Good <b>soil</b> and <i>compost</i>.</p></body></html>");
        Assert.Equal(2, b.Count);
        Assert.Equal(BlockKind.Heading, b[0].Kind); Assert.Equal(2, b[0].Level); Assert.Equal("Soil", b[0].PlainText);
        Assert.Equal("Good soil and compost.", b[1].PlainText);
        Assert.Contains(b[1].Runs, r => r.Bold && r.Text == "soil");
        Assert.Contains(b[1].Runs, r => r.Italic && r.Text == "compost");
    }

    [Fact]
    public void Scripts_comments_and_entities()
    {
        var b = HtmlBlocks.Parse("<p>Tom &amp; Jerry &eacute;</p><script>alert('x')</script><!-- note --><p>after</p>");
        Assert.Equal(new[] { "Tom & Jerry é", "after" }, b.Select(x => x.PlainText));
    }

    [Fact]
    public void Lists_are_numbered_or_bulleted()
    {
        var b = HtmlBlocks.Parse("<ol><li>Dig</li><li>Plant</li></ol><ul><li>Water</li></ul>");
        Assert.Equal(new[] { "1.", "2.", "•" }, b.Select(x => x.Marker));
        Assert.All(b, x => Assert.Equal(BlockKind.ListItem, x.Kind));
    }

    [Fact]
    public void Quotes_preformatted_breaks_images_and_rules()
    {
        var b = HtmlBlocks.Parse("<blockquote><p>Wise words</p></blockquote><pre>a  b\n c</pre><p>line one<br/>line two</p><img src=\"img/a%20b.png\" alt=\"A plan\"/><hr/>");
        Assert.Equal(BlockKind.Quote, b[0].Kind);
        Assert.Equal(BlockKind.Preformatted, b[1].Kind); Assert.Equal("a  b\n c", b[1].PlainText);
        Assert.Equal("line one\nline two", b[2].PlainText);
        Assert.Equal("img/a%20b.png", b[3].ImageSrc); Assert.Equal("A plan", b[3].ImageAlt);
        Assert.Equal(BlockKind.Rule, b[4].Kind);
    }

    [Fact]
    public void Whitespace_is_collapsed_and_empty_blocks_dropped()
    {
        var b = HtmlBlocks.Parse("<div>\n   <p>  Many    spaces\n here </p>  <p>   </p>\n</div>");
        Assert.Equal("Many spaces here", Assert.Single(b).PlainText);
    }

    [Fact]
    public void Plain_text_and_markdown_become_blocks()
    {
        var b = HtmlBlocks.FromPlainText("Notes", "# Beds\nDig deep.\n- water\n\nLast line");
        Assert.Equal(new[] { BlockKind.Heading, BlockKind.Heading, BlockKind.Paragraph, BlockKind.ListItem, BlockKind.Paragraph }, b.Select(x => x.Kind));
        Assert.Equal("Beds", b[1].PlainText);
    }

    [Fact]
    public void Image_paths_cannot_escape_the_book_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "gh-rd-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "OEBPS"));
        File.WriteAllBytes(Path.Combine(root, "OEBPS", "a b.png"), new byte[] { 1 });
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "outside-" + Path.GetFileName(root) + ".png"), "x");
        try
        {
            var dir = Path.Combine(root, "OEBPS");
            Assert.NotNull(ReaderContentBuilder.ResolveImage(dir, root, "a%20b.png"));
            Assert.Null(ReaderContentBuilder.ResolveImage(dir, root, "../../outside-" + Path.GetFileName(root) + ".png"));
            Assert.Null(ReaderContentBuilder.ResolveImage(dir, root, "https://example.com/x.png"));
            Assert.Null(ReaderContentBuilder.ResolveImage(dir, root, "data:image/png;base64,AAAA"));
            Assert.Null(ReaderContentBuilder.ResolveImage(dir, root, "missing.png"));
            Assert.Null(ReaderContentBuilder.Locate(root, "../../etc/passwd"));
        }
        finally { try { Directory.Delete(root, true); } catch { /* temp */ } }
    }

    [Fact]
    public async Task Imported_epub_and_text_are_readable_natively_and_pdf_is_a_page()
    {
        using var env = await TestEnv.CreateAsync();
        var epub = Fixtures.Epub(env.File("book.epub"), "Mulching Handbook");
        var txt = env.File("notes.txt"); await File.WriteAllTextAsync(txt, "Garden notes\n\nCompost needs air and moisture to break down quickly.");
        var results = await env.Library.ImportAsync(new[] { epub, txt });
        var builder = new ReaderContentBuilder(env.Paths, env.Library);

        var epubBook = results[0].Book!;
        var c = await builder.BuildAsync(epubBook, 1);
        Assert.Equal(ReaderKind.Blocks, c.Kind);
        Assert.NotEmpty(c.Blocks);

        var c2 = await builder.BuildAsync(results[1].Book!, 1);
        Assert.Contains(c2.Blocks, b => b.PlainText.Contains("Compost needs air"));

        var pdf = new GardenHelper.Core.Domain.Book { Id = 99, Format = "PDF", StoredFile = "x.pdf" };
        await File.WriteAllBytesAsync(Path.Combine(env.Paths.LibraryDir, "x.pdf"), new byte[] { 1 });
        var p = await builder.BuildAsync(pdf, 7);
        Assert.Equal(ReaderKind.Pdf, p.Kind); Assert.Equal(7, p.Page);
        await Assert.ThrowsAsync<FileNotFoundException>(() => builder.BuildAsync(new GardenHelper.Core.Domain.Book { Id = 5, Format = "PDF", StoredFile = "gone.pdf" }, 1));
    }
}
