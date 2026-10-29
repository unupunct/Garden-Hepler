using GardenHelper.Core.Data;
using GardenHelper.Core.Services;
using Xunit;

namespace GardenHelper.Tests;

public class SeedPhotoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gh-seed-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly SeedPhotoStore _store;
    public SeedPhotoTests() => _store = new SeedPhotoStore(new AppPaths(_root));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { /* temp */ } }

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
    private static readonly byte[] Jpg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10 };

    [Fact]
    public void Formats_are_recognised_by_content()
    {
        Assert.Equal(".png", SeedPhotoStore.DetectExtension(Png));
        Assert.Equal(".jpg", SeedPhotoStore.DetectExtension(Jpg));
        Assert.Null(SeedPhotoStore.DetectExtension("hello, not a picture"u8));
        Assert.Null(SeedPhotoStore.DetectExtension(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public async Task Pictures_are_copied_and_can_be_removed()
    {
        var src = Path.Combine(_root, "packet.txt");                       // wrong name on purpose: content decides
        await File.WriteAllBytesAsync(src, Png);
        var name = await _store.AddFileAsync(src);
        Assert.NotNull(name);
        Assert.EndsWith(".png", name);
        Assert.True(File.Exists(_store.PathOf(name)));
        File.Delete(src);                                                   // the original may go away
        Assert.True(File.Exists(_store.PathOf(name)));
        _store.Delete(name);
        Assert.False(File.Exists(_store.PathOf(name)));
    }

    [Fact]
    public async Task Non_pictures_and_missing_files_are_refused()
    {
        var text = Path.Combine(_root, "fake.jpg");
        await File.WriteAllTextAsync(text, "just text pretending to be a jpg");
        Assert.Null(await _store.AddFileAsync(text));
        Assert.Null(await _store.AddFileAsync(Path.Combine(_root, "missing.png")));
        Assert.Null(await _store.AddBytesAsync(Array.Empty<byte>()));
    }

    [Theory]
    [InlineData("..\\..\\evil.png")] [InlineData("sub/dir.png")] [InlineData("C:\\Windows\\win.ini")] [InlineData("")] [InlineData(null)]
    public void Only_plain_file_names_are_accepted(string? name) => Assert.Null(_store.PathOf(name));
}
