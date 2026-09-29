namespace manage365.Routes.API.Auth.PasswordReset;

public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    public string HashKey { get; init; } = string.Empty;
    public int CodeLifetimeMinutes { get; init; } = 10;
    public int ResetTokenLifetimeMinutes { get; init; } = 5;
    public int MaxAttempts { get; init; } = 5;
}

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; init; } = "smtp.gmail.com";
    public int Port { get; init; } = 587;
    public bool EnableSsl { get; init; } = true;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string FromEmail { get; init; } = string.Empty;
    public string FromName { get; init; } = "Retail365";
}
