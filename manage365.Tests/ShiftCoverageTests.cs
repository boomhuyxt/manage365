using manage365.Routes.API.Shifts;
using manage365.Repositories.Shifts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace manage365.Tests;

public sealed class ShiftCoverageTests
{
    private static ShiftAssignmentDto Existing(long id, long employee, string start, string end,
        string date = "2026-10-19", bool overnight = false, string status = "Approved")
        => new(id, id, employee, $"Employee {employee}", $"NV-{employee}", null, null, "Ca", date,
            start, end, overnight, "#ffffff", status, null, null);

    [Fact]
    public void PartialOverlap_CountsBySegment_NotWholeShift()
    {
        var result = ShiftCoverage.Calculate([new(2, "2026-10-19", "08:00", "14:00")],
            [Existing(1, 1, "08:00", "12:00"), Existing(2, 3, "10:00", "14:00")]);
        Assert.Equal(3, result.Segments.Count);
        Assert.Equal([1, 2, 1], result.Segments.Select(s => s.CurrentEmployeeIds.Count));
        Assert.Equal([2, 3, 2], result.Segments.Select(s => s.ProjectedEmployeeIds.Count));
        Assert.EndsWith("T10:00:00", result.Segments[0].End);
        Assert.EndsWith("T12:00:00", result.Segments[1].End);
    }

    [Fact]
    public void SplitShift_DoesNotCountRest_AndAdjacentBoundariesDoNotOverlap()
    {
        var result = ShiftCoverage.Calculate([
            new(1, "2026-10-19", "08:00", "12:00"), new(1, "2026-10-19", "17:00", "21:00")],
            [Existing(2, 2, "12:00", "17:00")]);
        Assert.Equal(2, result.Segments.Count);
        Assert.All(result.Segments, s => { Assert.Empty(s.CurrentEmployeeIds); Assert.Equal([1L], s.ProjectedEmployeeIds); });
    }

    [Fact]
    public void Overnight_UsesPreviousDay_AndDistinctEmployees()
    {
        var result = ShiftCoverage.Calculate([new(2, "2026-10-20", "05:00", "08:00")], [
            Existing(1, 1, "22:00", "06:00", overnight: true),
            Existing(2, 1, "23:00", "06:00", overnight: true),
            Existing(3, 3, "05:00", "08:00", date: "2026-10-20", status: "Cancelled")]);
        Assert.Equal(2, result.Segments.Count);
        Assert.Equal([1L], result.Segments[0].CurrentEmployeeIds);
        Assert.Equal([1L, 2L], result.Segments[0].ProjectedEmployeeIds);
        Assert.Equal([2L], result.Segments[1].ProjectedEmployeeIds);
    }

    [Fact]
    public void Editing_ReplacesOldIntervalWithoutDoubleCounting()
    {
        var result = ShiftCoverage.Calculate([new(1, "2026-10-19", "10:00", "14:00", ExcludeAssignmentId: 7)],
            [Existing(7, 1, "08:00", "12:00"), Existing(8, 2, "10:00", "14:00")]);
        Assert.All(result.Segments, s => Assert.Equal([1L, 2L], s.ProjectedEmployeeIds));
        Assert.Equal([1L, 2L], result.Segments[0].CurrentEmployeeIds);
        Assert.Equal([2L], result.Segments[1].CurrentEmployeeIds);
    }

    [Fact]
    public async Task CoverageEndpoint_LoadsPreviousDay_AndAllowsMultipleEmployees()
    {
        var repository = new FakeShiftSchedulingRepository();
        await repository.AssignShiftAsync(new(101, "2026-10-19", CustomStartTime: "22:00", CustomEndTime: "06:00", IsOvernight: true));
        var controller = new ShiftAssignmentsController(repository);
        var response = await controller.GetCoverage(new([new(102, "2026-10-20", "05:00", "08:00")]));
        var coverage = Assert.IsType<ShiftCoverageResponse>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Contains(101L, coverage.Segments[0].CurrentEmployeeIds);
        Assert.Contains(102L, coverage.Segments[0].ProjectedEmployeeIds);
        var attribute = Assert.Single(typeof(ShiftAssignmentsController).GetMethod(nameof(ShiftAssignmentsController.GetCoverage))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>());
        Assert.Equal("Admin,Manager", attribute.Roles);
    }

    [Fact]
    public async Task CoverageEndpoint_RejectsInvalidInputAndUnrelatedEdit()
    {
        var controller = new ShiftAssignmentsController(new FakeShiftSchedulingRepository());
        await Assert.ThrowsAsync<ArgumentException>(() => controller.GetCoverage(new([])));
        await Assert.ThrowsAsync<ArgumentException>(() => controller.GetCoverage(new([new(1, "2026-10-19", "25:00", "12:00")])));
        await Assert.ThrowsAsync<ArgumentException>(() => controller.GetCoverage(new([new(1, "2026-10-19", "08:00", "12:00", ExcludeAssignmentId: 999)])));
        await Assert.ThrowsAsync<ArgumentException>(() => controller.GetCoverage(new([new(1, "2026-10-19", "08:00", "12:00"), new(1, "2026-12-19", "08:00", "12:00")])));
    }
}
