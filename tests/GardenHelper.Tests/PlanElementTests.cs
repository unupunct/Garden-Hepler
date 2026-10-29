using GardenHelper.Core.Design;
using GardenHelper.Core.Domain;
using Xunit;

namespace GardenHelper.Tests;

public class PlanElementTests
{
    [Fact]
    public void Catalogue_is_consistent()
    {
        Assert.Equal(PlanElements.All.Length, PlanElements.All.Select(e => e.Key).Distinct().Count());
        Assert.All(PlanElements.All, e =>
        {
            Assert.Contains(e.Group, PlanElements.Groups);
            Assert.True(e.Width >= 10 && e.Height >= 10, e.Key);
            Assert.Matches("^#[0-9A-F]{6}$", e.Color);
            Assert.False(string.IsNullOrEmpty(e.Glyph));
        });
        Assert.All(PlanElements.Groups.Where(g => g != "devices"), g => Assert.NotEmpty(PlanElements.InGroup(g)));
    }

    [Fact]
    public void Structures_match_the_property_place_kinds()
    {
        var structures = PlanElements.All.Where(e => e.IsStructure).Select(e => e.Key).OrderBy(k => k).ToList();
        Assert.Equal(FeatureCatalog.Structures.OrderBy(k => k).ToList(), structures);
        Assert.All(PlanElements.Objects, e => Assert.False(FeatureCatalog.IsStructure(e.Key)));
    }

    [Theory]
    [InlineData("fence")] [InlineData("gate")] [InlineData("bin")] [InlineData("garage")] [InlineData("compost")] [InlineData("coop")] [InlineData("hive")]
    public void The_elements_the_user_asked_for_exist(string key) => Assert.NotNull(PlanElements.Get(key));

    [Fact]
    public void Fences_are_long_and_thin_and_unknown_kinds_get_a_default()
    {
        var (w, h) = PlanElements.SizeOf("fence");
        Assert.True(w / h > 20);
        Assert.Equal((200, 200), PlanElements.SizeOf("nothing"));
    }
}
