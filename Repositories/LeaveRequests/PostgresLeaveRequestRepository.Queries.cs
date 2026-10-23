using manage365.Routes.API.LeaveRequests;
using Npgsql;
using NpgsqlTypes;

namespace manage365.Repositories.LeaveRequests;

public sealed partial class PostgresLeaveRequestRepository
{
    private sealed record PreviewCalcResult(
        string RequestedPeriod,
        int? CalendarDays,
        int ScheduledLeaveMinutes,
        string ScheduleBasis,
        bool HasExpandedCoverage,
        IReadOnlyList<LeaveIntervalDto> EffectiveIntervals,
        IReadOnlyList<AffectedAssignmentDto> AffectedAssignments,
        IReadOnlyList<LeaveDayAllocationDto> DayAllocations,
        IReadOnlyList<string> Warnings);

    private async Task<PreviewCalcResult> ComputePreviewCalculationsAsync(
        long employeeId,
        CreateLeaveRequestInput input,
        long? excludeLeaveRequestId,
        CancellationToken ct, NpgsqlConnection? connection = null)
    {
        var warnings = new List<string>();

        if (input.Unit == LeaveUnits.Shift)
        {
            const string shiftSql = """
                SELECT dkc.id, cl.id, cl.loai_ca, cl.ngay_lam, cl.gio_bat_dau, cl.gio_ket_thuc, cl.qua_dem
                FROM public.dang_ky_ca dkc
                JOIN public.ca_lam cl ON cl.id = dkc.id_ca_lam
                WHERE dkc.id = @assignmentId
                  AND dkc.id_nhan_vien = @employeeId
                  AND COALESCE(dkc.trang_thai, 'Approved') != 'Cancelled';
                """;

            await using var cmd = connection is null ? dataSource.CreateCommand(shiftSql) : new NpgsqlCommand(shiftSql, connection);
            cmd.Parameters.AddWithValue("assignmentId", input.ShiftAssignmentId!.Value);
            cmd.Parameters.AddWithValue("employeeId", employeeId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                throw new LeaveNotFoundException("shift_not_found", "Không tìm thấy phân công ca làm việc này của bạn hoặc ca đã bị hủy.");
            }

            var assignId = reader.GetInt64(0);
            var shiftId = reader.GetInt64(1);
            var shiftName = reader.GetString(2);
            var shiftDate = DateOnly.FromDateTime(reader.GetDateTime(3));
            var startTime = reader.GetTimeSpan(4);
            var endTime = reader.GetTimeSpan(5);
            var isOvernight = reader.GetBoolean(6);

            var startLocal = shiftDate.ToDateTime(TimeOnly.FromTimeSpan(startTime));
            var endLocal = (isOvernight ? shiftDate.AddDays(1) : shiftDate).ToDateTime(TimeOnly.FromTimeSpan(endTime));

            var startUtc = ConvertLocalToUtc(startLocal);
            var endUtc = ConvertLocalToUtc(endLocal);
            var durationMins = (int)Math.Round((endUtc - startUtc).TotalMinutes);

            var affected = new List<AffectedAssignmentDto>
            {
                new(
                    AssignmentId: assignId,
                    ShiftId: shiftId,
                    ShiftName: shiftName,
                    ShiftDate: shiftDate.ToString("yyyy-MM-dd"),
                    StartTime: startTime.ToString(@"hh\:mm"),
                    EndTime: endTime.ToString(@"hh\:mm"),
                    IsOvernight: isOvernight,
                    ScheduledMinutes: durationMins,
                    IsBeyondRequestedDates: false)
            };

            var dayAllocs = CalculateDayAllocations(affected);
            var intervals = new List<LeaveIntervalDto>
            {
                new(
                    StartUtc: startUtc,
                    EndUtc: endUtc,
                    StartLocal: startLocal.ToString("yyyy-MM-dd HH:mm"),
                    EndLocal: endLocal.ToString("yyyy-MM-dd HH:mm"))
            };

            var period = $"{shiftName} ngày {shiftDate:dd/MM/yyyy} ({startTime:hh\\:mm} - {endTime:hh\\:mm})";

            return new PreviewCalcResult(
                RequestedPeriod: period,
                CalendarDays: null,
                ScheduledLeaveMinutes: durationMins,
                ScheduleBasis: LeaveScheduleBases.Available,
                HasExpandedCoverage: false,
                EffectiveIntervals: intervals,
                AffectedAssignments: affected,
                DayAllocations: dayAllocs,
                Warnings: warnings);
        }

        if (input.Unit == LeaveUnits.Day)
        {
            var date = DateOnly.ParseExact(input.Date!, "yyyy-MM-dd");
            var baseStartLocal = date.ToDateTime(TimeOnly.MinValue);
            var baseEndLocal = date.AddDays(1).ToDateTime(TimeOnly.MinValue);

            var baseStartUtc = ConvertLocalToUtc(baseStartLocal);
            var baseEndUtc = ConvertLocalToUtc(baseEndLocal);

            // Fetch shifts of employee in window [date - 1, date + 1]
            var shifts = await GetEmployeeShiftsInDateWindowAsync(employeeId, date.AddDays(-1), date.AddDays(1), ct, connection);

            var affected = new List<AffectedAssignmentDto>();
            var rawIntervals = new List<(DateTimeOffset Start, DateTimeOffset End)>
            {
                (baseStartUtc, baseEndUtc)
            };

            var hasExpandedCoverage = false;

            foreach (var s in shifts)
            {
                // Check if shift intersects [baseStartUtc, baseEndUtc)
                if (s.StartUtc < baseEndUtc && baseStartUtc < s.EndUtc)
                {
                    var isBeyond = s.StartUtc < baseStartUtc || s.EndUtc > baseEndUtc;
                    if (isBeyond)
                    {
                        hasExpandedCoverage = true;
                    }

                    affected.Add(new AffectedAssignmentDto(
                        AssignmentId: s.AssignmentId,
                        ShiftId: s.ShiftId,
                        ShiftName: s.ShiftName,
                        ShiftDate: s.ShiftDate.ToString("yyyy-MM-dd"),
                        StartTime: s.StartTime.ToString(@"hh\:mm"),
                        EndTime: s.EndTime.ToString(@"hh\:mm"),
                        IsOvernight: s.IsOvernight,
                        ScheduledMinutes: s.ScheduledMinutes,
                        IsBeyondRequestedDates: isBeyond));

                    rawIntervals.Add((s.StartUtc, s.EndUtc));
                }
            }

            var merged = MergeIntervals(rawIntervals);
            var dayAllocs = CalculateDayAllocations(affected);
            var scheduledMins = affected.Sum(a => a.ScheduledMinutes);
            dayAllocs = IncludeUnscheduledDays(dayAllocs, date, date);
            var scheduleBasis = affected.Count > 0 ? LeaveScheduleBases.Available : LeaveScheduleBases.Missing;

            if (affected.Count == 0)
            {
                warnings.Add($"Ngày {date:dd/MM/yyyy} hiện chưa có ca làm việc nào được phân. Đơn nghỉ phép vẫn được tạo nhưng chưa có phút nghỉ theo lịch.");
            }

            return new PreviewCalcResult(
                RequestedPeriod: $"Cả ngày {date:dd/MM/yyyy}",
                CalendarDays: 1,
                ScheduledLeaveMinutes: scheduledMins,
                ScheduleBasis: scheduleBasis,
                HasExpandedCoverage: hasExpandedCoverage,
                EffectiveIntervals: merged,
                AffectedAssignments: affected,
                DayAllocations: dayAllocs,
                Warnings: warnings);
        }

        if (input.Unit == LeaveUnits.DateRange)
        {
            var startDate = DateOnly.ParseExact(input.StartDate!, "yyyy-MM-dd");
            var endDate = DateOnly.ParseExact(input.EndDate!, "yyyy-MM-dd");
            var calendarDays = endDate.DayNumber - startDate.DayNumber + 1;

            var baseStartLocal = startDate.ToDateTime(TimeOnly.MinValue);
            var baseEndLocal = endDate.AddDays(1).ToDateTime(TimeOnly.MinValue);

            var baseStartUtc = ConvertLocalToUtc(baseStartLocal);
            var baseEndUtc = ConvertLocalToUtc(baseEndLocal);

            var shifts = await GetEmployeeShiftsInDateWindowAsync(employeeId, startDate.AddDays(-1), endDate.AddDays(1), ct, connection);

            var affected = new List<AffectedAssignmentDto>();
            var rawIntervals = new List<(DateTimeOffset Start, DateTimeOffset End)>
            {
                (baseStartUtc, baseEndUtc)
            };

            var hasExpandedCoverage = false;
            var daysWithShifts = new HashSet<DateOnly>();

            foreach (var s in shifts)
            {
                if (s.StartUtc < baseEndUtc && baseStartUtc < s.EndUtc)
                {
                    var isBeyond = s.StartUtc < baseStartUtc || s.EndUtc > baseEndUtc;
                    if (isBeyond)
                    {
                        hasExpandedCoverage = true;
                    }

                    affected.Add(new AffectedAssignmentDto(
                        AssignmentId: s.AssignmentId,
                        ShiftId: s.ShiftId,
                        ShiftName: s.ShiftName,
                        ShiftDate: s.ShiftDate.ToString("yyyy-MM-dd"),
                        StartTime: s.StartTime.ToString(@"hh\:mm"),
                        EndTime: s.EndTime.ToString(@"hh\:mm"),
                        IsOvernight: s.IsOvernight,
                        ScheduledMinutes: s.ScheduledMinutes,
                        IsBeyondRequestedDates: isBeyond));

                    daysWithShifts.Add(s.ShiftDate);
                    rawIntervals.Add((s.StartUtc, s.EndUtc));
                }
            }

            var merged = MergeIntervals(rawIntervals);
            var dayAllocs = CalculateDayAllocations(affected);
            var scheduledMins = affected.Sum(a => a.ScheduledMinutes);

            daysWithShifts = dayAllocs.Where(d => d.ScheduledMinutes > 0)
                .Select(d => DateOnly.ParseExact(d.Date, "yyyy-MM-dd"))
                .Where(d => d >= startDate && d <= endDate).ToHashSet();
            dayAllocs = IncludeUnscheduledDays(dayAllocs, startDate, endDate);
            string scheduleBasis;
            if (daysWithShifts.Count == 0)
            {
                scheduleBasis = LeaveScheduleBases.Missing;
                warnings.Add("Toàn bộ các ngày trong khoảng nghỉ hiện chưa có ca làm việc nào được phân.");
            }
            else if (daysWithShifts.Count < calendarDays)
            {
                scheduleBasis = LeaveScheduleBases.Partial;
                warnings.Add($"Chỉ có {daysWithShifts.Count}/{calendarDays} ngày trong khoảng nghỉ đã có ca làm việc.");
            }
            else
            {
                scheduleBasis = LeaveScheduleBases.Available;
            }

            return new PreviewCalcResult(
                RequestedPeriod: $"Từ {startDate:dd/MM/yyyy} đến {endDate:dd/MM/yyyy} ({calendarDays} ngày)",
                CalendarDays: calendarDays,
                ScheduledLeaveMinutes: scheduledMins,
                ScheduleBasis: scheduleBasis,
                HasExpandedCoverage: hasExpandedCoverage,
                EffectiveIntervals: merged,
                AffectedAssignments: affected,
                DayAllocations: dayAllocs,
                Warnings: warnings);
        }

        throw new LeaveValidationException("invalid_unit_fields", $"Đơn vị không hợp lệ: '{input.Unit}'.");
    }

