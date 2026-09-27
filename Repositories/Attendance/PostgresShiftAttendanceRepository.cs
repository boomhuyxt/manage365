using manage365.Routes.API.Attendance;
using Microsoft.Extensions.Options;
using Npgsql;

namespace manage365.Repositories.Attendance;

public sealed class PostgresShiftAttendanceRepository(
    NpgsqlDataSource dataSource,
    IOptions<AttendancePolicyOptions> options) : IShiftAttendanceRepository
{
    private readonly AttendancePolicyOptions _policy = options.Value;

    private TimeZoneInfo GetStoreTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(_policy.TimeZone);
        }
        catch
        {
            try
            {
                // Fallback cho Windows
                return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch
            {
                return TimeZoneInfo.Utc;
            }
        }
    }



    public async Task<List<EligibleShiftDto>> GetEligibleShiftsAsync(
        long employeeId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
var tz = GetStoreTimeZone();
        var localTime = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var today = DateOnly.FromDateTime(localTime.DateTime);
        var yesterday = today.AddDays(-1);
        const string sql = """
            SELECT dkc.id AS dang_ky_ca_id,
                   cl.id AS ca_lam_id,
                   cl.loai_ca,
                   cl.ngay_lam,
                   cl.gio_bat_dau,
                   cl.gio_ket_thuc,
                   COALESCE(cl.qua_dem, FALSE) AS qua_dem,
                   cc.id AS cham_cong_id,
                   cc.gio_vao,
                   cc.gio_ra,
                   cc.so_gio_thuc_te,
                   COALESCE(cc.trang_thai, 'Assigned') AS trang_thai_cham_cong
            FROM public.dang_ky_ca AS dkc
            JOIN public.ca_lam AS cl ON cl.id = dkc.id_ca_lam
            LEFT JOIN public.cham_cong AS cc ON cc.id_dang_ky_ca = dkc.id
            WHERE dkc.id_nhan_vien = @employeeId
              AND (dkc.trang_thai = 'Approved' OR dkc.trang_thai = 'Pending' OR dkc.trang_thai IS NULL)
              AND (
                  cl.ngay_lam = @today
                  OR (cl.ngay_lam = @yesterday AND COALESCE(cl.qua_dem, FALSE) = TRUE)
              )
            ORDER BY cl.ngay_lam ASC, cl.gio_bat_dau ASC;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("employeeId", employeeId);
        command.Parameters.AddWithValue("today", today.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("yesterday", yesterday.ToDateTime(TimeOnly.MinValue));

        var results = new List<EligibleShiftDto>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var dkcId = reader.GetInt64(0);
            var caLamId = reader.GetInt64(1);
            var loaiCa = reader.GetString(2);
            var ngayLam = DateOnly.FromDateTime(reader.GetDateTime(3));
            var gioBatDau = reader.GetTimeSpan(4);
            var gioKetThuc = reader.GetTimeSpan(5);
            var quaDem = reader.GetBoolean(6);

            DateTimeOffset? gioVao = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8);
            DateTimeOffset? gioRa = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9);
            decimal? soGioThucTe = reader.IsDBNull(10) ? null : reader.GetDecimal(10);
            var trangThaiChamCong = reader.GetString(11);

            // Xác định trạng thái và hành động khả dụng
            string status;
            string allowedAction;
            bool canPerformAction;
            string actionHint;

            if (gioVao is null)
            {
                status = "Assigned";
                allowedAction = "CHECK_IN";
                canPerformAction = true;
                actionHint = "Sẵn sàng vào ca";
            }
            else if (gioRa is null)
            {
                status = "CheckedIn";
                allowedAction = "CHECK_OUT";
                canPerformAction = true;
                actionHint = $"Đã vào ca lúc {gioVao.Value.ToOffset(tz.GetUtcOffset(gioVao.Value)):HH:mm}. Có thể tan ca.";
            }
            else
            {
                status = "Completed";
                allowedAction = "NONE";
                canPerformAction = false;
                actionHint = $"Đã hoàn tất ca ({soGioThucTe:0.##}h)";
            }

            var shiftDateStr = ngayLam.ToString("dd/MM/yyyy");
            var startTimeStr = gioBatDau.ToString(@"hh\:mm");
            var endTimeStr = gioKetThuc.ToString(@"hh\:mm");

            results.Add(new EligibleShiftDto(
                ShiftAssignmentId: dkcId,
                ShiftId: caLamId,
                ShiftName: loaiCa,
                ShiftDate: shiftDateStr,
                StartTime: startTimeStr,
                EndTime: endTimeStr,
                IsOvernight: quaDem,
                Status: status,
                CheckInAt: gioVao,
                CheckOutAt: gioRa,
                ActualHours: soGioThucTe,
                AllowedAction: allowedAction,
                CanPerformAction: canPerformAction,
                ActionHint: actionHint));
        }

        return results;
    }

    public async Task<SubmitAttendanceResult> SubmitAttendanceAsync(
        long shiftAssignmentId,
        long employeeId,
        string action,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        action = action.Trim().ToUpperInvariant();

        // 1. Kiểm tra quyền sở hữu ca làm
        const string checkShiftSql = """
            SELECT dkc.id, cl.loai_ca, cc.id, cc.gio_vao, cc.gio_ra
            FROM public.dang_ky_ca AS dkc
            JOIN public.ca_lam AS cl ON cl.id = dkc.id_ca_lam
            LEFT JOIN public.cham_cong AS cc ON cc.id_dang_ky_ca = dkc.id
            WHERE dkc.id = @dkcId AND dkc.id_nhan_vien = @employeeId
            LIMIT 1;
            """;

        await using var checkCmd = dataSource.CreateCommand(checkShiftSql);
        checkCmd.Parameters.AddWithValue("dkcId", shiftAssignmentId);
        checkCmd.Parameters.AddWithValue("employeeId", employeeId);

        await using var reader = await checkCmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new SubmitAttendanceResult(false, "Không tìm thấy phân công ca làm việc này của bạn.", null, nowUtc, null, "shift_not_found");
        }

        var shiftName = reader.GetString(1);
        var hasRecord = !reader.IsDBNull(2);
        DateTimeOffset? existingCheckIn = hasRecord && !reader.IsDBNull(3) ? reader.GetFieldValue<DateTimeOffset>(3) : null;
        DateTimeOffset? existingCheckOut = hasRecord && !reader.IsDBNull(4) ? reader.GetFieldValue<DateTimeOffset>(4) : null;
        await reader.CloseAsync();

        if (action == "CHECK_IN")
        {
            if (existingCheckIn is not null)
            {
                return new SubmitAttendanceResult(false, "Ca làm việc này đã được Check-in trước đó.", shiftName, existingCheckIn.Value, null, "already_checked_in");
            }

            const string insertSql = """
                INSERT INTO public.cham_cong (id_dang_ky_ca, gio_vao, trang_thai, phuong_thuc_cham_cong, created_at, updated_at)
                VALUES (@dkcId, @nowUtc, 'CheckedIn', 'QR', @nowUtc, @nowUtc)
                ON CONFLICT (id_dang_ky_ca) DO NOTHING
                RETURNING id;
                """;

            await using var insertCmd = dataSource.CreateCommand(insertSql);
            insertCmd.Parameters.AddWithValue("dkcId", shiftAssignmentId);
            insertCmd.Parameters.AddWithValue("nowUtc", nowUtc);

            var result = await insertCmd.ExecuteScalarAsync(cancellationToken);
            if (result is null)
            {
                return new SubmitAttendanceResult(false, "Đã tồn tại bản ghi chấm công cho ca này.", shiftName, nowUtc, null, "duplicate_check_in");
            }

            return new SubmitAttendanceResult(true, $"Check-in thành công {shiftName}!", shiftName, nowUtc, null, null);
        }
        else if (action == "CHECK_OUT")
        {
            if (existingCheckIn is null)
            {
                return new SubmitAttendanceResult(false, "Bạn chưa thực hiện Check-in ca này.", shiftName, nowUtc, null, "not_checked_in");
            }

            if (existingCheckOut is not null)
            {
                return new SubmitAttendanceResult(false, "Ca làm việc này đã hoàn tất Check-out.", shiftName, existingCheckOut.Value, null, "already_checked_out");
            }

            var actualHours = Math.Round((decimal)(nowUtc - existingCheckIn.Value).TotalHours, 2);
            if (actualHours < 0.01m) actualHours = 0.01m;

            const string updateSql = """
                UPDATE public.cham_cong
                SET gio_ra = @nowUtc,
                    so_gio_thuc_te = @hours,
                    trang_thai = 'Completed',
                    updated_at = @nowUtc
                WHERE id_dang_ky_ca = @dkcId AND gio_ra IS NULL;
                """;

            await using var updateCmd = dataSource.CreateCommand(updateSql);
            updateCmd.Parameters.AddWithValue("nowUtc", nowUtc);
            updateCmd.Parameters.AddWithValue("hours", actualHours);
            updateCmd.Parameters.AddWithValue("dkcId", shiftAssignmentId);

            var rowsAffected = await updateCmd.ExecuteNonQueryAsync(cancellationToken);
            if (rowsAffected == 0)
            {
                return new SubmitAttendanceResult(false, "Không thể cập nhật Check-out (ca đã đóng).", shiftName, nowUtc, null, "checkout_failed");
            }

            return new SubmitAttendanceResult(true, $"Check-out thành công {shiftName} ({actualHours} giờ)!", shiftName, nowUtc, actualHours, null);
        }

        return new SubmitAttendanceResult(false, $"Hành động không hợp lệ: '{action}'.", shiftName, nowUtc, null, "invalid_action");
    }

    public async Task<List<EmployeeShiftRecordDto>> GetAttendanceHistoryAsync(
        long employeeId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT cc.id,
                   dkc.id AS dkc_id,
                   cl.loai_ca,
                   cl.ngay_lam,
                   cc.gio_vao,
                   cc.gio_ra,
                   cc.so_gio_thuc_te,
                   COALESCE(cc.trang_thai, 'Completed') AS trang_thai
            FROM public.cham_cong AS cc
            JOIN public.dang_ky_ca AS dkc ON dkc.id = cc.id_dang_ky_ca
            JOIN public.ca_lam AS cl ON cl.id = dkc.id_ca_lam
            WHERE dkc.id_nhan_vien = @employeeId
            ORDER BY cc.gio_vao DESC
            LIMIT @limit;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("employeeId", employeeId);
        command.Parameters.AddWithValue("limit", limit);

        var list = new List<EmployeeShiftRecordDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            var dkcId = reader.GetInt64(1);
            var loaiCa = reader.GetString(2);
            var ngayLam = DateOnly.FromDateTime(reader.GetDateTime(3));
            var gioVao = reader.GetFieldValue<DateTimeOffset>(4);
            DateTimeOffset? gioRa = reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5);
            decimal? hours = reader.IsDBNull(6) ? null : reader.GetDecimal(6);
            var trangThai = reader.GetString(7);

            list.Add(new EmployeeShiftRecordDto(
                AttendanceId: id,
                ShiftAssignmentId: dkcId,
                ShiftName: loaiCa,
                ShiftDate: ngayLam.ToString("dd/MM/yyyy"),
                CheckInAt: gioVao,
                CheckOutAt: gioRa,
                ActualHours: hours,
                Status: trangThai));
        }

        return list;
    }
}
