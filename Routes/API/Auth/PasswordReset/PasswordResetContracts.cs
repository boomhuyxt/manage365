using System.ComponentModel.DataAnnotations;

namespace manage365.Routes.API.Auth.PasswordReset;

public sealed class ForgotPasswordRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; init; } = string.Empty;
}

public sealed class VerifyResetCodeRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, RegularExpression("^[0-9]{6}$")]
    public string Code { get; init; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, MinLength(32), MaxLength(256)]
    public string ResetToken { get; init; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string NewPassword { get; init; } = string.Empty;
}

public sealed record PasswordResetMessageResponse(string Message);
public sealed record VerifyResetCodeResponse(string ResetToken, DateTimeOffset ExpiresAtUtc);
