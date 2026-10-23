using manage365.Routes.API.Reporting;

namespace manage365.Repositories.Reporting;

public interface IReportingRepository
{
    Task<DashboardSummaryDto> GetDashboardSummaryAsync(DateOnly date, int recentLimit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AttendanceStatisticsBucketDto>> GetAttendanceStatisticsAsync(
        DateOnly fromDate, DateOnly toDate, string groupBy, long? employeeId, long? departmentId,
        CancellationToken cancellationToken = default);
}
