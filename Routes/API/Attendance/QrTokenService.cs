using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace manage365.Routes.API.Attendance;

public interface IQrTokenService
{
    QrToken Create();
    string Hash(string payload);
}

public sealed record QrToken(string Payload, string Hash);

public sealed class QrTokenService : IQrTokenService
{
    public QrToken Create()
    {
        var payload = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return new QrToken(payload, Hash(payload));
    }

    public string Hash(string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
