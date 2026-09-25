using System.Collections.Concurrent;
using manage365.Routes.API.Attendance;

namespace manage365.Repositories.Attendance;

public interface IAttendanceRepository
{
    ValueTask<AttendanceSession> AddSessionAsync(
        AttendanceSession session,
        CancellationToken cancellationToken = default);
    ValueTask<AttendanceSession?> FindSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
    ValueTask<AttendanceSession?> CloseSessionAsync(
        Guid sessionId,
        DateTimeOffset closedAtUtc,
        CancellationToken cancellationToken = default);
    ValueTask<CheckInResult> TryCheckInAsync(
        string qrTokenHash,
        long userId,
        DateTimeOffset checkedInAtUtc,
        CancellationToken cancellationToken = default);
    ValueTask<PageResult<AttendanceRecord>> GetRecordsByUserAsync(
        long userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
    ValueTask<PageResult<AttendanceRecord>> GetRecordsBySessionAsync(
        Guid sessionId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryAttendanceRepository : IAttendanceRepository
{
    private readonly ConcurrentDictionary<Guid, AttendanceSession> _sessions = new();
    private readonly ConcurrentDictionary<string, Guid> _sessionIdsByQrHash =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(Guid SessionId, long UserId), AttendanceRecord> _records = new();
    private readonly object _writeLock = new();

    public ValueTask<AttendanceSession> AddSessionAsync(
        AttendanceSession session,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_writeLock)
        {
            if (!_sessions.TryAdd(session.Id, session) ||
                !_sessionIdsByQrHash.TryAdd(session.QrTokenHash, session.Id))
            {
                throw new InvalidOperationException("Attendance session could not be created.");
            }
        }

        return ValueTask.FromResult(session);
    }

    public ValueTask<AttendanceSession?> FindSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_sessions.GetValueOrDefault(sessionId));
    }

    public ValueTask<AttendanceSession?> CloseSessionAsync(
        Guid sessionId,
        DateTimeOffset closedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_writeLock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return ValueTask.FromResult<AttendanceSession?>(null);
            }

            if (session.ClosedAtUtc is not null)
            {
                return ValueTask.FromResult<AttendanceSession?>(session);
            }

            var closedSession = session with { ClosedAtUtc = closedAtUtc };
            _sessions[sessionId] = closedSession;
            return ValueTask.FromResult<AttendanceSession?>(closedSession);
        }
    }

    public ValueTask<CheckInResult> TryCheckInAsync(
        string qrTokenHash,
        long userId,
        DateTimeOffset checkedInAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_writeLock)
        {
            if (!_sessionIdsByQrHash.TryGetValue(qrTokenHash, out var sessionId) ||
                !_sessions.TryGetValue(sessionId, out var session))
            {
                return ValueTask.FromResult(new CheckInResult(CheckInStatus.InvalidQr));
            }

            if (session.ClosedAtUtc is not null)
            {
                return ValueTask.FromResult(new CheckInResult(CheckInStatus.Closed));
            }

            if (checkedInAtUtc < session.StartsAtUtc)
            {
                return ValueTask.FromResult(new CheckInResult(CheckInStatus.NotStarted));
            }

            if (checkedInAtUtc > session.ExpiresAtUtc)
            {
                return ValueTask.FromResult(new CheckInResult(CheckInStatus.Expired));
            }

            var key = (sessionId, userId);
            if (_records.ContainsKey(key))
            {
                return ValueTask.FromResult(new CheckInResult(CheckInStatus.Duplicate));
            }

            var record = new AttendanceRecord(Guid.NewGuid(), sessionId, userId, checkedInAtUtc);
            return ValueTask.FromResult(_records.TryAdd(key, record)
                ? new CheckInResult(CheckInStatus.Success, record)
                : new CheckInResult(CheckInStatus.Duplicate));
        }
    }

    public ValueTask<PageResult<AttendanceRecord>> GetRecordsByUserAsync(
        long userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            CreatePage(_records.Values.Where(record => record.UserId == userId), page, pageSize));
    }

    public ValueTask<PageResult<AttendanceRecord>> GetRecordsBySessionAsync(
        Guid sessionId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            CreatePage(_records.Values.Where(record => record.SessionId == sessionId), page, pageSize));
    }

    private static PageResult<AttendanceRecord> CreatePage(
        IEnumerable<AttendanceRecord> source,
        int page,
        int pageSize)
    {
        var ordered = source.OrderByDescending(record => record.CheckedInAtUtc).ToArray();
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return new PageResult<AttendanceRecord>(items, ordered.Length, page, pageSize);
    }
}
