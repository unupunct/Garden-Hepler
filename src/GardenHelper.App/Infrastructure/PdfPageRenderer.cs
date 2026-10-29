using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace GardenHelper.App.Infrastructure;

/// <summary>Draws PDF pages with the PDF engine that is part of Windows. No browser, viewer app or extra download is needed, and scanned (image-only) PDFs work too.</summary>
public sealed class PdfPageRenderer
{
    private string? _path;
    private PdfDocument? _doc;

    public int PageCount => (int)(_doc?.PageCount ?? 0);

    /// <summary>Loads the file if it is not the one already open. Throws <see cref="PdfProblem"/> with a plain message when the file cannot be shown.</summary>
    public async Task OpenAsync(string path)
    {
        if (_doc is not null && _path == path) return;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            _doc = await PdfDocument.LoadFromFileAsync(file);
            _path = path;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _doc = null; _path = null;
            throw new PdfProblem(ex.HResult == unchecked((int)0x8007052B) ? "locked" : "damaged", ex);
        }
    }

    /// <summary>Draws one page (1-based; a number past the end shows the last page) at the given width in pixels.</summary>
    public async Task<ImageSource> RenderAsync(string path, int page, int widthPx, bool invert)
    {
        await OpenAsync(path);
        var count = (int)_doc!.PageCount;
        if (count == 0) throw new PdfProblem("damaged", null);
        try
        {
            using var p = _doc.GetPage((uint)Math.Clamp(page - 1, 0, count - 1));
            var width = (uint)Math.Clamp(widthPx, 300, 4000);
            var height = (uint)Math.Max(1, Math.Round(width * p.Size.Height / Math.Max(1, p.Size.Width)));
            using var ms = new InMemoryRandomAccessStream();
            await p.RenderToStreamAsync(ms, new PdfPageRenderOptions { DestinationWidth = width, DestinationHeight = height });
            ms.Seek(0);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms.AsStreamForRead();
            bmp.EndInit();
            bmp.Freeze();
            return invert ? Invert(bmp) : bmp;
        }
        catch (Exception ex) when (ex is COMException or IOException or InvalidOperationException)
        {
            throw new PdfProblem("damaged", ex);
        }
    }

    /// <summary>White-on-dark version of a page for night reading.</summary>
    private static ImageSource Invert(BitmapSource src)
    {
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var stride = conv.PixelWidth * 4;
        var px = new byte[stride * conv.PixelHeight];
        conv.CopyPixels(px, stride, 0);
        for (var i = 0; i < px.Length; i += 4) { px[i] = (byte)(255 - px[i]); px[i + 1] = (byte)(255 - px[i + 1]); px[i + 2] = (byte)(255 - px[i + 2]); }
        var result = BitmapSource.Create(conv.PixelWidth, conv.PixelHeight, 96, 96, PixelFormats.Bgra32, null, px, stride);
        result.Freeze();
        return result;
    }
}

/// <summary>A PDF that cannot be shown. <see cref="Reason"/> is "locked" (password protected) or "damaged".</summary>
public sealed class PdfProblem : Exception
{
    public string Reason { get; }
    public PdfProblem(string reason, Exception? inner) : base(reason, inner) => Reason = reason;
}