    private async Task<List<InternalShiftAssignment>> GetEmployeeShiftsInDateWindowAsync(
        long employeeId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct, NpgsqlConnection? connection = null)
    {
        const string sql = """
            SELECT dkc.id, cl.id, cl.loai_ca, cl.ngay_lam, cl.gio_bat_dau, cl.gio_ket_thuc, cl.qua_dem
            FROM public.dang_ky_ca dkc
            JOIN public.ca_lam cl ON cl.id = dkc.id_ca_lam
            WHERE dkc.id_nhan_vien = @employeeId
              AND COALESCE(dkc.trang_thai, 'Approved') != 'Cancelled'
              AND cl.ngay_lam >= @fromDate AND cl.ngay_lam <= @toDate
            ORDER BY cl.ngay_lam, cl.gio_bat_dau;
            """;

        await using var cmd = connection is null ? dataSource.CreateCommand(sql) : new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("fromDate", fromDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("toDate", toDate.ToDateTime(TimeOnly.MinValue));

        var list = new List<InternalShiftAssignment>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var assignId = reader.GetInt64(0);
            var shiftId = reader.GetInt64(1);
            var shiftName = reader.GetString(2);
            var shiftDate = DateOnly.FromDateTime(reader.GetDateTime(3));
            var startTime = reader.GetTimeSpan(4);
            var endTime = reader.GetTimeSpan(5);
            var isOvernight = reader.GetBoolean(6);

            var startLocal = shiftDate.ToDateTime(TimeOnly.FromTimeSpan(startTime));
            var endLocal = (isOvernight ? shiftDate.AddDays(1) : shiftDate).ToDateTime(TimeOnly.FromTimeSpan(endTime));

            var startUtc = ConvertLocalToUtc(startLocal);
            var endUtc = ConvertLocalToUtc(endLocal);
            var durationMins = (int)Math.Round((endUtc - startUtc).TotalMinutes);

            list.Add(new InternalShiftAssignment(
                AssignmentId: assignId,
                ShiftId: shiftId,
                ShiftName: shiftName,
                ShiftDate: shiftDate,
                StartTime: startTime,
                EndTime: endTime,
                IsOvernight: isOvernight,
                StartUtc: startUtc,
                EndUtc: endUtc,
                ScheduledMinutes: durationMins));
        }

        return list;
    }

