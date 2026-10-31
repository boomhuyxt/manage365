using System.Security.Cryptography;
using System.Text;
using manage365.Repositories.Attendance;
using Microsoft.Extensions.Options;
using Npgsql;

namespace manage365.Routes.API.Attendance;

public sealed class BleAttendanceService(NpgsqlDataSource dataSource, IShiftAttendanceRepository attendanceRepository, IOptions<AttendancePolicyOptions> options)
{
    private const int MinimumRssiDbm = -72;
    private const int MinimumDwellSeconds = 20;
    private const int OpenAttendanceBlockHours = 24;

    public async Task<CreateBleCredentialResponse> CreateCredentialAsync(long employeeId, string deviceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 128)
            throw new BleAttendanceException("device_id_invalid", "Mã thiết bị không hợp lệ.", 400);

        // A rotating random service UUID is directly visible in the BLE advertisement.
        var rawToken = Guid.NewGuid().ToString();
        var credentialId = Guid.NewGuid();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(2);
        const string sql = """
            INSERT INTO public.ble_presence_credentials(id, employee_id, device_id, token_hash, expires_at)
            SELECT @id, @employeeId, @deviceId, @tokenHash, @expiresAt
            WHERE EXISTS (SELECT 1 FROM public.nhan_vien WHERE id=@employeeId AND COALESCE(trang_thai,'Active')='Active');
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", credentialId);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("deviceId", deviceId.Trim());
        cmd.Parameters.AddWithValue("tokenHash", Hash(rawToken));
        cmd.Parameters.AddWithValue("expiresAt", expiry);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1)
            throw new BleAttendanceException("account_inactive", "Tài khoản không hoạt động.", 403);
        return new(credentialId, rawToken, expiry, 60);
    }

    public async Task RevokeCredentialAsync(long employeeId, Guid credentialId, CancellationToken ct)
    {
        const string sql = "UPDATE public.ble_presence_credentials SET revoked_at=now() WHERE id=@id AND employee_id=@employeeId AND revoked_at IS NULL;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", credentialId);
        command.Parameters.AddWithValue("employeeId", employeeId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<BleAttendanceResponse> RecordObservationsAsync(
        string scannerId, string scannerToken, BleScannerEventRequest request, CancellationToken ct)
    {
        if (request.BatchId == Guid.Empty || request.Observations is null || request.Observations.Count > 30)
            throw new BleAttendanceException("ble_batch_invalid", "Mỗi batch tối đa 30 quan sát.", 400);
        var now = DateTimeOffset.UtcNow;
        if (Math.Abs((now - request.ObservedAtUtc).TotalSeconds) > 15)
            throw new BleAttendanceException("ble_observation_stale", "Thời gian quan sát đã cũ hoặc sai lệch.", 422);

        var storeCode = await AuthenticateScannerAsync(scannerId, scannerToken, now, ct);
        await using (var health = dataSource.CreateCommand("UPDATE public.ble_scanners SET scanner_state=@state,scanner_message=@message WHERE scanner_id=@id"))
        {
            health.Parameters.AddWithValue("id", scannerId);
            health.Parameters.AddWithValue("state", request.ScannerState is "running" or "stopped" or "error" ? request.ScannerState : "unknown");
            health.Parameters.AddWithValue("message", (request.ScannerMessage ?? "")[..Math.Min(request.ScannerMessage?.Length ?? 0, 300)]);
            await health.ExecuteNonQueryAsync(ct);
        }
        if (request.ScannerState != "running")
            return new(false, "Bộ quét Bluetooth chưa sẵn sàng; kiểm tra trạng thái tại kiosk.", "ble_scanner_not_running", null, null, "NONE", now, null);
        if (request.Observations.Count == 0)
            return new(true, $"Scanner {scannerId} tại {storeCode} đang hoạt động.", null, null, null, "NONE", now, null);
        BleAttendanceResponse? result = null;
        foreach (var item in request.Observations)
        {
            if (item is null || item.RssiDbm is < -127 or > 20 || item.Sequence < 0 || string.IsNullOrWhiteSpace(item.AdvertisementToken) || item.AdvertisementToken.Length > 128)
                continue;
            const string resolveSql = """
                SELECT c.id, c.employee_id FROM public.ble_presence_credentials c
                JOIN public.nhan_vien e ON e.id=c.employee_id
                WHERE c.token_hash=@tokenHash AND c.expires_at > @now AND c.revoked_at IS NULL
                  AND COALESCE(e.trang_thai,'Active')='Active'
                  AND EXISTS (SELECT 1 FROM public.ble_scheduled_employees se WHERE se.scanner_id=@scannerId AND se.employee_id=e.id);
                """;
            await using var resolve = dataSource.CreateCommand(resolveSql);
            resolve.Parameters.AddWithValue("tokenHash", Hash(item.AdvertisementToken));
            resolve.Parameters.AddWithValue("scannerId", scannerId);
            resolve.Parameters.AddWithValue("now", now);
            await using var reader = await resolve.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) continue;
            var observedCredential = reader.GetGuid(0);
            var observedEmployee = reader.GetInt64(1);
            await reader.DisposeAsync();

            const string insertSql = """
                INSERT INTO public.ble_presence_observations
                    (credential_id, scanner_id, batch_id, sequence, observed_at, rssi_dbm)
                VALUES (@credentialId, @scannerId, @batchId, @sequence, @observedAt, @rssi)
                ON CONFLICT (scanner_id, batch_id, sequence) DO NOTHING;
                """;
            await using var insert = dataSource.CreateCommand(insertSql);
            insert.Parameters.AddWithValue("credentialId", observedCredential);
            insert.Parameters.AddWithValue("scannerId", scannerId);
            insert.Parameters.AddWithValue("batchId", request.BatchId);
            insert.Parameters.AddWithValue("sequence", item.Sequence);
            insert.Parameters.AddWithValue("observedAt", request.ObservedAtUtc);
            insert.Parameters.AddWithValue("rssi", item.RssiDbm);
            if (await insert.ExecuteNonQueryAsync(ct) == 1)
            {
                BleAttendanceResponse current;
                try { current = await ProcessCredentialAsync(observedEmployee, observedCredential, scannerId, request.ObservedAtUtc, now, ct); }
                catch (BleAttendanceException ex)
                { current = new(false, ex.Message, ex.Code, null, null, "NONE", now, null); }
                await using var diagnostic = dataSource.CreateCommand("""
                    INSERT INTO public.ble_scanner_employees(scanner_id,employee_id,last_observed_at,last_rssi_dbm,last_result_code,last_result_message)
                    VALUES (@scannerId,@employeeId,@now,@rssi,@code,@message)
                    ON CONFLICT(scanner_id,employee_id) DO UPDATE SET last_observed_at=EXCLUDED.last_observed_at,
                        last_rssi_dbm=EXCLUDED.last_rssi_dbm,last_result_code=EXCLUDED.last_result_code,last_result_message=EXCLUDED.last_result_message;
                    """);
                diagnostic.Parameters.AddWithValue("now", now); diagnostic.Parameters.AddWithValue("rssi", item.RssiDbm);
                diagnostic.Parameters.AddWithValue("code", current.ErrorCode ?? current.Action);
                diagnostic.Parameters.AddWithValue("message", current.Message);
                diagnostic.Parameters.AddWithValue("scannerId", scannerId); diagnostic.Parameters.AddWithValue("employeeId", observedEmployee);
                await diagnostic.ExecuteNonQueryAsync(ct);
                if (result?.Success != true || current.Success) result = current;
            }
        }

        return result ?? new(false, "Không có credential hợp lệ mới trong batch.", "ble_credential_invalid", null, null, "NONE", now, null);
    }

    private async Task<BleAttendanceResponse> ProcessCredentialAsync(long employeeId, Guid credentialId,
        string scannerId, DateTimeOffset observedAt, DateTimeOffset now, CancellationToken ct)
    {
        const string dwellSql = """
            SELECT count(*), min(received_at), percentile_cont(0.5) WITHIN GROUP (ORDER BY rssi_dbm)::int
            FROM public.ble_presence_observations
            WHERE credential_id=@credentialId AND scanner_id=@scannerId AND received_at >= @windowStart;
            """;
        await using var dwell = dataSource.CreateCommand(dwellSql);
        dwell.Parameters.AddWithValue("credentialId", credentialId);
        dwell.Parameters.AddWithValue("scannerId", scannerId);
        dwell.Parameters.AddWithValue("windowStart", now.AddSeconds(-30));
        long count; DateTimeOffset? firstSeen; int medianRssi;
        await using (var reader = await dwell.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            count = reader.GetInt64(0);
            firstSeen = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
            medianRssi = reader.IsDBNull(2) ? -127 : reader.GetInt32(2);
        }
        var dwellSeconds = firstSeen is null ? 0 : Math.Max(0, (int)(now - firstSeen.Value).TotalSeconds);
        if (count < 5 || dwellSeconds < MinimumDwellSeconds)
            return new(false, "Đang xác nhận hiện diện BLE.", "ble_dwell_not_met", null, null, "NONE", now, null);

        var allowed = await ScheduledAssignmentsAsync(employeeId, scannerId, ct);
        var shifts = (await attendanceRepository.GetEligibleShiftsAsync(employeeId, now, ct))
            .Where(s => s.ShiftAssignmentId.HasValue && allowed.Contains(s.ShiftAssignmentId.Value)).ToArray();
        if (shifts.Any(x => x.AllowedAction == "CHECK_OUT" && x.CanPerformAction))
            return new(true, "Đang trong ca. Bấm Check-out trên app khi kết thúc làm việc.", null, null, null, "PRESENT", now, null);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
        var currentShifts = shifts.Where(s => BleAttendancePolicy.IsWithinCheckInWindow(s, now, timeZone, options.Value.EarlyCheckInMinutes)).ToArray();
        var decision = BleAttendancePolicy.EvaluateCheckIn(currentShifts, medianRssi, dwellSeconds, MinimumRssiDbm, MinimumDwellSeconds);
        if (!decision.CanCheckIn || decision.ShiftAssignmentId is null)
            return new(false, ErrorMessage(decision.ErrorCode), decision.ErrorCode, null, null, "CHECK_IN", now, null);

        return await CommitBleAsync(employeeId, decision.ShiftAssignmentId.Value, "CHECK_IN", scannerId,
            credentialId, observedAt, dwellSeconds, medianRssi, null, now, ct);
    }

    public async Task<BleAttendanceResponse> CheckoutAsync(long employeeId, string scannerId, BleCheckoutRequest request, CancellationToken ct)
    {
        if (request.IdempotencyKey == Guid.Empty || request.CredentialId == Guid.Empty || string.IsNullOrWhiteSpace(request.PresenceToken))
            throw new BleAttendanceException("ble_checkout_request_invalid", "Dữ liệu checkout BLE không hợp lệ.", 400);
        var now = DateTimeOffset.UtcNow;
        const string idempotencySql = """
            SELECT cc.id_nhan_vien, cc.id_dang_ky_ca, COALESCE(cl.loai_ca,'Ca làm việc'),
                   cc.gio_ra, cc.so_gio_thuc_te
            FROM public.ble_attendance_evidence e
            JOIN public.cham_cong cc ON cc.id=e.attendance_id
            LEFT JOIN public.dang_ky_ca dkc ON dkc.id=cc.id_dang_ky_ca
            LEFT JOIN public.ca_lam cl ON cl.id=dkc.id_ca_lam
            WHERE e.idempotency_key=@key LIMIT 1;
            """;
        await using (var prior = dataSource.CreateCommand(idempotencySql))
        {
            prior.Parameters.AddWithValue("key", request.IdempotencyKey);
            await using var reader = await prior.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                if (reader.GetInt64(0) != employeeId)
                    throw new BleAttendanceException("ble_idempotency_key_reused", "Idempotency key đã được sử dụng.", 409);
                return new(true, "Check-out BLE đã được ghi nhận trước đó.", null,
                    reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.GetString(2), "CHECK_OUT",
                    reader.GetFieldValue<DateTimeOffset>(3), reader.IsDBNull(4) ? null : reader.GetDecimal(4));
            }
        }
        const string presenceSql = """
            SELECT min(o.received_at), max(o.received_at), count(*),
                   percentile_cont(0.5) WITHIN GROUP (ORDER BY o.rssi_dbm)::int
            FROM public.ble_presence_credentials c
            JOIN public.ble_presence_observations o ON o.credential_id=c.id
            JOIN public.ble_scanners s ON s.scanner_id=o.scanner_id
            WHERE c.id=@credentialId AND c.employee_id=@employeeId AND c.token_hash=@tokenHash
              AND c.expires_at>@now AND c.revoked_at IS NULL AND s.scanner_id=@scannerId AND s.is_active
              AND EXISTS (SELECT 1 FROM public.ble_scheduled_employees se WHERE se.scanner_id=s.scanner_id AND se.employee_id=c.employee_id)
              AND o.received_at>=@freshAfter;
            """;
        await using var presence = dataSource.CreateCommand(presenceSql);
        presence.Parameters.AddWithValue("credentialId", request.CredentialId);
        presence.Parameters.AddWithValue("employeeId", employeeId);
        presence.Parameters.AddWithValue("tokenHash", Hash(request.PresenceToken));
        presence.Parameters.AddWithValue("now", now);
        presence.Parameters.AddWithValue("scannerId", scannerId);
        presence.Parameters.AddWithValue("freshAfter", now.AddSeconds(-45));
        DateTimeOffset observedAt;
        int stableSeconds;
        int medianRssi;
        await using (var reader = await presence.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
                throw new BleAttendanceException("ble_presence_expired", "Scanner chưa nhận diện thiết bị gần đây. Hãy đến gần kiosk hoặc dùng QR.", 422);
            observedAt = reader.GetFieldValue<DateTimeOffset>(1);
            stableSeconds = Math.Max(0, (int)(observedAt - reader.GetFieldValue<DateTimeOffset>(0)).TotalSeconds);
            medianRssi = reader.GetInt32(3);
        }
        if (medianRssi < MinimumRssiDbm)
            throw new BleAttendanceException("ble_signal_too_weak", "Tín hiệu BLE yếu, hãy đến gần kiosk để check-out.", 422);
        if (observedAt < now.AddSeconds(-45))
            throw new BleAttendanceException("ble_presence_expired", "Scanner chưa nhận diện thiết bị gần đây. Hãy đến gần kiosk hoặc dùng QR.", 422);

        var shiftAssignmentId = request.ShiftAssignmentId;
        if (shiftAssignmentId is null)
        {
            var allowed = await ScheduledAssignmentsAsync(employeeId, scannerId, ct);
            var eligible = (await attendanceRepository.GetEligibleShiftsAsync(employeeId, now, ct))
                .Where(shift => shift.CanPerformAction && shift.AllowedAction == "CHECK_OUT" && shift.ShiftAssignmentId.HasValue && allowed.Contains(shift.ShiftAssignmentId.Value))
                .ToArray();
            if (eligible.Length != 1)
                throw new BleAttendanceException(eligible.Length == 0 ? "not_checked_in" : "shift_ambiguous",
                    eligible.Length == 0 ? "Không tìm thấy ca đang check-in." : "Có nhiều ca đang mở; hãy dùng QR để chọn ca.", 409);
            shiftAssignmentId = eligible[0].ShiftAssignmentId;
        }

        var targetShiftId = shiftAssignmentId ?? throw new BleAttendanceException("not_checked_in", "Không tìm thấy ca đang check-in.", 409);
        return await CommitBleAsync(employeeId, targetShiftId, "CHECK_OUT", scannerId,
            request.CredentialId, observedAt, stableSeconds, medianRssi, request.IdempotencyKey, now, ct);
    }

    public async Task<BleAttendanceStatusResponse> GetStatusAsync(long employeeId, CancellationToken ct)
    {
        const string sql = """
            SELECT s.scanner_id,s.last_seen_at,o.last_seen,se.last_result_message,s.scanner_state
            FROM public.ble_scanners s
            LEFT JOIN public.ble_scanner_employees se ON se.scanner_id=s.scanner_id AND se.employee_id=@employeeId
            LEFT JOIN LATERAL (
                SELECT max(o.received_at) AS last_seen FROM public.ble_presence_observations o
                JOIN public.ble_presence_credentials c ON c.id=o.credential_id
                WHERE o.scanner_id=s.scanner_id AND c.employee_id=@employeeId
                    AND c.revoked_at IS NULL AND c.expires_at>now()
            ) o ON true
            WHERE s.is_active AND EXISTS (SELECT 1 FROM public.ble_scheduled_employees scheduled WHERE scheduled.scanner_id=s.scanner_id AND scheduled.employee_id=@employeeId)
            ORDER BY o.last_seen DESC NULLS LAST,s.last_seen_at DESC NULLS LAST LIMIT 1;
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var found = await reader.ReadAsync(ct);
        var scannerId = found ? reader.GetString(0) : null;
        var online = found && !reader.IsDBNull(1) && reader.GetFieldValue<DateTimeOffset>(1) > DateTimeOffset.UtcNow.AddSeconds(-60) && reader.GetString(4) == "running";
        DateTimeOffset? lastSeen = found && !reader.IsDBNull(2) ? reader.GetFieldValue<DateTimeOffset>(2) : null;
        var message = found && !reader.IsDBNull(3) ? reader.GetString(3) : null;
        await reader.DisposeAsync();
        const string attendanceSql = """
            SELECT e.action, COALESCE(cl.loai_ca,'Ca làm việc'),
                   CASE WHEN e.action='CHECK_IN' THEN cc.gio_vao ELSE cc.gio_ra END
            FROM public.ble_attendance_evidence e
            JOIN public.cham_cong cc ON cc.id=e.attendance_id
            LEFT JOIN public.dang_ky_ca dkc ON dkc.id=cc.id_dang_ky_ca
            LEFT JOIN public.ca_lam cl ON cl.id=dkc.id_ca_lam
            WHERE cc.id_nhan_vien=@employeeId
            ORDER BY e.created_at DESC LIMIT 1;
            """;
        await using var attendance = dataSource.CreateCommand(attendanceSql);
        attendance.Parameters.AddWithValue("employeeId", employeeId);
        await using var attendanceReader = await attendance.ExecuteReaderAsync(ct);
        string? lastAction = null;
        string? lastShiftName = null;
        DateTimeOffset? lastAttendanceAt = null;
        if (await attendanceReader.ReadAsync(ct))
        {
            lastAction = attendanceReader.GetString(0);
            lastShiftName = attendanceReader.GetString(1);
            lastAttendanceAt = attendanceReader.IsDBNull(2) ? null : attendanceReader.GetFieldValue<DateTimeOffset>(2);
        }
        return new(online, online && lastSeen > DateTimeOffset.UtcNow.AddSeconds(-45), lastSeen, scannerId,
            lastAction, lastShiftName, lastAttendanceAt,
            online ? message ?? "Kiosk đang quét; hãy bật BLE trên app và đứng gần kiosk." : "BLE chưa sẵn sàng; có thể chấm công bằng QR.");
    }

    private async Task<HashSet<long>> ScheduledAssignmentsAsync(long employeeId, string scannerId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("SELECT assignment_id FROM public.ble_scheduled_employees WHERE employee_id=@employee AND scanner_id=@scanner");
        cmd.Parameters.AddWithValue("employee", employeeId);
        cmd.Parameters.AddWithValue("scanner", scannerId);
        var ids = new HashSet<long>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) ids.Add(reader.GetInt64(0));
        return ids;
    }

    private async Task<BleAttendanceResponse> CommitBleAsync(long employeeId, long shiftId, string action,
        string scannerId, Guid credentialId, DateTimeOffset observedAt, int dwell, int rssi, Guid? idempotencyKey, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using (var employeeLock = new NpgsqlCommand("SELECT id FROM public.nhan_vien WHERE id=@id AND COALESCE(trang_thai,'Active')='Active' FOR UPDATE", connection, tx))
        {
            employeeLock.Parameters.AddWithValue("id", employeeId);
            if (await employeeLock.ExecuteScalarAsync(ct) is null)
                throw new BleAttendanceException("account_inactive", "Tài khoản không hoạt động.", 403);
        }
        if (idempotencyKey is not null)
        {
            await using var previous = new NpgsqlCommand("""
                SELECT cc.id, cc.so_gio_thuc_te, COALESCE(cl.loai_ca,'Ca làm việc')
                FROM public.ble_attendance_evidence e
                JOIN public.cham_cong cc ON cc.id=e.attendance_id
                LEFT JOIN public.dang_ky_ca dkc ON dkc.id=cc.id_dang_ky_ca
                LEFT JOIN public.ca_lam cl ON cl.id=dkc.id_ca_lam
                WHERE e.idempotency_key=@key AND cc.id_nhan_vien=@employeeId LIMIT 1;
                """, connection, tx);
            previous.Parameters.AddWithValue("key", idempotencyKey.Value); previous.Parameters.AddWithValue("employeeId", employeeId);
            await using var previousReader = await previous.ExecuteReaderAsync(ct);
            if (await previousReader.ReadAsync(ct))
            {
                decimal? priorHours = previousReader.IsDBNull(1) ? null : previousReader.GetDecimal(1);
                var priorShiftName = previousReader.GetString(2);
                await previousReader.DisposeAsync();
                await tx.CommitAsync(ct);
                return new(true, "Check-out BLE đã được ghi nhận trước đó.", null, shiftId, priorShiftName, action, now, priorHours);
            }
        }
        string shiftName;
        await using (var shift = new NpgsqlCommand("""
            SELECT cl.loai_ca FROM public.dang_ky_ca dkc JOIN public.ca_lam cl ON cl.id=dkc.id_ca_lam
            WHERE dkc.id=@shiftId AND dkc.id_nhan_vien=@employeeId AND COALESCE(dkc.trang_thai,'Approved') IN ('Approved','Pending')
              AND EXISTS (SELECT 1 FROM public.ble_scanners s JOIN public.nhan_vien owner ON owner.id=s.owner_employee_id
                          WHERE s.scanner_id=@scannerId AND s.is_active AND s.owner_employee_id=dkc.shop_owner_id
                            AND COALESCE(owner.trang_thai,'Active')='Active')
            FOR SHARE OF dkc;
            """, connection, tx))
        {
            shift.Parameters.AddWithValue("scannerId", scannerId);
            shift.Parameters.AddWithValue("shiftId", shiftId);
            shift.Parameters.AddWithValue("employeeId", employeeId);
            shiftName = (string?)await shift.ExecuteScalarAsync(ct) ?? throw new BleAttendanceException("shift_not_found", "Không tìm thấy ca được phân công.", 404);
        }

        long attendanceId;
        decimal? hours = null;
        if (action == "CHECK_IN")
        {
            await using (var openAttendance = new NpgsqlCommand("""
                SELECT id FROM public.cham_cong
                WHERE id_nhan_vien=@employeeId AND gio_vao IS NOT NULL AND gio_ra IS NULL
                  AND gio_vao >= @activeAttendanceSince
                LIMIT 1;
                """, connection, tx))
            {
                openAttendance.Parameters.AddWithValue("employeeId", employeeId);
                // A forgotten QR checkout from an old shift must not block every
                // future BLE check-in. Recent open attendance still prevents two
                // active shifts from being checked in at the same time.
                openAttendance.Parameters.AddWithValue("activeAttendanceSince", now.AddHours(-OpenAttendanceBlockHours));
                if (await openAttendance.ExecuteScalarAsync(ct) is not null)
                    throw new BleAttendanceException("already_checked_in", "Bạn đang có một ca chưa check-out.", 409);
            }
            await using (var leave = new NpgsqlCommand("""
                SELECT 1 FROM public.don_nghi_phep_ca x JOIN public.don_nghi_phep d ON d.id=x.id_don_nghi_phep
                WHERE x.id_dang_ky_ca=@shiftId AND d.trang_thai='Approved'
                UNION
                SELECT 1 FROM public.don_nghi_phep_khoang k JOIN public.don_nghi_phep d ON d.id=k.id_don_nghi_phep
                WHERE d.id_nhan_vien=@employeeId AND d.trang_thai='Approved'
                  AND k.gio_bat_dau_utc <= @now AND @now < k.gio_ket_thuc_utc
                LIMIT 1;
                """, connection, tx))
            {
                leave.Parameters.AddWithValue("shiftId", shiftId);
                leave.Parameters.AddWithValue("employeeId", employeeId);
                leave.Parameters.AddWithValue("now", now);
                if (await leave.ExecuteScalarAsync(ct) is not null)
                    throw new BleAttendanceException("approved_leave_conflict", "Ca này đã được duyệt nghỉ phép.", 409);
            }
            await using var insert = new NpgsqlCommand("""
                INSERT INTO public.cham_cong(id_dang_ky_ca,id_nhan_vien,ngay_cham_cong,gio_vao,trang_thai,phuong_thuc_cham_cong,created_at,updated_at)
                SELECT dkc.id,@employeeId,cl.ngay_lam,@now,'CheckedIn','BLE',@now,@now
                FROM public.dang_ky_ca dkc JOIN public.ca_lam cl ON cl.id=dkc.id_ca_lam
                WHERE dkc.id=@shiftId AND dkc.id_nhan_vien=@employeeId
                ON CONFLICT(id_dang_ky_ca) DO NOTHING RETURNING id;
                """, connection, tx);
            insert.Parameters.AddWithValue("shiftId", shiftId); insert.Parameters.AddWithValue("employeeId", employeeId); insert.Parameters.AddWithValue("now", now);
            attendanceId = (long?)await insert.ExecuteScalarAsync(ct) ?? throw new BleAttendanceException("already_checked_in", "Ca này đã được check-in.", 409);
        }
        else
        {
            await using var update = new NpgsqlCommand("""
                UPDATE public.cham_cong SET gio_ra=@now, so_gio_thuc_te=GREATEST(0.01,round((extract(epoch from (@now-gio_vao))/3600)::numeric,2)),
                    trang_thai=CASE WHEN EXISTS (
                        SELECT 1 FROM public.don_nghi_phep_khoang k JOIN public.don_nghi_phep d ON d.id=k.id_don_nghi_phep
                        WHERE d.id_nhan_vien=cham_cong.id_nhan_vien AND d.trang_thai='Approved'
                          AND cham_cong.gio_vao < k.gio_ket_thuc_utc AND k.gio_bat_dau_utc < @now
                    ) THEN 'NeedsReview' ELSE 'Completed' END,
                    phuong_thuc_cham_cong='BLE', updated_at=@now
                WHERE id_dang_ky_ca=@shiftId AND id_nhan_vien=@employeeId AND gio_vao IS NOT NULL AND gio_ra IS NULL
                RETURNING id,so_gio_thuc_te;
                """, connection, tx);
            update.Parameters.AddWithValue("now", now); update.Parameters.AddWithValue("shiftId", shiftId); update.Parameters.AddWithValue("employeeId", employeeId);
            await using var reader = await update.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new BleAttendanceException("not_checked_in", "Không tìm thấy lượt check-in đang mở cho ca.", 409);
            attendanceId = reader.GetInt64(0); hours = reader.GetDecimal(1);
        }

        await using (var evidence = new NpgsqlCommand("""
            INSERT INTO public.ble_attendance_evidence(attendance_id,action,scanner_id,credential_id,observed_at,stable_presence_seconds,median_rssi_dbm,idempotency_key)
            VALUES(@attendanceId,@action,@scannerId,@credentialId,@observedAt,@dwell,@rssi,@idempotencyKey);
            """, connection, tx))
        {
            evidence.Parameters.AddWithValue("attendanceId", attendanceId); evidence.Parameters.AddWithValue("action", action);
            evidence.Parameters.AddWithValue("scannerId", scannerId); evidence.Parameters.AddWithValue("credentialId", credentialId);
            evidence.Parameters.AddWithValue("observedAt", observedAt); evidence.Parameters.AddWithValue("dwell", dwell); evidence.Parameters.AddWithValue("rssi", rssi);
            evidence.Parameters.Add("idempotencyKey", NpgsqlTypes.NpgsqlDbType.Uuid).Value = (object?)idempotencyKey ?? DBNull.Value;
            await evidence.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new(true, action == "CHECK_IN" ? $"Check-in BLE thành công: {shiftName}." : $"Check-out BLE thành công: {shiftName}.", null, shiftId, shiftName, action, now, hours);
    }

    private async Task<string> AuthenticateScannerAsync(string scannerId, string token, DateTimeOffset now, CancellationToken ct)
    {
        const string sql = "UPDATE public.ble_scanners SET last_seen_at=@now,updated_at=@now WHERE scanner_id=@id AND is_active AND scanner_token_hash=@hash RETURNING store_code;";
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("now", now); cmd.Parameters.AddWithValue("id", scannerId); cmd.Parameters.AddWithValue("hash", Hash(token));
        return (string?)await cmd.ExecuteScalarAsync(ct) ?? throw new BleAttendanceException("ble_scanner_unauthorized", "Scanner không hợp lệ hoặc đã bị vô hiệu hóa.", 401);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string ErrorMessage(string? code) => code switch
    {
        "ble_signal_too_weak" => "Tín hiệu BLE yếu, hãy đến gần kiosk hoặc dùng QR.",
        "ble_dwell_not_met" => "Đang xác nhận hiện diện BLE.",
        "shift_ambiguous" => "Có nhiều ca phù hợp; vui lòng chọn ca và dùng QR.",
        _ => "Không có ca được phân công phù hợp để tự động check-in."
    };
}

public sealed class BleAttendanceException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}




