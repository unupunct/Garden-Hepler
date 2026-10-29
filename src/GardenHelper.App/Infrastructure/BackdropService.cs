using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GardenHelper.Core.Data;
using GardenHelper.Core.Site;

namespace GardenHelper.App.Infrastructure;

/// <summary>
/// Builds the satellite picture behind the garden designer: the map tiles covering the property, stitched into one image
/// whose position and ground scale are known, so beds are drawn on it at true size.
/// </summary>
public sealed class BackdropService
{
    private readonly SatelliteTileService _tiles;
    private readonly AppPaths _paths;

    public BackdropService(SatelliteTileService tiles, AppPaths paths) { _tiles = tiles; _paths = paths; }

    /// <summary>Downloads and stitches the tiles. Call from the UI thread (the stitching uses WPF imaging).</summary>
    public async Task<(string File, TileGrid Grid)> BuildAsync(int gardenId, IReadOnlyList<GeoPoint> area, CancellationToken ct = default)
    {
        var grid = TileGrid.ForBounds(area);
        var bytes = new byte[grid.TilesX * grid.TilesY][];
        using var gate = new SemaphoreSlim(6);
        await Task.WhenAll(Enumerable.Range(0, bytes.Length).Select(async i =>
        {
            await gate.WaitAsync(ct);
            try { bytes[i] = await _tiles.FetchTileAsync(grid.Zoom, grid.MinX + i % grid.TilesX, grid.MinY + i / grid.TilesX, ct); }
            finally { gate.Release(); }
        }));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            for (var i = 0; i < bytes.Length; i++)
            {
                var img = BitmapFrame.Create(new MemoryStream(bytes[i]), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                dc.DrawImage(img, new Rect(i % grid.TilesX * 256, i / grid.TilesX * 256, 256, 256));
            }
        var target = new RenderTargetBitmap(grid.PixelWidth, grid.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
        encoder.Frames.Add(BitmapFrame.Create(target));
        var file = $"backdrop-{gardenId}.jpg";
        using (var fs = File.Create(Path.Combine(_paths.SiteDir, file))) encoder.Save(fs);
        return (file, grid);
    }

    public ImageSource? Load(string? file)
    {
        if (file is null) return null;
        var path = Path.Combine(_paths.SiteDir, file);
        if (!File.Exists(path)) return null;
        var bmp = new BitmapImage();
        bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache; bmp.UriSource = new Uri(path); bmp.EndInit(); bmp.Freeze();
        return bmp;
    }
}
