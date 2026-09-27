using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace manage365.Routes.API.Attendance;

public interface IQrSignatureService
{
    KioskQrResponse CreateKioskQr(string storeCode = "STORE-01", string storeName = "Chi Nhánh Bến Nghé, Quận 1");
    QrValidationResult ValidateQr(string qrPayload);
}

public sealed record QrValidationResult(
    bool IsValid,
    string StoreCode,
    string? ErrorCode,
    string? ErrorMessage);

public sealed class HmacQrSignatureService(IOptions<AttendancePolicyOptions> options) : IQrSignatureService
{
    private readonly AttendancePolicyOptions _policy = options.Value;

    public KioskQrResponse CreateKioskQr(string storeCode = "STORE-01", string storeName = "Chi Nhánh Bến Nghé, Quận 1")
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddSeconds(_policy.QrValiditySeconds);
        var expiresEpoch = expiresAt.ToUnixTimeSeconds();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

        var dataToSign = $"STORE|{storeCode}|{expiresEpoch}|{nonce}";
        var signature = ComputeSignature(dataToSign);

        var qrPayload = $"{dataToSign}|{signature}";

        return new KioskQrResponse(
            StoreCode: storeCode,
            StoreName: storeName,
            QrPayload: qrPayload,
            ExpiresAtUtc: expiresAt,
            RefreshInSeconds: Math.Max(5, _policy.QrValiditySeconds - 5));
    }

    public QrValidationResult ValidateQr(string qrPayload)
    {
        if (string.IsNullOrWhiteSpace(qrPayload))
        {
            return new QrValidationResult(false, string.Empty, "empty_qr", "Mã QR không hợp lệ hoặc trống.");
        }

        var parts = qrPayload.Trim().Split('|');
        if (parts.Length != 5 || parts[0] != "STORE")
        {
            return new QrValidationResult(false, string.Empty, "invalid_format", "Định dạng mã QR không hợp lệ.");
        }

        var storeCode = parts[1];
        if (!long.TryParse(parts[2], out var expiresEpoch))
        {
            return new QrValidationResult(false, storeCode, "invalid_timestamp", "Mã QR chứa thời gian không hợp lệ.");
        }

        var nonce = parts[3];
        var providedSignature = parts[4];

        var dataToSign = $"STORE|{storeCode}|{expiresEpoch}|{nonce}";
        var expectedSignature = ComputeSignature(dataToSign);

        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(providedSignature),
            Encoding.UTF8.GetBytes(expectedSignature)))
        {
            return new QrValidationResult(false, storeCode, "invalid_signature", "Chữ ký bảo mật của mã QR không chính xác.");
        }

        var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Cho phép 5 giây buffer độ trễ mạng
        if (nowEpoch > expiresEpoch + 5)
        {
            return new QrValidationResult(false, storeCode, "qr_expired", "Mã QR đã hết hạn. Vui lòng quét mã mới trên màn hình Kiosk.");
        }

        return new QrValidationResult(true, storeCode, null, null);
    }

    private string ComputeSignature(string data)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_policy.QrHmacSecret);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(dataBytes);
        return Convert.ToHexString(hash);
    }
}
