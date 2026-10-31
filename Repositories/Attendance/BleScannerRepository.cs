using System.Security.Cryptography;
using System.Text;
using manage365.Routes.API.Attendance;
using Npgsql;

namespace manage365.Repositories.Attendance;

public sealed class BleScannerRepository(NpgsqlDataSource source)
{
    // Control remains authenticated while disabled so the installed agent can resume
    // without a new download. Observation ingestion still requires is_active=true.
    public async Task<BleScannerControlResponse> ControlAsync(string scannerId, string token, BleScannerControlRequest request, CancellationToken ct)
    {
        if (request.State is not ("running" or "stopped" or "error" or "unknown") || request.Version != "2.0")
            throw new BleAttendanceException("ble_agent_invalid", "Phiên bản hoặc trạng thái bộ quét không hợp lệ.", 400);
        await using var cmd = source.CreateCommand("""
            UPDATE public.ble_scanners s SET
              scan_started_at=CASE WHEN @state='running' AND is_active THEN
                CASE WHEN scanner_state<>'running' OR scan_started_at IS NULL OR last_seen_at<now()-interval '60 seconds'
                     THEN now() ELSE scan_started_at END ELSE NULL END,
              last_seen_at=now(),scanner_state=@state,
              scanner_message=@message,agent_version=@version
            WHERE scanner_id=@id AND scanner_token_hash=@hash
              AND EXISTS(SELECT 1 FROM public.nhan_vien nv WHERE nv.id=s.owner_employee_id
                         AND COALESCE(nv.trang_thai,'Active')='Active')
            RETURNING is_active;
            """);
        cmd.Parameters.AddWithValue("id", scannerId);
        cmd.Parameters.AddWithValue("hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant());
        cmd.Parameters.AddWithValue("state", request.State);
        cmd.Parameters.AddWithValue("message", (request.Message ?? "")[..Math.Min(request.Message?.Length ?? 0, 300)]);
        cmd.Parameters.AddWithValue("version", request.Version);
        var enabled = await cmd.ExecuteScalarAsync(ct);
        if (enabled is not bool active) throw new BleAttendanceException("ble_scanner_invalid", "Bộ quét chưa được xác thực.", 401);
        return new(active);
    }

    public async Task<List<BleScannerSummary>> ListAsync(long actorId, bool admin, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("""
            SELECT scanner_id,store_code,is_active,last_seen_at,scanner_state,scanner_message,
              (SELECT count(DISTINCT se.employee_id) FROM public.ble_scheduled_employees se WHERE se.scanner_id=s.scanner_id),s.agent_version
            FROM public.ble_scanners s WHERE @admin OR owner_employee_id=@actorId ORDER BY scanner_id;
            """);
        Actor(cmd, actorId, admin);
        var result = new List<BleScannerSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var seen = reader.IsDBNull(3) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(3);
            result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), seen,
                reader.GetBoolean(2) && seen > DateTimeOffset.UtcNow.AddSeconds(-60) && reader.GetString(4) == "running",
                reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt64(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return result;
    }

    public async Task RequireAccessAsync(string scannerId, long actorId, bool admin, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("SELECT 1 FROM public.ble_scanners WHERE scanner_id=@id AND (@admin OR owner_employee_id=@actorId)");
        cmd.Parameters.AddWithValue("id", scannerId); Actor(cmd, actorId, admin);
        if (await cmd.ExecuteScalarAsync(ct) is null)
            throw new BleAttendanceException("ble_scanner_not_found", "Không tìm thấy kiosk thuộc quyền quản lý của bạn.", 404);
    }

    public async Task<RegisterBleScannerResponse> RegisterAsync(RegisterBleScannerRequest request, long actorId, bool admin, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ScannerId) || request.ScannerId.Length > 80 ||
            !System.Text.RegularExpressions.Regex.IsMatch(request.ScannerId, "^[A-Za-z0-9_-]+$") ||
            string.IsNullOrWhiteSpace(request.StoreCode) || request.StoreCode.Trim().Length > 64)
            throw new BleAttendanceException("ble_scanner_config_invalid", "Mã kiosk chỉ gồm chữ, số, dấu gạch ngang/gạch dưới (tối đa 80 ký tự); mã cửa hàng tối đa 64 ký tự.", 400);
        var ownerId = request.ShopOwnerId ?? actorId;
        if (!admin && ownerId != actorId)
            throw new BleAttendanceException("ble_shop_forbidden", "Không được tạo kiosk cho cửa hàng khác.", 403);
        // Token rotation retains the existing owner; creating a kiosk requires an active shop owner.
        await using (var owner = source.CreateCommand("""
            SELECT 1 FROM public.ble_scanners WHERE scanner_id=@id
            UNION ALL
            SELECT 1 FROM public.nhan_vien nv JOIN public.vai_tro r ON r.id=nv.id_vai_tro
            WHERE nv.id=@owner AND COALESCE(nv.trang_thai,'Active')='Active' AND r.ten_vai_tro='Manager'
            LIMIT 1;
            """))
        {
            owner.Parameters.AddWithValue("id", request.ScannerId);
            owner.Parameters.AddWithValue("owner", ownerId);
            if (await owner.ExecuteScalarAsync(ct) is null)
                throw new BleAttendanceException("ble_shop_required", "Chọn tài khoản chủ shop đang hoạt động để tạo kiosk.", 400);
        }
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await using var cmd = source.CreateCommand("""
            INSERT INTO public.ble_scanners(scanner_id,store_code,scanner_token_hash,owner_employee_id)
            VALUES(@id,@store,@hash,@owner)
            ON CONFLICT(scanner_id) DO UPDATE SET scanner_token_hash=EXCLUDED.scanner_token_hash,
                is_active=true,last_seen_at=NULL,scanner_state='unknown',scanner_message=NULL,agent_version=NULL,scan_started_at=NULL,updated_at=now()
            WHERE @admin OR ble_scanners.owner_employee_id=@actorId
            RETURNING store_code;
            """);
        cmd.Parameters.AddWithValue("id", request.ScannerId); cmd.Parameters.AddWithValue("store", request.StoreCode.Trim());
        cmd.Parameters.AddWithValue("owner", ownerId);
        cmd.Parameters.AddWithValue("hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant());
        Actor(cmd, actorId, admin);
        var store = (string?)await cmd.ExecuteScalarAsync(ct);
        if (store is null) throw new BleAttendanceException("ble_scanner_forbidden", "Mã kiosk đã được một tài khoản khác quản lý.", 403);
        return new(request.ScannerId, store, token);
    }