    private async Task<(bool HasConflict, string? Reason)> CheckLeaveOverlapAsync(
        long employeeId,
        IReadOnlyList<LeaveIntervalDto> proposedIntervals,
        long? excludeLeaveRequestId,
        CancellationToken ct,
        NpgsqlConnection? connection = null)
    {
        if (proposedIntervals.Count == 0) return (false, null);

        const string sql = """
            SELECT dnpk.gio_bat_dau_utc, dnpk.gio_ket_thuc_utc, dnp.id, dnp.trang_thai
            FROM public.don_nghi_phep_khoang dnpk
            JOIN public.don_nghi_phep dnp ON dnp.id = dnpk.id_don_nghi_phep
            WHERE dnp.id_nhan_vien = @employeeId
              AND dnp.trang_thai IN ('Pending', 'Approved')
              AND (CAST(@excludeId AS bigint) IS NULL OR dnp.id != @excludeId);
            """;

        await using var cmd = connection is null ? dataSource.CreateCommand(sql) : new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("excludeId", (object?)excludeLeaveRequestId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var existStart = reader.GetFieldValue<DateTimeOffset>(0);
            var existEnd = reader.GetFieldValue<DateTimeOffset>(1);
            var otherId = reader.GetInt64(2);
            var otherStatus = reader.GetString(3);

            foreach (var prop in proposedIntervals)
            {
                if (prop.StartUtc < existEnd && existStart < prop.EndUtc)
                {
                    return (true, $"Khoảng thời gian này bị trùng lặp với đơn nghỉ phép #{otherId} ({otherStatus}) của bạn.");
                }
            }
        }

        return (false, null);
    }

