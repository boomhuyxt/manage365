using manage365.Routes.API.Notifications;
using Npgsql;
using System.Text.Json;

namespace manage365.Repositories.Notifications;

public sealed class PostgresNotificationRepository(NpgsqlDataSource dataSource) : INotificationRepository
{
    public async Task EnsureTablesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS public.notifications (
                id BIGSERIAL PRIMARY KEY,
                employee_id BIGINT NOT NULL REFERENCES public.nhan_vien(id) ON DELETE CASCADE,
                type VARCHAR(80) NOT NULL,
                title VARCHAR(200) NOT NULL,
                message VARCHAR(1000) NOT NULL,
                data JSONB NULL,
                idempotency_key VARCHAR(160) NOT NULL UNIQUE,
                created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                read_at_utc TIMESTAMPTZ NULL
            );
            CREATE INDEX IF NOT EXISTS ix_notifications_employee_created
                ON public.notifications(employee_id, created_at_utc DESC, id DESC);
            CREATE INDEX IF NOT EXISTS ix_notifications_employee_unread
                ON public.notifications(employee_id, created_at_utc DESC) WHERE read_at_utc IS NULL;
            """;
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PagedNotificationsResponse> GetAsync(long employeeId, bool unreadOnly, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;
        const string countSql = """
            SELECT COUNT(*) FILTER (WHERE NOT @unreadOnly OR read_at_utc IS NULL)::int,
                   COUNT(*) FILTER (WHERE read_at_utc IS NULL)::int
            FROM public.notifications WHERE employee_id = @employeeId;
            """;
        int total;
        int unread;
        await using (var count = dataSource.CreateCommand(countSql))
        {
            count.CommandTimeout = 15;
            count.Parameters.AddWithValue("employeeId", employeeId);
            count.Parameters.AddWithValue("unreadOnly", unreadOnly);
            await using var reader = await count.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            total = reader.GetInt32(0);
            unread = reader.GetInt32(1);
        }

        const string listSql = """
            SELECT id, type, title, message, created_at_utc, read_at_utc, data::text
            FROM public.notifications
            WHERE employee_id = @employeeId AND (NOT @unreadOnly OR read_at_utc IS NULL)
            ORDER BY created_at_utc DESC, id DESC
            LIMIT @limit OFFSET @offset;
            """;
        var items = new List<NotificationDto>();
        await using (var list = dataSource.CreateCommand(listSql))
        {
            list.CommandTimeout = 15;
            list.Parameters.AddWithValue("employeeId", employeeId);
            list.Parameters.AddWithValue("unreadOnly", unreadOnly);
            list.Parameters.AddWithValue("limit", pageSize);
            list.Parameters.AddWithValue("offset", offset);
            await using var reader = await list.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new NotificationDto(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                    reader.IsDBNull(6) ? null : JsonDocument.Parse(reader.GetString(6)).RootElement.Clone()));
            }
        }
        return new PagedNotificationsResponse(items, page, pageSize, total, unread);
    }

    public async Task<int> GetUnreadCountAsync(long employeeId, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("SELECT COUNT(*)::int FROM public.notifications WHERE employee_id = @employeeId AND read_at_utc IS NULL;");
        command.Parameters.AddWithValue("employeeId", employeeId);
        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    public async Task<bool> MarkReadAsync(long employeeId, long notificationId, DateTimeOffset readAtUtc, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("UPDATE public.notifications SET read_at_utc = COALESCE(read_at_utc, @readAt) WHERE id = @id AND employee_id = @employeeId;");
        command.Parameters.AddWithValue("readAt", readAtUtc);
        command.Parameters.AddWithValue("id", notificationId);
        command.Parameters.AddWithValue("employeeId", employeeId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<int> MarkAllReadAsync(long employeeId, DateTimeOffset readAtUtc, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("UPDATE public.notifications SET read_at_utc = @readAt WHERE employee_id = @employeeId AND read_at_utc IS NULL;");
        command.Parameters.AddWithValue("readAt", readAtUtc);
        command.Parameters.AddWithValue("employeeId", employeeId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
