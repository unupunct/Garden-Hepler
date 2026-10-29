using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace GardenHelper.App.Infrastructure;

/// <summary>Draws and reads QR codes inside the app, with no online service.</summary>
public static class QrCodes
{
    public static BitmapSource? Generate(string text, int size = 240)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 1200) return null;
        try
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions { Width = size, Height = size, Margin = 2, ErrorCorrection = ErrorCorrectionLevel.M, CharacterSet = "UTF-8" },
            };
            var pd = writer.Write(text);
            var bmp = BitmapSource.Create(pd.Width, pd.Height, 96, 96, PixelFormats.Bgra32, null, pd.Pixels, pd.Width * 4);
            bmp.Freeze();
            return bmp;
        }
        catch (WriterException) { return null; }
    }

    /// <summary>The text of the first QR code found in a picture (a photo or a screenshot), or null.</summary>
    public static string? Decode(BitmapSource image)
    {
        var conv = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        if (w < 20 || h < 20) return null;
        var pixels = new byte[w * 4 * h];
        conv.CopyPixels(pixels, w * 4, 0);
        var source = new RGBLuminanceSource(pixels, w, h, RGBLuminanceSource.BitmapFormat.BGRA32);
        var reader = new BarcodeReaderGeneric { AutoRotate = true, Options = new DecodingOptions { TryHarder = true, PossibleFormats = new[] { BarcodeFormat.QR_CODE } } };
        return reader.Decode(source)?.Text;
    }
}
