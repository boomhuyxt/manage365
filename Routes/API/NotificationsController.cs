using System.IdentityModel.Tokens.Jwt;
using manage365.Repositories.Notifications;
using manage365.Routes.API.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace manage365.Routes.API;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize]
[Tags("Notification")]
[Route("api/notifications")]
public sealed class NotificationsController(INotificationRepository repository) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedNotificationsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedNotificationsResponse>> Get(
        [FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetEmployeeId(out var employeeId)) return Unauthorized();
        return Ok(await repository.GetAsync(employeeId, unreadOnly, Math.Max(1, page), Math.Clamp(pageSize, 1, 100), cancellationToken));
    }

    [HttpGet("unread-count")]
    [ProducesResponseType<UnreadNotificationCountResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadNotificationCountResponse>> GetUnreadCount(CancellationToken cancellationToken)
    {
        if (!TryGetEmployeeId(out var employeeId)) return Unauthorized();
        return Ok(new UnreadNotificationCountResponse(await repository.GetUnreadCountAsync(employeeId, cancellationToken)));
    }

    [HttpPatch("{id:long}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(long id, CancellationToken cancellationToken)
    {
        if (!TryGetEmployeeId(out var employeeId)) return Unauthorized();
        return await repository.MarkReadAsync(employeeId, id, DateTimeOffset.UtcNow, cancellationToken)
            ? NoContent()
            : NotFound(new ProblemDetails { Status = 404, Title = "Không tìm thấy thông báo.", Extensions = { ["code"] = "notification_not_found" } });
    }

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        if (!TryGetEmployeeId(out var employeeId)) return Unauthorized();
        await repository.MarkAllReadAsync(employeeId, DateTimeOffset.UtcNow, cancellationToken);
        return NoContent();
    }

    private bool TryGetEmployeeId(out long employeeId) =>
        long.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out employeeId);
}
