using Npgsql;

namespace manage365.Routes.API.Attendance;

/// <summary>
/// Closes BLE check-ins after a healthy kiosk has not seen the employee for the
/// configured grace period. The attendance is marked NeedsReview because a lost
/// advertisement alone cannot distinguish departure from a phone failure.
/// </summary>
public sealed class BleAutoCheckoutService(
    NpgsqlDataSource dataSource,
    ILogger<BleAutoCheckoutService> logger,
    IConfiguration configuration) : BackgroundService
{
    private readonly TimeSpan _absenceGrace = TimeSpan.FromMinutes(
        Math.Clamp(configuration.GetValue("Attendance:Ble:AutoCheckoutAbsenceMinutes", 5), 2, 30));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CloseAbsentAttendancesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (PostgresException ex) when (ex.SqlState is "42P01" or "42703")
            {
                logger.LogWarning("BLE auto check-out is waiting for migrations 013-018: {Message}", ex.MessageText);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "BLE auto check-out sweep failed; open attendances remain unchanged.");
            }

            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task CloseAbsentAttendancesAsync(CancellationToken ct)
    {
        const string sql = """
            WITH candidates AS (
                SELECT cc.id, cc.gio_vao, cc.id_nhan_vien, last_seen.credential_id,
                       last_seen.scanner_id, last_seen.observed_at, last_seen.rssi_dbm
                FROM public.cham_cong cc
                JOIN LATERAL (
                    SELECT o.credential_id, o.scanner_id, o.observed_at, o.received_at, o.rssi_dbm
                    FROM public.ble_attendance_evidence checkin
                    JOIN public.ble_presence_observations o
                      ON o.scanner_id=checkin.scanner_id
                    JOIN public.ble_presence_credentials active_credential
                      ON active_credential.id=o.credential_id AND active_credential.employee_id=cc.id_nhan_vien
                    WHERE checkin.attendance_id=cc.id AND checkin.action='CHECK_IN'
                      AND checkin.scanner_id=o.scanner_id
                    ORDER BY o.received_at DESC LIMIT 1
                ) last_seen ON true
                JOIN public.ble_scanners scanner ON scanner.scanner_id=last_seen.scanner_id
                WHERE cc.phuong_thuc_cham_cong='BLE' AND cc.gio_vao IS NOT NULL AND cc.gio_ra IS NULL
                  AND last_seen.observed_at >= cc.gio_vao
                  AND last_seen.received_at <= now() - @absenceGrace
                  AND scanner.is_active AND scanner.scanner_state='running'
                  AND scanner.last_seen_at >= now() - interval '60 seconds'
                  AND (scanner.agent_version IS NULL OR last_seen.received_at >= scanner.scan_started_at)
                  AND EXISTS (SELECT 1 FROM public.ble_scheduled_employees se
                              WHERE se.scanner_id=scanner.scanner_id AND se.employee_id=cc.id_nhan_vien AND se.assignment_id=cc.id_dang_ky_ca)
                  AND NOT EXISTS (SELECT 1 FROM public.ble_attendance_evidence done
                                  WHERE done.attendance_id=cc.id AND done.action='CHECK_OUT')
                ORDER BY last_seen.received_at
                FOR UPDATE OF cc SKIP LOCKED
            ), closed AS (
                UPDATE public.cham_cong cc
                SET gio_ra=c.observed_at,
                    so_gio_thuc_te=GREATEST(0.01, round((extract(epoch from (c.observed_at-cc.gio_vao))/3600)::numeric,2)),
                    trang_thai='NeedsReview', updated_at=now()
                FROM candidates c
                WHERE cc.id=c.id AND cc.gio_ra IS NULL
                RETURNING cc.id,c.credential_id,c.scanner_id,c.observed_at,c.rssi_dbm,
                          GREATEST(0,extract(epoch from (now()-c.observed_at))::int) AS absent_seconds
            )
            INSERT INTO public.ble_attendance_evidence
                (attendance_id,action,scanner_id,credential_id,observed_at,stable_presence_seconds,
                 median_rssi_dbm,idempotency_key,checkout_source)
            SELECT id,'CHECK_OUT',scanner_id,credential_id,observed_at,absent_seconds,
                   rssi_dbm,NULL,'beacon_absence'
            FROM closed
            ON CONFLICT (attendance_id,action) DO NOTHING;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("absenceGrace", _absenceGrace);
        var count = await command.ExecuteNonQueryAsync(ct);
        if (count > 0)
            logger.LogInformation("Automatically closed {Count} BLE attendance(s) as NeedsReview after beacon absence.", count);
    }
}
