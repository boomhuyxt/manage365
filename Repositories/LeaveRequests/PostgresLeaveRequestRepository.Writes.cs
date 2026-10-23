using System.Text.Json;
using manage365.Routes.API.LeaveRequests;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using NpgsqlTypes;

namespace manage365.Repositories.LeaveRequests;

public sealed partial class PostgresLeaveRequestRepository
{
    private const string PreviewCachePrefix = "LeavePreview:";
    private static readonly TimeSpan PreviewTtl = TimeSpan.FromMinutes(10);

    public async Task<LeaveRequestPreviewResponse> GeneratePreviewAsync(
        long employeeId,
        CreateLeaveRequestInput input,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(input);

        var calc = await ComputePreviewCalculationsAsync(employeeId, input, null, cancellationToken);

        // Check late submission
        var earliestStartUtc = calc.EffectiveIntervals.Count > 0
            ? calc.EffectiveIntervals.Min(i => i.StartUtc)
            : nowUtc;

        var isLateSubmission = earliestStartUtc < nowUtc;
        if (isLateSubmission)
        {
            if (!input.IsEmergency)
            {
                throw new LeaveValidationException("validation_error", "Đơn nghỉ thông thường phải nộp trước khi thời gian nghỉ bắt đầu. Nếu là trường hợp khẩn cấp, xin vui lòng chọn cờ Đơn khẩn cấp.");
            }

            if ((nowUtc - earliestStartUtc).TotalDays > 30)
            {
                throw new LeaveValidationException("validation_error", "Đơn khẩn cấp nộp bổ sung tối đa 30 ngày kể từ thời điểm bắt đầu khoảng nghỉ.");
            }

            if (string.IsNullOrWhiteSpace(input.LateSubmissionReason) || input.LateSubmissionReason.Trim().Length < 10)
            {
                throw new LeaveValidationException("validation_error", "Đơn nộp muộn bắt buộc phải nhập lý do nộp muộn từ 10 đến 2000 ký tự.");
            }
        }

        var isLongLeave = (calc.CalendarDays ?? 0) > 30;

        // Check overlap with pending or approved leave requests of this employee
        var (hasConflict, conflictReason) = await CheckLeaveOverlapAsync(employeeId, calc.EffectiveIntervals, null, cancellationToken);

        // Check attendance records
        var warnings = new List<string>(calc.Warnings);
        var hasAttendance = await CheckAttendanceOverlapAsync(employeeId, calc.EffectiveIntervals, cancellationToken);
        if (hasAttendance)
        {
            warnings.Add("Cảnh báo: Đã phát hiện dữ liệu chấm công thực tế của bạn trong khoảng thời gian nghỉ này. Nếu có công thực tế, đơn sẽ không thể được duyệt.");
        }

        if (calc.HasExpandedCoverage)
        {
            warnings.Add("Lưu ý: Có ca làm việc qua đêm kéo dài ra ngoài khoảng ngày đã chọn. Phạm vi nghỉ sẽ bao trùm toàn bộ ca làm việc này.");
        }

        if (isLongLeave)
        {
            warnings.Add("Thông báo: Đơn nghỉ trên 30 ngày thuộc diện nghỉ dài hạn và cần người quản trị xét duyệt đặc biệt.");
        }

        var previewToken = Guid.NewGuid().ToString("N");
        var expiresAtUtc = nowUtc.Add(PreviewTtl);

        var requestHash = ComputeRequestHash(input);

        var cachePayload = new PreviewCachePayload(
            PreviewToken: previewToken,
            EmployeeId: employeeId,
            LeaveRequestId: null,
            RequestHash: requestHash,
            Unit: input.Unit,
            ReasonCode: input.ReasonCode,
            Reason: input.Reason.Trim(),
            IsEmergency: input.IsEmergency,
            IsLateSubmission: isLateSubmission,
            LateSubmissionReason: input.LateSubmissionReason?.Trim(),
            ShiftAssignmentId: input.ShiftAssignmentId,
            Date: input.Date,
            StartDate: input.StartDate,
            EndDate: input.EndDate,
            CalendarDays: calc.CalendarDays,
            ScheduledLeaveMinutes: calc.ScheduledLeaveMinutes,
            ScheduleBasis: calc.ScheduleBasis,
            IsLongLeave: isLongLeave,
            HasExpandedCoverage: calc.HasExpandedCoverage,
            RequestedPeriod: calc.RequestedPeriod,
            EffectiveIntervals: calc.EffectiveIntervals,
            AffectedAssignments: calc.AffectedAssignments,
            DayAllocations: calc.DayAllocations,
            ExistingVersion: null,
            ExpiresAtUtc: expiresAtUtc);

        memoryCache.Set(PreviewCachePrefix + previewToken, cachePayload, PreviewTtl);

        return new LeaveRequestPreviewResponse(
            PreviewToken: previewToken,
            ExpiresAtUtc: expiresAtUtc,
            RequestedPeriod: calc.RequestedPeriod,
            EffectiveIntervals: calc.EffectiveIntervals,
            AffectedAssignments: calc.AffectedAssignments,
            ScheduledLeaveMinutes: calc.ScheduledLeaveMinutes,
            CalendarDays: calc.CalendarDays,
            ScheduleBasis: calc.ScheduleBasis,
            IsLateSubmission: isLateSubmission,
            IsLongLeave: isLongLeave,
            HasExpandedCoverage: calc.HasExpandedCoverage,
            Warnings: warnings,
            HasConflict: hasConflict,
            ConflictReason: conflictReason,
            ExistingVersion: null);
    }

