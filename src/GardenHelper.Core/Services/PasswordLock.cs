using System.Security.Cryptography;

namespace GardenHelper.Core.Services;

/// <summary>
/// A password for opening the app. Only a salted hash is stored (never the password). This keeps people at the keyboard out of the app;
/// it does not encrypt the files in the data folder.
/// </summary>
public static class PasswordLock
{
    public const string EnabledKey = "lock.hash";
    private const int Iterations = 150_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"v1:{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split(':');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations) || iterations is < 1000 or > 5_000_000) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]); var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    /// <summary>Why a new password is not acceptable, or null when it is fine.</summary>
    public static string? Problem(string password) => password.Length < 4 ? "short" : password.Length > 200 ? "long" : null;
}
