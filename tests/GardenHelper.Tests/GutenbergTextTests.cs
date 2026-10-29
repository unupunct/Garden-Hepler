using GardenHelper.Core.Library;
using GardenHelper.Core.Library.Extractors;
using Xunit;

namespace GardenHelper.Tests;

public class GutenbergTextTests
{
    private static string Sample(string body) => string.Join("\n", new[]
    {
        "The Project Gutenberg eBook of Dwarf fruit trees", "",
        "This eBook is for the use of anyone anywhere in the United States.", "",
        "Title: Dwarf fruit trees", "", "Author: F. A. Waugh", "", "Release date: November 15, 2011 [eBook #38024]", "",
        "*** START OF THE PROJECT GUTENBERG EBOOK DWARF FRUIT TREES ***", "", body, "",
        "*** END OF THE PROJECT GUTENBERG EBOOK DWARF FRUIT TREES ***", "", "Updated editions will replace the previous one. Trademark licence boilerplate here.",
    });

    private static string Body => "Dwarf apples on paradise stock bear early and are easy to prune. " + string.Concat(Enumerable.Repeat("Pruning keeps the tree small and the fruit within reach. ", 12));

    [Fact]
    public void Header_gives_title_and_author_and_licence_text_is_dropped()
    {
        var book = new ExtractedBook();
        var text = GutenbergText.Unwrap(Sample(Body), book);
        Assert.Equal("Dwarf fruit trees", book.Title);
        Assert.Equal("F. A. Waugh", book.Author);
        Assert.StartsWith("Dwarf apples", text);
        Assert.DoesNotContain("Trademark", text);
        Assert.DoesNotContain("START OF THE PROJECT", text);
    }

    [Fact]
    public void Other_texts_are_left_alone()
    {
        var book = new ExtractedBook();
        const string plain = "My garden notes\n\nTitle: not a gutenberg file, just a line that starts with Title.";
        Assert.Equal(plain, GutenbergText.Unwrap(plain, book));
        Assert.Null(book.Title);
    }

    [Fact]
    public void A_broken_file_without_an_end_marker_still_imports_its_body()
    {
        var book = new ExtractedBook();
        var broken = Sample(Body).Replace("*** END OF THE PROJECT GUTENBERG EBOOK DWARF FRUIT TREES ***", "");
        Assert.Contains("Pruning keeps", GutenbergText.Unwrap(broken, book));
    }

    [Fact]
    public async Task Imported_file_is_found_by_content_not_by_licence_words()
    {
        using var env = await TestEnv.CreateAsync();
        var file = env.File("waugh.txt");
        await File.WriteAllTextAsync(file, Sample(Body));
        var result = (await env.Library.ImportAsync(new[] { file })).Single();
        Assert.Null(result.Error);
        Assert.Equal("Dwarf fruit trees", result.Book!.Title);
        Assert.Equal("F. A. Waugh", result.Book.Author);
        Assert.NotEmpty(await env.Library.SearchAsync("paradise stock"));
        Assert.Empty(await env.Library.SearchAsync("trademark"));
    }
}
