using manage365.Routes.API.Reporting;
using manage365.Routes.API.Attendance;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace manage365.Repositories.Reporting;

public sealed class PostgresReportingRepository(
    NpgsqlDataSource dataSource,
    IOptions<AttendancePolicyOptions> attendanceOptions) : IReportingRepository
{
    private readonly string timeZoneId = attendanceOptions.Value.TimeZone;

    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(DateOnly date, int recentLimit, CancellationToken cancellationToken = default)
    {
        const string totalsSql = """
            SELECT
                (SELECT COUNT(*)::int FROM public.dang_ky_ca dkc
                 JOIN public.ca_lam cl ON cl.id = dkc.id_ca_lam
                 WHERE cl.ngay_lam = @date AND COALESCE(dkc.trang_thai, 'Approved') <> 'Cancelled'),
                (SELECT COUNT(*) FILTER (WHERE gio_ra IS NULL)::int FROM public.cham_cong WHERE ngay_cham_cong = @date),
                (SELECT COUNT(*) FILTER (WHERE gio_ra IS NOT NULL)::int FROM public.cham_cong WHERE ngay_cham_cong = @date),
                (SELECT COUNT(*)::int FROM public.don_nghi_phep WHERE trang_thai = 'Pending'),
                (SELECT COUNT(*)::int
                 FROM public.dang_ky_ca dkc JOIN public.ca_lam cl ON cl.id = dkc.id_ca_lam
                 WHERE cl.ngay_lam = @date
                   AND COALESCE(dkc.trang_thai, 'Approved') <> 'Cancelled'
                   AND cl.ngay_lam + cl.gio_ket_thuc + CASE WHEN cl.qua_dem THEN INTERVAL '1 day' ELSE INTERVAL '0 day' END <= @nowLocal
                   AND NOT EXISTS (SELECT 1 FROM public.cham_cong cc WHERE cc.id_dang_ky_ca = dkc.id)
                   AND NOT EXISTS (
                       SELECT 1 FROM public.don_nghi_phep_ca dnpc
                       JOIN public.don_nghi_phep dnp ON dnp.id = dnpc.id_don_nghi_phep
                       WHERE dnpc.id_dang_ky_ca = dkc.id AND dnp.trang_thai = 'Approved'));
            """;
        int scheduled, checkedIn, completed, pendingLeave, absent;
        await using (var command = dataSource.CreateCommand(totalsSql))
        {
            command.CommandTimeout = 15;
            command.Parameters.AddWithValue("date", date);
            command.Parameters.AddWithValue("nowLocal", GetBusinessLocalNow());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            scheduled = reader.GetInt32(0);
            checkedIn = reader.GetInt32(1);
            completed = reader.GetInt32(2);
            pendingLeave = reader.GetInt32(3);
            absent = reader.GetInt32(4);
        }

        const string recentSql = """
            SELECT cc.id, cc.id_nhan_vien, nv.ho_ten, COALESCE(cl.loai_ca, 'Chấm công chưa xếp ca'),
                   cc.gio_vao, cc.gio_ra, COALESCE(cc.trang_thai, 'CheckedIn')
            FROM public.cham_cong cc
            JOIN public.nhan_vien nv ON nv.id = cc.id_nhan_vien
            LEFT JOIN public.dang_ky_ca dkc ON dkc.id = cc.id_dang_ky_ca
            LEFT JOIN public.ca_lam cl ON cl.id = dkc.id_ca_lam
            WHERE cc.ngay_cham_cong = @date
            ORDER BY cc.gio_vao DESC, cc.id DESC
            LIMIT @limit;
            """;
        var items = new List<DashboardAttendanceItemDto>();
        await using (var command = dataSource.CreateCommand(recentSql))
        {
            command.CommandTimeout = 15;
            command.Parameters.AddWithValue("date", date);
            command.Parameters.AddWithValue("limit", Math.Clamp(recentLimit, 1, 100));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new DashboardAttendanceItemDto(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
                    reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4),
                    reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5), reader.GetString(6)));
            }
        }

        return new DashboardSummaryDto(date, scheduled, checkedIn, completed,
            absent, pendingLeave, DateTimeOffset.UtcNow, items);
    }

    public async Task<IReadOnlyList<AttendanceStatisticsBucketDto>> GetAttendanceStatisticsAsync(
        DateOnly fromDate, DateOnly toDate, string groupBy, long? employeeId, long? departmentId,
        CancellationToken cancellationToken = default)
    {
        var truncation = groupBy switch { "week" => "week", "month" => "month", _ => "day" };
        var sql = $"""
            WITH scheduled AS (
                SELECT date_trunc('{truncation}', cl.ngay_lam::timestamp)::date AS bucket, COUNT(*)::int AS total
                       , COUNT(*) FILTER (WHERE
                           cl.ngay_lam + cl.gio_ket_thuc + CASE WHEN cl.qua_dem THEN INTERVAL '1 day' ELSE INTERVAL '0 day' END <= @nowLocal
                           AND NOT EXISTS (SELECT 1 FROM public.cham_cong cc WHERE cc.id_dang_ky_ca = dkc.id)
                           AND NOT EXISTS (
                               SELECT 1 FROM public.don_nghi_phep_ca dnpc
                               JOIN public.don_nghi_phep dnp ON dnp.id = dnpc.id_don_nghi_phep
                               WHERE dnpc.id_dang_ky_ca = dkc.id AND dnp.trang_thai = 'Approved'))::int AS absent
                FROM public.dang_ky_ca dkc JOIN public.ca_lam cl ON cl.id = dkc.id_ca_lam
                JOIN public.nhan_vien nv ON nv.id = dkc.id_nhan_vien
                WHERE cl.ngay_lam BETWEEN @fromDate AND @toDate
                  AND COALESCE(dkc.trang_thai, 'Approved') <> 'Cancelled'
                  AND (@employeeId IS NULL OR dkc.id_nhan_vien = @employeeId)
                  AND (@departmentId IS NULL OR nv.id_phong_ban = @departmentId)
                GROUP BY 1
            ), attendance AS (
                SELECT date_trunc('{truncation}', cc.ngay_cham_cong::timestamp)::date AS bucket,
                       COUNT(*) FILTER (WHERE cc.id_dang_ky_ca IS NOT NULL)::int AS worked_shifts,
                       COUNT(*) FILTER (WHERE cc.gio_ra IS NULL)::int AS checked_in,
                       COUNT(*) FILTER (WHERE cc.gio_ra IS NOT NULL)::int AS completed
                FROM public.cham_cong cc
                JOIN public.nhan_vien nv ON nv.id = cc.id_nhan_vien
                WHERE cc.ngay_cham_cong BETWEEN @fromDate AND @toDate
                  AND (@employeeId IS NULL OR cc.id_nhan_vien = @employeeId)
                  AND (@departmentId IS NULL OR nv.id_phong_ban = @departmentId)
                GROUP BY 1
            )
            SELECT COALESCE(s.bucket, a.bucket), COALESCE(s.total, 0), COALESCE(a.checked_in, 0),
                   COALESCE(a.completed, 0), COALESCE(s.absent, 0)
            FROM scheduled s FULL OUTER JOIN attendance a ON a.bucket = s.bucket
            ORDER BY 1;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.CommandTimeout = 15;
        command.Parameters.AddWithValue("fromDate", fromDate);
        command.Parameters.AddWithValue("toDate", toDate);
        command.Parameters.AddWithValue("employeeId", NpgsqlDbType.Bigint, (object?)employeeId ?? DBNull.Value);
        command.Parameters.AddWithValue("departmentId", NpgsqlDbType.Bigint, (object?)departmentId ?? DBNull.Value);
        command.Parameters.AddWithValue("nowLocal", GetBusinessLocalNow());
        var buckets = new List<AttendanceStatisticsBucketDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            buckets.Add(new AttendanceStatisticsBucketDto(DateOnly.FromDateTime(reader.GetDateTime(0)),
                reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
        }
        return buckets;
    }

    private DateTime GetBusinessLocalNow()
    {
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }

        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime, DateTimeKind.Unspecified);
    }
}
