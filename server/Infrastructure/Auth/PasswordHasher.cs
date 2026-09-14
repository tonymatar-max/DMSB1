using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace NexusDocs.Api.Infrastructure.Auth;

/// <summary>
/// PBKDF2 password hashing with a per-user random salt. Matches the approach used by Nexus Ops
/// (same iteration count / key size), so hashes are computed the same way across both products
/// even though each stores its own <c>PasswordHash</c> / <c>PasswordSalt</c> columns.
/// </summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltBytes = 16;
    private const int KeyBytes = 32;

    /// <summary>Hashes <paramref name="password"/> against a freshly generated random salt.</summary>
    public static (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = KeyDerivation.Pbkdf2(
            password, salt, KeyDerivationPrf.HMACSHA256, Iterations, KeyBytes);
        return (Convert.ToBase64String(key), Convert.ToBase64String(salt));
    }

    /// <summary>
    /// Recomputes the PBKDF2 key for <paramref name="password"/> using the stored
    /// <paramref name="salt"/> and compares it to the stored <paramref name="hash"/> in constant
    /// time.
    /// </summary>
    public static bool Verify(string password, string hash, string salt)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt)) return false;

        byte[] expected, saltBytes;
        try
        {
            expected = Convert.FromBase64String(hash);
            saltBytes = Convert.FromBase64String(salt);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = KeyDerivation.Pbkdf2(
            password, saltBytes, KeyDerivationPrf.HMACSHA256, Iterations, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
