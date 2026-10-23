namespace manage365.Routes.API.Reporting;

public sealed record DashboardSummaryDto(
    DateOnly Date,
    int Scheduled,
    int CheckedIn,
    int Completed,
    int Absent,
    int PendingLeaveRequests,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<DashboardAttendanceItemDto> RecentAttendance);

public sealed record DashboardAttendanceItemDto(
    long AttendanceId,
    long EmployeeId,
    string EmployeeName,
    string ShiftName,
    DateTimeOffset CheckInAt,
    DateTimeOffset? CheckOutAt,
    string Status);

public sealed record AttendanceStatisticsResponse(
    DateOnly FromDate,
    DateOnly ToDate,
    string GroupBy,
    IReadOnlyList<AttendanceStatisticsBucketDto> Buckets);

public sealed record AttendanceStatisticsBucketDto(
    DateOnly Date,
    int Scheduled,
    int CheckedIn,
    int Completed,
    int Absent);