    public async Task SetActiveAsync(string scannerId, bool active, long actorId, bool admin, CancellationToken ct)
    {
        await RequireAccessAsync(scannerId, actorId, admin, ct);
        await using var cmd = source.CreateCommand("UPDATE public.ble_scanners SET scan_started_at=CASE WHEN is_active<>@active THEN NULL ELSE scan_started_at END,is_active=@active,updated_at=now() WHERE scanner_id=@id");
        cmd.Parameters.AddWithValue("id", scannerId); cmd.Parameters.AddWithValue("active", active);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<BleScannerOverview> OverviewAsync(string scannerId, long actorId, bool admin, CancellationToken ct)
    {
        await RequireAccessAsync(scannerId, actorId, admin, ct);
        var employees = new List<BleEmployeePresence>();
        await using (var cmd = source.CreateCommand("""
            SELECT e.id,e.ho_ten,e.trang_thai,se.last_observed_at,se.last_rssi_dbm,se.last_result_code,se.last_result_message
            FROM (SELECT DISTINCT scanner_id,employee_id FROM public.ble_scheduled_employees WHERE scanner_id=@id) scheduled
            JOIN public.nhan_vien e ON e.id=scheduled.employee_id
            LEFT JOIN public.ble_scanner_employees se ON se.scanner_id=scheduled.scanner_id AND se.employee_id=e.id
            WHERE scheduled.scanner_id=@id ORDER BY e.ho_ten,e.id;
            """))
        {
            cmd.Parameters.AddWithValue("id", scannerId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) employees.Add(new(r.GetInt64(0),r.GetString(1),r.IsDBNull(2) ? "Active" : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetFieldValue<DateTimeOffset>(3),r.IsDBNull(4) ? null : r.GetInt16(4),
                r.IsDBNull(5) ? null : r.GetString(5),r.IsDBNull(6) ? null : r.GetString(6)));
        }
        var events = new List<BleAttendanceEvent>();
        await using (var cmd = source.CreateCommand("""
            SELECT e.ho_ten,b.action,b.created_at,COALESCE(cl.loai_ca,'Ca làm việc')
            FROM public.ble_attendance_evidence b JOIN public.cham_cong cc ON cc.id=b.attendance_id
            JOIN public.nhan_vien e ON e.id=cc.id_nhan_vien
            LEFT JOIN public.dang_ky_ca dkc ON dkc.id=cc.id_dang_ky_ca LEFT JOIN public.ca_lam cl ON cl.id=dkc.id_ca_lam
            WHERE b.scanner_id=@id ORDER BY b.created_at DESC LIMIT 30;
            """))
        {
            cmd.Parameters.AddWithValue("id", scannerId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) events.Add(new(r.GetString(0),r.GetString(1),r.GetFieldValue<DateTimeOffset>(2),r.GetString(3)));
        }
        return new(employees, events);
    }

    private static void Actor(NpgsqlCommand cmd, long id, bool admin)
    {
        cmd.Parameters.AddWithValue("actorId", id); cmd.Parameters.AddWithValue("admin", admin);
    }
}
