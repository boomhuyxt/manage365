using System.Globalization;
using manage365.Routes.API.Shifts;
using Npgsql;

namespace manage365.Repositories.Shifts;

public sealed partial class PostgresShiftSchedulingRepository(NpgsqlDataSource dataSource) : IShiftSchedulingRepository
{
    public async Task EnsureTablesAndSeedAsync(CancellationToken cancellationToken = default)
    {
        const string ddlSql = """
            CREATE SEQUENCE IF NOT EXISTS public.mau_ca_lam_id_seq;

            CREATE TABLE IF NOT EXISTS public.mau_ca_lam (
                id BIGINT PRIMARY KEY DEFAULT nextval('public.mau_ca_lam_id_seq'),
                ma_mau_ca VARCHAR(50) NOT NULL UNIQUE,
                ten_mau_ca VARCHAR(150) NOT NULL,
                gio_bat_dau TIME NOT NULL,
                gio_ket_thuc TIME NOT NULL,
                qua_dem BOOLEAN NOT NULL DEFAULT FALSE,
                mau_sac VARCHAR(30) NOT NULL DEFAULT '#f97316',
                mo_ta TEXT NULL,
                trang_thai VARCHAR(50) NOT NULL DEFAULT 'Active',
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE SEQUENCE IF NOT EXISTS public.ca_lam_id_seq;

            CREATE TABLE IF NOT EXISTS public.ca_lam (
                id BIGINT PRIMARY KEY DEFAULT nextval('public.ca_lam_id_seq'),
                ngay_lam DATE NOT NULL,
                loai_ca VARCHAR(150) NOT NULL,
                gio_bat_dau TIME NOT NULL,
                gio_ket_thuc TIME NOT NULL,
                qua_dem BOOLEAN NOT NULL DEFAULT FALSE,
                id_mau_ca BIGINT NULL REFERENCES public.mau_ca_lam(id) ON DELETE SET NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE SEQUENCE IF NOT EXISTS public.dang_ky_ca_id_seq;

            CREATE TABLE IF NOT EXISTS public.dang_ky_ca (
                id BIGINT PRIMARY KEY DEFAULT nextval('public.dang_ky_ca_id_seq'),
                id_ca_lam BIGINT NOT NULL REFERENCES public.ca_lam(id) ON DELETE CASCADE,
                id_nhan_vien BIGINT NOT NULL REFERENCES public.nhan_vien(id) ON DELETE CASCADE,
                trang_thai VARCHAR(50) NOT NULL DEFAULT 'Approved',
                ghi_chu TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT ux_dang_ky_ca_ca_nhan_vien UNIQUE (id_ca_lam, id_nhan_vien)
            );

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns 
                    WHERE table_schema = 'public' AND table_name = 'ca_lam' AND column_name = 'id_mau_ca'
                ) THEN
                    ALTER TABLE public.ca_lam ADD COLUMN id_mau_ca BIGINT NULL REFERENCES public.mau_ca_lam(id) ON DELETE SET NULL;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns 
                    WHERE table_schema = 'public' AND table_name = 'dang_ky_ca' AND column_name = 'ghi_chu'
                ) THEN
                    ALTER TABLE public.dang_ky_ca ADD COLUMN ghi_chu TEXT NULL;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns 
                    WHERE table_schema = 'public' AND table_name = 'dang_ky_ca' AND column_name = 'created_at'
                ) THEN
                    ALTER TABLE public.dang_ky_ca ADD COLUMN created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns 
                    WHERE table_schema = 'public' AND table_name = 'dang_ky_ca' AND column_name = 'updated_at'
                ) THEN
                    ALTER TABLE public.dang_ky_ca ADD COLUMN updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
                END IF;
            END $$;

            CREATE INDEX IF NOT EXISTS ix_ca_lam_ngay_lam ON public.ca_lam(ngay_lam);
            CREATE INDEX IF NOT EXISTS ix_ca_lam_mau_ca ON public.ca_lam(id_mau_ca);
            CREATE INDEX IF NOT EXISTS ix_dang_ky_ca_nhan_vien_trang_thai ON public.dang_ky_ca(id_nhan_vien, trang_thai);
            CREATE INDEX IF NOT EXISTS ix_dang_ky_ca_ca_lam ON public.dang_ky_ca(id_ca_lam);

            INSERT INTO public.mau_ca_lam (id, ma_mau_ca, ten_mau_ca, gio_bat_dau, gio_ket_thuc, qua_dem, mau_sac, mo_ta, trang_thai)
            VALUES
                (nextval('public.mau_ca_lam_id_seq'), 'CA_SANG', 'Ca Sáng (06:00 - 14:00)', '06:00:00', '14:00:00', FALSE, '#f59e0b', 'Ca sáng tiêu chuẩn mở cửa và phục vụ quầy', 'Active'),
                (nextval('public.mau_ca_lam_id_seq'), 'CA_CHIEU', 'Ca Chiều (14:00 - 22:00)', '14:00:00', '22:00:00', FALSE, '#3b82f6', 'Ca chiều tiêu chuẩn bán hàng và chốt két ca', 'Active'),
                (nextval('public.mau_ca_lam_id_seq'), 'CA_DEM', 'Ca Đêm (22:00 - 06:00)', '22:00:00', '06:00:00', TRUE, '#8b5cf6', 'Ca đêm trực quầy 24/7 và kiểm kho (+30% phụ cấp)', 'Active'),
                (nextval('public.mau_ca_lam_id_seq'), 'CA_PARTTIME_TRUA', 'Part-time Trưa (10:30 - 14:30)', '10:30:00', '14:30:00', FALSE, '#10b981', 'Ca tăng cường giờ cao điểm ăn trưa', 'Active'),
                (nextval('public.mau_ca_lam_id_seq'), 'CA_PARTTIME_TOI', 'Part-time Tối (17:30 - 21:30)', '17:30:00', '21:30:00', FALSE, '#ec4899', 'Ca tăng cường giờ cao điểm mua sắm buổi tối', 'Active')
            ON CONFLICT (ma_mau_ca) DO NOTHING;
            """;

        await using var command = dataSource.CreateCommand(ddlSql);
        command.CommandTimeout = 90;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<ShiftTemplateDto>> GetTemplatesAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, ma_mau_ca, ten_mau_ca, gio_bat_dau, gio_ket_thuc, qua_dem, mau_sac, mo_ta, trang_thai
            FROM public.mau_ca_lam
            WHERE (@activeOnly = FALSE OR trang_thai = 'Active')
            ORDER BY gio_bat_dau, id;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("activeOnly", activeOnly);

        var list = new List<ShiftTemplateDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var start = reader.GetTimeSpan(3);
            var end = reader.GetTimeSpan(4);
            var isOvernight = reader.GetBoolean(5);
            var duration = CalculateDurationHours(start, end, isOvernight);

            list.Add(new ShiftTemplateDto(
                Id: reader.GetInt64(0),
                Code: reader.GetString(1),
                Name: reader.GetString(2),
                StartTime: FormatTimeSpan(start),
                EndTime: FormatTimeSpan(end),
                IsOvernight: isOvernight,
                Color: reader.GetString(6),
                Description: reader.IsDBNull(7) ? null : reader.GetString(7),
                Status: reader.GetString(8),
                DurationHours: duration));
        }

