namespace manage365.Repositories.Auth;

public sealed record UserRefreshToken(
    long Id,
    long UserId,
    string TokenHash,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastUsedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string? ReplacedByTokenHash,
    string? DeviceInfo);

public interface IRefreshTokenRepository
{
    Task EnsureTableAsync(CancellationToken cancellationToken = default);

    Task SaveRefreshTokenAsync(
        long userId,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        string? deviceInfo = null,
        CancellationToken cancellationToken = default);

    Task<UserRefreshToken?> SlideExpirationAsync(
        string tokenHash,
        DateTimeOffset newExpiresAtUtc,
        CancellationToken cancellationToken = default);

    Task<UserRefreshToken?> FindValidTokenAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    Task RevokeTokenAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    Task RevokeAllUserTokensAsync(
        long userId,
        CancellationToken cancellationToken = default);
}