    public async Task<LeaveRequestPreviewResponse> GenerateReviewPreviewAsync(
        long leaveRequestId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default,
        long? adminId = null)
    {
        var existing = await GetDetailByIdAsync(leaveRequestId, cancellationToken)
            ?? throw new LeaveNotFoundException("leave_request_not_found", "Không tìm thấy đơn nghỉ phép.");

        if (existing.Request.Status != LeaveStatuses.Pending)
        {
            throw new LeaveConflictException("invalid_leave_status", "Đơn nghỉ phép này đã được xử lý (không còn ở trạng thái Pending).");
        }

        var input = new CreateLeaveRequestInput
        {
            Unit = existing.Request.Unit,
            ReasonCode = existing.Request.ReasonCode,
            Reason = existing.Request.Reason,
            IsEmergency = existing.Request.IsEmergency,
            ShiftAssignmentId = existing.Request.ShiftAssignmentId,
            Date = existing.Request.Date,
            StartDate = existing.Request.StartDate,
            EndDate = existing.Request.EndDate,
            LateSubmissionReason = existing.Request.LateSubmissionReason
        };

        var calc = await ComputePreviewCalculationsAsync(existing.Request.EmployeeId, input, leaveRequestId, cancellationToken);

        await using (var checkConnection = await dataSource.OpenConnectionAsync(cancellationToken))
            await EnsureOriginalShiftAsync(leaveRequestId, input.Unit, calc, checkConnection, cancellationToken);

        var (hasConflict, conflictReason) = await CheckLeaveOverlapAsync(existing.Request.EmployeeId, calc.EffectiveIntervals, leaveRequestId, cancellationToken);

        var warnings = new List<string>(calc.Warnings);
        var hasAttendance = await CheckAttendanceOverlapAsync(existing.Request.EmployeeId, calc.EffectiveIntervals, cancellationToken);
        if (hasAttendance)
        {
            warnings.Add("Cảnh báo: Nhân viên đã có chấm công thực tế trong khoảng thời gian này. Không thể duyệt đơn khi đã có dữ liệu công.");
        }

        if (calc.HasExpandedCoverage)
        {
            warnings.Add("Lưu ý: Có ca qua đêm mở rộng ngoài phạm vi ngày ban đầu. Admin cần xác nhận đồng ý mở rộng phạm vi nghỉ.");
        }

        var isLongLeave = (calc.CalendarDays ?? 0) > 30;
        if (isLongLeave)
        {
            warnings.Add("Lưu ý: Đơn nghỉ trên 30 ngày yêu cầu Admin xác nhận xem xét nghỉ dài hạn và ghi chú phê duyệt.");
        }

        var previewToken = Guid.NewGuid().ToString("N");
        var expiresAtUtc = nowUtc.Add(PreviewTtl);
        var requestHash = ComputeRequestHash(input);

        var cachePayload = new PreviewCachePayload(
            PreviewToken: previewToken,
            EmployeeId: existing.Request.EmployeeId,
            LeaveRequestId: leaveRequestId,
            RequestHash: requestHash,
            Unit: input.Unit,
            ReasonCode: input.ReasonCode,
            Reason: input.Reason.Trim(),
            IsEmergency: input.IsEmergency,
            IsLateSubmission: existing.Request.IsLateSubmission,
            LateSubmissionReason: input.LateSubmissionReason?.Trim(),
            ShiftAssignmentId: input.ShiftAssignmentId,
            Date: input.Date,
            StartDate: input.StartDate,
            EndDate: input.EndDate,
            CalendarDays: calc.CalendarDays,
            ScheduledLeaveMinutes: calc.ScheduledLeaveMinutes,
            ScheduleBasis: calc.ScheduleBasis,
            IsLongLeave: isLongLeave,
            HasExpandedCoverage: calc.HasExpandedCoverage,
            RequestedPeriod: calc.RequestedPeriod,
            EffectiveIntervals: calc.EffectiveIntervals,
            AffectedAssignments: calc.AffectedAssignments,
            DayAllocations: calc.DayAllocations,
            ExistingVersion: existing.Request.Version,
            ExpiresAtUtc: expiresAtUtc,
            AdminId: adminId);

        memoryCache.Set(PreviewCachePrefix + previewToken, cachePayload, PreviewTtl);

        return new LeaveRequestPreviewResponse(
            PreviewToken: previewToken,
            ExpiresAtUtc: expiresAtUtc,
            RequestedPeriod: calc.RequestedPeriod,
            EffectiveIntervals: calc.EffectiveIntervals,
            AffectedAssignments: calc.AffectedAssignments,
            ScheduledLeaveMinutes: calc.ScheduledLeaveMinutes,
            CalendarDays: calc.CalendarDays,
            ScheduleBasis: calc.ScheduleBasis,
            IsLateSubmission: existing.Request.IsLateSubmission,
            IsLongLeave: isLongLeave,
            HasExpandedCoverage: calc.HasExpandedCoverage,
            Warnings: warnings,
            HasConflict: hasConflict,
            ConflictReason: conflictReason,
            ExistingVersion: existing.Request.Version);
    }

    public async Task<LeaveRequestDto> CreateLeaveRequestAsync(
        long employeeId,
        CreateLeaveRequestCommand command,
        string? idempotencyKey,
        string? requestHash,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(command);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockEmployeeAsync(connection, employeeId, requireActive: true, cancellationToken);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            await using var retry = new NpgsqlCommand("SELECT request_hash, response_json FROM public.don_nghi_phep_idempotency WHERE id_nhan_vien=@employee AND key=@key AND endpoint='/api/leave-requests'", connection, transaction);
            retry.Parameters.AddWithValue("employee", employeeId);
            retry.Parameters.AddWithValue("key", idempotencyKey);
            await using var cached = await retry.ExecuteReaderAsync(cancellationToken);
            if (await cached.ReadAsync(cancellationToken))
            {
                if (cached.GetString(0) != requestHash)
                    throw new LeaveConflictException("idempotency_conflict", "Khóa yêu cầu đã được dùng với nội dung khác.");
                return JsonSerializer.Deserialize<LeaveRequestDto>(cached.GetString(1))!;
            }
        }

        if (string.IsNullOrWhiteSpace(command.PreviewToken))
        {
            throw new LeaveValidationException("validation_error", "PreviewToken là bắt buộc.");
        }

        if (!memoryCache.TryGetValue<PreviewCachePayload>(PreviewCachePrefix + command.PreviewToken, out var preview) || preview is null)
        {
            throw new LeaveConflictException("preview_expired", "Phiên xem trước (preview) đã hết hạn. Vui lòng xem trước lại trước khi gửi đơn.");
        }

        if (preview.EmployeeId != employeeId)
        {
            throw new LeaveConflictException("schedule_changed", "Preview token không thuộc về người dùng này.");
        }

