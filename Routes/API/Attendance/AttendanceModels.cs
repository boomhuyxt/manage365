namespace manage365.Routes.API.Attendance;

public sealed record AttendanceSession(
    Guid Id,
    string Title,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string QrTokenHash,
    long CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClosedAtUtc);

public sealed record AttendanceRecord(
    Guid Id,
    Guid SessionId,
    long UserId,
    DateTimeOffset CheckedInAtUtc);

public enum CheckInStatus
{
    Success,
    InvalidQr,
    NotStarted,
    Expired,
    Closed,
    Duplicate
}

public sealed record CheckInResult(CheckInStatus Status, AttendanceRecord? Record = null);

public sealed record PageResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
