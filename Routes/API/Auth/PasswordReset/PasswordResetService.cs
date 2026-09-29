using manage365.Repositories.Auth;
using Microsoft.Extensions.Options;

namespace manage365.Routes.API.Auth.PasswordReset;

public interface IPasswordResetService
{
    Task RequestCodeAsync(string email, CancellationToken cancellationToken);
    Task<ResetTokenResult?> VerifyCodeAsync(string email, string code, CancellationToken cancellationToken);
    Task<bool> ResetPasswordAsync(string email, string resetToken, string newPassword, CancellationToken cancellationToken);
}

public sealed record ResetTokenResult(string Token, DateTimeOffset ExpiresAtUtc);

public sealed class PasswordResetService(
    IUserRepository userRepository,
    IPasswordResetRepository resetRepository,
    IPasswordResetTokenProtector tokenProtector,
    IPasswordResetEmailSender emailSender,
    IPasswordHasher passwordHasher,
    IOptions<PasswordResetOptions> options) : IPasswordResetService
{
    private readonly PasswordResetOptions _options = options.Value;

    public async Task RequestCodeAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        var user = await userRepository.FindByEmailAsync(normalizedEmail, cancellationToken);
        if (user is null)
        {
            tokenProtector.HashCode(normalizedEmail, tokenProtector.CreateCode());
            return;
        }

        var code = tokenProtector.CreateCode();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.CodeLifetimeMinutes);
        var saved = await resetRepository.SaveCodeAsync(
            user.Id,
            tokenProtector.HashCode(normalizedEmail, code),
            expiresAt,
            cancellationToken);
        if (!saved)
        {
            return;
        }

        try
        {
            await emailSender.SendCodeAsync(
                user.Email,
                user.DisplayName,
                code,
                _options.CodeLifetimeMinutes,
                cancellationToken);
        }
        catch
        {
            await resetRepository.InvalidateCodeAsync(user.Id, CancellationToken.None);
            throw;
        }
    }

    public async Task<ResetTokenResult?> VerifyCodeAsync(
        string email,
        string code,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        var user = await userRepository.FindByEmailAsync(normalizedEmail, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var token = tokenProtector.CreateResetToken();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.ResetTokenLifetimeMinutes);
        var verified = await resetRepository.VerifyCodeAsync(
            user.Id,
            tokenProtector.HashCode(normalizedEmail, code),
            tokenProtector.HashResetToken(token),
            expiresAt,
            _options.MaxAttempts,
            cancellationToken);

        return verified ? new ResetTokenResult(token, expiresAt) : null;
    }

    public async Task<bool> ResetPasswordAsync(
        string email,
        string resetToken,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByEmailAsync(NormalizeEmail(email), cancellationToken);
        return user is not null && await resetRepository.ResetPasswordAsync(
            user.Id,
            tokenProtector.HashResetToken(resetToken),
            passwordHasher.Hash(newPassword),
            cancellationToken);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