        var currentHash = ComputeRequestHash(command);
        if (currentHash != preview.RequestHash)
        {
            throw new LeaveConflictException("schedule_changed", "Dữ liệu gửi lên không khớp với phiên xem trước. Vui lòng xem trước lại.");
        }

        if (preview.LeaveRequestId is not null || preview.ExpiresAtUtc <= nowUtc)
            throw new LeaveConflictException("preview_expired", "Vui lòng xem trước lại đơn.");
        var current = await ComputePreviewCalculationsAsync(employeeId, command, null, cancellationToken, connection);
        EnsurePreviewMatches(preview, current);
        ValidateSubmissionTime(command, current.EffectiveIntervals.Min(i => i.StartUtc), nowUtc);
        preview = preview with { IsLateSubmission = current.EffectiveIntervals.Min(i => i.StartUtc) <= nowUtc };

        // Re-check overlap under employee row lock
        var (hasConflict, conflictReason) = await CheckLeaveOverlapAsync(employeeId, preview.EffectiveIntervals, null, cancellationToken, connection);
        if (hasConflict)
        {
            throw new LeaveConflictException("leave_overlap", conflictReason ?? "Khoảng thời gian này trùng với đơn nghỉ phép khác đang chờ duyệt hoặc đã duyệt.");
        }

        // Insert don_nghi_phep
        const string insertSql = """
            INSERT INTO public.don_nghi_phep (
                id_nhan_vien, don_vi, ma_ly_do, ly_do, is_emergency, is_late_submission,
                late_submission_reason, id_dang_ky_ca, ngay_nghi, ngay_bat_dau, ngay_ket_thuc,
                so_ngay_lich, trang_thai, version, phuong_an_luong, payroll_readiness,
                schedule_basis, tong_phut_nghi, created_at, updated_at
            )
            VALUES (
                @employeeId, @unit, @reasonCode, @reason, @isEmergency, @isLateSubmission,
                @lateSubmissionReason, @shiftAssignmentId, @date, @startDate, @endDate,
                @calendarDays, 'Pending', 1, 'Unresolved', 'NeedsReview',
                @scheduleBasis, @scheduledLeaveMinutes, @nowUtc, @nowUtc
            )
            RETURNING id;
            """;

