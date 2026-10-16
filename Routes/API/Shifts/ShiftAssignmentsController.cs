using System.Globalization;
using System.Security.Claims;
using manage365.Repositories.Shifts;
using manage365.Routes.API.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace manage365.Routes.API.Shifts;

[ApiController]
[ShiftApiExceptionFilter]
[Tags("Shift Scheduling - Phân Ca Linh Động")]
[Route("api/shift-assignments")]
public sealed class ShiftAssignmentsController(IShiftSchedulingRepository shiftRepo) : ControllerBase
{
    /// <summary>
    /// Lấy ma trận lịch phân ca làm việc theo tuần hoặc theo khoảng ngày
    /// </summary>
    /// <param name="startDate">Ngày bắt đầu (yyyy-MM-dd), mặc định: Thứ 2 tuần hiện tại</param>
    /// <param name="endDate">Ngày kết thúc (yyyy-MM-dd), mặc định: Chủ nhật tuần hiện tại</param>
    /// <param name="departmentId">Lọc theo phòng ban (tùy chọn)</param>
    /// <param name="employeeId">Lọc theo nhân viên cụ thể (tùy chọn)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet("schedule")]
    [Authorize]
    [ProducesResponseType<WeeklyScheduleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WeeklyScheduleResponse>> GetSchedule(
        [FromQuery] string? startDate = null,
        [FromQuery] string? endDate = null,
        [FromQuery] long? departmentId = null,
        [FromQuery] long? employeeId = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsManager())
        {
            var currentId = CurrentEmployeeId();
            if (currentId is null) return Unauthorized();
            if (employeeId.HasValue && employeeId != currentId) return Forbid();
            employeeId = currentId;
        }
        DateOnly start;
        DateOnly end;

        if (string.IsNullOrWhiteSpace(startDate))
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
            var diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            start = today.AddDays(-diff);
        }
        else if (!DateOnly.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start) || start.Year is < 1900 or > 9998)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Định dạng startDate không hợp lệ (chuẩn yyyy-MM-dd).",
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_date_format" });
        }

        if (string.IsNullOrWhiteSpace(endDate))
        {
            end = start.AddDays(6);
        }
        else if (!DateOnly.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out end) || end.Year is < 1900 or > 9998)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Định dạng endDate không hợp lệ (chuẩn yyyy-MM-dd).",
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_date_format" });
        }

        if (end < start || end.DayNumber - start.DayNumber > 92)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Khoảng ngày phải tăng dần và không vượt quá 93 ngày.",
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_date_range" });
        }

        var result = await shiftRepo.GetScheduleAsync(start, end, departmentId, employeeId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Lấy chi tiết một ca làm việc đã được phân công theo ID
    /// </summary>
    /// <param name="id">ID bản ghi phân ca (dang_ky_ca.id)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet("{id:long}")]
    [Authorize]
    [ProducesResponseType<ShiftAssignmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShiftAssignmentDto>> GetAssignmentById(
        long id,
        CancellationToken cancellationToken = default)
    {
        var assignment = await shiftRepo.GetAssignmentByIdAsync(id, cancellationToken);
        if (assignment is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy bản ghi phân ca ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "assignment_not_found" });
        }

        if (!IsManager() && assignment.EmployeeId != CurrentEmployeeId()) return Forbid();
        return Ok(assignment);
    }

    /// <summary>
    /// Phân ca làm việc mới cho nhân viên (Hỗ trợ chọn ca mẫu HOẶC nhập giờ linh động tùy ý)
    /// </summary>
    /// <param name="request">Thông tin phân ca: employeeId, shiftDate, shiftTemplateId hoặc customStartTime/EndTime</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpPost]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType<ShiftAssignmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShiftAssignmentDto>> AssignShift(
        [FromBody] AssignShiftRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.EmployeeId <= 0 || string.IsNullOrWhiteSpace(request.ShiftDate))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "EmployeeId và ShiftDate là bắt buộc.",
                extensions: new Dictionary<string, object?> { ["code"] = "missing_required_fields" });
        }

        try
        {
            ShiftInput.Assignment(request);
            var created = await shiftRepo.AssignShiftAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetAssignmentById), new { id = created.AssignmentId }, created);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "shift_conflict_or_invalid" });
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_shift_data" });
        }
    }

    /// <summary>
    /// Cập nhật thông tin phân ca đã có (đổi giờ, đổi mẫu ca, đổi trạng thái hoặc ghi chú)
    /// </summary>
    /// <param name="id">ID bản ghi phân ca</param>
    /// <param name="request">Thông tin cập nhật</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpPut("{id:long}")]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType<ShiftAssignmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShiftAssignmentDto>> UpdateAssignment(
        long id,
        [FromBody] UpdateShiftAssignmentRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ShiftInput.Update(request);
            var updated = await shiftRepo.UpdateAssignmentAsync(id, request, cancellationToken);
            if (updated is null)
            {
                return Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: $"Không tìm thấy bản ghi phân ca ID {id} để cập nhật.",
                    extensions: new Dictionary<string, object?> { ["code"] = "assignment_not_found" });
            }

            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "shift_conflict_or_invalid" });
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_shift_data" });
        }
    }

    /// <summary>
    /// Hủy / Xóa một ca làm việc đã phân công (Admin / Manager)
    /// </summary>
    /// <param name="id">ID bản ghi phân ca</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAssignment(
        long id,
        CancellationToken cancellationToken = default)
    {
        var success = await shiftRepo.DeleteAssignmentAsync(id, cancellationToken);
        if (!success)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy bản ghi phân ca ID {id} để xóa.",
                extensions: new Dictionary<string, object?> { ["code"] = "assignment_not_found" });
        }

        return NoContent();
    }

    /// <summary>
    /// Phân ca hàng loạt cho nhiều nhân viên hoặc nhiều ngày trong tuần (Admin / Manager)
    /// </summary>
    /// <param name="request">Danh sách các ca cần phân công</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpPost("batch")]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType<BatchAssignShiftResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BatchAssignShiftResult>> BatchAssignShifts(
        [FromBody] BatchAssignShiftRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Assignments is null || request.Assignments.Count == 0 || request.Assignments.Count > 200 || request.Assignments.Any(a => a is null))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Danh sách phân ca phải có từ 1 đến 200 mục hợp lệ.",
                extensions: new Dictionary<string, object?> { ["code"] = "empty_assignments" });
        }

        var result = await shiftRepo.BatchAssignShiftsAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Sao chép toàn bộ lịch phân ca từ một tuần sang tuần khác (Tiện ích quản lý chuỗi bán lẻ)
    /// </summary>
    /// <param name="request">Ngày thứ 2 của tuần nguồn và ngày thứ 2 của tuần đích</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpPost("copy-week")]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType<BatchAssignShiftResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BatchAssignShiftResult>> CopyWeeklySchedule(
        [FromBody] CopyWeeklyScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!DateOnly.TryParseExact(request.SourceStartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sourceStart) ||
            !DateOnly.TryParseExact(request.TargetStartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var targetStart) ||
            sourceStart.Year is < 1900 or > 9998 || targetStart.Year is < 1900 or > 9998 ||
            sourceStart.DayOfWeek != DayOfWeek.Monday || targetStart.DayOfWeek != DayOfWeek.Monday || sourceStart == targetStart)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Chọn hai ngày thứ Hai khác nhau, định dạng yyyy-MM-dd (1900–9998).",
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_date_format" });
        }

        var result = await shiftRepo.CopyWeeklyScheduleAsync(sourceStart, targetStart, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Kiểm tra trước xung đột ca làm việc (Realtime validation cho UI Web/App mà không lưu DB)
    /// </summary>
    /// <param name="request">Thông tin ca dự kiến phân công</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpPost("validate")]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType<ValidateShiftConflictResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ValidateShiftConflictResponse>> ValidateConflict(
        [FromBody] ValidateShiftConflictRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.EmployeeId <= 0 || request.ExcludeAssignmentId is <= 0 ||
            !DateOnly.TryParseExact(request.ShiftDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var shiftDate) ||
            shiftDate.Year is < 1900 or > 9998 ||
            !TimeOnly.TryParseExact(request.StartTime, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime) ||
            !TimeOnly.TryParseExact(request.EndTime, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Định dạng ngày hoặc giờ không hợp lệ.",
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_format" });
        }

        var result = await shiftRepo.ValidateConflictAsync(
            request.EmployeeId,
            shiftDate,
            startTime,
            endTime,
            request.IsOvernight,
            request.ExcludeAssignmentId,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Danh sách nhân viên phục vụ phân ca, bao gồm người chưa có ca, không chứa dữ liệu lương.</summary>
    [HttpGet("employees")]
    [Authorize(Roles = AppRoles.Managers)]
    public async Task<ActionResult<ScheduleEmployeesResponse>> GetEmployees(
        [FromQuery] string? search = null, [FromQuery] long? departmentId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100, CancellationToken cancellationToken = default)
    {
        if (page < 1 || page > 100000 || pageSize is < 1 or > 100 || search?.Length > 150 || departmentId is <= 0)
            return Problem(statusCode: 400, title: "Bộ lọc/phân trang không hợp lệ.");
        return Ok(await shiftRepo.GetEmployeesAsync(search, departmentId, page, pageSize, cancellationToken));
    }

    private bool IsManager() => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Manager);
    private long? CurrentEmployeeId() => long.TryParse(User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0 ? id : null;
}
