using manage365.Routes.API.Attendance;
using Microsoft.Extensions.Options;
using Npgsql;

namespace manage365.Repositories.Attendance;

public sealed class PostgresShiftAttendanceRepository(
    NpgsqlDataSource dataSource,
    IOptions<AttendancePolicyOptions> options) : IShiftAttendanceRepository
{
    private const string UnassignedShiftName = "Chấm công chưa xếp ca";
    private readonly AttendancePolicyOptions _policy = options.Value;

    public async Task<List<EligibleShiftDto>> GetEligibleShiftsAsync(
        long employeeId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var timeZone = GetStoreTimeZone();
        var localTime = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        var today = DateOnly.FromDateTime(localTime.DateTime);
        var yesterday = today.AddDays(-1);

        const string sql = """
            SELECT dkc.id,
                   cl.id,
                   cl.loai_ca,
                   cl.ngay_lam,
                   cl.gio_bat_dau,
                   cl.gio_ket_thuc,
                   COALESCE(cl.qua_dem, FALSE),
                   cc.gio_vao,
                   cc.gio_ra,
                   cc.so_gio_thuc_te
            FROM public.dang_ky_ca AS dkc
            JOIN public.ca_lam AS cl ON cl.id = dkc.id_ca_lam
            LEFT JOIN public.cham_cong AS cc ON cc.id_dang_ky_ca = dkc.id
            WHERE dkc.id_nhan_vien = @employeeId
              AND (dkc.trang_thai IN ('Approved', 'Pending') OR dkc.trang_thai IS NULL)
              AND (
                  cl.ngay_lam = @today
                  OR (cl.ngay_lam = @yesterday AND COALESCE(cl.qua_dem, FALSE) = TRUE)
              )
            ORDER BY cl.ngay_lam, cl.gio_bat_dau;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("employeeId", employeeId);
        command.Parameters.AddWithValue("today", today.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("yesterday", yesterday.ToDateTime(TimeOnly.MinValue));

        var results = new List<EligibleShiftDto>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var shiftDate = DateOnly.FromDateTime(reader.GetDateTime(3));
                DateTimeOffset? checkInAt = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7);
                DateTimeOffset? checkOutAt = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8);
                decimal? actualHours = reader.IsDBNull(9) ? null : reader.GetDecimal(9);
                var state = BuildActionState(checkInAt, checkOutAt, actualHours, timeZone, false);

                results.Add(new EligibleShiftDto(
                    ShiftAssignmentId: reader.GetInt64(0),
                    ShiftId: reader.GetInt64(1),
                    ShiftName: reader.GetString(2),
                    ShiftDate: shiftDate.ToString("dd/MM/yyyy"),
                    StartTime: reader.GetTimeSpan(4).ToString(@"hh\:mm"),
                    EndTime: reader.GetTimeSpan(5).ToString(@"hh\:mm"),
                    IsOvernight: reader.GetBoolean(6),
                    Status: state.Status,
                    CheckInAt: checkInAt,
                    CheckOutAt: checkOutAt,
                    ActualHours: actualHours,
                    AllowedAction: state.AllowedAction,
                    CanPerformAction: state.CanPerformAction,
                    ActionHint: state.ActionHint));
            }
        }

        var unassigned = await GetUnassignedOptionAsync(employeeId, today, timeZone, cancellationToken);
        if (results.Count == 0 || unassigned.Status == "CheckedIn")
        {
            results.Add(unassigned);
        }

        return results;
    }

    public Task<SubmitAttendanceResult> SubmitAttendanceAsync(
        long? shiftAssignmentId,
        long employeeId,
        string action,
        ValidatedAttendanceLocation location,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        return shiftAssignmentId.HasValue
            ? SubmitScheduledAttendanceAsync(
                shiftAssignmentId.Value,
                employeeId,
                action,
                location,
                nowUtc,
                cancellationToken)
            : SubmitUnassignedAttendanceAsync(employeeId, action, location, nowUtc, cancellationToken);
    }

    public async Task<List<EmployeeShiftRecordDto>> GetAttendanceHistoryAsync(
        long employeeId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT cc.id,
                   cc.id_dang_ky_ca,
                   COALESCE(cl.loai_ca, @unassignedShiftName),
                   cc.ngay_cham_cong,
                   cc.gio_vao,
                   cc.gio_ra,
                   cc.so_gio_thuc_te,
                   COALESCE(cc.trang_thai, 'Completed')
            FROM public.cham_cong AS cc
            LEFT JOIN public.dang_ky_ca AS dkc ON dkc.id = cc.id_dang_ky_ca
            LEFT JOIN public.ca_lam AS cl ON cl.id = dkc.id_ca_lam
            WHERE cc.id_nhan_vien = @employeeId
            ORDER BY cc.gio_vao DESC
            LIMIT @limit;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("employeeId", employeeId);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("unassignedShiftName", UnassignedShiftName);

        var records = new List<EmployeeShiftRecordDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new EmployeeShiftRecordDto(
                AttendanceId: reader.GetInt64(0),
                ShiftAssignmentId: reader.IsDBNull(1) ? null : reader.GetInt64(1),
                ShiftName: reader.GetString(2),
                ShiftDate: DateOnly.FromDateTime(reader.GetDateTime(3)).ToString("dd/MM/yyyy"),
                CheckInAt: reader.GetFieldValue<DateTimeOffset>(4),
                CheckOutAt: reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                ActualHours: reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                Status: reader.GetString(7)));
        }

        return records;
    }

    public async Task<AdminAttendanceSnapshotDto> GetAdminSnapshotAsync(
        DateTimeOffset nowUtc,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var timeZone = GetStoreTimeZone();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime);

        const string sql = """
            SELECT cc.id,
                   cc.id_nhan_vien,
                   nv.ho_ten,
                   nv.email,
                   COALESCE(cl.loai_ca, @unassignedShiftName),
                   cc.gio_vao,
                   cc.gio_ra,
                   cc.so_gio_thuc_te,
                   COALESCE(cc.trang_thai, 'CheckedIn'),
                   check_in_location.store_code,
                   check_in_location.latitude,
                   check_in_location.longitude,
                   check_in_location.accuracy_meters,
                   check_in_location.distance_meters,
                   check_in_location.allowed_radius_meters,
                   check_in_location.captured_at,
                   check_in_location.is_mocked,
                   check_in_location.is_within_geofence,
                   check_out_location.store_code,
                   check_out_location.latitude,
                   check_out_location.longitude,
                   check_out_location.accuracy_meters,
                   check_out_location.distance_meters,
                   check_out_location.allowed_radius_meters,
                   check_out_location.captured_at,
                   check_out_location.is_mocked,
                   check_out_location.is_within_geofence
            FROM public.cham_cong AS cc
            JOIN public.nhan_vien AS nv ON nv.id = cc.id_nhan_vien
            LEFT JOIN public.dang_ky_ca AS dkc ON dkc.id = cc.id_dang_ky_ca
            LEFT JOIN public.ca_lam AS cl ON cl.id = dkc.id_ca_lam
            LEFT JOIN LATERAL (
                SELECT store_code, latitude, longitude, accuracy_meters,
                       distance_meters, allowed_radius_meters, captured_at,
                       is_mocked, is_within_geofence
                FROM public.vi_tri_cham_cong
                WHERE id_cham_cong = cc.id AND hanh_dong = 'CHECK_IN'
                ORDER BY id DESC
                LIMIT 1
            ) AS check_in_location ON TRUE
            LEFT JOIN LATERAL (
                SELECT store_code, latitude, longitude, accuracy_meters,
                       distance_meters, allowed_radius_meters, captured_at,
                       is_mocked, is_within_geofence
                FROM public.vi_tri_cham_cong
                WHERE id_cham_cong = cc.id AND hanh_dong = 'CHECK_OUT'
                ORDER BY id DESC
                LIMIT 1
            ) AS check_out_location ON TRUE
            WHERE cc.ngay_cham_cong = @today
            ORDER BY cc.gio_vao DESC
            LIMIT @limit;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("today", today.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("unassignedShiftName", UnassignedShiftName);

        var records = new List<AdminAttendanceRecordDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new AdminAttendanceRecordDto(
                AttendanceId: reader.GetInt64(0),
                EmployeeId: reader.GetInt64(1),
                EmployeeName: reader.GetString(2),
                EmployeeEmail: reader.IsDBNull(3) ? null : reader.GetString(3),
                ShiftName: reader.GetString(4),
                CheckInAt: reader.GetFieldValue<DateTimeOffset>(5),
                CheckOutAt: reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                ActualHours: reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                Status: reader.GetString(8),
                CheckInLocation: ReadLocation(reader, 9),
                CheckOutLocation: ReadLocation(reader, 18)));
        }

        return new AdminAttendanceSnapshotDto(
            ServerTimeUtc: nowUtc,
            TotalToday: records.Count,
            CheckedIn: records.Count(record => record.CheckOutAt is null),
            Completed: records.Count(record => record.CheckOutAt is not null),
            Records: records);
    }

    private async Task<EligibleShiftDto> GetUnassignedOptionAsync(
        long employeeId,
        DateOnly today,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT gio_vao, gio_ra, so_gio_thuc_te
            FROM public.cham_cong
            WHERE id_nhan_vien = @employeeId
              AND ngay_cham_cong = @today
              AND id_dang_ky_ca IS NULL
            LIMIT 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("employeeId", employeeId);
        command.Parameters.AddWithValue("today", today.ToDateTime(TimeOnly.MinValue));

        DateTimeOffset? checkInAt = null;
        DateTimeOffset? checkOutAt = null;
        decimal? actualHours = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            checkInAt = reader.GetFieldValue<DateTimeOffset>(0);
            checkOutAt = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
            actualHours = reader.IsDBNull(2) ? null : reader.GetDecimal(2);
        }

        var state = BuildActionState(checkInAt, checkOutAt, actualHours, timeZone, true);
        return new EligibleShiftDto(
            ShiftAssignmentId: null,
            ShiftId: null,
            ShiftName: UnassignedShiftName,
            ShiftDate: today.ToString("dd/MM/yyyy"),
            StartTime: "--:--",
            EndTime: "--:--",
            IsOvernight: false,
            Status: state.Status,
            CheckInAt: checkInAt,
            CheckOutAt: checkOutAt,
            ActualHours: actualHours,
            AllowedAction: state.AllowedAction,
            CanPerformAction: state.CanPerformAction,
            ActionHint: state.ActionHint);
    }

    private async Task<SubmitAttendanceResult> SubmitUnassignedAttendanceAsync(
        long employeeId,
        string action,
        ValidatedAttendanceLocation location,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        action = action.Trim().ToUpperInvariant();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, GetStoreTimeZone()).DateTime);

        const string currentSql = """
            SELECT id, gio_vao, gio_ra
            FROM public.cham_cong
            WHERE id_nhan_vien = @employeeId
              AND ngay_cham_cong = @today
              AND id_dang_ky_ca IS NULL
            LIMIT 1;
            """;

        long? attendanceId = null;
        DateTimeOffset? checkInAt = null;
        DateTimeOffset? checkOutAt = null;
        await using (var command = dataSource.CreateCommand(currentSql))
        {
            command.Parameters.AddWithValue("employeeId", employeeId);
            command.Parameters.AddWithValue("today", today.ToDateTime(TimeOnly.MinValue));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                attendanceId = reader.GetInt64(0);
                checkInAt = reader.GetFieldValue<DateTimeOffset>(1);
                checkOutAt = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2);
            }
        }

        if (action == "CHECK_IN")
        {
            if (checkInAt is not null)
            {
                return Failure("Bạn đã check-in hôm nay.", checkInAt.Value, "already_checked_in");
            }

            const string insertSql = """
                WITH inserted_attendance AS (
                    INSERT INTO public.cham_cong
                        (id_dang_ky_ca, id_nhan_vien, ngay_cham_cong, gio_vao,
                         trang_thai, phuong_thuc_cham_cong, created_at, updated_at)
                    VALUES
                        (NULL, @employeeId, @today, @nowUtc,
                         'CheckedIn', 'QR', @nowUtc, @nowUtc)
                    ON CONFLICT (id_nhan_vien, ngay_cham_cong)
                        WHERE id_dang_ky_ca IS NULL
                    DO NOTHING
                    RETURNING id
                )
                INSERT INTO public.vi_tri_cham_cong
                    (id_cham_cong, id_dia_diem, hanh_dong, store_code,
                     latitude, longitude, location, accuracy_meters,
                     distance_meters, allowed_radius_meters, captured_at,
                     received_at, is_mocked, is_within_geofence)
                SELECT id, @storeLocationId, 'CHECK_IN', @storeCode,
                       @latitude, @longitude,
                       extensions.ST_SetSRID(extensions.ST_MakePoint(@longitude, @latitude), 4326)::extensions.geography,
                       @accuracyMeters, @distanceMeters, @allowedRadiusMeters,
                       @capturedAtUtc, @receivedAtUtc, @isMocked, @isWithinGeofence
                FROM inserted_attendance
                RETURNING id_cham_cong;
                """;

            await using var command = dataSource.CreateCommand(insertSql);
            command.Parameters.AddWithValue("employeeId", employeeId);
            command.Parameters.AddWithValue("today", today.ToDateTime(TimeOnly.MinValue));
            command.Parameters.AddWithValue("nowUtc", nowUtc);
            AddLocationParameters(command, location);
            var inserted = await command.ExecuteScalarAsync(cancellationToken);
            return inserted is null
                ? Failure("Bạn đã check-in hôm nay.", nowUtc, "duplicate_check_in")
                : Success("Check-in thành công!", nowUtc, null);
        }

        if (action == "CHECK_OUT")
        {
            if (attendanceId is null || checkInAt is null)
            {
                return Failure("Bạn chưa check-in hôm nay.", nowUtc, "not_checked_in");
            }

            if (checkOutAt is not null)
            {
                return Failure("Bạn đã check-out hôm nay.", checkOutAt.Value, "already_checked_out");
            }

            var actualHours = CalculateActualHours(checkInAt.Value, nowUtc);
            const string updateSql = """
                WITH updated_attendance AS (
                    UPDATE public.cham_cong
                    SET gio_ra = @nowUtc,
                        so_gio_thuc_te = @hours,
                        trang_thai = 'Completed',
                        updated_at = @nowUtc
                    WHERE id = @attendanceId AND gio_ra IS NULL
                    RETURNING id
                )
                INSERT INTO public.vi_tri_cham_cong
                    (id_cham_cong, id_dia_diem, hanh_dong, store_code,
                     latitude, longitude, location, accuracy_meters,
                     distance_meters, allowed_radius_meters, captured_at,
                     received_at, is_mocked, is_within_geofence)
                SELECT id, @storeLocationId, 'CHECK_OUT', @storeCode,
                       @latitude, @longitude,
                       extensions.ST_SetSRID(extensions.ST_MakePoint(@longitude, @latitude), 4326)::extensions.geography,
                       @accuracyMeters, @distanceMeters, @allowedRadiusMeters,
                       @capturedAtUtc, @receivedAtUtc, @isMocked, @isWithinGeofence
                FROM updated_attendance
                RETURNING id_cham_cong;
                """;

            await using var command = dataSource.CreateCommand(updateSql);
            command.Parameters.AddWithValue("nowUtc", nowUtc);
            command.Parameters.AddWithValue("hours", actualHours);
            command.Parameters.AddWithValue("attendanceId", attendanceId.Value);
            AddLocationParameters(command, location);
            var updated = await command.ExecuteScalarAsync(cancellationToken);
            return updated is null
                ? Failure("Không thể cập nhật check-out.", nowUtc, "checkout_failed")
                : Success($"Check-out thành công ({actualHours} giờ)!", nowUtc, actualHours);
        }

        return Failure($"Hành động không hợp lệ: '{action}'.", nowUtc, "invalid_action");
    }

    private async Task<SubmitAttendanceResult> SubmitScheduledAttendanceAsync(
        long shiftAssignmentId,
        long employeeId,
        string action,
        ValidatedAttendanceLocation location,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        action = action.Trim().ToUpperInvariant();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, GetStoreTimeZone()).DateTime);

        const string checkSql = """
            SELECT cl.loai_ca, cc.id, cc.gio_vao, cc.gio_ra
            FROM public.dang_ky_ca AS dkc
            JOIN public.ca_lam AS cl ON cl.id = dkc.id_ca_lam
            LEFT JOIN public.cham_cong AS cc ON cc.id_dang_ky_ca = dkc.id
            WHERE dkc.id = @assignmentId
              AND dkc.id_nhan_vien = @employeeId
            LIMIT 1;
            """;

        string shiftName;
        DateTimeOffset? checkInAt;
        DateTimeOffset? checkOutAt;
        await using (var command = dataSource.CreateCommand(checkSql))
        {
            command.Parameters.AddWithValue("assignmentId", shiftAssignmentId);
            command.Parameters.AddWithValue("employeeId", employeeId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new SubmitAttendanceResult(
                    false,
                    "Không tìm thấy phân công ca làm việc này của bạn.",
                    null,
                    nowUtc,
                    null,
                    "shift_not_found");
            }

            shiftName = reader.GetString(0);
            checkInAt = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2);
            checkOutAt = reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3);
        }

        if (action == "CHECK_IN")
        {
            if (checkInAt is not null)
            {
                return new SubmitAttendanceResult(false, "Ca này đã được check-in.", shiftName, checkInAt.Value, null, "already_checked_in");
            }

            const string insertSql = """
                WITH inserted_attendance AS (
                    INSERT INTO public.cham_cong
                        (id_dang_ky_ca, id_nhan_vien, ngay_cham_cong, gio_vao,
                         trang_thai, phuong_thuc_cham_cong, created_at, updated_at)
                    VALUES
                        (@assignmentId, @employeeId, @today, @nowUtc,
                         'CheckedIn', 'QR', @nowUtc, @nowUtc)
                    ON CONFLICT (id_dang_ky_ca) DO NOTHING
                    RETURNING id
                )
                INSERT INTO public.vi_tri_cham_cong
                    (id_cham_cong, id_dia_diem, hanh_dong, store_code,
                     latitude, longitude, location, accuracy_meters,
                     distance_meters, allowed_radius_meters, captured_at,
                     received_at, is_mocked, is_within_geofence)
                SELECT id, @storeLocationId, 'CHECK_IN', @storeCode,
                       @latitude, @longitude,
                       extensions.ST_SetSRID(extensions.ST_MakePoint(@longitude, @latitude), 4326)::extensions.geography,
                       @accuracyMeters, @distanceMeters, @allowedRadiusMeters,
                       @capturedAtUtc, @receivedAtUtc, @isMocked, @isWithinGeofence
                FROM inserted_attendance
                RETURNING id_cham_cong;
                """;

            await using var command = dataSource.CreateCommand(insertSql);
            command.Parameters.AddWithValue("assignmentId", shiftAssignmentId);
            command.Parameters.AddWithValue("employeeId", employeeId);
            command.Parameters.AddWithValue("today", today.ToDateTime(TimeOnly.MinValue));
            command.Parameters.AddWithValue("nowUtc", nowUtc);
            AddLocationParameters(command, location);
            var inserted = await command.ExecuteScalarAsync(cancellationToken);
            return inserted is null
                ? new SubmitAttendanceResult(false, "Đã tồn tại chấm công cho ca này.", shiftName, nowUtc, null, "duplicate_check_in")
                : new SubmitAttendanceResult(true, $"Check-in thành công {shiftName}!", shiftName, nowUtc, null, null);
        }

        if (action == "CHECK_OUT")
        {
            if (checkInAt is null)
            {
                return new SubmitAttendanceResult(false, "Bạn chưa check-in ca này.", shiftName, nowUtc, null, "not_checked_in");
            }

            if (checkOutAt is not null)
            {
                return new SubmitAttendanceResult(false, "Ca này đã check-out.", shiftName, checkOutAt.Value, null, "already_checked_out");
            }

            var actualHours = CalculateActualHours(checkInAt.Value, nowUtc);
            const string updateSql = """
                WITH updated_attendance AS (
                    UPDATE public.cham_cong
                    SET gio_ra = @nowUtc,
                        so_gio_thuc_te = @hours,
                        trang_thai = 'Completed',
                        updated_at = @nowUtc
                    WHERE id_dang_ky_ca = @assignmentId AND gio_ra IS NULL
                    RETURNING id
                )
                INSERT INTO public.vi_tri_cham_cong
                    (id_cham_cong, id_dia_diem, hanh_dong, store_code,
                     latitude, longitude, location, accuracy_meters,
                     distance_meters, allowed_radius_meters, captured_at,
                     received_at, is_mocked, is_within_geofence)
                SELECT id, @storeLocationId, 'CHECK_OUT', @storeCode,
                       @latitude, @longitude,
                       extensions.ST_SetSRID(extensions.ST_MakePoint(@longitude, @latitude), 4326)::extensions.geography,
                       @accuracyMeters, @distanceMeters, @allowedRadiusMeters,
                       @capturedAtUtc, @receivedAtUtc, @isMocked, @isWithinGeofence
                FROM updated_attendance
                RETURNING id_cham_cong;
                """;

            await using var command = dataSource.CreateCommand(updateSql);
            command.Parameters.AddWithValue("nowUtc", nowUtc);
            command.Parameters.AddWithValue("hours", actualHours);
            command.Parameters.AddWithValue("assignmentId", shiftAssignmentId);
            AddLocationParameters(command, location);
            var updated = await command.ExecuteScalarAsync(cancellationToken);
            return updated is null
                ? new SubmitAttendanceResult(false, "Không thể cập nhật check-out.", shiftName, nowUtc, null, "checkout_failed")
                : new SubmitAttendanceResult(true, $"Check-out thành công {shiftName} ({actualHours} giờ)!", shiftName, nowUtc, actualHours, null);
        }

        return new SubmitAttendanceResult(false, $"Hành động không hợp lệ: '{action}'.", shiftName, nowUtc, null, "invalid_action");
    }

    private static (string Status, string AllowedAction, bool CanPerformAction, string ActionHint) BuildActionState(
        DateTimeOffset? checkInAt,
        DateTimeOffset? checkOutAt,
        decimal? actualHours,
        TimeZoneInfo timeZone,
        bool unassigned)
    {
        if (checkInAt is null)
        {
            return (
                unassigned ? "Unscheduled" : "Assigned",
                "CHECK_IN",
                true,
                unassigned ? "Sẵn sàng chấm công chưa xếp ca" : "Sẵn sàng vào ca");
        }

        if (checkOutAt is null)
        {
            var localCheckIn = TimeZoneInfo.ConvertTime(checkInAt.Value, timeZone);
            return ("CheckedIn", "CHECK_OUT", true, $"Đã vào lúc {localCheckIn:HH:mm}. Có thể check-out.");
        }

        return ("Completed", "NONE", false, $"Đã hoàn tất ({actualHours:0.##}h)");
    }

    private SubmitAttendanceResult Success(string message, DateTimeOffset timestamp, decimal? hours) =>
        new(true, message, UnassignedShiftName, timestamp, hours, null);

    private SubmitAttendanceResult Failure(string message, DateTimeOffset timestamp, string errorCode) =>
        new(false, message, UnassignedShiftName, timestamp, null, errorCode);

    private static decimal CalculateActualHours(DateTimeOffset checkInAt, DateTimeOffset checkOutAt) =>
        Math.Max(0.01m, Math.Round((decimal)(checkOutAt - checkInAt).TotalHours, 2));

    private static void AddLocationParameters(NpgsqlCommand command, ValidatedAttendanceLocation location)
    {
        command.Parameters.AddWithValue("storeLocationId", location.StoreLocationId);
        command.Parameters.AddWithValue("storeCode", location.StoreCode);
        command.Parameters.AddWithValue("latitude", location.Latitude);
        command.Parameters.AddWithValue("longitude", location.Longitude);
        command.Parameters.AddWithValue("accuracyMeters", location.AccuracyMeters);
        command.Parameters.AddWithValue("distanceMeters", location.DistanceMeters);
        command.Parameters.AddWithValue("allowedRadiusMeters", location.AllowedRadiusMeters);
        command.Parameters.AddWithValue("capturedAtUtc", location.CapturedAtUtc);
        command.Parameters.AddWithValue("receivedAtUtc", location.ReceivedAtUtc);
        command.Parameters.AddWithValue("isMocked", location.IsMocked);
        command.Parameters.AddWithValue("isWithinGeofence", location.IsWithinGeofence);
    }

    private static AttendanceLocationDto? ReadLocation(NpgsqlDataReader reader, int offset)
    {
        if (reader.IsDBNull(offset))
        {
            return null;
        }

        return new AttendanceLocationDto(
            StoreCode: reader.GetString(offset),
            Latitude: reader.GetDouble(offset + 1),
            Longitude: reader.GetDouble(offset + 2),
            AccuracyMeters: reader.GetDouble(offset + 3),
            DistanceMeters: reader.GetDouble(offset + 4),
            AllowedRadiusMeters: reader.GetDouble(offset + 5),
            CapturedAtUtc: reader.GetFieldValue<DateTimeOffset>(offset + 6),
            IsMocked: reader.GetBoolean(offset + 7),
            IsWithinGeofence: reader.GetBoolean(offset + 8));
    }

    private TimeZoneInfo GetStoreTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(_policy.TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.Utc;
            }
        }
    }
}
