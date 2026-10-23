using manage365.Repositories.Reporting;
using manage365.Routes.API.Reporting;
using Microsoft.AspNetCore.Mvc;
using manage365.Routes.API.Attendance;
using Microsoft.Extensions.Options;
using Xunit;

namespace manage365.Tests;

public sealed class ReportingApiTests
{
    [Fact]
    public async Task GetAttendanceStatistics_ReturnsBadRequest_WhenRangeExceeds366Days()
    {
        var controller = new StatisticsController(new FakeReportingRepository());

        var result = await controller.GetAttendance(
            new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 2), "day", null, null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAttendanceStatistics_ReturnsBadRequest_WhenGroupByIsUnsupported()
    {
        var controller = new StatisticsController(new FakeReportingRepository());

        var result = await controller.GetAttendance(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), "year", null, null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetDashboardSummary_ClampsRecentItemsLimit()
    {
        var repository = new FakeReportingRepository();
        var controller = new DashboardController(repository, Options.Create(new AttendancePolicyOptions { TimeZone = "Asia/Ho_Chi_Minh" }));

        await controller.GetSummary(new DateOnly(2026, 1, 1), 500, CancellationToken.None);

        Assert.Equal(100, repository.LastRecentLimit);
    }

    private sealed class FakeReportingRepository : IReportingRepository
    {
        public int LastRecentLimit { get; private set; }

        public Task<DashboardSummaryDto> GetDashboardSummaryAsync(DateOnly date, int recentLimit, CancellationToken cancellationToken = default)
        {
            LastRecentLimit = recentLimit;
            return Task.FromResult(new DashboardSummaryDto(date, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow, []));
        }

        public Task<IReadOnlyList<AttendanceStatisticsBucketDto>> GetAttendanceStatisticsAsync(
            DateOnly fromDate, DateOnly toDate, string groupBy, long? employeeId, long? departmentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AttendanceStatisticsBucketDto>>([]);
    }
}
