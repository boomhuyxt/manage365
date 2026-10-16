namespace manage365.Routes.API.Shifts;

public sealed record ScheduleEmployeeDto(long Id, string Code, string DisplayName, long? DepartmentId, string? DepartmentName, string Status);
public sealed record ScheduleEmployeesResponse(List<ScheduleEmployeeDto> Items, int Page, int PageSize, int TotalCount);

/// <summary>
/// DTO thông tin mẫu ca làm việc của chuỗi cửa hàng.
/// </summary>
public sealed record ShiftTemplateDto(
    long Id,
    string Code,
    string Name,
    string StartTime,
    string EndTime,
    bool IsOvernight,
    string Color,
    string? Description,
    string Status,
    double DurationHours);

/// <summary>
/// Yêu cầu tạo mẫu ca làm việc mới.
/// </summary>
public sealed record CreateShiftTemplateRequest(
    string Code,
    string Name,
    string StartTime,
    string EndTime,
    bool IsOvernight = false,
    string? Color = "#f97316",
    string? Description = null);

/// <summary>
/// Yêu cầu cập nhật mẫu ca làm việc.
/// </summary>
public sealed record UpdateShiftTemplateRequest(
    string Name,
    string StartTime,
    string EndTime,
    bool IsOvernight = false,
    string? Color = "#f97316",
    string? Description = null,
    string? Status = "Active");

/// <summary>
/// DTO phân ca làm việc chi tiết cho nhân viên.
/// </summary>
public sealed record ShiftAssignmentDto(
    long AssignmentId,
    long ShiftId,
    long EmployeeId,
    string EmployeeName,
    string EmployeeCode,
    string? DepartmentName,
    long? ShiftTemplateId,
    string ShiftName,
    string ShiftDate,
    string StartTime,
    string EndTime,
    bool IsOvernight,
    string Color,
    string Status,
    string? Note,
    ShiftAttendanceBriefDto? Attendance);

/// <summary>
/// Tóm tắt trạng thái chấm công của ca làm.
/// </summary>
public sealed record ShiftAttendanceBriefDto(
    long AttendanceId,
    DateTimeOffset? CheckInAt,
    DateTimeOffset? CheckOutAt,
    decimal? ActualHours,
    string Status);

/// <summary>
/// Yêu cầu phân ca cho nhân viên (chọn mẫu ca hoặc tự chọn giờ tùy biến).
/// </summary>
public sealed record AssignShiftRequest(
    long EmployeeId,
    string ShiftDate,
    long? ShiftTemplateId = null,
    string? CustomShiftName = null,
    string? CustomStartTime = null,
    string? CustomEndTime = null,
    bool? IsOvernight = null,
    string? Note = null);

/// <summary>
/// Yêu cầu cập nhật ca làm đã phân công.
/// </summary>
public sealed record UpdateShiftAssignmentRequest(
    long? ShiftTemplateId = null,
    string? ShiftName = null,
    string? StartTime = null,
    string? EndTime = null,
    bool? IsOvernight = null,
    string? Status = null,
    string? Note = null);

/// <summary>
/// Yêu cầu phân ca hàng loạt (cho nhiều nhân viên hoặc nhiều ngày).
/// </summary>
public sealed record BatchAssignShiftRequest(
    List<AssignShiftRequest> Assignments);

/// <summary>
/// Kết quả phân ca hàng loạt.
/// </summary>
public sealed record BatchAssignShiftResult(
    int TotalRequested,
    int SuccessCount,
    int FailedCount,
    List<string> Errors,
    List<ShiftAssignmentDto> CreatedAssignments);

/// <summary>
/// Yêu cầu kiểm tra xung đột ca làm việc trước khi lưu.
/// </summary>
public sealed record ValidateShiftConflictRequest(
    long EmployeeId,
    string ShiftDate,
    string StartTime,
    string EndTime,
    bool IsOvernight = false,
    long? ExcludeAssignmentId = null);

/// <summary>
/// Kết quả kiểm tra xung đột ca làm việc.
/// </summary>
public sealed record ValidateShiftConflictResponse(
    bool HasConflict,
    string? ConflictType,
    string Message,
    List<string> Warnings,
    List<ConflictingShiftDto> ConflictingShifts);

/// <summary>
/// Thông tin ca làm bị xung đột.
/// </summary>
public sealed record ConflictingShiftDto(
    long AssignmentId,
    string ShiftName,
    string ShiftDate,
    string StartTime,
    string EndTime);

/// <summary>
/// Báo cáo lịch phân ca theo tuần hoặc theo khoảng thời gian.
/// </summary>
public sealed record WeeklyScheduleResponse(
    string StartDate,
    string EndDate,
    int TotalShifts,
    int TotalEmployees,
    List<ShiftAssignmentDto> Assignments,
    List<string> CoverageWarnings);

/// <summary>
/// Yêu cầu sao chép lịch phân ca từ tuần này sang tuần khác.
/// </summary>
public sealed record CopyWeeklyScheduleRequest(
    string SourceStartDate,
    string TargetStartDate);
