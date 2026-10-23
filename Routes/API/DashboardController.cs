using manage365.Repositories.Reporting;
using manage365.Routes.API.Auth;
using manage365.Routes.API.Attendance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;

namespace manage365.Routes.API.Reporting;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Roles = AppRoles.Managers)]
[Tags("Dashboard")]
[Route("api/dashboard")]
[EnableRateLimiting("reporting")]
public sealed class DashboardController(
    IReportingRepository repository,
    IOptions<AttendancePolicyOptions> attendanceOptions) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(
        [FromQuery] DateOnly? date, [FromQuery] int recentLimit = 20, CancellationToken cancellationToken = default)
    {
        var today = GetBusinessDate(attendanceOptions.Value.TimeZone);
        var requestedDate = date ?? today;
        if (requestedDate < today.AddYears(-5) || requestedDate > today.AddDays(1))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Ngày yêu cầu nằm ngoài phạm vi cho phép.",
                Extensions = { ["code"] = "date_out_of_range" }
            });
        }

        return Ok(await repository.GetDashboardSummaryAsync(requestedDate, Math.Clamp(recentLimit, 1, 100), cancellationToken));
    }

    private static DateOnly GetBusinessDate(string timeZoneId)
    {
        try
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime);
        }
        catch (TimeZoneNotFoundException)
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")).DateTime);
        }
    }
}
