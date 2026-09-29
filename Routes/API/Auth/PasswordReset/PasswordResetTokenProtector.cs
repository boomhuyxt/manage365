using System.Security.Cryptography;
using System.Text;

namespace manage365.Routes.API.Auth.PasswordReset;

public interface IPasswordResetTokenProtector
{
    string CreateCode();
    string HashCode(string email, string code);
    string CreateResetToken();
    string HashResetToken(string token);
}

public sealed class PasswordResetTokenProtector : IPasswordResetTokenProtector
{
    private readonly byte[] _key;

    public PasswordResetTokenProtector(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _key = Encoding.UTF8.GetBytes(key);
        if (_key.Length < 32)
        {
            throw new ArgumentException("Password reset hash key must be at least 32 bytes.", nameof(key));
        }
    }

    public string CreateCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public string HashCode(string email, string code) =>
        ComputeHash($"code:{email.Trim().ToLowerInvariant()}:{code}");

    public string CreateResetToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return token.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public string HashResetToken(string token) => ComputeHash($"token:{token}");

    private string ComputeHash(string value) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
