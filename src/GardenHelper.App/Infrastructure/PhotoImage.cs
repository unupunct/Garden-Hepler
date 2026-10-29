using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GardenHelper.App.Infrastructure;

/// <summary>Loads a picture file for display without keeping the file locked, at a limited width so many thumbnails stay cheap.</summary>
public static class PhotoImage
{
    public static ImageSource? Load(string? path, int decodeWidth)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = decodeWidth;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UriFormatException) { return null; }
    }
}
