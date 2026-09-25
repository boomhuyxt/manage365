using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using manage365.Repositories.Attendance;
using manage365.Routes.API.Auth;

namespace manage365.Routes.API.Attendance;

[ApiController]
[Authorize(Roles = AppRoles.Employee)]
[Route("api/attendance-check-ins")]
public sealed class AttendanceCheckInsController(
    IAttendanceRepository repository,
    IQrTokenService qrTokenService) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("attendance-scan")]
    [ProducesResponseType<AttendanceRecordResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AttendanceRecordResponse>> Create(
        CreateCheckInRequest request,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId))
        {
            return Unauthorized();
        }

        var result = await repository.TryCheckInAsync(
            qrTokenService.Hash(request.QrPayload.Trim()),
            userId,
            DateTimeOffset.UtcNow,
            cancellationToken);

        return result.Status switch
        {
            CheckInStatus.Success => Created(
                $"/api/attendance-records/{result.Record!.Id}",
                result.Record.ToResponse()),
            CheckInStatus.Duplicate => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Attendance has already been recorded.",
                extensions: new Dictionary<string, object?> { ["code"] = "duplicate_check_in" }),
            CheckInStatus.NotStarted => Unprocessable("attendance_not_started", "Attendance session has not started."),
            CheckInStatus.Expired => Unprocessable("qr_expired", "QR code has expired."),
            CheckInStatus.Closed => Unprocessable("attendance_closed", "Attendance session is closed."),
            _ => Unprocessable("invalid_qr", "QR code is invalid.")
        };
    }

    private ObjectResult Unprocessable(string code, string title) =>
        Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: title,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
