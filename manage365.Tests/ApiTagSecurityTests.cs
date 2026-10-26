using System.Reflection;
using manage365.Routes.API.Attendance;
using manage365.Routes.API.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace manage365.Tests;

public sealed class ApiTagSecurityTests
{
    [Fact]
    public void AttendanceLocationRoutes_RequireManagerOrAdmin()
    {
        var controllerAuthorization = typeof(AttendanceLocationAdminController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(controllerAuthorization);
        Assert.Equal("Admin,Manager", controllerAuthorization.Roles);
    }

    [Fact]
    public void KioskQrRoute_RequiresManagerOrAdmin()
    {
        var authorization = typeof(AttendanceKioskController)
            .GetMethod(nameof(AttendanceKioskController.GetKioskQr))!
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.Equal("Admin,Manager", authorization.Roles);
    }

    [Fact]
    public void AdminAttendanceSnapshot_RequiresManagerOrAdmin()
    {
        var authorization = typeof(AttendanceShiftController)
            .GetMethod(nameof(AttendanceShiftController.GetAdminSnapshot))!
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.Equal("Admin,Manager", authorization.Roles);
    }

    [Fact]
    public void AdminAttendanceHistory_RequiresManagerOrAdmin()
    {
        var authorization = typeof(AttendanceShiftController)
            .GetMethod(nameof(AttendanceShiftController.GetAdminHistory))!
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.Equal("Admin,Manager", authorization.Roles);
    }

    [Fact]
    public async Task AdminAttendanceHistory_RejectsInvalidDateRange()
    {
        var controller = new AttendanceShiftController(null!, null!, null!);

        var result = await controller.GetAdminHistory(
            new DateOnly(2026, 10, 26),
            new DateOnly(2026, 10, 25),
            cancellationToken: CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void ReportingRoutes_RequireManagerOrAdmin()
    {
        Assert.Equal("Admin,Manager", typeof(DashboardController).GetCustomAttribute<AuthorizeAttribute>()?.Roles);
        Assert.Equal("Admin,Manager", typeof(StatisticsController).GetCustomAttribute<AuthorizeAttribute>()?.Roles);
    }
}
