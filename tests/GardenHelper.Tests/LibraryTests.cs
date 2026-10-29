using System.IO.Compression;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Library;
using GardenHelper.Core.Library.Extractors;
using Microsoft.EntityFrameworkCore;

namespace GardenHelper.Tests;

public class LibraryTests
{
    [Fact]
    public async Task Epub_is_imported_with_metadata_toc_titles_and_subjects()
    {
        using var env = await TestEnv.CreateAsync();
        var result = (await env.Library.ImportAsync(new[] { Fixtures.Epub(env.File("a.epub")) })).Single();

        Assert.Null(result.Error);
        var book = await env.Library.GetAsync(result.Book!.Id);
        Assert.Equal("Mulching Handbook", book!.Title);
        Assert.Equal("A. Gardener", book.Author);
        Assert.Equal(2, book.SectionCount);
        Assert.Equal(IndexStatus.Indexed, book.IndexStatus);
        Assert.Contains(book.Subjects, s => s.Term == "permaculture" && s.Source == "metadata");
        var sections = await env.Library.GetSectionHeadersAsync(book.Id);
        Assert.Equal(new[] { "Soil", "Water" }, sections.Select(s => s.Title));
    }

    [Fact]
    public async Task Search_ignores_diacritics_and_case_and_returns_snippet()
    {
        using var env = await TestEnv.CreateAsync();
        await env.Library.ImportAsync(new[] { Fixtures.Epub(env.File("a.epub")) });

        var hits = await env.Library.SearchAsync("samanta rosii");
        var hit = Assert.Single(hits);
        Assert.Equal("Soil", hit.SectionTitle);
        Assert.Contains("[[", hit.Snippet);
        Assert.NotEmpty(await env.Library.SearchAsync("MULC"));        // prefix match on the last word
        Assert.Empty(await env.Library.SearchAsync("zzzunknown"));
    }

