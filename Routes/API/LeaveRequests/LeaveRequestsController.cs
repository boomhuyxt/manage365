using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using manage365.Repositories.LeaveRequests;
using manage365.Routes.API.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace manage365.Routes.API.LeaveRequests;

/// <summary>
/// Quản lý và xử lý đơn xin nghỉ phép (Leave Requests)
/// </summary>
[ApiController]
[Authorize]
[Route("api/leave-requests")]
[LeaveApiExceptionFilter]
[TypeFilter(typeof(LeaveAccountFilter))]
public sealed class LeaveRequestsController(
    ILeaveRequestRepository repository) : ControllerBase
{

    /// <summary>
    /// Lấy danh mục các tùy chọn và giới hạn tạo đơn
    /// </summary>
    [HttpGet("options")]
    [ProducesResponseType<LeaveOptionsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LeaveOptionsResponse>> GetOptions(CancellationToken cancellationToken)
    {
        var isAdmin = User.IsInRole(AppRoles.Admin);
        var options = await repository.GetOptionsAsync(isAdmin, cancellationToken);
        return Ok(options);
    }

    /// <summary>
    /// Xem trước tác động, khoảng thời gian thực tế và nhận preview token trước khi gửi đơn
    /// </summary>
    [HttpPost("preview")]
    [ProducesResponseType<LeaveRequestPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LeaveRequestPreviewResponse>> Preview(
        [FromBody] CreateLeaveRequestInput input,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var preview = await repository.GeneratePreviewAsync(userId, input, DateTimeOffset.UtcNow, cancellationToken);
        return Ok(preview);
    }

    /// <summary>
    /// Gửi đơn xin nghỉ phép mới (kèm preview token, hỗ trợ header Idempotency-Key)
    /// </summary>
    [HttpPost]
    [ProducesResponseType<LeaveRequestDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LeaveRequestDto>> Create(
        [FromBody] CreateLeaveRequestCommand command,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        string? requestHash = null;
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            if (!Guid.TryParse(idempotencyKey, out var key))
                throw new LeaveValidationException("validation_error", "Idempotency-Key phải là UUID.");
            idempotencyKey = key.ToString();
            var raw = JsonSerializer.Serialize((CreateLeaveRequestInput)command);
            requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        }

        var result = await repository.CreateLeaveRequestAsync(
            userId,
            command,
            idempotencyKey,
            requestHash,
            DateTimeOffset.UtcNow,
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Lấy danh sách đơn nghỉ phép của chính mình (phân trang, lọc theo trạng thái/thời gian)
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType<PagedLeaveRequestsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedLeaveRequestsResponse>> GetMyLeaveRequests(
        [FromQuery] string? status,
        [FromQuery] string? unit,
        [FromQuery] string? fromDate,
        [FromQuery] string? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        DateOnly? parsedFrom = ParseFilterDate(fromDate);
        DateOnly? parsedTo = ParseFilterDate(toDate);

        ValidateFilters(status, unit, null, null, parsedFrom, parsedTo, page, pageSize);

        var result = await repository.GetMyLeaveRequestsAsync(
            userId, status, unit, parsedFrom, parsedTo, page, pageSize, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Lấy chi tiết đơn nghỉ phép, ca ảnh hưởng và lịch sử thao tác (chính chủ hoặc Admin)
    /// </summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<LeaveRequestDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LeaveRequestDetailDto>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var detail = await repository.GetDetailByIdAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Không tìm thấy đơn nghỉ phép.",
                Extensions = { ["code"] = "leave_request_not_found" }
            });
        }

        var isAdmin = User.IsInRole(AppRoles.Admin);
        if (!isAdmin && detail.Request.EmployeeId != userId)
        {
            // Do not leak existence to unauthorized employees
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Không tìm thấy đơn nghỉ phép.",
                Extensions = { ["code"] = "leave_request_not_found" }
            });
        }

        return Ok(detail);
    }

    /// <summary>
    /// Nhân viên tự hủy đơn nghỉ phép của mình (chỉ áp dụng khi đơn đang ở trạng thái Pending)
    /// </summary>
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType<LeaveRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LeaveRequestDto>> Cancel(
        long id,
        [FromBody] CancelLeaveRequestCommand command,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var result = await repository.CancelLeaveRequestAsync(
            id, userId, command, DateTimeOffset.UtcNow, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// (Admin) Xem danh sách tất cả đơn nghỉ phép trong toàn hệ thống kèm bộ lọc
    /// </summary>
    [HttpGet]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<PagedLeaveRequestsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedLeaveRequestsResponse>> GetAllAdmin(
        [FromQuery] string? status,
        [FromQuery] string? unit,
        [FromQuery] string? reasonCode,
        [FromQuery] string? payrollTreatment,
        [FromQuery] long? employeeId,
        [FromQuery] long? departmentId,
        [FromQuery] string? fromDate,
        [FromQuery] string? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        DateOnly? parsedFrom = ParseFilterDate(fromDate);
        DateOnly? parsedTo = ParseFilterDate(toDate);

        ValidateFilters(status, unit, reasonCode, payrollTreatment, parsedFrom, parsedTo, page, pageSize);
        if (employeeId is <= 0 || departmentId is <= 0)
            throw new LeaveValidationException("validation_error", "ID phải là số dương.");

        var result = await repository.GetLeaveRequestsAdminAsync(
            status, unit, reasonCode, payrollTreatment, employeeId, departmentId,
            parsedFrom, parsedTo, page, pageSize, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// (Admin) Xem trước dữ liệu xét duyệt trước khi thực hiện Approve
    /// </summary>
    [HttpGet("{id:long}/review-preview")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<LeaveRequestPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LeaveRequestPreviewResponse>> ReviewPreview(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var adminId)) return Unauthorized();
        var preview = await repository.GenerateReviewPreviewAsync(id, DateTimeOffset.UtcNow, cancellationToken, adminId);
        return Ok(preview);
    }

    /// <summary>
    /// (Admin) Duyệt đơn nghỉ phép (chốt snapshot ca, phân loại phương án lương)
    /// </summary>
    [HttpPost("{id:long}/approve")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<LeaveRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LeaveRequestDto>> Approve(
        long id,
        [FromBody] ApproveLeaveRequestCommand command,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var adminId)) return Unauthorized();

        var result = await repository.ApproveLeaveRequestAsync(
            id, adminId, command, DateTimeOffset.UtcNow, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// (Admin) Từ chối đơn nghỉ phép (bắt buộc nêu rõ lý do)
    /// </summary>
    [HttpPost("{id:long}/reject")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<LeaveRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LeaveRequestDto>> Reject(
        long id,
        [FromBody] RejectLeaveRequestCommand command,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var adminId)) return Unauthorized();

        var result = await repository.RejectLeaveRequestAsync(
            id, adminId, command, DateTimeOffset.UtcNow, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// (Admin) Thu hồi đơn đã duyệt trước khi khoảng nghỉ bắt đầu
    /// </summary>
    [HttpPost("{id:long}/revoke")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<LeaveRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LeaveRequestDto>> Revoke(
        long id,
        [FromBody] RevokeLeaveRequestCommand command,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var adminId)) return Unauthorized();

        var result = await repository.RevokeLeaveRequestAsync(
            id, adminId, command, DateTimeOffset.UtcNow, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// (Admin) Đọc báo cáo tổng hợp phút nghỉ theo ngày phục vụ đối soát lương
    /// </summary>
    [HttpGet("payroll-summary")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<PayrollLeaveSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PayrollLeaveSummaryResponse>> GetPayrollSummary(
        [FromQuery] string fromDate,
        [FromQuery] string toDate,
        [FromQuery] long? employeeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        ValidateFilters(null, null, null, null, ParseFilterDate(fromDate), ParseFilterDate(toDate), page, pageSize);
        if (employeeId is <= 0) throw new LeaveValidationException("validation_error", "ID phải là số dương.");
        if (!DateOnly.TryParseExact(fromDate, "yyyy-MM-dd", out var from) ||
            !DateOnly.TryParseExact(toDate, "yyyy-MM-dd", out var to))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "fromDate và toDate phải đúng định dạng yyyy-MM-dd.",
                Extensions = { ["code"] = "invalid_date_format" }
            });
        }

        var result = await repository.GetPayrollSummaryAsync(
            from, to, employeeId, page, pageSize, cancellationToken);

        return Ok(result);
    }

    private static DateOnly? ParseFilterDate(string? value)
    {
        if (value is null) return null;
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date) || date.Year < 1900 || date.Year > 9998)
            throw new LeaveValidationException("invalid_date_range", "Ngày phải có dạng yyyy-MM-dd, năm từ 1900 đến 9998.");
        return date;
    }

    private static void ValidateFilters(string? status, string? unit, string? reason, string? treatment,
        DateOnly? from, DateOnly? to, int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new LeaveValidationException("invalid_pagination", "Phân trang không hợp lệ.");
        if (from > to) throw new LeaveValidationException("invalid_date_range", "Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.");
        if ((status is not null && !LeaveStatuses.All.Contains(status)) ||
            (unit is not null && !LeaveUnits.All.Contains(unit)) ||
            (reason is not null && !LeaveReasonCodes.All.Contains(reason)) ||
            (treatment is not null && !LeavePayrollTreatments.All.Contains(treatment)))
            throw new LeaveValidationException("validation_error", "Bộ lọc không hợp lệ.");
    }

    private bool TryGetUserId(out long userId)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(subject, out userId) && userId > 0;
    }
}
