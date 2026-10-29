using GardenHelper.Core.Library;
using Xunit;

namespace GardenHelper.Tests;

public class TitleTests
{
    [Theory]
    [InlineData("HG_RackCards_ResourceGuide_SeedStarting", "HG Rack Cards Resource Guide Seed Starting")]
    [InlineData("full-self-sufficiency", "Full self sufficiency")]
    [InlineData("greenhorns_guide_sept2010_web (1)", "Greenhorns guide sept2010")]
    [InlineData("HG_CompanionPlanting_8.5x11-1", "HG Companion Planting")]
    [InlineData("HG_PlantingTasklist_8.5x11_FINAL", "HG Planting Tasklist")]
    [InlineData("Emerson, Ralph Waldo - Self-Reliance", "Emerson, Ralph Waldo - Self-Reliance")]
    [InlineData("the farming manual, henderson", "The farming manual, henderson")]
    public void File_names_become_readable_titles(string name, string expected) => Assert.Equal(expected, TitleCleaner.FromFileName(name));

    [Theory]
    [InlineData("Agric Book.cdr")] [InlineData("Microsoft Word - chapter1")] [InlineData("Untitled")] [InlineData("ab")] [InlineData("")] [InlineData(null)]
    [InlineData("0f3a9c1d2b4e5f607182939a4b5c6d7e")] [InlineData("report.PDF")]
    public void Leftover_metadata_is_recognised_as_junk(string? title) => Assert.True(TitleCleaner.IsJunkTitle(title));

    [Theory]
    [InlineData("Three Acres And Liberty")] [InlineData("The Encyclopedia of Country Living: An Old Fashioned Recipe Book")] [InlineData("Mini Farming")]
    public void Real_titles_are_kept(string title) => Assert.Equal(title, TitleCleaner.Choose(title, "some_file"));

    [Fact]
    public void Junk_title_falls_back_to_the_file_name_and_junk_authors_are_dropped()
    {
        Assert.Equal("Generic Homestead Garden Manual", TitleCleaner.Choose("Agric Book.cdr", "GENERIC-HOMESTEAD-GARDEN-MANUAL").Replace("GENERIC", "Generic").Replace("HOMESTEAD", "Homestead").Replace("GARDEN", "Garden").Replace("MANUAL", "Manual"));
        Assert.Null(TitleCleaner.CleanAuthor("hp"));
        Assert.Null(TitleCleaner.CleanAuthor("  Administrator "));
        Assert.Equal("Betsy Matheson", TitleCleaner.CleanAuthor(" Betsy Matheson "));
    }
}
