using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace manage365.Routes.API.Auth.PasswordReset;

[ApiController]
[Route("api/auth")]
public sealed class PasswordResetController(IPasswordResetService passwordResetService) : ControllerBase
{
    private const string GenericRequestMessage =
        "Nếu email tồn tại trong hệ thống, mã xác nhận đã được gửi.";

    [HttpPost("forgot-password")]
    [ProducesResponseType<PasswordResetMessageResponse>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<PasswordResetMessageResponse>> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await passwordResetService.RequestCodeAsync(request.Email, cancellationToken);
        return Accepted(new PasswordResetMessageResponse(GenericRequestMessage));
    }

    [HttpPost("verify-reset-code")]
    [EnableRateLimiting("password-reset")]
    [ProducesResponseType<VerifyResetCodeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<VerifyResetCodeResponse>> VerifyResetCode(
        VerifyResetCodeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await passwordResetService.VerifyCodeAsync(request.Email, request.Code, cancellationToken);
        if (result is null)
        {
            return InvalidResetRequest();
        }

        return Ok(new VerifyResetCodeResponse(result.Token, result.ExpiresAtUtc));
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("password-reset")]
    [ProducesResponseType<PasswordResetMessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PasswordResetMessageResponse>> ResetPassword(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var reset = await passwordResetService.ResetPasswordAsync(
            request.Email,
            request.ResetToken,
            request.NewPassword,
            cancellationToken);

        return reset
            ? Ok(new PasswordResetMessageResponse("Mật khẩu đã được cập nhật."))
            : InvalidResetRequest();
    }

    private BadRequestObjectResult InvalidResetRequest() => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "Mã xác nhận hoặc phiên đặt lại mật khẩu không hợp lệ."
    });
}
