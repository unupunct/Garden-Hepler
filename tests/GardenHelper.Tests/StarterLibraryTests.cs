using GardenHelper.Core.Library;
using GardenHelper.Core.Services;
using Xunit;

namespace GardenHelper.Tests;

public class StarterLibraryTests
{
    private static string MakeFolder(TestEnv env)
    {
        var dir = Path.Combine(env.Root, "starter");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "mulch.txt"), "Mulching guide\n\nMulch keeps soil moist and suppresses weeds. Spread compost mulch around the beds in spring.");
        File.WriteAllText(Path.Combine(dir, "sub", "seeds.txt"), "Seed starting\n\nStart tomato seeds indoors six weeks before the last frost.");
        File.WriteAllText(Path.Combine(dir, "readme.md.bak"), "not a book format");
        File.WriteAllBytes(Path.Combine(dir, "photo.jpg"), new byte[] { 0xFF, 0xD8, 0xFF });
        return dir;
    }

    [Fact]
    public async Task Only_readable_books_are_found_including_subfolders()
    {
        using var env = await TestEnv.CreateAsync();
        var found = StarterLibrary.BooksIn(MakeFolder(env)).Select(Path.GetFileName).ToList();
        Assert.Equal(new[] { "mulch.txt", "seeds.txt" }, found);
        Assert.Empty(StarterLibrary.BooksIn(Path.Combine(env.Root, "nope")));
    }

    [Fact]
    public async Task First_run_adds_the_books_once_and_search_finds_them()
    {
        using var env = await TestEnv.CreateAsync();
        var starter = new StarterLibrary(env.Library, new SettingsService(env.Factory));
        var dir = MakeFolder(env);
        Assert.Equal(2, await starter.ImportOnFirstRunAsync(dir));
        Assert.Equal(0, await starter.ImportOnFirstRunAsync(dir));          // never twice, even if the user deleted a book
        Assert.Equal(0, await starter.ImportAsync(dir));                    // manual re-add skips books already present
        Assert.NotEmpty(await env.Library.SearchAsync("tomato frost"));
    }

    [Fact]
    public async Task A_missing_folder_is_not_an_error()
    {
        using var env = await TestEnv.CreateAsync();
        var starter = new StarterLibrary(env.Library, new SettingsService(env.Factory));
        Assert.Equal(0, await starter.ImportOnFirstRunAsync(Path.Combine(env.Root, "absent")));
    }
}
