using System.Text.RegularExpressions;
using Xunit;

namespace GardenHelper.Tests;

public class XamlSanityTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "GardenHelper.App"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    /// <summary>WPF has no percentage lengths: Margin="22%" compiles but crashes the page when it is drawn.</summary>
    [Fact]
    public void No_xaml_uses_percentage_sizes()
    {
        var bad = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "GardenHelper.App"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
            .Where(x => Regex.IsMatch(x.line, @"\b(Margin|Padding|Width|Height|MinWidth|MinHeight|MaxWidth|MaxHeight|FontSize|StrokeThickness|BorderThickness)=""[0-9.,\s]*%"""))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}")
            .ToList();
        Assert.Empty(bad);
    }
}
