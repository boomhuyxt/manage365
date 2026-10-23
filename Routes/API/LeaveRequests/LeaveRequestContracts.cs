using System.ComponentModel.DataAnnotations;

namespace manage365.Routes.API.LeaveRequests;

public static class LeaveUnits
{
    public const string Shift = "Shift";
    public const string Day = "Day";
    public const string DateRange = "DateRange";

    public static readonly IReadOnlyList<string> All = [Shift, Day, DateRange];
}

public static class LeaveReasonCodes
{
    public const string Personal = "Personal";
    public const string FamilyEmergency = "FamilyEmergency";
    public const string Sickness = "Sickness";
    public const string Maternity = "Maternity";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All = [Personal, FamilyEmergency, Sickness, Maternity, Other];
}

public static class LeaveStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    public const string Revoked = "Revoked";

    public static readonly IReadOnlyList<string> All = [Pending, Approved, Rejected, Cancelled, Revoked];
}

public static class LeavePayrollTreatments
{
    public const string Unresolved = "Unresolved";
    public const string PaidLeave = "PaidLeave";
    public const string UnpaidLeave = "UnpaidLeave";
    public const string ExternalBenefitReview = "ExternalBenefitReview";

    public static readonly IReadOnlyList<string> All = [Unresolved, PaidLeave, UnpaidLeave, ExternalBenefitReview];
}

public static class LeavePayrollReadinesses
{
    public const string NeedsReview = "NeedsReview";
    public const string Ready = "Ready";
    public const string Processed = "Processed";
}

public static class LeaveScheduleBases
{
    public const string Available = "Available";
    public const string Partial = "Partial";
    public const string Missing = "Missing";
}

public class CreateLeaveRequestInput
{
    [Required(ErrorMessage = "Đơn vị nghỉ (unit) không được để trống.")]
    public string Unit { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã lý do (reasonCode) không được để trống.")]
    public string ReasonCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Giải trình lý do không được để trống.")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "Lý do nghỉ phải từ 10 đến 2000 ký tự.")]
    public string Reason { get; set; } = string.Empty;

    public bool IsEmergency { get; set; }

    public long? ShiftAssignmentId { get; set; }

    public string? Date { get; set; }

    public string? StartDate { get; set; }

    public string? EndDate { get; set; }

    [StringLength(2000, ErrorMessage = "Lý do nộp muộn tối đa 2000 ký tự.")]
    public string? LateSubmissionReason { get; set; }
}

public sealed class CreateLeaveRequestCommand : CreateLeaveRequestInput
{
    [Required(ErrorMessage = "PreviewToken là bắt buộc.")]
    public string PreviewToken { get; set; } = string.Empty;
}

public sealed class CancelLeaveRequestCommand
{
    [Required(ErrorMessage = "Version là bắt buộc.")]
    public int Version { get; set; }

    [StringLength(2000, ErrorMessage = "Ghi chú tối đa 2000 ký tự.")]
    public string? Note { get; set; }
}

public sealed class ApproveLeaveRequestCommand
{
    [Required(ErrorMessage = "Version là bắt buộc.")]
    public int Version { get; set; }

    [Required(ErrorMessage = "PreviewToken là bắt buộc.")]
    public string PreviewToken { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phương án lương (payrollTreatment) là bắt buộc.")]
    public string PayrollTreatment { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "Ghi chú duyệt tối đa 2000 ký tự.")]
    public string? ReviewNote { get; set; }

    public bool LongLeaveAcknowledged { get; set; }

    public bool ExpandedCoverageAcknowledged { get; set; }
}

public sealed class RejectLeaveRequestCommand
{
    [Required(ErrorMessage = "Version là bắt buộc.")]
    public int Version { get; set; }

