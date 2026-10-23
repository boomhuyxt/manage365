using manage365.Repositories.Reporting;
using manage365.Routes.API.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace manage365.Routes.API.Reporting;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Roles = AppRoles.Managers)]
[Tags("Statistics")]
[Route("api/statistics")]
[EnableRateLimiting("reporting")]
public sealed class StatisticsController(IReportingRepository repository) : ControllerBase
{
    private static readonly HashSet<string> SupportedGroups = new(StringComparer.OrdinalIgnoreCase) { "day", "week", "month" };

    [HttpGet("attendance")]
    [ProducesResponseType<AttendanceStatisticsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AttendanceStatisticsResponse>> GetAttendance(
        [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate, [FromQuery] string groupBy = "day",
        [FromQuery] long? employeeId = null, [FromQuery] long? departmentId = null,
        CancellationToken cancellationToken = default)
    {
        if (fromDate == DateOnly.MinValue || toDate == DateOnly.MinValue || fromDate > toDate ||
            toDate.DayNumber - fromDate.DayNumber >= 366 || !SupportedGroups.Contains(groupBy) ||
            employeeId is <= 0 || departmentId is <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Khoảng ngày, cách nhóm hoặc mã nhân viên không hợp lệ.",
                Extensions = { ["code"] = "statistics_query_invalid" }
            });
        }

        var normalizedGroup = groupBy.ToLowerInvariant();
        var buckets = await repository.GetAttendanceStatisticsAsync(
            fromDate, toDate, normalizedGroup, employeeId, departmentId, cancellationToken);
        return Ok(new AttendanceStatisticsResponse(fromDate, toDate, normalizedGroup, buckets));
    }
}
