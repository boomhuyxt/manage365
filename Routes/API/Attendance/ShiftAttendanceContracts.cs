namespace manage365.Routes.API.Attendance;

public sealed class AttendancePolicyOptions
{
    public const string SectionName = "AttendancePolicy";

    public string TimeZone { get; set; } = "Asia/Ho_Chi_Minh";
    public int QrValiditySeconds { get; set; } = 35;
    public int EarlyCheckInMinutes { get; set; } = 45;
    public int LateCheckOutMinutes { get; set; } = 90;
    public double MaxLocationAccuracyMeters { get; set; } = 30;
    public int MaxLocationAgeSeconds { get; set; } = 60;
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
    long? ShiftAssignmentId,
    long? ShiftId,
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
    long? ShiftAssignmentId,
    string Action,
    string QrPayload,
    AttendanceLocationRequest? Location);

public sealed record AttendanceLocationRequest(
    double Latitude,
    double Longitude,
    double AccuracyMeters,
    DateTimeOffset CapturedAtUtc,
    bool IsMocked);

public sealed record SubmitAttendanceResponse(
    bool Success,
    string Message,
    long? ShiftAssignmentId,
    string ShiftName,
    string Action,
    DateTimeOffset TimestampUtc,
    decimal? ActualHours,
    AttendanceLocationDto Location);

public sealed record EmployeeShiftRecordDto(
    long AttendanceId,
    long? ShiftAssignmentId,
    string ShiftName,
    string ShiftDate,
    DateTimeOffset CheckInAt,
    DateTimeOffset? CheckOutAt,
    decimal? ActualHours,
    string Status);

public sealed record AttendanceLocationDto(
    string StoreCode,
    double Latitude,
    double Longitude,
    double AccuracyMeters,
    double DistanceMeters,
    double AllowedRadiusMeters,
    DateTimeOffset CapturedAtUtc,
    bool IsMocked,
    bool IsWithinGeofence);

public sealed record StoreGeofenceDto(
    long Id,
    string StoreCode,
    string StoreName,
    double Latitude,
    double Longitude,
    double AllowedRadiusMeters,
    double? ConfiguredAccuracyMeters,
    DateTimeOffset UpdatedAtUtc,
    long? ConfiguredByEmployeeId);

public sealed record UpdateStoreGeofenceRequest(
    string StoreName,
    double Latitude,
    double Longitude,
    double AccuracyMeters,
    double AllowedRadiusMeters);

public sealed record AdminAttendanceSnapshotDto(
    DateTimeOffset ServerTimeUtc,
    int TotalToday,
    int CheckedIn,
    int Completed,
    List<AdminAttendanceRecordDto> Records);

public sealed record AdminAttendanceRecordDto(
    long AttendanceId,
    long EmployeeId,
    string EmployeeName,
    string? EmployeeEmail,
    string ShiftName,
    DateTimeOffset CheckInAt,
    DateTimeOffset? CheckOutAt,
    decimal? ActualHours,
    string Status,
    AttendanceLocationDto? CheckInLocation,
    AttendanceLocationDto? CheckOutLocation);