    [Required(ErrorMessage = "Lý do từ chối không được để trống.")]
    [StringLength(2000, MinimumLength = 1, ErrorMessage = "Lý do từ chối từ 1 đến 2000 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}

public sealed class RevokeLeaveRequestCommand
{
    [Required(ErrorMessage = "Version là bắt buộc.")]
    public int Version { get; set; }

    [Required(ErrorMessage = "Lý do thu hồi không được để trống.")]
    [StringLength(2000, MinimumLength = 1, ErrorMessage = "Lý do thu hồi từ 1 đến 2000 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}

public sealed record LeaveOptionsResponse(
    IReadOnlyList<string> Units,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string>? PayrollTreatments,
    int MinReasonLength,
    int MaxReasonLength,
    int MaxTechnicalDays,
    int LongLeaveThresholdDays,
    int LateSubmissionWindowDays);

public sealed record LeaveIntervalDto(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string StartLocal,
    string EndLocal);

public sealed record AffectedAssignmentDto(
    long AssignmentId,
    long ShiftId,
    string ShiftName,
    string ShiftDate,
    string StartTime,
    string EndTime,
    bool IsOvernight,
    int ScheduledMinutes,
    bool IsBeyondRequestedDates);

public sealed record LeaveRequestPreviewResponse(
    string PreviewToken,
    DateTimeOffset ExpiresAtUtc,
    string RequestedPeriod,
    IReadOnlyList<LeaveIntervalDto> EffectiveIntervals,
    IReadOnlyList<AffectedAssignmentDto> AffectedAssignments,
    int ScheduledLeaveMinutes,
    int? CalendarDays,
    string ScheduleBasis,
    bool IsLateSubmission,
    bool IsLongLeave,
    bool HasExpandedCoverage,
    IReadOnlyList<string> Warnings,
    bool HasConflict,
    string? ConflictReason,
    int? ExistingVersion);

public sealed record LeaveRequestAuditDto(
    long Id,
    long ActorId,
    string ActorName,
    string Action,
    string? PreviousStatus,
    string NewStatus,
    string? Note,
    DateTimeOffset TimestampUtc);

public sealed record LeaveDayAllocationDto(
    string Date,
    int ScheduledMinutes);

public sealed record LeaveRequestDto(
    long Id,
    int Version,
    long EmployeeId,
    string EmployeeName,
    string EmployeeCode,
    string? EmployeeTypeSnapshot,
    string? DepartmentName,
    string Unit,
    string ReasonCode,
    string Reason,
    bool IsEmergency,
    bool IsLateSubmission,
    string? LateSubmissionReason,
    long? ShiftAssignmentId,
    string? Date,
    string? StartDate,
    string? EndDate,
    int? CalendarDays,
    int ScheduledLeaveMinutes,
    int AffectedAssignmentCount,
    string ScheduleBasis,
    string Status,
    string PayrollTreatment,
    string PayrollReadiness,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long? ReviewedById,
    string? ReviewedByName,
    DateTimeOffset? ReviewedAtUtc,
    string? ReviewNote,
    string? RejectionReason,
    string? RevocationReason,
    DateTimeOffset? CancelledAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string RequestedPeriod,
    IReadOnlyList<LeaveIntervalDto> EffectiveIntervals);

public sealed record LeaveRequestDetailDto(
    LeaveRequestDto Request,
    IReadOnlyList<AffectedAssignmentDto> AffectedAssignments,
    IReadOnlyList<LeaveDayAllocationDto> DayAllocations,
    IReadOnlyList<LeaveRequestAuditDto> History);

public sealed record PagedLeaveRequestsResponse(
    IReadOnlyList<LeaveRequestDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record PayrollLeaveSummaryItemDto(
    string Date,
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string? DepartmentName,
    string EmployeeType,
    long LeaveRequestId,
    string Unit,
    string ReasonCode,
    string PayrollTreatment,
    string PayrollReadiness,
    int ScheduledMinutes,
    bool NeedsAudit,
    string? AuditNote);

public sealed record PayrollLeaveSummaryResponse(
    IReadOnlyList<PayrollLeaveSummaryItemDto> Items,
    string FromDate,
    string ToDate,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
