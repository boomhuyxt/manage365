using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using manage365.Repositories.Attendance;
using manage365.Routes.API.Auth;

namespace manage365.Routes.API.Attendance;

[ApiController]
[Authorize(Roles = AppRoles.Employee)]
[Route("api/attendance-records")]
public sealed class AttendanceRecordsController(IAttendanceRepository repository) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<PagedResponse<AttendanceRecordResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<AttendanceRecordResponse>>> GetMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId))
        {
            return Unauthorized();
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var result = await repository.GetRecordsByUserAsync(
            userId,
            page,
            pageSize,
            cancellationToken);

        return Ok(new PagedResponse<AttendanceRecordResponse>(
            result.Items.Select(item => item.ToResponse()).ToArray(),
            result.Total,
            result.Page,
            result.PageSize));
    }
}
