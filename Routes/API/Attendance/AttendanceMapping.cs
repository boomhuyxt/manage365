namespace manage365.Routes.API.Attendance;

public static class AttendanceMapping
{
    public static AttendanceSessionResponse ToResponse(
        this AttendanceSession session,
        DateTimeOffset now)
    {
        var status = session.ClosedAtUtc is not null
            ? "closed"
            : now < session.StartsAtUtc
                ? "scheduled"
                : now > session.ExpiresAtUtc
                    ? "expired"
                    : "open";

        return new AttendanceSessionResponse(
            session.Id,
            session.Title,
            session.StartsAtUtc,
            session.ExpiresAtUtc,
            status,
            session.CreatedByUserId,
            session.CreatedAtUtc,
            session.ClosedAtUtc);
    }

    public static AttendanceRecordResponse ToResponse(this AttendanceRecord record) =>
        new(record.Id, record.SessionId, record.UserId, record.CheckedInAtUtc);
}
