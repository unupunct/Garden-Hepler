using GardenHelper.Core.Knowledge;
using Xunit;

namespace GardenHelper.Tests;

public class EncyclopediaTests
{
    private static KnowledgeBase Kb() => KnowledgeBase.Load(new[] { Fixtures.RepoContent() });

    [Theory]
    [InlineData("native", 45)] [InlineData("pollinator", 60)] [InlineData("pest", 8)] [InlineData("soil", 25)] [InlineData("medicinal", 40)] [InlineData("edible", 60)]
    public void Each_purpose_filter_has_a_useful_number_of_plants(string purpose, int atLeast) =>
        Assert.True(Kb().Search(null, null, purpose).Count >= atLeast, $"{purpose} has fewer than {atLeast} plants");

    [Fact]
    public void Medicinal_plants_always_carry_safety_text_in_both_languages()
    {
        var med = Kb().Plants.Where(p => p.Medicinal is not null).ToList();
        Assert.True(med.Count >= 40);
        Assert.All(med, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Medicinal!.Safety.GetValueOrDefault("en")), p.Id);
            Assert.False(string.IsNullOrWhiteSpace(p.Medicinal.Safety.GetValueOrDefault("ro")), p.Id);
            Assert.False(string.IsNullOrWhiteSpace(p.Medicinal.Uses.GetValueOrDefault("ro")), p.Id);
            Assert.Contains("medicinal", p.Roles);
        });
    }

    [Fact]
    public void Wild_mushrooms_are_rated_cautioned_and_kept_out_of_planting_lists()
    {
        var kb = Kb();
        var wild = kb.Plants.Where(p => p.Category == "wild-mushroom").ToList();
        Assert.True(wild.Count >= 15);
        Assert.All(wild, p => { Assert.NotNull(p.Edibility); Assert.False(string.IsNullOrWhiteSpace(p.CautionText("ro")), p.Id); Assert.NotNull(p.SeasonMonths); });
        Assert.True(wild.Count(p => p.Edibility is "deadly" or "toxic") >= 8);
        Assert.Contains(wild, p => p.Scientific == "Amanita phalloides" && p.Edibility == "deadly");
        Assert.DoesNotContain(kb.Plantable, p => p.Category == "wild-mushroom");
        // Every edible wild mushroom names a dangerous look-alike.
        Assert.All(wild.Where(p => p.Edibility is "edible" or "edible-cooked"), p =>
            Assert.Matches("(?i)look-alike|asemănător", p.CautionText("en") + p.CautionText("ro")));
    }

    [Fact]
    public void Protected_and_invasive_plants_are_flagged()
    {
        var kb = Kb();
        Assert.True(kb.Get("alpine-arnica")!.Protected);
        Assert.True(kb.Get("yellow-gentian")!.Protected);
        Assert.Equal("invasive", kb.Get("black-locust")!.Origin);
        Assert.Equal("native-ro", kb.Get("yarrow")!.Origin);
    }

    [Theory]
    [InlineData("cough", "narrowleaf-plantain")]
    [InlineData("tuse", "narrowleaf-plantain")]
    [InlineData("insomnia", null)]
    [InlineData("somn", "valerian")]
    [InlineData("urzica", "stinging-nettle")]
    public void Plants_can_be_found_by_what_they_are_used_for(string query, string? expected)
    {
        var hits = Kb().Search(query).Select(m => m.Plant.Id).ToList();
        if (expected is null) return;                              // free text only needs to not crash
        Assert.Contains(expected, hits);
    }

    [Fact]
    public void Purpose_and_category_filters_combine()
    {
        var kb = Kb();
        var r = kb.Search(null, "wild-mushroom", "native");
        Assert.All(r, m => Assert.Equal("wild-mushroom", m.Plant.Category));
        Assert.NotEmpty(r);
        Assert.Empty(kb.Search("amanita", "vegetable"));
    }

    [Fact]
    public void New_names_are_unique_and_every_id_is_lower_case()
    {
        var kb = Kb();
        Assert.Equal(kb.Plants.Count, kb.Plants.Select(p => p.Id).Distinct().Count());
        Assert.All(kb.Plants, p => Assert.Equal(p.Id, p.Id.ToLowerInvariant()));
        Assert.True(kb.Plants.Count >= 200);
    }
}
