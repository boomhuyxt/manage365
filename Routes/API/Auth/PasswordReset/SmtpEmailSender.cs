using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;

namespace manage365.Routes.API.Auth.PasswordReset;

public interface IPasswordResetEmailSender
{
    Task SendCodeAsync(string recipientEmail, string displayName, string code, int lifetimeMinutes, CancellationToken cancellationToken);
}

public static class SmtpCredential
{
    public static string NormalizePassword(string host, string password)
    {
        if (!host.Equals("smtp.gmail.com", StringComparison.OrdinalIgnoreCase))
        {
            return password;
        }

        return string.Concat(password.Where(character => !char.IsWhiteSpace(character)));
    }
}

public sealed class SmtpPasswordResetEmailSender(IOptions<SmtpOptions> options) : IPasswordResetEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendCodeAsync(
        string recipientEmail,
        string displayName,
        string code,
        int lifetimeMinutes,
        CancellationToken cancellationToken)
    {
        ValidateConfiguration();

        var safeName = HtmlEncoder.Default.Encode(displayName);
        var safeCode = HtmlEncoder.Default.Encode(code);
        var fromEmail = string.IsNullOrWhiteSpace(_options.FromEmail) ? _options.Username : _options.FromEmail;

        using var message = new MailMessage
        {
            From = new MailAddress(fromEmail, _options.FromName, Encoding.UTF8),
            Subject = "Mã xác nhận đặt lại mật khẩu Retail365",
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            IsBodyHtml = true,
            Body = $"""
                <!doctype html>
                <html lang="vi">
                <body style="font-family:Arial,sans-serif;color:#2d2926;line-height:1.6">
                  <h2>Đặt lại mật khẩu Retail365</h2>
                  <p>Xin chào {safeName},</p>
                  <p>Mã xác nhận của bạn là:</p>
                  <p style="font-size:30px;font-weight:700;letter-spacing:8px;color:#e86f25">{safeCode}</p>
                  <p>Mã có hiệu lực trong {lifetimeMinutes} phút và chỉ dùng được một lần.</p>
                  <p>Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này.</p>
                </body>
                </html>
                """
        };
        message.To.Add(new MailAddress(recipientEmail));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            $"Xin chào {displayName}. Mã xác nhận Retail365 của bạn là {code}. Mã có hiệu lực trong {lifetimeMinutes} phút.",
            Encoding.UTF8,
            MediaTypeNames.Text.Plain));

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(
                _options.Username,
                SmtpCredential.NormalizePassword(_options.Host, _options.Password))
        };

        await client.SendMailAsync(message, cancellationToken);
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.Host) ||
            string.IsNullOrWhiteSpace(_options.Username) ||
            string.IsNullOrWhiteSpace(_options.Password))
        {
            throw new InvalidOperationException("SMTP configuration is incomplete.");
        }
    }
}
