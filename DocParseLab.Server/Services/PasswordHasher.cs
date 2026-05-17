namespace DocParseLab.Server.Services;

public static class PasswordHasher
{
    public static (string Hash, string Salt) HashPassword(string password)
    {
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        var saltBytes = new byte[16];
        rng.GetBytes(saltBytes);

        using var deriveBytes = new System.Security.Cryptography.Rfc2898DeriveBytes(
            password, saltBytes, 100_000, System.Security.Cryptography.HashAlgorithmName.SHA256);
        var hashBytes = deriveBytes.GetBytes(32);

        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    public static bool VerifyPassword(string password, string storedHash, string storedSalt)
    {
        var saltBytes = Convert.FromBase64String(storedSalt);
        using var deriveBytes = new System.Security.Cryptography.Rfc2898DeriveBytes(
            password, saltBytes, 100_000, System.Security.Cryptography.HashAlgorithmName.SHA256);
        var hashBytes = deriveBytes.GetBytes(32);
        return Convert.ToBase64String(hashBytes) == storedHash;
    }
}