    private async Task<bool> CheckAttendanceOverlapAsync(
        long employeeId,
        IReadOnlyList<LeaveIntervalDto> intervals,
        CancellationToken ct,
        NpgsqlConnection? connection = null,
        NpgsqlTransaction? transaction = null)
    {
        if (intervals.Count == 0) return false;

        const string sql = """
            SELECT cc.id
            FROM public.cham_cong cc
            WHERE cc.id_nhan_vien = @employeeId
              AND cc.gio_vao IS NOT NULL
              AND ((cc.gio_vao < @intervalEndUtc
                    AND COALESCE(cc.gio_ra, 'infinity'::timestamptz) > @intervalStartUtc)
                OR EXISTS (
                    SELECT 1 FROM public.dang_ky_ca d JOIN public.ca_lam c ON c.id=d.id_ca_lam
                    WHERE d.id=cc.id_dang_ky_ca
                      AND (c.ngay_lam + c.gio_bat_dau) AT TIME ZONE 'Asia/Ho_Chi_Minh' < @intervalEndUtc
                      AND (c.ngay_lam + c.gio_ket_thuc + CASE WHEN c.qua_dem THEN INTERVAL '1 day' ELSE INTERVAL '0 days' END)
                          AT TIME ZONE 'Asia/Ho_Chi_Minh' > @intervalStartUtc))
            LIMIT 1;
            """;

        foreach (var interval in intervals)
        {
            await using var cmd = connection is null ? dataSource.CreateCommand(sql) : new NpgsqlCommand(sql, connection, transaction);
            cmd.Parameters.AddWithValue("employeeId", employeeId);
            cmd.Parameters.AddWithValue("intervalStartUtc", interval.StartUtc);
            cmd.Parameters.AddWithValue("intervalEndUtc", interval.EndUtc);

            var res = await cmd.ExecuteScalarAsync(ct);
            if (res is not null)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<List<LeaveIntervalDto>> GetIntervalsForLeaveRequestAsync(
        long leaveRequestId,
        CancellationToken ct,
        NpgsqlConnection? connection = null,
        NpgsqlTransaction? transaction = null)
    {
        const string sql = """
            SELECT gio_bat_dau_utc, gio_ket_thuc_utc
            FROM public.don_nghi_phep_khoang
            WHERE id_don_nghi_phep = @leaveRequestId
            ORDER BY gio_bat_dau_utc;
            """;

        await using var cmd = connection is null ? dataSource.CreateCommand(sql) : new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("leaveRequestId", leaveRequestId);

        var list = new List<LeaveIntervalDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var startUtc = reader.GetFieldValue<DateTimeOffset>(0);
            var endUtc = reader.GetFieldValue<DateTimeOffset>(1);
            list.Add(new LeaveIntervalDto(
                StartUtc: startUtc,
                EndUtc: endUtc,
                StartLocal: ConvertUtcToLocal(startUtc).ToString("yyyy-MM-dd HH:mm"),
                EndLocal: ConvertUtcToLocal(endUtc).ToString("yyyy-MM-dd HH:mm")));
        }

        return list;
    }

    public Task<LeaveRequestDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => GetByIdCoreAsync(id, cancellationToken);

    private async Task<LeaveRequestDto?> GetByIdCoreAsync(
        long id,
        CancellationToken ct,
        NpgsqlConnection? connection = null,
        NpgsqlTransaction? transaction = null)
    {
        const string sql = """
            SELECT dnp.id, dnp.version, dnp.id_nhan_vien, nv.ho_ten, pb.ten_phong_ban,
                   dnp.loai_nhan_vien_snapshot, dnp.don_vi, dnp.ma_ly_do, dnp.ly_do,
                   dnp.is_emergency, dnp.is_late_submission, dnp.late_submission_reason,
                   dnp.id_dang_ky_ca, dnp.ngay_nghi, dnp.ngay_bat_dau, dnp.ngay_ket_thuc,
                   dnp.so_ngay_lich, dnp.tong_phut_nghi, dnp.schedule_basis, dnp.trang_thai,
                   dnp.phuong_an_luong, dnp.payroll_readiness, dnp.created_at, dnp.updated_at,
                   dnp.id_nguoi_duyet, reviewer.ho_ten, dnp.ngay_duyet, dnp.ghi_chu_duyet,
                   dnp.ly_do_tu_choi, dnp.ly_do_thu_hoi, dnp.ngay_huy, dnp.ngay_thu_hoi,
                   (SELECT COUNT(*) FROM public.don_nghi_phep_ca WHERE id_don_nghi_phep = dnp.id)
            FROM public.don_nghi_phep dnp
            JOIN public.nhan_vien nv ON nv.id = dnp.id_nhan_vien
            LEFT JOIN public.phong_ban pb ON pb.id = nv.id_phong_ban
            LEFT JOIN public.nhan_vien reviewer ON reviewer.id = dnp.id_nguoi_duyet
            WHERE dnp.id = @id
            LIMIT 1;
            """;

        await using var cmd = connection is null ? dataSource.CreateCommand(sql) : new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var dto = ReadLeaveRequestDto(reader);
        await reader.CloseAsync();

        var intervals = await GetIntervalsForLeaveRequestAsync(id, ct, connection, transaction);
        return dto with { EffectiveIntervals = intervals };
    }

    public async Task<LeaveRequestDetailDto?> GetDetailByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var main = await GetByIdAsync(id, cancellationToken);
        if (main is null) return null;

        // Fetch affected assignments
        const string caSql = """
            SELECT id_dang_ky_ca, id_ca_lam, ten_ca, ngay_lam, gio_bat_dau, gio_ket_thuc, qua_dem, phut_theo_lich
            FROM public.don_nghi_phep_ca
            WHERE id_don_nghi_phep = @id
            ORDER BY ngay_lam, gio_bat_dau;
            """;

        var affected = new List<AffectedAssignmentDto>();
        await using (var cmd = dataSource.CreateCommand(caSql))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var assignId = reader.GetInt64(0);
                var shiftId = reader.GetInt64(1);
                var shiftName = reader.GetString(2);
                var shiftDate = DateOnly.FromDateTime(reader.GetDateTime(3));
                var startTime = reader.GetTimeSpan(4);
                var endTime = reader.GetTimeSpan(5);
                var isOvernight = reader.GetBoolean(6);
                var mins = reader.GetInt32(7);

                affected.Add(new AffectedAssignmentDto(
                    AssignmentId: assignId,
                    ShiftId: shiftId,
                    ShiftName: shiftName,
                    ShiftDate: shiftDate.ToString("yyyy-MM-dd"),
                    StartTime: startTime.ToString(@"hh\:mm"),
                    EndTime: endTime.ToString(@"hh\:mm"),
                    IsOvernight: isOvernight,
                    ScheduledMinutes: mins,
                    IsBeyondRequestedDates: false));
            }
        }

        // Fetch day allocations
        const string daySql = """
            SELECT ngay, phut_nghi
            FROM public.don_nghi_phep_ngay
            WHERE id_don_nghi_phep = @id
            ORDER BY ngay;
            """;

        var days = new List<LeaveDayAllocationDto>();
        await using (var cmd = dataSource.CreateCommand(daySql))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var d = DateOnly.FromDateTime(reader.GetDateTime(0));
                var m = reader.GetInt32(1);
                days.Add(new LeaveDayAllocationDto(d.ToString("yyyy-MM-dd"), m));
            }
        }

        // Fetch history
        const string histSql = """
            SELECT h.id, h.id_nguoi_thuc_hien, nv.ho_ten, h.hanh_dong, h.trang_thai_truoc, h.trang_thai_sau, h.ghi_chu, h.thoi_gian_utc
            FROM public.lich_su_don_nghi_phep h
            JOIN public.nhan_vien nv ON nv.id = h.id_nguoi_thuc_hien
            WHERE h.id_don_nghi_phep = @id
            ORDER BY h.thoi_gian_utc DESC, h.id DESC;
            """;

        var history = new List<LeaveRequestAuditDto>();
        await using (var cmd = dataSource.CreateCommand(histSql))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                history.Add(new LeaveRequestAuditDto(
                    Id: reader.GetInt64(0),
                    ActorId: reader.GetInt64(1),
                    ActorName: reader.GetString(2),
                    Action: reader.GetString(3),
                    PreviousStatus: reader.IsDBNull(4) ? null : reader.GetString(4),
                    NewStatus: reader.GetString(5),
                    Note: reader.IsDBNull(6) ? null : reader.GetString(6),
                    TimestampUtc: reader.GetFieldValue<DateTimeOffset>(7)));
            }
        }

        return new LeaveRequestDetailDto(main, affected, days, history);
    }

    public async Task<PagedLeaveRequestsResponse> GetMyLeaveRequestsAsync(
        long employeeId,
        string? status,
        string? unit,
        DateOnly? fromDate,
        DateOnly? toDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        return await QueryLeaveRequestsCoreAsync(
            filterEmployeeId: employeeId,
            filterDepartmentId: null,
            status: status,
            unit: unit,
            reasonCode: null,
            payrollTreatment: null,
            fromDate: fromDate,
            toDate: toDate,
            page: page,
            pageSize: pageSize,
            ct: cancellationToken);
    }

    public async Task<PagedLeaveRequestsResponse> GetLeaveRequestsAdminAsync(
        string? status,
        string? unit,
        string? reasonCode,
        string? payrollTreatment,
        long? employeeId,
        long? departmentId,
        DateOnly? fromDate,
        DateOnly? toDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        return await QueryLeaveRequestsCoreAsync(
            filterEmployeeId: employeeId,
            filterDepartmentId: departmentId,
            status: status,
            unit: unit,
            reasonCode: reasonCode,
            payrollTreatment: payrollTreatment,
            fromDate: fromDate,
            toDate: toDate,
            page: page,
            pageSize: pageSize,
            ct: cancellationToken);
    }

    private async Task<PagedLeaveRequestsResponse> QueryLeaveRequestsCoreAsync(
        long? filterEmployeeId,
        long? filterDepartmentId,
        string? status,
        string? unit,
        string? reasonCode,
        string? payrollTreatment,
        DateOnly? fromDate,
        DateOnly? toDate,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var whereClauses = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        if (filterEmployeeId.HasValue)
        {
            whereClauses.Add("dnp.id_nhan_vien = @filterEmployeeId");
            parameters.Add(new NpgsqlParameter("filterEmployeeId", filterEmployeeId.Value));
        }

        if (filterDepartmentId.HasValue)
        {
            whereClauses.Add("nv.id_phong_ban = @filterDepartmentId");
            parameters.Add(new NpgsqlParameter("filterDepartmentId", filterDepartmentId.Value));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            whereClauses.Add("dnp.trang_thai = @status");
            parameters.Add(new NpgsqlParameter("status", status.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(unit))
        {
            whereClauses.Add("dnp.don_vi = @unit");
            parameters.Add(new NpgsqlParameter("unit", unit.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(reasonCode))
        {
            whereClauses.Add("dnp.ma_ly_do = @reasonCode");
            parameters.Add(new NpgsqlParameter("reasonCode", reasonCode.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(payrollTreatment))
        {
            whereClauses.Add("dnp.phuong_an_luong = @payrollTreatment");
            parameters.Add(new NpgsqlParameter("payrollTreatment", payrollTreatment.Trim()));
        }

        // Date overlap filtering
        if (fromDate.HasValue || toDate.HasValue)
        {
            var rangeStartUtc = fromDate.HasValue ? ConvertLocalToUtc(fromDate.Value.ToDateTime(TimeOnly.MinValue)) : DateTimeOffset.MinValue;
            var rangeEndUtc = toDate.HasValue ? ConvertLocalToUtc(toDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue)) : DateTimeOffset.MaxValue;

            whereClauses.Add("""
                EXISTS (
                    SELECT 1 FROM public.don_nghi_phep_khoang k
                    WHERE k.id_don_nghi_phep = dnp.id
                      AND k.gio_bat_dau_utc < @rangeEndUtc
                      AND @rangeStartUtc < k.gio_ket_thuc_utc
                )
                """);
            parameters.Add(new NpgsqlParameter("rangeStartUtc", rangeStartUtc));
            parameters.Add(new NpgsqlParameter("rangeEndUtc", rangeEndUtc));
        }

        var whereSql = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : string.Empty;

        var countSql = $"""
            SELECT COUNT(*)
            FROM public.don_nghi_phep dnp
            JOIN public.nhan_vien nv ON nv.id = dnp.id_nhan_vien
            {whereSql};
            """;

        await using var countCmd = dataSource.CreateCommand(countSql);
        foreach (var p in parameters) countCmd.Parameters.Add(p.Clone());
        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        var selectSql = $"""
            SELECT dnp.id, dnp.version, dnp.id_nhan_vien, nv.ho_ten, pb.ten_phong_ban,
                   dnp.loai_nhan_vien_snapshot, dnp.don_vi, dnp.ma_ly_do, dnp.ly_do,
                   dnp.is_emergency, dnp.is_late_submission, dnp.late_submission_reason,
                   dnp.id_dang_ky_ca, dnp.ngay_nghi, dnp.ngay_bat_dau, dnp.ngay_ket_thuc,
                   dnp.so_ngay_lich, dnp.tong_phut_nghi, dnp.schedule_basis, dnp.trang_thai,
                   dnp.phuong_an_luong, dnp.payroll_readiness, dnp.created_at, dnp.updated_at,
                   dnp.id_nguoi_duyet, reviewer.ho_ten, dnp.ngay_duyet, dnp.ghi_chu_duyet,
                   dnp.ly_do_tu_choi, dnp.ly_do_thu_hoi, dnp.ngay_huy, dnp.ngay_thu_hoi,
                   (SELECT COUNT(*) FROM public.don_nghi_phep_ca WHERE id_don_nghi_phep = dnp.id)
            FROM public.don_nghi_phep dnp
            JOIN public.nhan_vien nv ON nv.id = dnp.id_nhan_vien
            LEFT JOIN public.phong_ban pb ON pb.id = nv.id_phong_ban
            LEFT JOIN public.nhan_vien reviewer ON reviewer.id = dnp.id_nguoi_duyet
            {whereSql}
            ORDER BY dnp.created_at DESC, dnp.id DESC
            LIMIT @limit OFFSET @offset;
            """;

        await using var selectCmd = dataSource.CreateCommand(selectSql);
        foreach (var p in parameters) selectCmd.Parameters.Add(p.Clone());
        selectCmd.Parameters.AddWithValue("limit", pageSize);
        selectCmd.Parameters.AddWithValue("offset", (page - 1) * pageSize);

        var items = new List<LeaveRequestDto>();
        await using (var reader = await selectCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                items.Add(ReadLeaveRequestDto(reader));
            }
        }

        // Populate effective intervals for items in batch
        if (items.Count > 0)
        {
            var ids = items.Select(i => i.Id).ToList();
            var intervalMap = await GetIntervalsBatchAsync(ids, ct);
            for (int i = 0; i < items.Count; i++)
            {
                if (intervalMap.TryGetValue(items[i].Id, out var intervals))
                {
                    items[i] = items[i] with { EffectiveIntervals = intervals };
                }
            }
        }

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        return new PagedLeaveRequestsResponse(items, page, pageSize, totalCount, totalPages);
    }

    private async Task<Dictionary<long, List<LeaveIntervalDto>>> GetIntervalsBatchAsync(List<long> leaveRequestIds, CancellationToken ct)
    {
        const string sql = """
            SELECT id_don_nghi_phep, gio_bat_dau_utc, gio_ket_thuc_utc
            FROM public.don_nghi_phep_khoang
            WHERE id_don_nghi_phep = ANY(@ids)
            ORDER BY id_don_nghi_phep, gio_bat_dau_utc;
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("ids", leaveRequestIds);

        var result = new Dictionary<long, List<LeaveIntervalDto>>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetInt64(0);
            var startUtc = reader.GetFieldValue<DateTimeOffset>(1);
            var endUtc = reader.GetFieldValue<DateTimeOffset>(2);

            if (!result.TryGetValue(id, out var list))
            {
                list = [];
                result[id] = list;
            }

            list.Add(new LeaveIntervalDto(
                StartUtc: startUtc,
                EndUtc: endUtc,
                StartLocal: ConvertUtcToLocal(startUtc).ToString("yyyy-MM-dd HH:mm"),
                EndLocal: ConvertUtcToLocal(endUtc).ToString("yyyy-MM-dd HH:mm")));
        }

        return result;
    }

    public async Task<PayrollLeaveSummaryResponse> GetPayrollSummaryAsync(
        DateOnly fromDate,
        DateOnly toDate,
        long? employeeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (fromDate > toDate)
        {
            throw new LeaveValidationException("invalid_date_range", "Ngày bắt đầu phải nhỏ hơn hoặc bằng ngày kết thúc.");
        }

        if ((toDate.DayNumber - fromDate.DayNumber + 1) > 366)
        {
            throw new LeaveValidationException("invalid_date_range", "Truy vấn tổng hợp lương tối đa trong phạm vi 366 ngày.");
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var whereClauses = new List<string>
        {
            "dnp.trang_thai = 'Approved'",
            "dpn.ngay >= @fromDate",
            "dpn.ngay <= @toDate"
        };
        var parameters = new List<NpgsqlParameter>
        {
            new("fromDate", fromDate.ToDateTime(TimeOnly.MinValue)),
            new("toDate", toDate.ToDateTime(TimeOnly.MinValue))
        };

        if (employeeId.HasValue)
        {
            whereClauses.Add("dpn.id_nhan_vien = @employeeId");
            parameters.Add(new NpgsqlParameter("employeeId", employeeId.Value));
        }

        var whereSql = "WHERE " + string.Join(" AND ", whereClauses);

        var countSql = $"""
            SELECT COUNT(*)
            FROM public.don_nghi_phep_ngay dpn
            JOIN public.don_nghi_phep dnp ON dnp.id = dpn.id_don_nghi_phep
            JOIN public.nhan_vien nv ON nv.id = dpn.id_nhan_vien
            {whereSql};
            """;

        await using var countCmd = dataSource.CreateCommand(countSql);
        foreach (var p in parameters) countCmd.Parameters.Add(p.Clone());
        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken));

        var selectSql = $"""
            SELECT dpn.ngay, dpn.id_nhan_vien, nv.ho_ten, pb.ten_phong_ban,
                   COALESCE(dnp.loai_nhan_vien_snapshot, nv.loai_nhan_vien, 'FullTime'),
                   dnp.id, dnp.don_vi, dnp.ma_ly_do, dnp.phuong_an_luong,
                   dnp.payroll_readiness, dpn.phut_nghi
            FROM public.don_nghi_phep_ngay dpn
            JOIN public.don_nghi_phep dnp ON dnp.id = dpn.id_don_nghi_phep
            JOIN public.nhan_vien nv ON nv.id = dpn.id_nhan_vien
            LEFT JOIN public.phong_ban pb ON pb.id = nv.id_phong_ban
            {whereSql}
            ORDER BY dpn.ngay ASC, nv.ho_ten ASC
            LIMIT @limit OFFSET @offset;
            """;

        await using var selectCmd = dataSource.CreateCommand(selectSql);
        foreach (var p in parameters) selectCmd.Parameters.Add(p.Clone());
        selectCmd.Parameters.AddWithValue("limit", pageSize);
        selectCmd.Parameters.AddWithValue("offset", (page - 1) * pageSize);

        var items = new List<PayrollLeaveSummaryItemDto>();
        await using (var reader = await selectCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var day = DateOnly.FromDateTime(reader.GetDateTime(0));
                var empId = reader.GetInt64(1);
                var empName = reader.GetString(2);
                var deptName = reader.IsDBNull(3) ? null : reader.GetString(3);
                var empType = reader.GetString(4);
                var leaveId = reader.GetInt64(5);
                var unit = reader.GetString(6);
                var reason = reader.GetString(7);
                var payrollTreatment = reader.GetString(8);
                var payrollReadiness = reader.GetString(9);
                var minutes = reader.GetInt32(10);

                var needsAudit = payrollReadiness == LeavePayrollReadinesses.NeedsReview || payrollTreatment == LeavePayrollTreatments.Unresolved ||
                                 payrollTreatment == LeavePayrollTreatments.ExternalBenefitReview;

                string? auditNote = needsAudit
                    ? (payrollTreatment == LeavePayrollTreatments.ExternalBenefitReview
                        ? "Cần đối soát chế độ bảo hiểm / chính sách ngoài"
                        : "Cần đối soát chính sách, hạn mức và cơ sở tính lương")
                    : null;

                items.Add(new PayrollLeaveSummaryItemDto(
                    Date: day.ToString("yyyy-MM-dd"),
                    EmployeeId: empId,
                    EmployeeCode: $"NV-{empId:D4}",
                    EmployeeName: empName,
                    DepartmentName: deptName,
                    EmployeeType: empType,
                    LeaveRequestId: leaveId,
                    Unit: unit,
                    ReasonCode: reason,
                    PayrollTreatment: payrollTreatment,
                    PayrollReadiness: payrollReadiness,
                    ScheduledMinutes: minutes,
                    NeedsAudit: needsAudit,
                    AuditNote: auditNote));
            }
        }

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        return new PayrollLeaveSummaryResponse(
            Items: items,
            FromDate: fromDate.ToString("yyyy-MM-dd"),
            ToDate: toDate.ToString("yyyy-MM-dd"),
            Page: page,
            PageSize: pageSize,
            TotalCount: totalCount,
            TotalPages: totalPages);
    }

    public async Task<bool> HasApprovedLeaveConflictAsync(
        long employeeId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT 1
            FROM public.don_nghi_phep_khoang dnpk
            JOIN public.don_nghi_phep dnp ON dnp.id = dnpk.id_don_nghi_phep
            WHERE dnp.id_nhan_vien = @employeeId
              AND dnp.trang_thai = 'Approved'
              AND dnpk.gio_bat_dau_utc < @endUtc
              AND @startUtc < dnpk.gio_ket_thuc_utc
            LIMIT 1;
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("startUtc", startUtc);
        cmd.Parameters.AddWithValue("endUtc", endUtc);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    private static LeaveRequestDto ReadLeaveRequestDto(NpgsqlDataReader reader)
    {
        var id = reader.GetInt64(0);
        var version = reader.GetInt32(1);
        var empId = reader.GetInt64(2);
        var empName = reader.GetString(3);
        var deptName = reader.IsDBNull(4) ? null : reader.GetString(4);
        var empTypeSnap = reader.IsDBNull(5) ? null : reader.GetString(5);
        var unit = reader.GetString(6);
        var reasonCode = reader.GetString(7);
        var reason = reader.GetString(8);
        var isEmergency = reader.GetBoolean(9);
        var isLate = reader.GetBoolean(10);
        var lateReason = reader.IsDBNull(11) ? null : reader.GetString(11);
        long? shiftAssignId = reader.IsDBNull(12) ? null : reader.GetInt64(12);
        var date = reader.IsDBNull(13) ? null : DateOnly.FromDateTime(reader.GetDateTime(13)).ToString("yyyy-MM-dd");
        var startDate = reader.IsDBNull(14) ? null : DateOnly.FromDateTime(reader.GetDateTime(14)).ToString("yyyy-MM-dd");
        var endDate = reader.IsDBNull(15) ? null : DateOnly.FromDateTime(reader.GetDateTime(15)).ToString("yyyy-MM-dd");
        int? calendarDays = reader.IsDBNull(16) ? null : reader.GetInt32(16);
        var scheduledMins = reader.GetInt32(17);
        var scheduleBasis = reader.GetString(18);
        var status = reader.GetString(19);
        var payrollTreatment = reader.GetString(20);
        var payrollReadiness = reader.GetString(21);
        var createdAt = reader.GetFieldValue<DateTimeOffset>(22);
        var updatedAt = reader.GetFieldValue<DateTimeOffset>(23);
        long? reviewedById = reader.IsDBNull(24) ? null : reader.GetInt64(24);
        var reviewedByName = reader.IsDBNull(25) ? null : reader.GetString(25);
        DateTimeOffset? reviewedAt = reader.IsDBNull(26) ? null : reader.GetFieldValue<DateTimeOffset>(26);
        var reviewNote = reader.IsDBNull(27) ? null : reader.GetString(27);
        var rejReason = reader.IsDBNull(28) ? null : reader.GetString(28);
        var revReason = reader.IsDBNull(29) ? null : reader.GetString(29);
        DateTimeOffset? cancelledAt = reader.IsDBNull(30) ? null : reader.GetFieldValue<DateTimeOffset>(30);
        DateTimeOffset? revokedAt = reader.IsDBNull(31) ? null : reader.GetFieldValue<DateTimeOffset>(31);
        var affCount = reader.GetInt32(32);

        string requestedPeriod = unit switch
        {
            LeaveUnits.Shift => $"Ca làm việc #{shiftAssignId}",
            LeaveUnits.Day => $"Cả ngày {date}",
            LeaveUnits.DateRange => $"Từ {startDate} đến {endDate} ({calendarDays} ngày)",
            _ => string.Empty
        };

        return new LeaveRequestDto(
            Id: id,
            Version: version,
            EmployeeId: empId,
            EmployeeName: empName,
            EmployeeCode: $"NV-{empId:D4}",
            EmployeeTypeSnapshot: empTypeSnap,
            DepartmentName: deptName,
            Unit: unit,
            ReasonCode: reasonCode,
            Reason: reason,
            IsEmergency: isEmergency,
            IsLateSubmission: isLate,
            LateSubmissionReason: lateReason,
            ShiftAssignmentId: shiftAssignId,
            Date: date,
            StartDate: startDate,
            EndDate: endDate,
            CalendarDays: calendarDays,
            ScheduledLeaveMinutes: scheduledMins,
            AffectedAssignmentCount: affCount,
            ScheduleBasis: scheduleBasis,
            Status: status,
            PayrollTreatment: payrollTreatment,
            PayrollReadiness: payrollReadiness,
            CreatedAtUtc: createdAt,
            UpdatedAtUtc: updatedAt,
            ReviewedById: reviewedById,
            ReviewedByName: reviewedByName,
            ReviewedAtUtc: reviewedAt,
            ReviewNote: reviewNote,
            RejectionReason: rejReason,
            RevocationReason: revReason,
            CancelledAtUtc: cancelledAt,
            RevokedAtUtc: revokedAt,
            RequestedPeriod: requestedPeriod,
            EffectiveIntervals: []);
    }
}
