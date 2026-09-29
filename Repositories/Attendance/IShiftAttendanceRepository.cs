using manage365.Routes.API.Attendance;

namespace manage365.Repositories.Attendance;

public interface IShiftAttendanceRepository
{
    Task<List<EligibleShiftDto>> GetEligibleShiftsAsync(
        long employeeId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<SubmitAttendanceResult> SubmitAttendanceAsync(
        long? shiftAssignmentId,
        long employeeId,
        string action,
        ValidatedAttendanceLocation location,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<List<EmployeeShiftRecordDto>> GetAttendanceHistoryAsync(
        long employeeId,
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<AdminAttendanceSnapshotDto> GetAdminSnapshotAsync(
        DateTimeOffset nowUtc,
        int limit = 50,
        CancellationToken cancellationToken = default);
}

public sealed record SubmitAttendanceResult(
    bool Success,
    string Message,
    string? ShiftName,
    DateTimeOffset TimestampUtc,
    decimal? ActualHours,
    string? ErrorCode);

public sealed record ValidatedAttendanceLocation(
    long StoreLocationId,
    string StoreCode,
    double Latitude,
    double Longitude,
    double AccuracyMeters,
    double DistanceMeters,
    double AllowedRadiusMeters,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset ReceivedAtUtc,
    bool IsMocked,
    bool IsWithinGeofence)
{
    public AttendanceLocationDto ToDto() => new(
        StoreCode,
        Latitude,
        Longitude,
        AccuracyMeters,
        DistanceMeters,
        AllowedRadiusMeters,
        CapturedAtUtc,
        IsMocked,
        IsWithinGeofence);
}
