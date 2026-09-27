using manage365.Routes.API.Attendance;

namespace manage365.Repositories.Attendance;

public interface IShiftAttendanceRepository
{
    Task<List<EligibleShiftDto>> GetEligibleShiftsAsync(
        long employeeId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<SubmitAttendanceResult> SubmitAttendanceAsync(
        long shiftAssignmentId,
        long employeeId,
        string action,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<List<EmployeeShiftRecordDto>> GetAttendanceHistoryAsync(
        long employeeId,
        int limit = 20,
        CancellationToken cancellationToken = default);
}

public sealed record SubmitAttendanceResult(
    bool Success,
    string Message,
    string? ShiftName,
    DateTimeOffset TimestampUtc,
    decimal? ActualHours,
    string? ErrorCode);
