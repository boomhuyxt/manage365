using System.ComponentModel.DataAnnotations;

namespace manage365.Routes.API.Attendance;

public sealed class CreateAttendanceSessionRequest
{
    [Required, MinLength(2), MaxLength(150)]
    public string Title { get; init; } = string.Empty;

    [Required]
    public DateTimeOffset? StartsAtUtc { get; init; }

    [Required]
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

public sealed class UpdateAttendanceSessionRequest
{
    [Required]
    public string Status { get; init; } = string.Empty;
}

public sealed class CreateCheckInRequest
{
    [Required, MinLength(20), MaxLength(500)]
    public string QrPayload { get; init; } = string.Empty;
}

public sealed record AttendanceSessionResponse(
    Guid Id,
    string Title,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Status,
    long CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClosedAtUtc);

public sealed record AttendanceSessionCreatedResponse(
    AttendanceSessionResponse Session,
    string QrPayload);

public sealed record AttendanceRecordResponse(
    Guid Id,
    Guid SessionId,
    long UserId,
    DateTimeOffset CheckedInAtUtc);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Page,
    int PageSize);
