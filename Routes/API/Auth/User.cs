namespace manage365.Routes.API.Auth;

public sealed record User(
    long Id,
    string Email,
    string DisplayName,
    string PasswordHash,
    string Role,
    DateTimeOffset CreatedAtUtc);
