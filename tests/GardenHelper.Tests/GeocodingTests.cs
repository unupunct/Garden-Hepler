using GardenHelper.Core.Site;
using Xunit;

namespace GardenHelper.Tests;

public class GeocodingTests
{
    [Fact]
    public void Results_are_read_and_bad_json_gives_nothing()
    {
        var hits = Geocoding.Parse("{\"results\":[{\"name\":\"Moieciu de Sus\",\"latitude\":45.48,\"longitude\":25.31,\"country\":\"Romania\",\"admin1\":\"Brasov\"},{\"name\":\"no coordinates\"}]}");
        var h = Assert.Single(hits);
        Assert.Equal("Moieciu de Sus, Brasov, Romania", h.ToString()); Assert.Equal(45.48, h.Lat);
        Assert.Empty(Geocoding.Parse("not json")); Assert.Empty(Geocoding.Parse("{}"));
    }
}