        return list;
    }

    public Task<ShiftTemplateDto?> GetTemplateByIdAsync(long id, CancellationToken cancellationToken = default)
        => GetTemplateByIdCoreAsync(id, cancellationToken);

    private async Task<ShiftTemplateDto?> GetTemplateByIdCoreAsync(long id, CancellationToken cancellationToken, NpgsqlConnection? connection = null)
    {
        const string sql = """
            SELECT id, ma_mau_ca, ten_mau_ca, gio_bat_dau, gio_ket_thuc, qua_dem, mau_sac, mo_ta, trang_thai
            FROM public.mau_ca_lam
            WHERE id = @id
            LIMIT 1;
            """;

        await using var command = CreateCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var start = reader.GetTimeSpan(3);
            var end = reader.GetTimeSpan(4);
            var isOvernight = reader.GetBoolean(5);

            return new ShiftTemplateDto(
                Id: reader.GetInt64(0),
                Code: reader.GetString(1),
                Name: reader.GetString(2),
                StartTime: FormatTimeSpan(start),
                EndTime: FormatTimeSpan(end),
                IsOvernight: isOvernight,
                Color: reader.GetString(6),
                Description: reader.IsDBNull(7) ? null : reader.GetString(7),
                Status: reader.GetString(8),
                DurationHours: CalculateDurationHours(start, end, isOvernight));
        }

        return null;
    }

    public async Task<ShiftTemplateDto> CreateTemplateAsync(CreateShiftTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var start = ParseTimeSpan(request.StartTime);
        var end = ParseTimeSpan(request.EndTime);

        if (start == end || (request.IsOvernight ? end > start : end < start))
        {
            throw new ArgumentException("Giờ kết thúc phải lớn hơn giờ bắt đầu (hoặc bật cờ qua đêm nếu ca kết thúc vào ngày hôm sau).");
        }

        const string sql = """
            INSERT INTO public.mau_ca_lam (
                ma_mau_ca, ten_mau_ca, gio_bat_dau, gio_ket_thuc, qua_dem, mau_sac, mo_ta, trang_thai, created_at, updated_at
            )
            VALUES (
                @code, @name, @start, @end, @isOvernight, @color, @description, 'Active', NOW(), NOW()
            )
            RETURNING id, ma_mau_ca, ten_mau_ca, gio_bat_dau, gio_ket_thuc, qua_dem, mau_sac, mo_ta, trang_thai;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("code", request.Code.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("start", start);
        command.Parameters.AddWithValue("end", end);
        command.Parameters.AddWithValue("isOvernight", request.IsOvernight);
        command.Parameters.AddWithValue("color", string.IsNullOrWhiteSpace(request.Color) ? "#f97316" : request.Color.Trim());
        command.Parameters.AddWithValue("description", (object?)request.Description ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new ShiftTemplateDto(
                Id: reader.GetInt64(0),
                Code: reader.GetString(1),
                Name: reader.GetString(2),
                StartTime: FormatTimeSpan(reader.GetTimeSpan(3)),
                EndTime: FormatTimeSpan(reader.GetTimeSpan(4)),
                IsOvernight: reader.GetBoolean(5),
                Color: reader.GetString(6),
                Description: reader.IsDBNull(7) ? null : reader.GetString(7),
                Status: reader.GetString(8),
                DurationHours: CalculateDurationHours(reader.GetTimeSpan(3), reader.GetTimeSpan(4), reader.GetBoolean(5)));
        }

        throw new InvalidOperationException("Không thể tạo mẫu ca làm việc.");
    }

    public async Task<ShiftTemplateDto?> UpdateTemplateAsync(long id, UpdateShiftTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var start = ParseTimeSpan(request.StartTime);
        var end = ParseTimeSpan(request.EndTime);

        if (start == end || (request.IsOvernight ? end > start : end < start))
        {
            throw new ArgumentException("Giờ kết thúc phải lớn hơn giờ bắt đầu (hoặc bật cờ qua đêm nếu ca kết thúc vào ngày hôm sau).");
        }

        const string sql = """
            UPDATE public.mau_ca_lam
            SET ten_mau_ca = @name,
                gio_bat_dau = @start,
                gio_ket_thuc = @end,
                qua_dem = @isOvernight,
                mau_sac = @color,
                mo_ta = @description,
                trang_thai = @status,
                updated_at = NOW()
            WHERE id = @id
            RETURNING id, ma_mau_ca, ten_mau_ca, gio_bat_dau, gio_ket_thuc, qua_dem, mau_sac, mo_ta, trang_thai;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("start", start);
        command.Parameters.AddWithValue("end", end);
        command.Parameters.AddWithValue("isOvernight", request.IsOvernight);
        command.Parameters.AddWithValue("color", string.IsNullOrWhiteSpace(request.Color) ? "#f97316" : request.Color.Trim());
        command.Parameters.AddWithValue("description", (object?)request.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("status", string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new ShiftTemplateDto(
                Id: reader.GetInt64(0),
                Code: reader.GetString(1),
                Name: reader.GetString(2),
                StartTime: FormatTimeSpan(reader.GetTimeSpan(3)),
                EndTime: FormatTimeSpan(reader.GetTimeSpan(4)),
                IsOvernight: reader.GetBoolean(5),
                Color: reader.GetString(6),
                Description: reader.IsDBNull(7) ? null : reader.GetString(7),
                Status: reader.GetString(8),
                DurationHours: CalculateDurationHours(reader.GetTimeSpan(3), reader.GetTimeSpan(4), reader.GetBoolean(5)));
        }

        return null;
    }

    public async Task<bool> DeleteTemplateAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE public.mau_ca_lam
            SET trang_thai = 'Inactive', updated_at = NOW()
            WHERE id = @id;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<WeeklyScheduleResponse> GetScheduleAsync(
        DateOnly startDate,
        DateOnly endDate,
        long? departmentId = null,
        long? employeeId = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT dkc.id,
                   cl.id,
                   nv.id,
                   nv.ho_ten,
                   pb.ten_phong_ban,
                   cl.id_mau_ca,
                   cl.loai_ca,
                   cl.ngay_lam,
                   cl.gio_bat_dau,
                   cl.gio_ket_thuc,
                   cl.qua_dem,
                   COALESCE(mcl.mau_sac, '#f97316'),
                   dkc.trang_thai,
                   dkc.ghi_chu,
                   cc.id,
                   cc.gio_vao,
                   cc.gio_ra,
                   cc.so_gio_thuc_te,
                   cc.trang_thai
            FROM public.dang_ky_ca dkc
            JOIN public.ca_lam cl ON cl.id = dkc.id_ca_lam
            JOIN public.nhan_vien nv ON nv.id = dkc.id_nhan_vien
            LEFT JOIN public.phong_ban pb ON pb.id = nv.id_phong_ban
            LEFT JOIN public.mau_ca_lam mcl ON mcl.id = cl.id_mau_ca
            LEFT JOIN public.cham_cong cc ON cc.id_dang_ky_ca = dkc.id
            WHERE COALESCE(dkc.trang_thai, 'Approved') != 'Cancelled' AND cl.ngay_lam >= @startDate AND cl.ngay_lam <= @endDate
              AND (CAST(@departmentId AS bigint) IS NULL OR nv.id_phong_ban = @departmentId)
              AND (CAST(@employeeId AS bigint) IS NULL OR nv.id = @employeeId)
            ORDER BY cl.ngay_lam, cl.gio_bat_dau, nv.ho_ten;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("startDate", startDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("endDate", endDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("departmentId", (object?)departmentId ?? DBNull.Value);
        command.Parameters.AddWithValue("employeeId", (object?)employeeId ?? DBNull.Value);

        var assignments = new List<ShiftAssignmentDto>();
        var employeeSet = new HashSet<long>();
        var dayAssignmentCount = new Dictionary<DateOnly, int>();

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var assignmentId = reader.GetInt64(0);
                var shiftId = reader.GetInt64(1);
                var empId = reader.GetInt64(2);
                var empName = reader.GetString(3);
                var deptName = reader.IsDBNull(4) ? null : reader.GetString(4);
                long? templateId = reader.IsDBNull(5) ? null : reader.GetInt64(5);
                var shiftName = reader.GetString(6);
                var shiftDate = DateOnly.FromDateTime(reader.GetDateTime(7));
                var start = reader.GetTimeSpan(8);
                var end = reader.GetTimeSpan(9);
                var isOvernight = reader.GetBoolean(10);
                var color = reader.GetString(11);
                var status = reader.IsDBNull(12) ? "Approved" : reader.GetString(12);
                var note = reader.IsDBNull(13) ? null : reader.GetString(13);

                ShiftAttendanceBriefDto? attendance = null;
                if (!reader.IsDBNull(14))
                {
                    attendance = new ShiftAttendanceBriefDto(
                        AttendanceId: reader.GetInt64(14),
                        CheckInAt: reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15),
                        CheckOutAt: reader.IsDBNull(16) ? null : reader.GetFieldValue<DateTimeOffset>(16),
                        ActualHours: reader.IsDBNull(17) ? null : reader.GetDecimal(17),
                        Status: reader.IsDBNull(18) ? "Pending" : reader.GetString(18));
                }

                employeeSet.Add(empId);
                dayAssignmentCount[shiftDate] = dayAssignmentCount.GetValueOrDefault(shiftDate) + 1;

                assignments.Add(new ShiftAssignmentDto(
                    AssignmentId: assignmentId,
                    ShiftId: shiftId,
                    EmployeeId: empId,
                    EmployeeName: empName,
                    EmployeeCode: $"NV-{empId:D4}",
                    DepartmentName: deptName,
                    ShiftTemplateId: templateId,
                    ShiftName: shiftName,
                    ShiftDate: shiftDate.ToString("yyyy-MM-dd"),
                    StartTime: FormatTimeSpan(start),
                    EndTime: FormatTimeSpan(end),
                    IsOvernight: isOvernight,
                    Color: color,
                    Status: status,
                    Note: note,
                    Attendance: attendance));
            }
        }

        var warnings = new List<string>();
        for (var d = startDate; d <= endDate; d = d.AddDays(1))
        {
            if (!dayAssignmentCount.ContainsKey(d) || dayAssignmentCount[d] == 0)
            {
                warnings.Add($"Ngày {d:dd/MM/yyyy} ({GetVietnameseDayOfWeek(d)}): Chưa có bất kỳ nhân viên nào được phân ca.");
            }
        }

        return new WeeklyScheduleResponse(
            StartDate: startDate.ToString("yyyy-MM-dd"),
            EndDate: endDate.ToString("yyyy-MM-dd"),
            TotalShifts: assignments.Count,
            TotalEmployees: employeeSet.Count,
            Assignments: assignments,
            CoverageWarnings: warnings);
    }

    public Task<ShiftAssignmentDto?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default)
        => GetAssignmentByIdCoreAsync(assignmentId, cancellationToken);

    private async Task<ShiftAssignmentDto?> GetAssignmentByIdCoreAsync(long assignmentId, CancellationToken cancellationToken, NpgsqlConnection? connection = null)
    {
        const string sql = """
            SELECT dkc.id,
                   cl.id,
                   nv.id,
                   nv.ho_ten,
                   pb.ten_phong_ban,
                   cl.id_mau_ca,
                   cl.loai_ca,
                   cl.ngay_lam,
                   cl.gio_bat_dau,
                   cl.gio_ket_thuc,
                   cl.qua_dem,
                   COALESCE(mcl.mau_sac, '#f97316'),
                   dkc.trang_thai,
                   dkc.ghi_chu,
                   cc.id,
                   cc.gio_vao,
                   cc.gio_ra,
                   cc.so_gio_thuc_te,
                   cc.trang_thai
            FROM public.dang_ky_ca dkc
            JOIN public.ca_lam cl ON cl.id = dkc.id_ca_lam
            JOIN public.nhan_vien nv ON nv.id = dkc.id_nhan_vien
            LEFT JOIN public.phong_ban pb ON pb.id = nv.id_phong_ban
            LEFT JOIN public.mau_ca_lam mcl ON mcl.id = cl.id_mau_ca
            LEFT JOIN public.cham_cong cc ON cc.id_dang_ky_ca = dkc.id
            WHERE dkc.id = @id
            LIMIT 1;
            """;

        await using var command = CreateCommand(sql, connection);
        command.Parameters.AddWithValue("id", assignmentId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var empId = reader.GetInt64(2);
            var shiftDate = DateOnly.FromDateTime(reader.GetDateTime(7));
            var start = reader.GetTimeSpan(8);
            var end = reader.GetTimeSpan(9);
            var isOvernight = reader.GetBoolean(10);

            ShiftAttendanceBriefDto? attendance = null;
            if (!reader.IsDBNull(14))
            {
                attendance = new ShiftAttendanceBriefDto(
                    AttendanceId: reader.GetInt64(14),
                    CheckInAt: reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15),
                    CheckOutAt: reader.IsDBNull(16) ? null : reader.GetFieldValue<DateTimeOffset>(16),
                    ActualHours: reader.IsDBNull(17) ? null : reader.GetDecimal(17),
                    Status: reader.IsDBNull(18) ? "Pending" : reader.GetString(18));
            }

            return new ShiftAssignmentDto(
                AssignmentId: reader.GetInt64(0),
                ShiftId: reader.GetInt64(1),
                EmployeeId: empId,
                EmployeeName: reader.GetString(3),
                EmployeeCode: $"NV-{empId:D4}",
                DepartmentName: reader.IsDBNull(4) ? null : reader.GetString(4),
                ShiftTemplateId: reader.IsDBNull(5) ? null : reader.GetInt64(5),
                ShiftName: reader.GetString(6),
                ShiftDate: shiftDate.ToString("yyyy-MM-dd"),
                StartTime: FormatTimeSpan(start),
                EndTime: FormatTimeSpan(end),
                IsOvernight: isOvernight,
                Color: reader.GetString(11),
                Status: reader.IsDBNull(12) ? "Approved" : reader.GetString(12),
                Note: reader.IsDBNull(13) ? null : reader.GetString(13),
                Attendance: attendance);
        }

        return null;
    }

    public Task<ValidateShiftConflictResponse> ValidateConflictAsync(long employeeId, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime, bool isOvernight, long? excludeAssignmentId = null, CancellationToken cancellationToken = default)
        => ValidateConflictCoreAsync(employeeId, shiftDate, startTime, endTime, isOvernight, excludeAssignmentId, cancellationToken);

    private async Task<ValidateShiftConflictResponse> ValidateConflictCoreAsync(
        long employeeId,
        DateOnly shiftDate,
        TimeOnly startTime,
        TimeOnly endTime,
        bool isOvernight,
        long? excludeAssignmentId,
        CancellationToken cancellationToken, NpgsqlConnection? connection = null)
    {
        if (startTime == endTime || (isOvernight ? endTime > startTime : endTime < startTime))
        {
            return new ValidateShiftConflictResponse(
                HasConflict: true,
                ConflictType: "INVALID_TIME",
                Message: "Giờ kết thúc phải sau giờ bắt đầu (nếu ca qua đêm xin hãy bật cờ qua đêm).",
                Warnings: [],
                ConflictingShifts: []);
        }

        var prevDay = shiftDate.AddDays(-1);
        var nextDay = shiftDate.AddDays(1);

        const string sql = """
            SELECT dkc.id,
                   cl.loai_ca,
                   cl.ngay_lam,
                   cl.gio_bat_dau,
                   cl.gio_ket_thuc,
                   cl.qua_dem
            FROM public.dang_ky_ca dkc
            JOIN public.ca_lam cl ON cl.id = dkc.id_ca_lam
            WHERE dkc.id_nhan_vien = @employeeId
              AND COALESCE(dkc.trang_thai, 'Approved') != 'Cancelled'
              AND (CAST(@excludeId AS bigint) IS NULL OR dkc.id != @excludeId)
              AND cl.ngay_lam >= @prevDay AND cl.ngay_lam <= @nextDay
            ORDER BY cl.ngay_lam, cl.gio_bat_dau;
            """;

        await using var command = CreateCommand(sql, connection);
        command.Parameters.AddWithValue("employeeId", employeeId);
        command.Parameters.AddWithValue("excludeId", (object?)excludeAssignmentId ?? DBNull.Value);
        command.Parameters.AddWithValue("prevDay", prevDay.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("nextDay", nextDay.ToDateTime(TimeOnly.MinValue));

        var proposedStart = shiftDate.ToDateTime(startTime);
        var proposedEnd = isOvernight ? shiftDate.AddDays(1).ToDateTime(endTime) : shiftDate.ToDateTime(endTime);

        var conflictingShifts = new List<ConflictingShiftDto>();
        var warnings = new List<string>();

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var assignId = reader.GetInt64(0);
                var shiftName = reader.GetString(1);
                var existDate = DateOnly.FromDateTime(reader.GetDateTime(2));
                var existStartSpan = reader.GetTimeSpan(3);
                var existEndSpan = reader.GetTimeSpan(4);
                var existIsOvernight = reader.GetBoolean(5);

                var existStartTime = TimeOnly.FromTimeSpan(existStartSpan);
                var existEndTime = TimeOnly.FromTimeSpan(existEndSpan);

                var existStart = existDate.ToDateTime(existStartTime);
                var existEnd = existIsOvernight ? existDate.AddDays(1).ToDateTime(existEndTime) : existDate.ToDateTime(existEndTime);

                // Overlap condition: startA < endB && startB < endA
                if (proposedStart < existEnd && existStart < proposedEnd)
                {
                    conflictingShifts.Add(new ConflictingShiftDto(
                        AssignmentId: assignId,
                        ShiftName: shiftName,
                        ShiftDate: existDate.ToString("yyyy-MM-dd"),
                        StartTime: FormatTimeSpan(existStartSpan),
                        EndTime: FormatTimeSpan(existEndSpan)));
                }
                else
                {
                    // Rest period check: if gap < 8 hours
                    double gapHours = -1;
                    if (proposedStart >= existEnd)
                    {
                        gapHours = (proposedStart - existEnd).TotalHours;
                    }
                    else if (existStart >= proposedEnd)
                    {
                        gapHours = (existStart - proposedEnd).TotalHours;
                    }

                    if (gapHours is >= 0 and < 8.0)
                    {
                        warnings.Add($"Khoảng cách nghỉ giữa ca [{shiftName} ngày {existDate:dd/MM}] và ca mới chỉ là {gapHours:F1} giờ (khuyến nghị tối thiểu 8 giờ để đảm bảo sức khỏe nhân viên).");
                    }
                }
            }
        }

        if (conflictingShifts.Count > 0)
        {
            var first = conflictingShifts[0];
            return new ValidateShiftConflictResponse(
                HasConflict: true,
                ConflictType: "OVERLAP",
                Message: $"Xung đột giờ làm: Nhân viên đã được phân ca [{first.ShiftName} ({first.StartTime} - {first.EndTime})] vào ngày {first.ShiftDate}.",
                Warnings: warnings,
                ConflictingShifts: conflictingShifts);
        }

        return new ValidateShiftConflictResponse(
            HasConflict: false,
            ConflictType: "NONE",
            Message: "Khung giờ phân ca hợp lệ, không có xung đột trùng giờ.",
            Warnings: warnings,
            ConflictingShifts: []);
    }

    private static TimeSpan ParseTimeSpan(string timeString) => ShiftInput.Time(timeString).ToTimeSpan();

    private static string FormatTimeSpan(TimeSpan ts) => ts.ToString(@"hh\:mm");

    private static double CalculateDurationHours(TimeSpan start, TimeSpan end, bool isOvernight)
    {
        var diff = end - start;
        if (isOvernight || diff <= TimeSpan.Zero)
        {
            diff += TimeSpan.FromDays(1);
        }
        return Math.Round(diff.TotalHours, 2);
    }

    private static string GetVietnameseDayOfWeek(DateOnly date)
    {
        return date.DayOfWeek switch
        {
            DayOfWeek.Monday => "Thứ Hai",
            DayOfWeek.Tuesday => "Thứ Ba",
            DayOfWeek.Wednesday => "Thứ Tư",
            DayOfWeek.Thursday => "Thứ Năm",
            DayOfWeek.Friday => "Thứ Sáu",
            DayOfWeek.Saturday => "Thứ Bảy",
            DayOfWeek.Sunday => "Chủ Nhật",
            _ => string.Empty
        };
    }
}
