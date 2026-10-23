using manage365.Routes.API.LeaveRequests;
using Npgsql;
using NpgsqlTypes;

namespace manage365.Repositories.LeaveRequests;

public sealed partial class PostgresLeaveRequestRepository
{
    private static CreateLeaveRequestInput InputFromPreview(PreviewCachePayload p) => new()
    {
        Unit = p.Unit, ReasonCode = p.ReasonCode, Reason = p.Reason,
        IsEmergency = p.IsEmergency, LateSubmissionReason = p.LateSubmissionReason,
        ShiftAssignmentId = p.ShiftAssignmentId, Date = p.Date, StartDate = p.StartDate, EndDate = p.EndDate
    };

    private static void EnsurePreviewMatches(PreviewCachePayload p, PreviewCalcResult current)
    {
        if (!p.EffectiveIntervals.SequenceEqual(current.EffectiveIntervals) ||
            !p.AffectedAssignments.OrderBy(a => a.AssignmentId).SequenceEqual(current.AffectedAssignments.OrderBy(a => a.AssignmentId)))
            throw new LeaveConflictException("schedule_changed", "Lịch làm đã thay đổi. Vui lòng xem trước lại.");
    }

    private static void ValidateSubmissionTime(CreateLeaveRequestInput input, DateTimeOffset start, DateTimeOffset now)
    {
        if (start <= now && (!input.IsEmergency || (now - start).TotalDays > 30 ||
            string.IsNullOrWhiteSpace(input.LateSubmissionReason) || input.LateSubmissionReason.Trim().Length < 10))
            throw new LeaveValidationException("validation_error", "Đơn nộp bổ sung cần giải trình khẩn cấp và trong vòng 30 ngày.");
    }

    private static List<LeaveDayAllocationDto> IncludeUnscheduledDays(List<LeaveDayAllocationDto> days, DateOnly from, DateOnly to)
    {
        var values = days.ToDictionary(d => d.Date, d => d.ScheduledMinutes);
        for (var date = from; date <= to; date = date.AddDays(1))
            values.TryAdd(date.ToString("yyyy-MM-dd"), 0);
        return values.OrderBy(p => p.Key).Select(p => new LeaveDayAllocationDto(p.Key, p.Value)).ToList();
    }

    private static async Task LockRequestEmployeeAsync(NpgsqlConnection connection, long id, CancellationToken ct)
    {
        await using var lookup = new NpgsqlCommand("SELECT id_nhan_vien FROM public.don_nghi_phep WHERE id=@id", connection);
        lookup.Parameters.AddWithValue("id", id);
        var employee = await lookup.ExecuteScalarAsync(ct);
        if (employee is null) throw new LeaveNotFoundException();
        await LockEmployeeAsync(connection, (long)employee, true, ct);
    }

    private static async Task EnsureOriginalShiftAsync(long id, string unit, PreviewCalcResult current, NpgsqlConnection connection, CancellationToken ct)
    {
        if (unit != LeaveUnits.Shift) return;
        await using var command = new NpgsqlCommand("SELECT gio_bat_dau_utc, gio_ket_thuc_utc FROM public.don_nghi_phep_khoang WHERE id_don_nghi_phep=@id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.GetFieldValue<DateTimeOffset>(0) != current.EffectiveIntervals[0].StartUtc ||
            reader.GetFieldValue<DateTimeOffset>(1) != current.EffectiveIntervals[0].EndUtc)
            throw new LeaveConflictException("schedule_changed", "Ca đã đổi giờ. Vui lòng hủy đơn và tạo lại theo lịch mới.");
    }

    private static async Task ReplaceSnapshotAsync(long id, long employeeId, PreviewCalcResult calc,
        NpgsqlConnection connection, NpgsqlTransaction transaction, DateTimeOffset now, CancellationToken ct)
    {
        await using (var clear = new NpgsqlCommand("""
            DELETE FROM public.don_nghi_phep_khoang WHERE id_don_nghi_phep=@id;
            DELETE FROM public.don_nghi_phep_ca WHERE id_don_nghi_phep=@id;
            DELETE FROM public.don_nghi_phep_ngay WHERE id_don_nghi_phep=@id;
            UPDATE public.don_nghi_phep SET tong_phut_nghi=@minutes, schedule_basis=@basis WHERE id=@id;
            """, connection, transaction))
        {
            clear.Parameters.AddWithValue("id", id);
            clear.Parameters.AddWithValue("minutes", calc.ScheduledLeaveMinutes);
            clear.Parameters.AddWithValue("basis", calc.ScheduleBasis);
            await clear.ExecuteNonQueryAsync(ct);
        }
        foreach (var interval in calc.EffectiveIntervals)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO public.don_nghi_phep_khoang(id_don_nghi_phep,id_nhan_vien,gio_bat_dau_utc,gio_ket_thuc_utc,created_at)
                VALUES (@id,@employee,@start,@end,@now)
                """, connection, transaction);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("employee", employeeId);
            command.Parameters.AddWithValue("start", interval.StartUtc);
            command.Parameters.AddWithValue("end", interval.EndUtc);
            command.Parameters.AddWithValue("now", now);
            await command.ExecuteNonQueryAsync(ct);
        }
        foreach (var shift in calc.AffectedAssignments)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO public.don_nghi_phep_ca(id_don_nghi_phep,id_dang_ky_ca,id_ca_lam,ten_ca,ngay_lam,gio_bat_dau,gio_ket_thuc,qua_dem,phut_theo_lich,created_at)
                VALUES (@id,@assignment,@shift,@name,@date,@start,@end,@overnight,@minutes,@now)
                """, connection, transaction);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("assignment", shift.AssignmentId);
            command.Parameters.AddWithValue("shift", shift.ShiftId);
            command.Parameters.AddWithValue("name", shift.ShiftName);
            command.Parameters.AddWithValue("date", NpgsqlDbType.Date, DateOnly.ParseExact(shift.ShiftDate, "yyyy-MM-dd"));
            command.Parameters.AddWithValue("start", TimeSpan.Parse(shift.StartTime));
            command.Parameters.AddWithValue("end", TimeSpan.Parse(shift.EndTime));
            command.Parameters.AddWithValue("overnight", shift.IsOvernight);
            command.Parameters.AddWithValue("minutes", shift.ScheduledMinutes);
            command.Parameters.AddWithValue("now", now);
            await command.ExecuteNonQueryAsync(ct);
        }
        foreach (var day in calc.DayAllocations)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO public.don_nghi_phep_ngay(id_don_nghi_phep,id_nhan_vien,ngay,phut_nghi,created_at)
                VALUES (@id,@employee,@date,@minutes,@now)
                """, connection, transaction);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("employee", employeeId);
            command.Parameters.AddWithValue("date", NpgsqlDbType.Date, DateOnly.ParseExact(day.Date, "yyyy-MM-dd"));
            command.Parameters.AddWithValue("minutes", day.ScheduledMinutes);
            command.Parameters.AddWithValue("now", now);
            await command.ExecuteNonQueryAsync(ct);
        }
    }
}
