using System.Security.Claims;
using manage365.Repositories.Shifts;
using manage365.Routes.API.Shifts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Xunit;

namespace manage365.Tests;

public sealed class ShiftSchedulingRegressionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ShiftAssignmentsController Controller(IShiftSchedulingRepository repository, string role = "Manager", string id = "101") => new(repository)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id), new Claim(ClaimTypes.Role, role)], "Test")) } }
    };

    [Theory]
    [InlineData("not-a-date", "06:00", "14:00")]
    [InlineData("2026-10-15", "not-a-time", "14:00")]
    [InlineData("2026-10-15", "25:00", "14:00")]
    [InlineData("2026-10-15", "06:00", "06:00")]
    [InlineData("15/10/2026", "06:00", "14:00")]
    public async Task InvalidAssignment_Returns400_BeforeDatabaseAccess(string date, string start, string end)
    {
        await using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1");
        var result = await Controller(new PostgresShiftSchedulingRepository(source)).AssignShift(new(101, date, CustomStartTime: start, CustomEndTime: end), Ct);
        Assert.Equal(400, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task InvalidTemplateTime_Returns400()
    {
        var controller = new ShiftTemplatesController(new FakeShiftSchedulingRepository());
        var result = await controller.CreateTemplate(new("BAD", "Bad", "wrong", "14:00"), Ct);
        Assert.Equal(400, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task EmployeeCannotReadOtherEmployeeScheduleOrDetail()
    {
        var repository = new FakeShiftSchedulingRepository();
        var assignment = await repository.AssignShiftAsync(new(202, "2026-10-15", ShiftTemplateId: 1), Ct);
        var controller = Controller(repository, "Employee");
        Assert.IsType<ForbidResult>((await controller.GetSchedule("2026-10-12", "2026-10-18", employeeId: 202, cancellationToken: Ct)).Result);
        Assert.IsType<ForbidResult>((await controller.GetAssignmentById(assignment.AssignmentId, Ct)).Result);
        var result = await controller.GetSchedule("2026-10-12", "2026-10-18", cancellationToken: Ct);
        Assert.Empty(Assert.IsType<WeeklyScheduleResponse>(Assert.IsType<OkObjectResult>(result.Result).Value).Assignments);
    }

    [Theory]
    [InlineData("2026-10-12", "2026-10-12")]
    [InlineData("2026-10-13", "2026-10-19")]
    [InlineData("wrong", "2026-10-19")]
    public async Task InvalidCopyWeek_Returns400(string source, string target)
    {
        var result = await Controller(new FakeShiftSchedulingRepository()).CopyWeeklySchedule(new(source, target), Ct);
        Assert.Equal(400, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task OversizedScheduleRange_Returns400()
    {
        var result = await Controller(new FakeShiftSchedulingRepository()).GetSchedule("2026-01-01", "2026-12-31", cancellationToken: Ct);
        Assert.Equal(400, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }
}
