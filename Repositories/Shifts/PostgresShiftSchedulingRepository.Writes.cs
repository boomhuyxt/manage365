using manage365.Routes.API.Shifts;
using Npgsql;
using NpgsqlTypes;

namespace manage365.Repositories.Shifts;

public sealed partial class PostgresShiftSchedulingRepository
{
    private async Task<long?> ResolveShopAsync(long? requested, long? existing, CancellationToken ct)
    {
        var actor = httpContextAccessor?.HttpContext?.User;
        // Non-HTTP callers (maintenance/tests) may retain unknown legacy scope;
        // NULL never grants BLE access.
        if (actor is null) return requested ?? existing;
        var owner = ShopScopeRepository.Resolve(actor, requested, existing);
        var shops = await new ShopScopeRepository(dataSource).ListAsync(actor, ct);
        if (!shops.Any(s => s.OwnerId == owner)) throw new ArgumentException("Cửa hàng không tồn tại hoặc tài khoản phụ trách đã bị khóa.");
        return owner;
    }

    private NpgsqlCommand CreateCommand(string sql, NpgsqlConnection? connection = null)
        => connection is null ? dataSource.CreateCommand(sql) : new NpgsqlCommand(sql, connection);

    public async Task<ScheduleEmployeesResponse> GetEmployeesAsync(string? search, long? departmentId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        const string where = """
            FROM public.nhan_vien nv LEFT JOIN public.phong_ban pb ON pb.id = nv.id_phong_ban
            WHERE (CAST(@department AS bigint) IS NULL OR nv.id_phong_ban = @department)
              AND (@search = '' OR nv.ho_ten ILIKE @pattern OR ('NV-' || lpad(nv.id::text, greatest(4, length(nv.id::text)), '0')) ILIKE @pattern)
            """;
        await using var count = dataSource.CreateCommand("SELECT count(*) " + where);
        AddFilter(count);
        var total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken));
        await using var command = dataSource.CreateCommand("SELECT nv.id, nv.ho_ten, nv.id_phong_ban, pb.ten_phong_ban, COALESCE(nv.trang_thai, 'Active') " + where + " ORDER BY nv.ho_ten, nv.id LIMIT @limit OFFSET @offset");
        AddFilter(command);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (page - 1) * pageSize);
        var items = new List<ScheduleEmployeeDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new(reader.GetInt64(0), $"NV-{reader.GetInt64(0):D4}", reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4)));
        return new(items, page, pageSize, total);

        void AddFilter(NpgsqlCommand cmd)
        {
            cmd.Parameters.AddWithValue("department", NpgsqlDbType.Bigint, (object?)departmentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("search", search?.Trim() ?? "");
            cmd.Parameters.AddWithValue("pattern", "%" + (search?.Trim() ?? "") + "%");
        }
    }

    // Every scheduling write for an employee takes the same row lock inside its transaction.
    // The conflict check consequently sees the preceding writer's committed assignments.
    private static async Task LockEmployeeAsync(NpgsqlConnection connection, long employeeId, bool requireActive, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT COALESCE(trang_thai, 'Active') FROM public.nhan_vien WHERE id = @id FOR UPDATE", connection);
        command.Parameters.AddWithValue("id", employeeId);
        var status = await command.ExecuteScalarAsync(ct);
        if (status is null) throw new KeyNotFoundException("Không tìm thấy nhân viên.");
        if (requireActive && !string.Equals(status.ToString(), "Active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Nhân viên không hoạt động, không thể phân ca.");
    }

    private async Task<ShiftTemplateDto> ActiveTemplateAsync(long id, NpgsqlConnection connection, CancellationToken ct)
    {
        // Hold a share lock so a template cannot be disabled or changed during assignment.
        await using var gate = new NpgsqlCommand("SELECT id FROM public.mau_ca_lam WHERE id = @id FOR SHARE", connection);
        gate.Parameters.AddWithValue("id", id);
        if (await gate.ExecuteScalarAsync(ct) is null) throw new KeyNotFoundException("Không tìm thấy mẫu ca.");
        var template = (await GetTemplateByIdCoreAsync(id, ct, connection))!;
        if (template.Status != "Active") throw new InvalidOperationException("Mẫu ca đã ngừng sử dụng.");
        return template;
    }

    private static async Task<long> InsertShiftAsync(NpgsqlConnection connection, DateOnly date, string name, TimeOnly start, TimeOnly end, bool overnight, long? templateId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO public.ca_lam (ngay_lam, loai_ca, gio_bat_dau, gio_ket_thuc, qua_dem, id_mau_ca)
            VALUES (@date, @name, @start, @end, @overnight, @template) RETURNING id;
            """, connection);
        command.Parameters.AddWithValue("date", date);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("start", start);
        command.Parameters.AddWithValue("end", end);
        command.Parameters.AddWithValue("overnight", overnight);
        command.Parameters.AddWithValue("template", NpgsqlDbType.Bigint, (object?)templateId ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    public async Task<ShiftAssignmentDto> AssignShiftAsync(AssignShiftRequest request, CancellationToken cancellationToken = default)
    {
        ShiftInput.Assignment(request);
        var shopOwnerId = await ResolveShopAsync(request.ShopOwnerId, null, cancellationToken);
        var date = ShiftInput.Date(request.ShiftDate);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockEmployeeAsync(connection, request.EmployeeId, true, cancellationToken);
        var template = request.ShiftTemplateId is { } id ? await ActiveTemplateAsync(id, connection, cancellationToken) : null;
        var start = ShiftInput.Time(template?.StartTime ?? request.CustomStartTime);
        var end = ShiftInput.Time(template?.EndTime ?? request.CustomEndTime);
        var overnight = template?.IsOvernight ?? request.IsOvernight ?? end < start;
        ShiftInput.Interval(start, end, overnight);
        var validation = await ValidateConflictCoreAsync(request.EmployeeId, date, start, end, overnight, null, cancellationToken, connection);
        if (validation.HasConflict) throw new InvalidOperationException(validation.Message);
        var shiftId = await InsertShiftAsync(connection, date, template?.Name ?? (string.IsNullOrWhiteSpace(request.CustomShiftName) ? "Ca tùy chỉnh" : request.CustomShiftName.Trim()), start, end, overnight, template?.Id, cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO public.dang_ky_ca (id_ca_lam, id_nhan_vien, trang_thai, ghi_chu, shop_owner_id)
            VALUES (@shift, @employee, 'Approved', @note, @shop) RETURNING id;
            """, connection);
        command.Parameters.AddWithValue("shift", shiftId);
        command.Parameters.AddWithValue("employee", request.EmployeeId);
        command.Parameters.AddWithValue("shop", NpgsqlDbType.Bigint, (object?)shopOwnerId ?? DBNull.Value);
        command.Parameters.AddWithValue("note", NpgsqlDbType.Text, (object?)request.Note ?? DBNull.Value);
        var assignmentId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        var result = (await GetAssignmentByIdCoreAsync(assignmentId, cancellationToken, connection))!;
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ShiftAssignmentDto?> UpdateAssignmentAsync(long assignmentId, UpdateShiftAssignmentRequest request, CancellationToken cancellationToken = default)
    {
        ShiftInput.Update(request);
        var original = await GetAssignmentByIdAsync(assignmentId, cancellationToken);
        if (original is null) return null;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockEmployeeAsync(connection, original.EmployeeId, request.Status != "Cancelled", cancellationToken);
        var existing = await GetAssignmentByIdCoreAsync(assignmentId, cancellationToken, connection);
        if (existing is null) return null;
        var shopOwnerId = await ResolveShopAsync(request.ShopOwnerId, existing.ShopOwnerId, cancellationToken);
        if (existing.Attendance is not null)
            throw new InvalidOperationException("Ca đã có chấm công; không được sửa hoặc hủy để giữ nguyên lịch sử.");
        if (existing.Status == "Cancelled")
            throw new InvalidOperationException("Ca đã hủy. Hãy tạo phân ca mới nếu cần.");

        const string checkLeaveSql = """
            SELECT 1 FROM public.don_nghi_phep_ca dnpc
            JOIN public.don_nghi_phep dnp ON dnp.id = dnpc.id_don_nghi_phep
            WHERE dnpc.id_dang_ky_ca = @assignmentId AND dnp.trang_thai = 'Approved'
            LIMIT 1;
            """;
        await using (var checkLeaveCmd = new NpgsqlCommand(checkLeaveSql, connection))
        {
            checkLeaveCmd.Parameters.AddWithValue("assignmentId", assignmentId);
            if (await checkLeaveCmd.ExecuteScalarAsync(cancellationToken) is not null)
            {
                throw new InvalidOperationException("Ca làm việc này đã được gắn với đơn nghỉ phép đã duyệt. Vui lòng thu hồi đơn nghỉ phép trước khi điều chỉnh hoặc hủy ca.");
            }
        }

        var shiftId = existing.ShiftId;
        var changesTime = request.ShiftTemplateId.HasValue || request.ShiftName is not null || request.StartTime is not null || request.EndTime is not null || request.IsOvernight.HasValue;
        if (request.Status == "Cancelled" && changesTime) throw new ArgumentException("Hủy ca không được đồng thời đổi giờ.");
        if (changesTime)
        {
            var template = request.ShiftTemplateId is { } id ? await ActiveTemplateAsync(id, connection, cancellationToken) : null;
            var start = ShiftInput.Time(template?.StartTime ?? request.StartTime ?? existing.StartTime);
            var end = ShiftInput.Time(template?.EndTime ?? request.EndTime ?? existing.EndTime);
            var overnight = template?.IsOvernight ?? request.IsOvernight ?? existing.IsOvernight;
            ShiftInput.Interval(start, end, overnight);
            var date = ShiftInput.Date(existing.ShiftDate);
            var validation = await ValidateConflictCoreAsync(existing.EmployeeId, date, start, end, overnight, assignmentId, cancellationToken, connection);
            if (validation.HasConflict) throw new InvalidOperationException(validation.Message);
            // A legacy ca_lam can be shared. Give this assignment its own immutable time snapshot.
            shiftId = await InsertShiftAsync(connection, date, template?.Name ?? request.ShiftName ?? existing.ShiftName, start, end, overnight, template?.Id, cancellationToken);
        }
        await using var command = new NpgsqlCommand("""
            UPDATE public.dang_ky_ca SET id_ca_lam = @shift, trang_thai = @status,
                ghi_chu = @note, shop_owner_id = @shop, updated_at = NOW() WHERE id = @id;
            """, connection);
        command.Parameters.AddWithValue("shift", shiftId);
        command.Parameters.AddWithValue("shop", NpgsqlDbType.Bigint, (object?)shopOwnerId ?? DBNull.Value);
        command.Parameters.AddWithValue("status", request.Status ?? existing.Status);
        command.Parameters.AddWithValue("note", NpgsqlDbType.Text, (object?)(request.Note ?? existing.Note) ?? DBNull.Value);
        command.Parameters.AddWithValue("id", assignmentId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (shiftId != existing.ShiftId)
        {
            await using var cleanup = new NpgsqlCommand("DELETE FROM public.ca_lam WHERE id = @id AND NOT EXISTS (SELECT 1 FROM public.dang_ky_ca WHERE id_ca_lam = @id)", connection);
            cleanup.Parameters.AddWithValue("id", existing.ShiftId);
            await cleanup.ExecuteNonQueryAsync(cancellationToken);
        }
        var result = await GetAssignmentByIdCoreAsync(assignmentId, cancellationToken, connection);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<bool> DeleteAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        var existing = await GetAssignmentByIdAsync(assignmentId, cancellationToken);
        if (existing is null) return false;
        if (existing.Status == "Cancelled") return true;
        return await UpdateAssignmentAsync(assignmentId, new UpdateShiftAssignmentRequest(Status: "Cancelled"), cancellationToken) is not null;
    }

    public async Task<BatchAssignShiftResult> BatchAssignShiftsAsync(BatchAssignShiftRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Assignments is null || request.Assignments.Count is < 1 or > 200 || request.Assignments.Any(a => a is null))
            throw new ArgumentException("Danh sách phân ca phải có 1–200 mục hợp lệ.");
        var created = new List<ShiftAssignmentDto>();
        var errors = new List<string>();
        foreach (var item in request.Assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { created.Add(await AssignShiftAsync(item, cancellationToken)); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
            { errors.Add($"NV-{item.EmployeeId:D4} ({item.ShiftDate}, {item.CustomStartTime ?? $"mẫu #{item.ShiftTemplateId}"}–{item.CustomEndTime ?? ""}): {ex.Message}"); }
            catch (PostgresException ex) when (ex.SqlState is "23503" or "23505")
            { errors.Add($"NV-{item.EmployeeId:D4} ({item.ShiftDate}, {item.CustomStartTime ?? $"mẫu #{item.ShiftTemplateId}"}–{item.CustomEndTime ?? ""}): Dữ liệu đã thay đổi hoặc bị trùng. Vui lòng tải lại."); }
        }
        return new(request.Assignments.Count, created.Count, errors.Count, errors, created);
    }

    public async Task<BatchAssignShiftResult> CopyWeeklyScheduleAsync(DateOnly sourceStartDate, DateOnly targetStartDate, CancellationToken cancellationToken = default)
    {
        if (sourceStartDate == targetStartDate || sourceStartDate.DayOfWeek != DayOfWeek.Monday || targetStartDate.DayOfWeek != DayOfWeek.Monday)
            throw new ArgumentException("Chọn hai tuần khác nhau, bắt đầu vào thứ Hai.");
        var schedule = await GetScheduleAsync(sourceStartDate, sourceStartDate.AddDays(6), cancellationToken: cancellationToken);
        var offset = targetStartDate.DayNumber - sourceStartDate.DayNumber;
        var requests = schedule.Assignments.Where(a => a.Status != "Cancelled" && (httpContextAccessor?.HttpContext?.User.IsInRole("Manager") != true || a.ShopOwnerId == ShopScopeRepository.ActorId(httpContextAccessor.HttpContext.User))).Select(a => new AssignShiftRequest(
            a.EmployeeId, ShiftInput.Date(a.ShiftDate).AddDays(offset).ToString("yyyy-MM-dd"),
            CustomShiftName: a.ShiftName, CustomStartTime: a.StartTime, CustomEndTime: a.EndTime,
            IsOvernight: a.IsOvernight, Note: $"Sao chép từ tuần {sourceStartDate:dd/MM/yyyy}", ShopOwnerId: a.ShopOwnerId)).ToList();
        if (requests.Count == 0) return new(0, 0, 0, [], []);
        if (requests.Count > 200) throw new ArgumentException("Tuần nguồn có hơn 200 ca; hãy phân ca theo từng nhóm.");
        return await BatchAssignShiftsAsync(new(requests), cancellationToken);
    }
}
