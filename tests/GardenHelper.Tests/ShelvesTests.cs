using GardenHelper.Core.Library;
using Xunit;

namespace GardenHelper.Tests;

public class ShelvesTests
{
    [Fact]
    public void Spines_are_stable_and_within_their_ranges()
    {
        var a = Shelves.Spine("The Encyclopedia of Country Living", 960_000);
        Assert.Equal(a, Shelves.Spine("The Encyclopedia of Country Living", 960_000));
        Assert.Equal(Shelves.Spine("Ăsta", 100).ColorIndex, Shelves.Spine("Asta", 100).ColorIndex);          // accents do not change the look
        foreach (var (t, w) in new[] { ("x", 0), ("Mini Farming", 61_000), ("Big", 3_000_000), ("Leaflet", 90) })
        {
            var s = Shelves.Spine(t, w);
            Assert.InRange(s.Width, 30, 58); Assert.InRange(s.Height, 150, 210); Assert.InRange(s.ColorIndex, 0, Shelves.PaletteSize - 1);
        }
        Assert.True(Shelves.Spine("Big", 3_000_000).Width > Shelves.Spine("Leaflet", 90).Width);
    }

    [Fact]
    public void Shelves_fill_left_to_right_and_wrap()
    {
        var rows = Shelves.Rows(new double[] { 40, 40, 40, 40, 40 }, maxRowWidth: 130, gap: 3);        // 40+3+40+3+40 = 126 fits three
        Assert.Equal(new[] { 3, 2 }, rows.Select(r => r.Count));
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, rows.SelectMany(r => r));
        Assert.Single(Shelves.Rows(new double[] { 500 }, 100, 3));                                      // a book wider than the shelf still gets one
        Assert.Empty(Shelves.Rows(Array.Empty<double>(), 100, 3));
    }

    [Theory]
    [InlineData("The Farming Manual", "F")] [InlineData("A Complete Guide", "C")] [InlineData("An Orchard", "O")] [InlineData("Ștefan cel Mare", "S")]
    [InlineData("Ăsta e un test", "A")] [InlineData("1001 Gardening Tips", "#")] [InlineData("\"Quoted\" title", "Q")] [InlineData("", "#")] [InlineData("The", "T")]
    public void Titles_are_filed_under_their_letter(string title, string letter) => Assert.Equal(letter, Shelves.LetterOf(title));

    [Fact]
    public void Index_groups_titles_in_order_with_symbols_last()
    {
        var groups = Shelves.Group(new[] { "Zero to Hero", "The Apple Book", "Beekeeping", "1001 Tips", "Ananas", "Ăgrișe" }, t => t);
        Assert.Equal(new[] { "A", "B", "Z", "#" }, groups.Select(g => g.Letter));
        Assert.Equal(new[] { "Ananas", "Ăgrișe", "The Apple Book" }.OrderBy(t => Shelves.SortKey(t), StringComparer.Ordinal), groups[0].Items);
        Assert.Equal("1001 Tips", groups[^1].Items.Single());
    }
}
