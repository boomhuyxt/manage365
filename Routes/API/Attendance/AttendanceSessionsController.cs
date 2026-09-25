using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using manage365.Repositories.Attendance;
using manage365.Routes.API.Auth;

namespace manage365.Routes.API.Attendance;

[ApiController]
[Authorize(Roles = AppRoles.Managers)]
[Route("api/attendance-sessions")]
public sealed class AttendanceSessionsController(
    IAttendanceRepository repository,
    IQrTokenService qrTokenService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<AttendanceSessionCreatedResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AttendanceSessionCreatedResponse>> Create(
        CreateAttendanceSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var startsAt = request.StartsAtUtc!.Value;
        var expiresAt = request.ExpiresAtUtc!.Value;
        var now = DateTimeOffset.UtcNow;

        if (expiresAt <= startsAt || expiresAt <= now || expiresAt - startsAt > TimeSpan.FromHours(24))
        {
            return ValidationError(
                nameof(request.ExpiresAtUtc),
                "Expiry must be after the start and current time, with a maximum duration of 24 hours.");
        }

        var qrToken = qrTokenService.Create();
        var session = await repository.AddSessionAsync(new AttendanceSession(
            Guid.NewGuid(),
            request.Title.Trim(),
            startsAt,
            expiresAt,
            qrToken.Hash,
            userId,
            now,
            null), cancellationToken);
        var response = new AttendanceSessionCreatedResponse(session.ToResponse(now), qrToken.Payload);

        return CreatedAtAction(nameof(GetById), new { sessionId = session.Id }, response);
    }

    [HttpGet("{sessionId:guid}")]
    [ProducesResponseType<AttendanceSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AttendanceSessionResponse>> GetById(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await repository.FindSessionAsync(sessionId, cancellationToken);
        return session is null
            ? NotFoundProblem("attendance_session_not_found", "Attendance session was not found.")
            : Ok(session.ToResponse(DateTimeOffset.UtcNow));
    }

    [HttpPatch("{sessionId:guid}")]
    [ProducesResponseType<AttendanceSessionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AttendanceSessionResponse>> Update(
        Guid sessionId,
        UpdateAttendanceSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Status, "closed", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationError(
                nameof(request.Status),
                "The only supported status transition is 'closed'.");
        }

        var now = DateTimeOffset.UtcNow;
        var session = await repository.CloseSessionAsync(sessionId, now, cancellationToken);
        return session is null
            ? NotFoundProblem("attendance_session_not_found", "Attendance session was not found.")
            : Ok(session.ToResponse(now));
    }

    [HttpGet("{sessionId:guid}/records")]
    [ProducesResponseType<PagedResponse<AttendanceRecordResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<AttendanceRecordResponse>>> GetRecords(
        Guid sessionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (await repository.FindSessionAsync(sessionId, cancellationToken) is null)
        {
            return NotFoundProblem("attendance_session_not_found", "Attendance session was not found.");
        }

        NormalizePage(ref page, ref pageSize);
        var result = await repository.GetRecordsBySessionAsync(
            sessionId,
            page,
            pageSize,
            cancellationToken);
        return Ok(new PagedResponse<AttendanceRecordResponse>(
            result.Items.Select(item => item.ToResponse()).ToArray(),
            result.Total,
            result.Page,
            result.PageSize));
    }

    private bool TryGetUserId(out long userId) =>
        long.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId);

    private ActionResult NotFoundProblem(string code, string title)
    {
        var problem = new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = title };
        problem.Extensions["code"] = code;
        return NotFound(problem);
    }

    private ActionResult ValidationError(string field, string message)
    {
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            [field] = [message]
        })
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "One or more validation errors occurred."
        };
        problem.Extensions["code"] = "validation_error";
        return UnprocessableEntity(problem);
    }

    private static void NormalizePage(ref int page, ref int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
    }
}