        await using var insertCmd = new NpgsqlCommand(insertSql, connection, transaction);
        insertCmd.Parameters.AddWithValue("employeeId", employeeId);
        insertCmd.Parameters.AddWithValue("unit", command.Unit);
        insertCmd.Parameters.AddWithValue("reasonCode", command.ReasonCode);
        insertCmd.Parameters.AddWithValue("reason", command.Reason.Trim());
        insertCmd.Parameters.AddWithValue("isEmergency", command.IsEmergency);
        insertCmd.Parameters.AddWithValue("isLateSubmission", preview.IsLateSubmission);
        insertCmd.Parameters.AddWithValue("lateSubmissionReason", (object?)command.LateSubmissionReason?.Trim() ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("shiftAssignmentId", (object?)command.ShiftAssignmentId ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("date", command.Date is not null ? DateOnly.ParseExact(command.Date, "yyyy-MM-dd").ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        insertCmd.Parameters.AddWithValue("startDate", command.StartDate is not null ? DateOnly.ParseExact(command.StartDate, "yyyy-MM-dd").ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        insertCmd.Parameters.AddWithValue("endDate", command.EndDate is not null ? DateOnly.ParseExact(command.EndDate, "yyyy-MM-dd").ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        insertCmd.Parameters.AddWithValue("calendarDays", (object?)preview.CalendarDays ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("scheduleBasis", preview.ScheduleBasis);
        insertCmd.Parameters.AddWithValue("scheduledLeaveMinutes", preview.ScheduledLeaveMinutes);
        insertCmd.Parameters.AddWithValue("nowUtc", nowUtc);

        var leaveRequestId = Convert.ToInt64(await insertCmd.ExecuteScalarAsync(cancellationToken));

        // Insert don_nghi_phep_khoang
        const string insertIntervalSql = """
            INSERT INTO public.don_nghi_phep_khoang (id_don_nghi_phep, id_nhan_vien, gio_bat_dau_utc, gio_ket_thuc_utc, created_at)
            VALUES (@leaveRequestId, @employeeId, @startUtc, @endUtc, @nowUtc);
            """;

        foreach (var interval in preview.EffectiveIntervals)
        {
            await using var intCmd = new NpgsqlCommand(insertIntervalSql, connection, transaction);
            intCmd.Parameters.AddWithValue("leaveRequestId", leaveRequestId);
            intCmd.Parameters.AddWithValue("employeeId", employeeId);
            intCmd.Parameters.AddWithValue("startUtc", interval.StartUtc);
            intCmd.Parameters.AddWithValue("endUtc", interval.EndUtc);
            intCmd.Parameters.AddWithValue("nowUtc", nowUtc);
            await intCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Insert don_nghi_phep_ca
        const string insertShiftSql = """
            INSERT INTO public.don_nghi_phep_ca (
                id_don_nghi_phep, id_dang_ky_ca, id_ca_lam, ten_ca, ngay_lam,
                gio_bat_dau, gio_ket_thuc, qua_dem, phut_theo_lich, created_at
            )
            VALUES (
                @leaveRequestId, @assignmentId, @shiftId, @shiftName, @shiftDate,
                @startTime, @endTime, @isOvernight, @scheduledMinutes, @nowUtc
            );
            """;

        foreach (var aff in preview.AffectedAssignments)
        {
            await using var sCmd = new NpgsqlCommand(insertShiftSql, connection, transaction);
            sCmd.Parameters.AddWithValue("leaveRequestId", leaveRequestId);
            sCmd.Parameters.AddWithValue("assignmentId", aff.AssignmentId);
            sCmd.Parameters.AddWithValue("shiftId", aff.ShiftId);
            sCmd.Parameters.AddWithValue("shiftName", aff.ShiftName);
            sCmd.Parameters.AddWithValue("shiftDate", DateOnly.ParseExact(aff.ShiftDate, "yyyy-MM-dd").ToDateTime(TimeOnly.MinValue));
            sCmd.Parameters.AddWithValue("startTime", TimeSpan.Parse(aff.StartTime));
            sCmd.Parameters.AddWithValue("endTime", TimeSpan.Parse(aff.EndTime));
            sCmd.Parameters.AddWithValue("isOvernight", aff.IsOvernight);
            sCmd.Parameters.AddWithValue("scheduledMinutes", aff.ScheduledMinutes);
            sCmd.Parameters.AddWithValue("nowUtc", nowUtc);
            await sCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Insert don_nghi_phep_ngay
        const string insertDaySql = """
            INSERT INTO public.don_nghi_phep_ngay (id_don_nghi_phep, id_nhan_vien, ngay, phut_nghi, created_at)
            VALUES (@leaveRequestId, @employeeId, @date, @minutes, @nowUtc);
            """;

        foreach (var day in preview.DayAllocations)
        {
            await using var dCmd = new NpgsqlCommand(insertDaySql, connection, transaction);
            dCmd.Parameters.AddWithValue("leaveRequestId", leaveRequestId);
            dCmd.Parameters.AddWithValue("employeeId", employeeId);
            dCmd.Parameters.AddWithValue("date", DateOnly.ParseExact(day.Date, "yyyy-MM-dd").ToDateTime(TimeOnly.MinValue));
            dCmd.Parameters.AddWithValue("minutes", day.ScheduledMinutes);
            dCmd.Parameters.AddWithValue("nowUtc", nowUtc);
            await dCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Insert audit log
        const string insertAuditSql = """
            INSERT INTO public.lich_su_don_nghi_phep (id_don_nghi_phep, id_nguoi_thuc_hien, hanh_dong, trang_thai_truoc, trang_thai_sau, ghi_chu, thoi_gian_utc)
            VALUES (@leaveRequestId, @employeeId, 'CREATED', NULL, 'Pending', 'Nhân viên gửi đơn nghỉ phép', @nowUtc);
            """;
        await using var auditCmd = new NpgsqlCommand(insertAuditSql, connection, transaction);
        auditCmd.Parameters.AddWithValue("leaveRequestId", leaveRequestId);
        auditCmd.Parameters.AddWithValue("employeeId", employeeId);
        auditCmd.Parameters.AddWithValue("nowUtc", nowUtc);
        await auditCmd.ExecuteNonQueryAsync(cancellationToken);

        var dto = (await GetByIdCoreAsync(leaveRequestId, cancellationToken, connection, transaction))!;

        // Idempotency save
        if (!string.IsNullOrWhiteSpace(idempotencyKey) && !string.IsNullOrWhiteSpace(requestHash))
        {
            const string idempSql = """
                INSERT INTO public.don_nghi_phep_idempotency (id_nhan_vien, key, endpoint, request_hash, response_json, status_code, created_at)
                VALUES (@employeeId, @key, '/api/leave-requests', @hash, @responseJson, 201, @nowUtc)
                ON CONFLICT (id_nhan_vien, key, endpoint) DO NOTHING;
                """;
            await using var idempCmd = new NpgsqlCommand(idempSql, connection, transaction);
            idempCmd.Parameters.AddWithValue("employeeId", employeeId);
            idempCmd.Parameters.AddWithValue("key", idempotencyKey);
            idempCmd.Parameters.AddWithValue("hash", requestHash);
            idempCmd.Parameters.AddWithValue("responseJson", JsonSerializer.Serialize(dto));
            idempCmd.Parameters.AddWithValue("nowUtc", nowUtc);
            await idempCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        memoryCache.Remove(PreviewCachePrefix + command.PreviewToken);

        return dto;
    }

    public async Task<LeaveRequestDto> CancelLeaveRequestAsync(
        long leaveRequestId,
        long employeeId,
        CancelLeaveRequestCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await LockEmployeeAsync(connection, employeeId, requireActive: false, cancellationToken);

        const string selectSql = """
            SELECT id, id_nhan_vien, trang_thai, version
            FROM public.don_nghi_phep
            WHERE id = @id
            FOR UPDATE;
            """;

        await using var selectCmd = new NpgsqlCommand(selectSql, connection, transaction);
        selectCmd.Parameters.AddWithValue("id", leaveRequestId);
        await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new LeaveNotFoundException("leave_request_not_found", "Không tìm thấy đơn nghỉ phép.");
        }

        var ownerId = reader.GetInt64(1);
        var status = reader.GetString(2);
        var currentVersion = reader.GetInt32(3);
        await reader.CloseAsync();

        if (ownerId != employeeId)
        {
            throw new LeaveNotFoundException("leave_request_not_found", "Không tìm thấy đơn nghỉ phép.");
        }

        if (status != LeaveStatuses.Pending)
        {
            throw new LeaveConflictException("invalid_leave_status", "Chỉ có thể hủy đơn nghỉ phép đang ở trạng thái Chờ duyệt (Pending).");
        }

        if (currentVersion != command.Version)
        {
            throw new LeaveConflictException("stale_version", "Đơn nghỉ phép đã bị cập nhật bởi thao tác khác. Vui lòng tải lại trang.");
        }

        const string updateSql = """
            UPDATE public.don_nghi_phep
            SET trang_thai = 'Cancelled',
                version = version + 1,
                ngay_huy = @nowUtc,
                updated_at = @nowUtc
            WHERE id = @id;
            """;

        await using var updateCmd = new NpgsqlCommand(updateSql, connection, transaction);
        updateCmd.Parameters.AddWithValue("id", leaveRequestId);
        updateCmd.Parameters.AddWithValue("nowUtc", nowUtc);
        await updateCmd.ExecuteNonQueryAsync(cancellationToken);

        const string auditSql = """
            INSERT INTO public.lich_su_don_nghi_phep (id_don_nghi_phep, id_nguoi_thuc_hien, hanh_dong, trang_thai_truoc, trang_thai_sau, ghi_chu, thoi_gian_utc)
            VALUES (@leaveRequestId, @employeeId, 'CANCELLED', 'Pending', 'Cancelled', @note, @nowUtc);
            """;
        await using var auditCmd = new NpgsqlCommand(auditSql, connection, transaction);
        auditCmd.Parameters.AddWithValue("leaveRequestId", leaveRequestId);
        auditCmd.Parameters.AddWithValue("employeeId", employeeId);
        auditCmd.Parameters.AddWithValue("note", (object?)command.Note?.Trim() ?? "Nhân viên tự hủy đơn");
        auditCmd.Parameters.AddWithValue("nowUtc", nowUtc);
        await auditCmd.ExecuteNonQueryAsync(cancellationToken);

        var dto = (await GetByIdCoreAsync(leaveRequestId, cancellationToken, connection, transaction))!;
        await transaction.CommitAsync(cancellationToken);
        return dto;
    }

    public async Task<LeaveRequestDto> ApproveLeaveRequestAsync(
        long leaveRequestId,
        long adminId,
        ApproveLeaveRequestCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.PreviewToken))
        {
            throw new LeaveValidationException("validation_error", "PreviewToken xét duyệt là bắt buộc.");
        }

        if (!LeavePayrollTreatments.All.Contains(command.PayrollTreatment))
        {
            throw new LeaveValidationException("validation_error", $"Phương án tính lương không hợp lệ: '{command.PayrollTreatment}'.");
        }

        if (!memoryCache.TryGetValue<PreviewCachePayload>(PreviewCachePrefix + command.PreviewToken, out var preview) || preview is null)
        {
            throw new LeaveConflictException("preview_expired", "Phiên xem xét duyệt đã hết hạn. Vui lòng tải lại trang và xem trước lại.");
        }

        if (preview.LeaveRequestId != leaveRequestId)
        {
            throw new LeaveConflictException("schedule_changed", "Preview token không khớp với đơn nghỉ phép này.");
        }
        if (preview.AdminId.HasValue && preview.AdminId != adminId)
            throw new LeaveForbiddenException("forbidden", "Phiên xem xét thuộc người duyệt khác. Vui lòng xem trước lại.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await LockRequestEmployeeAsync(connection, leaveRequestId, cancellationToken);

        const string selectSql = """
            SELECT dnp.id, dnp.id_nhan_vien, dnp.trang_thai, dnp.version, dnp.so_ngay_lich, dnp.is_late_submission,
                   COALESCE(nv.loai_nhan_vien, 'FullTime')
            FROM public.don_nghi_phep dnp
            JOIN public.nhan_vien nv ON nv.id = dnp.id_nhan_vien
            WHERE dnp.id = @id
            FOR UPDATE OF dnp;
            """;

        await using var selectCmd = new NpgsqlCommand(selectSql, connection, transaction);
        selectCmd.Parameters.AddWithValue("id", leaveRequestId);
        await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new LeaveNotFoundException("leave_request_not_found", "Không tìm thấy đơn nghỉ phép.");
        }

        var employeeId = reader.GetInt64(1);
        var status = reader.GetString(2);
        var currentVersion = reader.GetInt32(3);
        var calendarDays = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
        var isLateSubmission = reader.GetBoolean(5);
        var currentEmployeeType = reader.GetString(6);
        await reader.CloseAsync();

        if (employeeId == adminId)
        {
            throw new LeaveForbiddenException("self_review_not_allowed", "Admin không được phép tự duyệt đơn nghỉ phép của chính mình.");
        }

        if (status != LeaveStatuses.Pending)
        {
            throw new LeaveConflictException("invalid_leave_status", "Đơn nghỉ phép này đã được xử lý (không ở trạng thái Pending).");
        }

        if (currentVersion != command.Version)
        {
            throw new LeaveConflictException("stale_version", "Đơn nghỉ phép đã bị thay đổi bởi thao tác khác. Vui lòng tải lại.");
        }

        // Lock employee row
        await LockEmployeeAsync(connection, employeeId, requireActive: false, cancellationToken);

        if (preview.ExpiresAtUtc <= nowUtc || preview.ExistingVersion != currentVersion)
            throw new LeaveConflictException("stale_version", "Phiên xem trước đã cũ. Vui lòng tải lại.");
        var current = await ComputePreviewCalculationsAsync(employeeId, InputFromPreview(preview), leaveRequestId, cancellationToken, connection);
        EnsurePreviewMatches(preview, current);
        await EnsureOriginalShiftAsync(leaveRequestId, preview.Unit, current, connection, cancellationToken);
        var overlap = await CheckLeaveOverlapAsync(employeeId, current.EffectiveIntervals, leaveRequestId, cancellationToken, connection);
        if (overlap.HasConflict)
            throw new LeaveConflictException("leave_overlap", overlap.Reason ?? "Trùng đơn nghỉ phép khác.");

        // Long leave check
        var isLongLeave = (calendarDays ?? 0) > 30;
        if (isLongLeave)
        {
            if (!command.LongLeaveAcknowledged)
            {
                throw new LeaveValidationException("validation_error", "Đơn nghỉ trên 30 ngày cần Admin xác nhận xem xét nghỉ dài hạn.");
            }
            if (string.IsNullOrWhiteSpace(command.ReviewNote))
            {
                throw new LeaveValidationException("validation_error", "Đơn nghỉ dài hạn bắt buộc phải nhập ghi chú xét duyệt.");
            }
        }

        // Late submission check
        if (isLateSubmission && string.IsNullOrWhiteSpace(command.ReviewNote))
        {
            throw new LeaveValidationException("validation_error", "Đơn nộp muộn bổ sung bắt buộc phải có ghi chú duyệt nêu rõ căn cứ chấp nhận.");
        }

        // Expanded coverage check
        if (preview.HasExpandedCoverage && !command.ExpandedCoverageAcknowledged)
        {
            throw new LeaveValidationException("validation_error", "Cần xác nhận mở rộng phạm vi nghỉ cho các ca làm việc qua đêm liên quan.");
        }

        // Attendance conflict check: if employee checked in or completed attendance in interval -> 409
        var hasAttendance = await CheckAttendanceOverlapAsync(employeeId, preview.EffectiveIntervals, cancellationToken, connection, transaction);
        if (hasAttendance)
        {
            throw new LeaveConflictException("attendance_conflict", "Nhân viên đã có dữ liệu chấm công thực tế trong khoảng thời gian nghỉ này. Không thể duyệt đơn qua luồng nghỉ phép.");
        }

        await ReplaceSnapshotAsync(leaveRequestId, employeeId, current, connection, transaction, nowUtc, cancellationToken);

        // Update don_nghi_phep
        const string updateSql = """
            UPDATE public.don_nghi_phep
            SET trang_thai = 'Approved',
                version = version + 1,
                phuong_an_luong = @payrollTreatment,
                payroll_readiness = 'NeedsReview',
                loai_nhan_vien_snapshot = @employeeTypeSnapshot,
                id_nguoi_duyet = @adminId,
                ngay_duyet = @nowUtc,
                ghi_chu_duyet = @reviewNote,
                updated_at = @nowUtc
            WHERE id = @id;
            """;

        await using var updateCmd = new NpgsqlCommand(updateSql, connection, transaction);
        updateCmd.Parameters.AddWithValue("id", leaveRequestId);
        updateCmd.Parameters.AddWithValue("payrollTreatment", command.PayrollTreatment);
        updateCmd.Parameters.AddWithValue("employeeTypeSnapshot", currentEmployeeType);
        updateCmd.Parameters.AddWithValue("adminId", adminId);
        updateCmd.Parameters.AddWithValue("nowUtc", nowUtc);
        updateCmd.Parameters.AddWithValue("reviewNote", (object?)command.ReviewNote?.Trim() ?? DBNull.Value);
        await updateCmd.ExecuteNonQueryAsync(cancellationToken);

        // Update don_nghi_phep_ca snapshot employee type
        const string updateCaSql = """
            UPDATE public.don_nghi_phep_ca
            SET loai_nhan_vien_snapshot = @employeeTypeSnapshot
            WHERE id_don_nghi_phep = @leaveRequestId;
            """;
        await using var updateCaCmd = new NpgsqlCommand(updateCaSql, connection, transaction);
        updateCaCmd.Parameters.AddWithValue("employeeTypeSnapshot", currentEmployeeType);
        updateCaCmd.Parameters.AddWithValue("leaveRequestId", leaveRequestId);
        await updateCaCmd.ExecuteNonQueryAsync(cancellationToken);

        // Insert audit log
        const string auditSql = """
            INSERT INTO public.lich_su_don_nghi_phep (id_don_nghi_phep, id_nguoi_thuc_hien, hanh_dong, trang_thai_truoc, trang_thai_sau, ghi_chu, thoi_gian_utc)
            VALUES (@leaveRequestId, @adminId, 'APPROVED', 'Pending', 'Approved', @note, @nowUtc);
            """;
        await using var auditCmd = new NpgsqlCommand(auditSql, connection, transaction);
        auditCmd.Parameters.AddWithValue("leaveRequestId", leaveRequestId);
        auditCmd.Parameters.AddWithValue("adminId", adminId);
        auditCmd.Parameters.AddWithValue("note", (object?)command.ReviewNote?.Trim() ?? "Phê duyệt đơn nghỉ phép");
        auditCmd.Parameters.AddWithValue("nowUtc", nowUtc);
        await auditCmd.ExecuteNonQueryAsync(cancellationToken);

        await InsertEmployeeNotificationAsync(connection, transaction, employeeId, leaveRequestId, "Approved",
            currentVersion + 1, nowUtc, cancellationToken);

        var dto = (await GetByIdCoreAsync(leaveRequestId, cancellationToken, connection, transaction))!;
        await transaction.CommitAsync(cancellationToken);

        memoryCache.Remove(PreviewCachePrefix + command.PreviewToken);
        return dto;
    }

    public async Task<LeaveRequestDto> RejectLeaveRequestAsync(
        long leaveRequestId,
        long adminId,
        RejectLeaveRequestCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new LeaveValidationException("validation_error", "Lý do từ chối không được để trống.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await LockRequestEmployeeAsync(connection, leaveRequestId, cancellationToken);

        const string selectSql = """
            SELECT id, id_nhan_vien, trang_thai, version
            FROM public.don_nghi_phep
            WHERE id = @id
            FOR UPDATE;
            """;

        await using var selectCmd = new NpgsqlCommand(selectSql, connection, transaction);
        selectCmd.Parameters.AddWithValue("id", leaveRequestId);
        await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new LeaveNotFoundException("leave_request_not_found", "Không tìm thấy đơn nghỉ phép.");
        }

        var employeeId = reader.GetInt64(1);
        var status = reader.GetString(2);
        var currentVersion = reader.GetInt32(3);
        await reader.CloseAsync();

        if (employeeId == adminId)
        {
            throw new LeaveForbiddenException("self_review_not_allowed", "Admin không được phép tự từ chối đơn của chính mình.");
        }

        if (status != LeaveStatuses.Pending)
        {
            throw new LeaveConflictException("invalid_leave_status", "Đơn không ở trạng thái Chờ duyệt (Pending).");
        }

        if (currentVersion != command.Version)
        {
            throw new LeaveConflictException("stale_version", "Đơn đã bị thay đổi bởi thao tác khác. Vui lòng tải lại.");
        }

        await LockEmployeeAsync(connection, employeeId, requireActive: false, cancellationToken);

        const string updateSql = """
            UPDATE public.don_nghi_phep
            SET trang_thai = 'Rejected',
                version = version + 1,
                id_nguoi_duyet = @adminId,
                ngay_duyet = @nowUtc,
                ly_do_tu_choi = @reason,
                updated_at = @nowUtc
            WHERE id = @id;
            """;

        await using var updateCmd = new NpgsqlCommand(updateSql, connection, transaction);
        updateCmd.Parameters.AddWithValue("id", leaveRequestId);
        updateCmd.Parameters.AddWithValue("adminId", adminId);
        updateCmd.Parameters.AddWithValue("reason", command.Reason.Trim());
        updateCmd.Parameters.AddWithValue("nowUtc", nowUtc);
        await updateCmd.ExecuteNonQueryAsync(cancellationToken);

        const string auditSql = """
            INSERT INTO public.lich_su_don_nghi_phep (id_don_nghi_phep, id_nguoi_thuc_hien, hanh_dong, trang_thai_truoc, trang_thai_sau, ghi_chu, thoi_gian_utc)
            VALUES (@leaveRequestId, @adminId, 'REJECTED', 'Pending', 'Rejected', @reason, @nowUtc);
            """;
        await using var auditCmd = new NpgsqlCommand(auditSql, connection, transaction);
        auditCmd.Parameters.AddWithValue("leaveRequestId", leaveRequestId);
        auditCmd.Parameters.AddWithValue("adminId", adminId);
        auditCmd.Parameters.AddWithValue("reason", command.Reason.Trim());
        auditCmd.Parameters.AddWithValue("nowUtc", nowUtc);
        await auditCmd.ExecuteNonQueryAsync(cancellationToken);

        await InsertEmployeeNotificationAsync(connection, transaction, employeeId, leaveRequestId, "Rejected",
            currentVersion + 1, nowUtc, cancellationToken);

        var dto = (await GetByIdCoreAsync(leaveRequestId, cancellationToken, connection, transaction))!;
        await transaction.CommitAsync(cancellationToken);
        return dto;
    }

    public async Task<LeaveRequestDto> RevokeLeaveRequestAsync(
        long leaveRequestId,
        long adminId,
        RevokeLeaveRequestCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new LeaveValidationException("validation_error", "Lý do thu hồi không được để trống.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await LockRequestEmployeeAsync(connection, leaveRequestId, cancellationToken);

        const string selectSql = """
            SELECT id, id_nhan_vien, trang_thai, version
            FROM public.don_nghi_phep
            WHERE id = @id
            FOR UPDATE;
            """;

        await using var selectCmd = new NpgsqlCommand(selectSql, connection, transaction);
        selectCmd.Parameters.AddWithValue("id", leaveRequestId);
        await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new LeaveNotFoundException("leave_request_not_found", "Không tìm thấy đơn nghỉ phép.");
        }

        var employeeId = reader.GetInt64(1);
        var status = reader.GetString(2);
        var currentVersion = reader.GetInt32(3);
        await reader.CloseAsync();

        if (status != LeaveStatuses.Approved)
        {
            throw new LeaveConflictException("invalid_leave_status", "Chỉ có thể thu hồi đơn đang ở trạng thái Đã duyệt (Approved).");
        }

        if (currentVersion != command.Version)
        {
            throw new LeaveConflictException("stale_version", "Đơn đã bị thay đổi bởi thao tác khác. Vui lòng tải lại.");
        }

        // Check earliest interval start time
        const string minStartSql = """
            SELECT MIN(gio_bat_dau_utc)
            FROM public.don_nghi_phep_khoang
            WHERE id_don_nghi_phep = @leaveRequestId;
            """;
        await using var minStartCmd = new NpgsqlCommand(minStartSql, connection, transaction);
        minStartCmd.Parameters.AddWithValue("leaveRequestId", leaveRequestId);
        var minStartObj = await minStartCmd.ExecuteScalarAsync(cancellationToken);
        if (minStartObj is not null && minStartObj != DBNull.Value)
        {
            var earliestStartUtc = new DateTimeOffset((DateTime)minStartObj, TimeSpan.Zero);
            if (earliestStartUtc <= nowUtc)
            {
                throw new LeaveConflictException("invalid_leave_status", "Chỉ có thể thu hồi đơn đã duyệt TRƯỚC KHI thời gian nghỉ thực tế bắt đầu.");
            }
        }

        // Check attendance
        var intervals = await GetIntervalsForLeaveRequestAsync(leaveRequestId, cancellationToken, connection, transaction);
        var hasAttendance = await CheckAttendanceOverlapAsync(employeeId, intervals, cancellationToken, connection, transaction);
        if (hasAttendance)
        {
            throw new LeaveConflictException("attendance_conflict", "Đã có dữ liệu chấm công liên quan, không thể thu hồi đơn.");
        }

        await LockEmployeeAsync(connection, employeeId, requireActive: false, cancellationToken);

        const string updateSql = """
            UPDATE public.don_nghi_phep
            SET trang_thai = 'Revoked',
                version = version + 1,
                ngay_thu_hoi = @nowUtc,
                ly_do_thu_hoi = @reason,
                updated_at = @nowUtc
            WHERE id = @id;
            """;

        await using var updateCmd = new NpgsqlCommand(updateSql, connection, transaction);
        updateCmd.Parameters.AddWithValue("id", leaveRequestId);
        updateCmd.Parameters.AddWithValue("reason", command.Reason.Trim());
        updateCmd.Parameters.AddWithValue("nowUtc", nowUtc);
        await updateCmd.ExecuteNonQueryAsync(cancellationToken);

        const string auditSql = """
            INSERT INTO public.lich_su_don_nghi_phep (id_don_nghi_phep, id_nguoi_thuc_hien, hanh_dong, trang_thai_truoc, trang_thai_sau, ghi_chu, thoi_gian_utc)
            VALUES (@leaveRequestId, @adminId, 'REVOKED', 'Approved', 'Revoked', @reason, @nowUtc);
            """;
        await using var auditCmd = new NpgsqlCommand(auditSql, connection, transaction);
        auditCmd.Parameters.AddWithValue("leaveRequestId", leaveRequestId);
        auditCmd.Parameters.AddWithValue("adminId", adminId);
        auditCmd.Parameters.AddWithValue("reason", command.Reason.Trim());
        auditCmd.Parameters.AddWithValue("nowUtc", nowUtc);
        await auditCmd.ExecuteNonQueryAsync(cancellationToken);

        var dto = (await GetByIdCoreAsync(leaveRequestId, cancellationToken, connection, transaction))!;
        await transaction.CommitAsync(cancellationToken);
        return dto;
    }

    public async Task<(int StatusCode, string ResponseJson)?> GetIdempotencyRecordAsync(
        long employeeId,
        string key,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT status_code, response_json, request_hash
            FROM public.don_nghi_phep_idempotency
            WHERE id_nhan_vien = @employeeId AND key = @key AND endpoint = @endpoint
            LIMIT 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("employeeId", employeeId);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("endpoint", endpoint);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return (reader.GetInt32(0), reader.GetString(1));
        }

        return null;
    }

    public async Task SaveIdempotencyRecordAsync(
        long employeeId,
        string key,
        string endpoint,
        string requestHash,
        string responseJson,
        int statusCode,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO public.don_nghi_phep_idempotency (id_nhan_vien, key, endpoint, request_hash, response_json, status_code, created_at)
            VALUES (@employeeId, @key, @endpoint, @hash, @responseJson, @statusCode, NOW())
            ON CONFLICT (id_nhan_vien, key, endpoint) DO NOTHING;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("employeeId", employeeId);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("endpoint", endpoint);
        command.Parameters.AddWithValue("hash", requestHash);
        command.Parameters.AddWithValue("responseJson", responseJson);
        command.Parameters.AddWithValue("statusCode", statusCode);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateInput(CreateLeaveRequestInput input)
    {
        if (!LeaveUnits.All.Contains(input.Unit))
        {
            throw new LeaveValidationException("invalid_unit_fields", $"Đơn vị nghỉ không hợp lệ: '{input.Unit}'. Hợp lệ: Shift, Day, DateRange.");
        }

        if (!LeaveReasonCodes.All.Contains(input.ReasonCode))
        {
            throw new LeaveValidationException("validation_error", $"Mã lý do không hợp lệ: '{input.ReasonCode}'.");
        }

        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length < 10 || input.Reason.Trim().Length > 2000)
        {
            throw new LeaveValidationException("validation_error", "Giải trình lý do phải từ 10 đến 2000 ký tự.");
        }

        foreach (var value in new[] { input.Date, input.StartDate, input.EndDate })
            if (value is not null && DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date) && (date.Year < 1900 || date.Year > 9998))
                throw new LeaveValidationException("invalid_date_range", "Năm phải từ 1900 đến 9998.");
        if (input.LateSubmissionReason?.Length > 2000)
            throw new LeaveValidationException("validation_error", "Giải trình nộp muộn tối đa 2000 ký tự.");

        if (input.Unit == LeaveUnits.Shift)
        {
            if (!input.ShiftAssignmentId.HasValue || input.ShiftAssignmentId.Value <= 0)
            {
                throw new LeaveValidationException("invalid_unit_fields", "Với đơn vị Shift, shiftAssignmentId là bắt buộc.");
            }
            if (input.Date is not null || input.StartDate is not null || input.EndDate is not null)
            {
                throw new LeaveValidationException("invalid_unit_fields", "Với đơn vị Shift, không được cung cấp date, startDate hoặc endDate.");
            }
        }
        else if (input.Unit == LeaveUnits.Day)
        {
            if (string.IsNullOrWhiteSpace(input.Date) || !DateOnly.TryParseExact(input.Date, "yyyy-MM-dd", out _))
            {
                throw new LeaveValidationException("invalid_unit_fields", "Với đơn vị Day, date (yyyy-MM-dd) là bắt buộc và phải hợp lệ.");
            }
            if (input.ShiftAssignmentId.HasValue || input.StartDate is not null || input.EndDate is not null)
            {
                throw new LeaveValidationException("invalid_unit_fields", "Với đơn vị Day, không được cung cấp shiftAssignmentId, startDate hoặc endDate.");
            }
        }
        else if (input.Unit == LeaveUnits.DateRange)
        {
            if (string.IsNullOrWhiteSpace(input.StartDate) || !DateOnly.TryParseExact(input.StartDate, "yyyy-MM-dd", out var start) ||
                string.IsNullOrWhiteSpace(input.EndDate) || !DateOnly.TryParseExact(input.EndDate, "yyyy-MM-dd", out var end))
            {
                throw new LeaveValidationException("invalid_unit_fields", "Với đơn vị DateRange, startDate và endDate (yyyy-MM-dd) là bắt buộc.");
            }
            if (input.ShiftAssignmentId.HasValue || input.Date is not null)
            {
                throw new LeaveValidationException("invalid_unit_fields", "Với đơn vị DateRange, không được cung cấp shiftAssignmentId hoặc date.");
            }
            if (start >= end)
            {
                throw new LeaveValidationException("invalid_date_range", "Ngày bắt đầu phải trước ngày kết thúc (tối thiểu 2 ngày liên tiếp).");
            }
            var days = end.DayNumber - start.DayNumber + 1;
            if (days > 366)
            {
                throw new LeaveValidationException("invalid_date_range", "Khoảng thời gian nghỉ tối đa là 366 ngày trên một đơn.");
            }
        }
    }

    private static async Task InsertEmployeeNotificationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long employeeId,
        long leaveRequestId,
        string status,
        int version,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        var isApproved = status == "Approved";
        const string sql = """
            INSERT INTO public.notifications
                (employee_id, type, title, message, data, idempotency_key, created_at_utc)
            VALUES
                (@employeeId, @type, @title, @message, @data, @idempotencyKey, @createdAtUtc)
            ON CONFLICT (idempotency_key) DO NOTHING;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("employeeId", employeeId);
        command.Parameters.AddWithValue("type", $"leave.{status.ToLowerInvariant()}");
        command.Parameters.AddWithValue("title", isApproved ? "Đơn nghỉ phép đã được duyệt" : "Đơn nghỉ phép đã bị từ chối");
        command.Parameters.AddWithValue("message", $"Đơn nghỉ phép #{leaveRequestId} đã được { (isApproved ? "duyệt" : "từ chối") }.");
        command.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(new { leaveRequestId, status }));
        command.Parameters.AddWithValue("idempotencyKey", $"leave:{leaveRequestId}:{status.ToLowerInvariant()}:{version}");
        command.Parameters.AddWithValue("createdAtUtc", createdAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LockEmployeeAsync(NpgsqlConnection connection, long employeeId, bool requireActive, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT COALESCE(trang_thai, 'Active') FROM public.nhan_vien WHERE id = @id FOR UPDATE", connection);
        command.Parameters.AddWithValue("id", employeeId);
        var status = await command.ExecuteScalarAsync(ct);
        if (status is null) throw new LeaveNotFoundException("employee_not_found", "Không tìm thấy nhân viên.");
        if (requireActive && !string.Equals(status.ToString(), "Active", StringComparison.OrdinalIgnoreCase))
            throw new LeaveForbiddenException("employee_inactive", "Nhân viên không hoạt động, không thể thực hiện thao tác.");
    }
}