    [Fact]
    public async Task Search_with_hostile_input_does_not_throw()
    {
        using var env = await TestEnv.CreateAsync();
        await env.Library.ImportAsync(new[] { Fixtures.Epub(env.File("a.epub")) });
        foreach (var q in new[] { "\"", "AND OR NOT", "mulch*)(", "col:val", "'; DROP TABLE Books;--", "   ", "-" })
            await env.Library.SearchAsync(q);
        Assert.Single(await env.Library.ListAsync());
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    [InlineData(".html")]
    [InlineData(".docx")]
    [InlineData(".fb2")]
    [InlineData(".rtf")]
    [InlineData(".mobi")]
    [InlineData(".pdf")]
    public async Task Every_text_format_is_indexed_and_searchable(string ext)
    {
        using var env = await TestEnv.CreateAsync();
        var path = env.File("book" + ext);
        const string plain = "Compost heaps need nitrogen and carbon in balance.";
        switch (ext)
        {
            case ".txt": File.WriteAllText(path, plain); break;
            case ".md": File.WriteAllText(path, "# Compost\n\n" + plain); break;
            case ".html": File.WriteAllText(path, $"<html><head><title>Compost</title></head><body><p>{plain}</p></body></html>"); break;
            case ".docx": Fixtures.Docx(path, plain); break;
            case ".fb2": Fixtures.Fb2(path, plain); break;
            case ".rtf": Fixtures.Rtf(path, plain); break;
            case ".mobi": Fixtures.Mobi(path, "Compost", $"<html><body><h1>Compost</h1><p>{plain}</p></body></html>"); break;
            case ".pdf": Fixtures.Pdf(path, plain); break;
        }

        var result = (await env.Library.ImportAsync(new[] { path })).Single();

        Assert.True(result.Error is null, result.Error);
        Assert.NotEmpty(await env.Library.SearchAsync("nitrogen carbon"));
        Assert.True(result.Book!.WordCount >= 8);
    }

    [Fact]
    public async Task Romanian_fb2_keeps_metadata_and_diacritics()
    {
        using var env = await TestEnv.CreateAsync();
        var r = (await env.Library.ImportAsync(new[] { Fixtures.Fb2(env.File("g.fb2"), Fixtures.Seeds) })).Single();
        Assert.Equal("Grădina mea", r.Book!.Title);
        Assert.Equal("Ana Pop", r.Book.Author);
        Assert.Equal("ro", r.Book.Language);
        Assert.NotEmpty(await env.Library.SearchAsync("sămânța"));
    }

    [Fact]
    public async Task Pdf_pages_become_sections_with_page_numbers()
    {
        using var env = await TestEnv.CreateAsync();
        var r = (await env.Library.ImportAsync(new[] { Fixtures.Pdf(env.File("p.pdf"), "Alpha page text", "Beta page text") })).Single();
        Assert.Equal(2, r.Book!.SectionCount);
        var hit = Assert.Single(await env.Library.SearchAsync("beta"));
        Assert.Equal(2, hit.Ordinal);
    }

    [Fact]
    public async Task Duplicate_files_are_rejected()
    {
        using var env = await TestEnv.CreateAsync();
        var path = Fixtures.Epub(env.File("a.epub"));
        await env.Library.ImportAsync(new[] { path });
        var second = (await env.Library.ImportAsync(new[] { path })).Single();
        Assert.True(second.Duplicate);
        Assert.Single(await env.Library.ListAsync());
    }

    [Fact]
    public async Task Unsupported_and_damaged_files_report_errors_without_leaving_files_behind()
    {
        using var env = await TestEnv.CreateAsync();
        File.WriteAllText(env.File("x.djvu"), "x");
        File.WriteAllText(env.File("broken.epub"), "this is not a zip");
        File.WriteAllBytes(env.File("broken.pdf"), new byte[] { 1, 2, 3, 4 });
        var results = await env.Library.ImportAsync(new[] { env.File("x.djvu"), env.File("broken.epub"), env.File("broken.pdf"), env.File("missing.epub") });

        Assert.All(results, r => Assert.NotNull(r.Error));
        Assert.Contains("Unsupported", results[0].Error);
        Assert.Empty(await env.Library.ListAsync());
        Assert.Empty(Directory.GetFiles(env.Paths.LibraryDir));
    }

    [Fact]
    public async Task Delete_removes_sections_from_search_and_the_stored_file()
    {
        using var env = await TestEnv.CreateAsync();
        var r = (await env.Library.ImportAsync(new[] { Fixtures.Epub(env.File("a.epub")) })).Single();
        Assert.NotEmpty(await env.Library.SearchAsync("mulch"));

        await env.Library.DeleteAsync(r.Book!.Id);

        Assert.Empty(await env.Library.SearchAsync("mulch"));
        Assert.Empty(Directory.GetFiles(env.Paths.LibraryDir));
        await using var db = env.Factory.CreateDbContext();
        Assert.Equal(0, await db.BookSections.CountAsync());
        Assert.Equal(0, await db.BookSubjects.CountAsync());
    }

    [Fact]
    public async Task Reindex_rebuilds_the_index()
    {
        using var env = await TestEnv.CreateAsync();
        var r = (await env.Library.ImportAsync(new[] { Fixtures.Epub(env.File("a.epub")) })).Single();
        await env.Library.ReindexAsync(r.Book!.Id);
        Assert.Single(await env.Library.SearchAsync("samanta"));
        Assert.Equal(2, (await env.Library.GetSectionHeadersAsync(r.Book.Id)).Count);
    }

    [Fact]
    public async Task Image_only_comic_archive_can_be_read_but_has_no_text()
    {
        using var env = await TestEnv.CreateAsync();
        var path = env.File("c.cbz");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            foreach (var n in new[] { "p10.png", "p2.png" }) zip.CreateEntry(n).Open().Dispose();
        var r = (await env.Library.ImportAsync(new[] { path })).Single();
        Assert.Equal(IndexStatus.NoText, r.Book!.IndexStatus);
        var sections = await env.Library.GetSectionHeadersAsync(r.Book.Id);
        Assert.Equal(new[] { "p2.png", "p10.png" }, sections.Select(s => s.ContentPath));   // natural page order
    }

    [Fact]
    public async Task Reader_preparer_creates_a_viewable_file_for_each_kind()
    {
        using var env = await TestEnv.CreateAsync();
        var prep = new ReaderPreparer(env.Paths, env.Library);
        var epub = (await env.Library.ImportAsync(new[] { Fixtures.Epub(env.File("a.epub")) })).Single().Book!;
        var txt = (await env.Library.ImportAsync(new[] { Write(env.File("n.txt"), "Hello garden <b>world</b>") })).Single().Book!;

        var t1 = await prep.PrepareAsync(epub, 2);
        Assert.True(File.Exists(t1.Uri.LocalPath));
        Assert.EndsWith("ch2.xhtml", t1.Uri.LocalPath);
        var t2 = await prep.PrepareAsync(txt, 1);
        Assert.Contains("&lt;b&gt;", await File.ReadAllTextAsync(t2.Uri.LocalPath));   // text is escaped, never executed
    }

    [Fact]
    public void Palm_decompression_handles_literals_spaces_and_back_references()
    {
        var ms = new MemoryStream();
        MobiExtractor.PalmDecompress(new byte[] { 0x61, 0xC2 }, ms);
        Assert.Equal("a B", System.Text.Encoding.ASCII.GetString(ms.ToArray()));
        ms = new MemoryStream();
        MobiExtractor.PalmDecompress(new byte[] { 0x61, 0x62, 0x63, 0x64, 0x80, 0x21 }, ms);
        Assert.Equal("abcdabcd", System.Text.Encoding.ASCII.GetString(ms.ToArray()));
    }

    [Fact]
    public void Subjects_skip_stop_words_and_rank_topic_words()
    {
        var book = new ExtractedBook();
        for (var i = 0; i < 8; i++)
            book.Sections.Add(new ExtractedSection(i == 0 ? "Composting basics" : "Notes " + i,
                "The heap needs moisture. " + (i % 2 == 0 ? "Compost turns waste into soil. Compost needs air. " : "") + "Această grădină este bună."));
        var terms = SubjectExtractor.Extract(book).Select(t => t.Term).ToList();
        Assert.Contains("compost", terms);
        Assert.DoesNotContain("this", terms);
        Assert.DoesNotContain("această", terms);
    }

    [Theory]
    [InlineData("tomato", "\"tomato\"*")]
    [InlineData("tomato ", "\"tomato\"")]
    [InlineData("soil moisture", "\"soil\" \"moisture\"*")]
    [InlineData("\"soil moisture\"", "\"soil moisture\"")]
    [InlineData("  ", null)]
    [InlineData("!!!", null)]
    public void Match_expression_is_safe(string input, string? expected) =>
        Assert.Equal(expected, LibraryService.BuildMatch(input));

    private static string Write(string path, string text) { File.WriteAllText(path, text); return path; }
}
