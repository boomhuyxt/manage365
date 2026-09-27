using System.IdentityModel.Tokens.Jwt;
using manage365.Repositories.Attendance;
using manage365.Routes.API.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace manage365.Routes.API.Attendance;

[ApiController]
[Authorize]
[Route("api/attendance")]
public sealed class AttendanceShiftController(
    IQrSignatureService qrSignatureService,
    IShiftAttendanceRepository shiftRepository) : ControllerBase
{
    [HttpPost("verify-qr")]
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
                extensions: new Dictionary<string, object?> { ["code"] = qrCheck.ErrorCode ?? "invalid_qr" });
        }

        var now = DateTimeOffset.UtcNow;
        var shifts = await shiftRepository.GetEligibleShiftsAsync(userId, now, cancellationToken);

        string? recommendedAction = null;
        if (shifts.Count > 0)
        {
            // Tìm ca đang CheckedIn trước
            var activeShift = shifts.FirstOrDefault(s => s.Status == "CheckedIn");
            if (activeShift != null)
            {
                recommendedAction = "CHECK_OUT";
            }
            else
            {
                var assignedShift = shifts.FirstOrDefault(s => s.Status == "Assigned");
                if (assignedShift != null)
                {
                    recommendedAction = "CHECK_IN";
                }
            }
        }

        var message = shifts.Count == 0
            ? "Không tìm thấy phân công ca làm việc nào của bạn tại cửa hàng này hôm nay."
            : $"Đã xác thực QR cửa hàng thành công! Tìm thấy {shifts.Count} ca làm việc của bạn.";

        return Ok(new VerifyQrResponse(
            StoreCode: qrCheck.StoreCode,
            StoreName: "Chi Nhánh Bến Nghé, Quận 1",
            ServerTimeUtc: now,
            EligibleShifts: shifts,
            RecommendedAction: recommendedAction,
            Message: message));
    }

    [HttpPost("submit")]
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

        // 1. Kiểm tra lại QR Code
        var qrCheck = qrSignatureService.ValidateQr(request.QrPayload);
        if (!qrCheck.IsValid)
        {
            return Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: qrCheck.ErrorMessage ?? "Mã QR không hợp lệ hoặc đã hết hạn.",
                extensions: new Dictionary<string, object?> { ["code"] = qrCheck.ErrorCode ?? "invalid_qr" });
        }

        var now = DateTimeOffset.UtcNow;
        var result = await shiftRepository.SubmitAttendanceAsync(
            request.ShiftAssignmentId,
            userId,
            request.Action,
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
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode ?? "error" });
        }

        return Ok(new SubmitAttendanceResponse(
            Success: true,
            Message: result.Message,
            ShiftAssignmentId: request.ShiftAssignmentId,
            ShiftName: result.ShiftName ?? "Ca làm việc",
            Action: request.Action.ToUpperInvariant(),
            TimestampUtc: result.TimestampUtc,
            ActualHours: result.ActualHours));
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

        var history = await shiftRepository.GetAttendanceHistoryAsync(userId, Math.Clamp(limit, 1, 100), cancellationToken);
        return Ok(history);
    }

    private bool TryGetUserId(out long userId)
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return long.TryParse(sub, out userId);
    }
}
