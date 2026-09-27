namespace manage365.Routes.API.Attendance;

public sealed class AttendancePolicyOptions
{
    public const string SectionName = "AttendancePolicy";

    public string TimeZone { get; set; } = "Asia/Ho_Chi_Minh";
    public int QrValiditySeconds { get; set; } = 35;
    public int EarlyCheckInMinutes { get; set; } = 45;
    public int LateCheckOutMinutes { get; set; } = 90;
    public string QrHmacSecret { get; set; } = string.Empty;
}

public sealed record KioskQrResponse(
    string StoreCode,
    string StoreName,
    string QrPayload,
    DateTimeOffset ExpiresAtUtc,
    int RefreshInSeconds);

public sealed record VerifyQrRequest(string QrPayload);

public sealed record VerifyQrResponse(
    string StoreCode,
    string StoreName,
    DateTimeOffset ServerTimeUtc,
    List<EligibleShiftDto> EligibleShifts,
    string? RecommendedAction,
    string Message);

public sealed record EligibleShiftDto(
    long ShiftAssignmentId,
    long ShiftId,
    string ShiftName,
    string ShiftDate,
    string StartTime,
    string EndTime,
    bool IsOvernight,
    string Status,
    DateTimeOffset? CheckInAt,
    DateTimeOffset? CheckOutAt,
    decimal? ActualHours,
    string AllowedAction,
    bool CanPerformAction,
    string ActionHint);

public sealed record SubmitAttendanceRequest(
    long ShiftAssignmentId,
    string Action,
    string QrPayload);

public sealed record SubmitAttendanceResponse(
    bool Success,
    string Message,
    long ShiftAssignmentId,
    string ShiftName,
    string Action,
    DateTimeOffset TimestampUtc,
    decimal? ActualHours);

public sealed record EmployeeShiftRecordDto(
    long AttendanceId,
    long ShiftAssignmentId,
    string ShiftName,
    string ShiftDate,
    DateTimeOffset CheckInAt,
    DateTimeOffset? CheckOutAt,
    decimal? ActualHours,
    string Status);
