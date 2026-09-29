using System.IdentityModel.Tokens.Jwt;
using manage365.Repositories.Attendance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace manage365.Routes.API.Attendance;

[ApiController]
[Authorize]
[Route("api/attendance")]
public sealed class AttendanceShiftController(
    IQrSignatureService qrSignatureService,
    IAttendanceGeofenceService geofenceService,
    IShiftAttendanceRepository shiftRepository) : ControllerBase
{
    [HttpPost("verify-qr")]
    [EnableRateLimiting("attendance-scan")]
    [ProducesResponseType<VerifyQrResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<VerifyQrResponse>> VerifyQr(
        [FromBody] VerifyQrRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var qrCheck = qrSignatureService.ValidateQr(request.QrPayload);
        if (!qrCheck.IsValid)
        {
            return Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: qrCheck.ErrorMessage ?? "Mã QR không hợp lệ hoặc đã hết hạn.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = qrCheck.ErrorCode ?? "invalid_qr"
                });
        }

        var now = DateTimeOffset.UtcNow;
        var shifts = await shiftRepository.GetEligibleShiftsAsync(userId, now, cancellationToken);
        var recommendedAction = shifts
            .FirstOrDefault(shift => shift.CanPerformAction && shift.AllowedAction == "CHECK_OUT")
            ?.AllowedAction
            ?? shifts.FirstOrDefault(shift => shift.CanPerformAction && shift.AllowedAction == "CHECK_IN")
                ?.AllowedAction;

        var hasScheduledShift = shifts.Any(shift => shift.ShiftAssignmentId.HasValue);
        var message = hasScheduledShift
            ? $"Đã xác thực QR. Tìm thấy {shifts.Count(shift => shift.ShiftAssignmentId.HasValue)} ca được phân công."
            : "Đã xác thực QR. Bạn có thể chấm công ở chế độ chưa xếp ca.";

        return Ok(new VerifyQrResponse(
            StoreCode: qrCheck.StoreCode ?? string.Empty,
            StoreName: "Chi Nhánh Bến Nghé, Quận 1",
            ServerTimeUtc: now,
            EligibleShifts: shifts,
            RecommendedAction: recommendedAction,
            Message: message));
    }

    [HttpPost("submit")]
    [EnableRateLimiting("attendance-scan")]
    [ProducesResponseType<SubmitAttendanceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SubmitAttendanceResponse>> Submit(
        [FromBody] SubmitAttendanceRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var qrCheck = qrSignatureService.ValidateQr(request.QrPayload);
        if (!qrCheck.IsValid)
        {
            return Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: qrCheck.ErrorMessage ?? "Mã QR không hợp lệ hoặc đã hết hạn.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = qrCheck.ErrorCode ?? "invalid_qr"
                });
        }

        var now = DateTimeOffset.UtcNow;
        var geofence = await geofenceService.ValidateAsync(
            qrCheck.StoreCode ?? string.Empty,
            request.Location,
            now,
            cancellationToken);

        if (!geofence.IsValid || geofence.Location is null)
        {
            return Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: geofence.ErrorMessage ?? "Không thể xác thực vị trí chấm công.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = geofence.ErrorCode ?? "location_invalid",
                    ["distanceMeters"] = geofence.DistanceMeters,
                    ["allowedRadiusMeters"] = geofence.AllowedRadiusMeters
                });
        }

        var result = await shiftRepository.SubmitAttendanceAsync(
            request.ShiftAssignmentId,
            userId,
            request.Action,
            geofence.Location,
            now,
            cancellationToken);

        if (!result.Success)
        {
            var statusCode = result.ErrorCode switch
            {
                "already_checked_in" or "duplicate_check_in" => StatusCodes.Status409Conflict,
                "not_checked_in" or "already_checked_out" => StatusCodes.Status409Conflict,
                "shift_not_found" => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status400BadRequest
            };

            return Problem(
                statusCode: statusCode,
                title: result.Message,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = result.ErrorCode ?? "error"
                });
        }

        return Ok(new SubmitAttendanceResponse(
            Success: true,
            Message: result.Message,
            ShiftAssignmentId: request.ShiftAssignmentId,
            ShiftName: result.ShiftName ?? "Chấm công chưa xếp ca",
            Action: request.Action.Trim().ToUpperInvariant(),
            TimestampUtc: result.TimestampUtc,
            ActualHours: result.ActualHours,
            Location: geofence.Location.ToDto()));
    }

    [HttpGet("history")]
    [ProducesResponseType<List<EmployeeShiftRecordDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<EmployeeShiftRecordDto>>> GetHistory(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var history = await shiftRepository.GetAttendanceHistoryAsync(
            userId,
            Math.Clamp(limit, 1, 100),
            cancellationToken);
        return Ok(history);
    }

    [HttpGet("admin/snapshot")]
    [ProducesResponseType<AdminAttendanceSnapshotDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminAttendanceSnapshotDto>> GetAdminSnapshot(
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await shiftRepository.GetAdminSnapshotAsync(
            DateTimeOffset.UtcNow,
            Math.Clamp(limit, 1, 100),
            cancellationToken);
        return Ok(snapshot);
    }

    private bool TryGetUserId(out long userId)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return long.TryParse(subject, out userId);
    }
}
