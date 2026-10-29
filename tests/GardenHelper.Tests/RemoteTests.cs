using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Greenhouse;
using GardenHelper.Core.Planning;
using GardenHelper.Core.Services;
using Xunit;

namespace GardenHelper.Tests;

public class RemoteTests
{
    private static readonly DateOnly Today = new(2026, 6, 10);

    [Fact]
    public async Task Summary_counts_tasks_animals_and_greenhouse()
    {
        using var env = await TestEnv.CreateAsync();
        var repo = new Repository(env.Factory);
        var gh = new GreenhouseService(repo);
        var svc = new RemoteService(repo, gh);
        var garden = await repo.SaveAsync(new Garden { Name = "G", IsActive = true });
        await repo.SaveAsync(new GardenTask { Title = "late", DueDate = Today.AddDays(-2) });
        await repo.SaveAsync(new GardenTask { Title = "today", DueDate = Today });
        await repo.SaveAsync(new GardenTask { Title = "soon", DueDate = Today.AddDays(3) });
        await repo.SaveAsync(new GardenTask { Title = "done", DueDate = Today, Done = true });
        var hens = await repo.SaveAsync(new AnimalGroup { GardenId = garden.Id, Name = "Hens", Count = 30 });
        await repo.SaveAsync(new AnimalLog { GroupId = hens.Id, Kind = "production", Date = Today, Quantity = 24 });
        await repo.SaveAsync(new AnimalLog { GroupId = hens.Id, Kind = "treatment", Date = Today, Text = "wormer", WithdrawalUntil = Today.AddDays(4) });
        var house = await repo.SaveAsync(new PropertyFeature { GardenId = garden.Id, Name = "Tunnel", Kind = "greenhouse" });
        await gh.IngestAsync(house.Id, new Dictionary<string, double> { ["air_temp"] = 31, ["soil_moisture"] = 50 }, DateTime.UtcNow);

        var s = await svc.SummaryAsync(Today, DateTime.UtcNow);
        Assert.Equal(new TaskCounts(1, 1, 1), s.Tasks);
        Assert.Equal(30, s.AnimalsTotal);
        Assert.Equal(24, s.EggsToday);
        Assert.True(s.WithdrawalActive);
        Assert.Equal(Today.AddDays(4).ToString("yyyy-MM-dd"), s.Animals.Single().WithdrawalUntil);
        var h = s.Greenhouses.Single();
        Assert.Equal(31, h.AirTemp); Assert.Equal(50, h.SoilMoisture); Assert.True(h.Switches["vent"]);
        Assert.Contains(s.Upcoming, u => u.Kind == "withdrawal");
    }

    [Fact]
    public async Task Tasks_can_be_completed_and_production_validated()
    {
        using var env = await TestEnv.CreateAsync();
        var repo = new Repository(env.Factory);
        var svc = new RemoteService(repo, new GreenhouseService(repo));
        var t = await repo.SaveAsync(new GardenTask { Title = "x", DueDate = Today });
        Assert.True(await svc.CompleteTaskAsync(t.Id));
        Assert.True(await svc.CompleteTaskAsync(t.Id));                 // twice is harmless
        Assert.False(await svc.CompleteTaskAsync(9999));
        Assert.Empty(await svc.OpenTasksAsync(Today));
        var garden = await repo.SaveAsync(new Garden { Name = "G", IsActive = true });
        var g = await repo.SaveAsync(new AnimalGroup { GardenId = garden.Id, Name = "Hens", Count = 5 });
        Assert.True(await svc.AddProductionAsync(g.Id, 4, Today));
        Assert.False(await svc.AddProductionAsync(g.Id, -3, Today));
        Assert.False(await svc.AddProductionAsync(g.Id, double.NaN, Today));
        Assert.False(await svc.AddProductionAsync(12345, 2, Today));
        Assert.Equal(4, (await svc.SummaryAsync(Today, DateTime.UtcNow)).EggsToday);
    }
}
