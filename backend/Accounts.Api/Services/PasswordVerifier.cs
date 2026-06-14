using Accounts.Api.Entities;
using System.Security.Cryptography;

namespace Accounts.Api.Services;

public interface IPasswordVerifier
{
    bool Verify(string password, UserAccount? user);
}

public sealed class Pbkdf2PasswordVerifier : IPasswordVerifier
{
    public const int PasswordIterations = 210_000;
    private const int MinimumSaltBytes = 16;
    private const int MinimumStoredHashBytes = 16;
    private const string DummySaltBase64 = "YWNjb3VudHMtYXV0aC1kdW1teS1zYWx0LXYx";
    private const string DummyHashBase64 = "eZAjjvGhK6uVghrFop1FI6NG6MSbn+ORO/kUpioLQeo=";

    private static readonly byte[] DummySalt = Convert.FromBase64String(DummySaltBase64);
    private static readonly byte[] DummyHash = Convert.FromBase64String(DummyHashBase64);

    public bool Verify(string password, UserAccount? user)
    {
        if (!TryDecodeStoredPassword(user, out var salt, out var storedHash))
        {
            return VerifyHash(password, DummySalt, DummyHash);
        }

        return VerifyHash(password, salt, storedHash);
    }

    private static bool VerifyHash(string password, byte[] salt, byte[] storedHash)
    {
        var candidateHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            PasswordIterations,
            HashAlgorithmName.SHA256,
            storedHash.Length);

        return CryptographicOperations.FixedTimeEquals(candidateHash, storedHash);
    }

    private static bool TryDecodeStoredPassword(UserAccount? user, out byte[] salt, out byte[] storedHash)
    {
        salt = [];
        storedHash = [];

        if (user is null
            || user.Tenant is null
            || user.PasswordAlgorithm != AuthService.PasswordAlgorithm)
            return false;

        try
        {
            salt = Convert.FromBase64String(user.PasswordSalt);
            storedHash = Convert.FromBase64String(user.PasswordHash);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length >= MinimumSaltBytes && storedHash.Length >= MinimumStoredHashBytes;
    }
}

public static class PasswordHasher
{
    public static (string Hash, string Salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Pbkdf2PasswordVerifier.PasswordIterations,
            HashAlgorithmName.SHA256,
            32);

        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }
}
