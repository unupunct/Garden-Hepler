using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GardenHelper.Core.Cameras;

namespace GardenHelper.App.Infrastructure;

/// <summary>Camera passwords are protected with the Windows account (DPAPI) so they are never stored as plain text.</summary>
public static class CameraSecrets
{
    public static string? Protect(string? password) => string.IsNullOrEmpty(password) ? null
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser));

    public static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), null, DataProtectionScope.CurrentUser)); }
        catch (CryptographicException) { return null; }   // saved by another Windows account: ask for the password again
    }
}

/// <summary>Grabs one picture from a camera and turns it into something WPF can show.</summary>
public sealed class CameraFeed
{
    private readonly CameraClient _client;
    public CameraFeed(CameraClient client) => _client = client;

    public async Task<ImageSource> GrabAsync(Camera camera, string? passwordOverride = null, CancellationToken ct = default)
    {
        var bytes = await _client.GrabFrameAsync(camera, passwordOverride ?? CameraSecrets.Unprotect(camera.SecretProtected), ct);
        var bmp = new BitmapImage();
        bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = new MemoryStream(bytes); bmp.EndInit(); bmp.Freeze();
        return bmp;
    }
}
