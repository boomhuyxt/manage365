namespace manage365.Routes.API.Auth;

public sealed record User(
    Guid Id,
    string Email,
    string DisplayName,
    string PasswordHash,
    DateTimeOffset CreatedAtUtc);
