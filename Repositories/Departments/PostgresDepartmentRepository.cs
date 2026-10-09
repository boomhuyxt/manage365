using Npgsql;

namespace manage365.Repositories.Departments;

public sealed class PostgresDepartmentRepository(NpgsqlDataSource dataSource) : IDepartmentRepository
{
    public async Task EnsureTablesAndSeedAsync(CancellationToken cancellationToken = default)
    {
        const string ddlSql = """
            CREATE SEQUENCE IF NOT EXISTS public.phong_ban_id_seq;

            CREATE TABLE IF NOT EXISTS public.phong_ban (
                id BIGINT PRIMARY KEY DEFAULT nextval('public.phong_ban_id_seq'),
                ma_phong_ban VARCHAR(50) NOT NULL UNIQUE,
                ten_phong_ban VARCHAR(150) NOT NULL,
                mo_ta TEXT NULL,
                trang_thai VARCHAR(50) NOT NULL DEFAULT 'Active',
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE SEQUENCE IF NOT EXISTS public.cong_cu_phong_ban_id_seq;

            CREATE TABLE IF NOT EXISTS public.cong_cu_phong_ban (
                id BIGINT PRIMARY KEY DEFAULT nextval('public.cong_cu_phong_ban_id_seq'),
                id_phong_ban BIGINT NOT NULL REFERENCES public.phong_ban(id) ON DELETE CASCADE,
                ma_cong_cu VARCHAR(100) NOT NULL UNIQUE,
                ten_cong_cu VARCHAR(200) NOT NULL,
                icon VARCHAR(100) NULL,
                route_path VARCHAR(200) NULL,
                thu_tu_hien_thi INT NOT NULL DEFAULT 0,
                trang_thai VARCHAR(50) NOT NULL DEFAULT 'Active',
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'nhan_vien'
                      AND column_name = 'id_phong_ban'
                ) THEN
                    ALTER TABLE public.nhan_vien
                        ADD COLUMN id_phong_ban BIGINT NULL REFERENCES public.phong_ban(id) ON DELETE SET NULL;
                END IF;
            END $$;

            CREATE INDEX IF NOT EXISTS ix_cong_cu_phong_ban ON public.cong_cu_phong_ban(id_phong_ban);
            CREATE INDEX IF NOT EXISTS ix_nhan_vien_phong_ban ON public.nhan_vien(id_phong_ban);

            INSERT INTO public.phong_ban (id, ma_phong_ban, ten_phong_ban, mo_ta, trang_thai, created_at, updated_at)
            VALUES
                (nextval('public.phong_ban_id_seq'), 'WAREHOUSE', 'Bộ phận Kho vận', 'Quản lý kho hàng, xuất nhập và kiểm kê tồn kho', 'Active', NOW(), NOW()),
                (nextval('public.phong_ban_id_seq'), 'POS_SALES', 'Bộ phận Bán hàng / Thu ngân', 'Bán hàng quầy POS, in hóa đơn và thanh toán', 'Active', NOW(), NOW()),
                (nextval('public.phong_ban_id_seq'), 'HR_ADMIN', 'Bộ phận Nhân sự & Quản trị', 'Xếp ca làm việc, chấm công và tính lương', 'Active', NOW(), NOW())
            ON CONFLICT (ma_phong_ban) DO NOTHING;

            INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
            SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_STOCK_IN', 'Nhập kho hàng hóa', 'move_to_inbox', '/warehouse/stock-in', 1, 'Active', NOW(), NOW()
            FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'WAREHOUSE'
            ON CONFLICT (ma_cong_cu) DO NOTHING;

            INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
            SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_STOCK_OUT', 'Xuất kho & Điều chuyển', 'outbox', '/warehouse/stock-out', 2, 'Active', NOW(), NOW()
            FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'WAREHOUSE'
            ON CONFLICT (ma_cong_cu) DO NOTHING;

            INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
            SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_INVENTORY_AUDIT', 'Kiểm kê & Soát tồn', 'fact_check', '/warehouse/inventory-audit', 3, 'Active', NOW(), NOW()
            FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'WAREHOUSE'
            ON CONFLICT (ma_cong_cu) DO NOTHING;

            INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
            SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_BARCODE_SCAN', 'Quét mã vạch kiểm tra', 'qr_code_scanner', '/warehouse/barcode-scan', 4, 'Active', NOW(), NOW()
            FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'WAREHOUSE'
            ON CONFLICT (ma_cong_cu) DO NOTHING;

            INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
            SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_POS_CHECKOUT', 'Quầy bán hàng POS', 'point_of_sale', '/pos/checkout', 1, 'Active', NOW(), NOW()
            FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'POS_SALES'
            ON CONFLICT (ma_cong_cu) DO NOTHING;

            INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
            SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_INVOICE_HISTORY', 'Lịch sử & In lại hóa đơn', 'receipt_long', '/pos/invoices', 2, 'Active', NOW(), NOW()
            FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'POS_SALES'
            ON CONFLICT (ma_cong_cu) DO NOTHING;

            INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
            SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_PROMOTION_LOOKUP', 'Tra cứu khuyến mãi', 'loyalty', '/pos/promotions', 3, 'Active', NOW(), NOW()
            FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'POS_SALES'
            ON CONFLICT (ma_cong_cu) DO NOTHING;

            INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
            SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_SHIFT_CASH_CLOSING', 'Chốt ca két tiền', 'payments', '/pos/cash-closing', 4, 'Active', NOW(), NOW()
            FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'POS_SALES'
            ON CONFLICT (ma_cong_cu) DO NOTHING;

            INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
            SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_ATTENDANCE_KIOSK', 'Quản lý chấm công & QR', 'access_time', '/attendance/manage', 1, 'Active', NOW(), NOW()
            FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'HR_ADMIN'
            ON CONFLICT (ma_cong_cu) DO NOTHING;

            INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
            SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_SHIFT_SCHEDULING', 'Xếp lịch phân ca', 'calendar_month', '/schedule/manage', 2, 'Active', NOW(), NOW()
            FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'HR_ADMIN'
            ON CONFLICT (ma_cong_cu) DO NOTHING;

            INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
            SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_EMPLOYEE_LIST', 'Hồ sơ nhân viên', 'badge', '/employees/list', 3, 'Active', NOW(), NOW()
            FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'HR_ADMIN'
            ON CONFLICT (ma_cong_cu) DO NOTHING;

            INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
            SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_PAYROLL_REPORT', 'Báo cáo lương', 'attach_money', '/payroll/summary', 4, 'Active', NOW(), NOW()
            FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'HR_ADMIN'
            ON CONFLICT (ma_cong_cu) DO NOTHING;

            UPDATE public.nhan_vien nv
            SET id_phong_ban = (SELECT id FROM public.phong_ban WHERE ma_phong_ban = 'POS_SALES' LIMIT 1)
            WHERE nv.id_phong_ban IS NULL;
            """;

        await using var command = dataSource.CreateCommand(ddlSql);
        command.CommandTimeout = 90;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DepartmentItem>> GetAllDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT pb.id, pb.ma_phong_ban, pb.ten_phong_ban, pb.mo_ta, pb.trang_thai,
                   COUNT(DISTINCT nv.id)::int AS employee_count,
                   COUNT(DISTINCT cc.id)::int AS tool_count,
                   pb.created_at, pb.updated_at
            FROM public.phong_ban pb
            LEFT JOIN public.nhan_vien nv ON nv.id_phong_ban = pb.id
            LEFT JOIN public.cong_cu_phong_ban cc ON cc.id_phong_ban = pb.id
            GROUP BY pb.id, pb.ma_phong_ban, pb.ten_phong_ban, pb.mo_ta, pb.trang_thai, pb.created_at, pb.updated_at
            ORDER BY pb.id ASC;
            """;

        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var list = new List<DepartmentItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadDepartment(reader));
        }

        return list;
    }

    public async Task<DepartmentDetailItem?> GetDepartmentByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT pb.id, pb.ma_phong_ban, pb.ten_phong_ban, pb.mo_ta, pb.trang_thai,
                   (SELECT COUNT(*)::int FROM public.nhan_vien WHERE id_phong_ban = pb.id) AS employee_count,
                   pb.created_at, pb.updated_at
            FROM public.phong_ban pb
            WHERE pb.id = @id
            LIMIT 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken)) return null;

        var deptId = reader.GetInt64(0);
        var code = reader.GetString(1);
        var name = reader.GetString(2);
        var desc = reader.IsDBNull(3) ? null : reader.GetString(3);
        var status = reader.GetString(4);
        var empCount = reader.GetInt32(5);
        var createdAt = reader.GetFieldValue<DateTimeOffset>(6);
        var updatedAt = reader.GetFieldValue<DateTimeOffset>(7);
        await reader.CloseAsync();

        var tools = await GetDepartmentToolsAsync(deptId, cancellationToken);
        return new DepartmentDetailItem(deptId, code, name, desc, status, empCount, tools, createdAt, updatedAt);
    }

    public async Task<DepartmentItem?> GetDepartmentByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT pb.id, pb.ma_phong_ban, pb.ten_phong_ban, pb.mo_ta, pb.trang_thai,
                   (SELECT COUNT(*)::int FROM public.nhan_vien WHERE id_phong_ban = pb.id) AS employee_count,
                   (SELECT COUNT(*)::int FROM public.cong_cu_phong_ban WHERE id_phong_ban = pb.id) AS tool_count,
                   pb.created_at, pb.updated_at
            FROM public.phong_ban pb
            WHERE LOWER(pb.ma_phong_ban) = LOWER(@code)
            LIMIT 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("code", code.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadDepartment(reader) : null;
    }

    public async Task<DepartmentItem?> CreateDepartmentAsync(string code, string name, string? description, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO public.phong_ban (id, ma_phong_ban, ten_phong_ban, mo_ta, trang_thai, created_at, updated_at)
            VALUES (nextval('public.phong_ban_id_seq'), @code, @name, @description, 'Active', NOW(), NOW())
            RETURNING id, ma_phong_ban, ten_phong_ban, mo_ta, trang_thai, 0, 0, created_at, updated_at;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("code", code.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.AddWithValue("description", (object?)description?.Trim() ?? DBNull.Value);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadDepartment(reader) : null;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }
    }

    public async Task<DepartmentItem?> UpdateDepartmentAsync(long id, string name, string? description, string? status, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE public.phong_ban
            SET ten_phong_ban = @name,
                mo_ta = @description,
                trang_thai = COALESCE(@status, trang_thai),
                updated_at = NOW()
            WHERE id = @id
            RETURNING id, ma_phong_ban, ten_phong_ban, mo_ta, trang_thai,
                      (SELECT COUNT(*)::int FROM public.nhan_vien WHERE id_phong_ban = @id),
                      (SELECT COUNT(*)::int FROM public.cong_cu_phong_ban WHERE id_phong_ban = @id),
                      created_at, updated_at;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.AddWithValue("description", (object?)description?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("status", (object?)status?.Trim() ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDepartment(reader) : null;
    }

    public async Task<bool> DeleteDepartmentAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM public.phong_ban WHERE id = @id;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<IReadOnlyList<DepartmentToolItem>> GetDepartmentToolsAsync(long departmentId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at
            FROM public.cong_cu_phong_ban
            WHERE id_phong_ban = @departmentId
            ORDER BY thu_tu_hien_thi ASC, id ASC;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("departmentId", departmentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var list = new List<DepartmentToolItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadTool(reader));
        }

        return list;
    }

    public async Task<DepartmentToolItem?> GetToolByIdAsync(long toolId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at
            FROM public.cong_cu_phong_ban
            WHERE id = @toolId
            LIMIT 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("toolId", toolId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadTool(reader) : null;
    }

    public async Task<DepartmentToolItem?> GetToolByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at
            FROM public.cong_cu_phong_ban
            WHERE LOWER(ma_cong_cu) = LOWER(@code)
            LIMIT 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("code", code.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadTool(reader) : null;
    }

    public async Task<DepartmentToolItem?> CreateToolAsync(
        long departmentId,
        string code,
        string name,
        string? icon,
        string? routePath,
        int displayOrder,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
            VALUES (nextval('public.cong_cu_phong_ban_id_seq'), @departmentId, @code, @name, @icon, @routePath, @displayOrder, 'Active', NOW(), NOW())
            RETURNING id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("departmentId", departmentId);
        command.Parameters.AddWithValue("code", code.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.AddWithValue("icon", (object?)icon?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("routePath", (object?)routePath?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("displayOrder", displayOrder);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadTool(reader) : null;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }
    }

    public async Task<DepartmentToolItem?> UpdateToolAsync(
        long toolId,
        string name,
        string? icon,
        string? routePath,
        int displayOrder,
        string? status,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE public.cong_cu_phong_ban
            SET ten_cong_cu = @name,
                icon = @icon,
                route_path = @routePath,
                thu_tu_hien_thi = @displayOrder,
                trang_thai = COALESCE(@status, trang_thai),
                updated_at = NOW()
            WHERE id = @toolId
            RETURNING id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("toolId", toolId);
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.AddWithValue("icon", (object?)icon?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("routePath", (object?)routePath?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("displayOrder", displayOrder);
        command.Parameters.AddWithValue("status", (object?)status?.Trim() ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTool(reader) : null;
    }

    public async Task<bool> DeleteToolAsync(long toolId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM public.cong_cu_phong_ban WHERE id = @toolId;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("toolId", toolId);
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<bool> AssignEmployeeToDepartmentAsync(long employeeId, long? departmentId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE public.nhan_vien
            SET id_phong_ban = @departmentId,
                updated_at = NOW()
            WHERE id = @employeeId;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("employeeId", employeeId);
        command.Parameters.AddWithValue("departmentId", (object?)departmentId ?? DBNull.Value);
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<UserToolsItem?> GetToolsByUserIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        const string userSql = """
            SELECT nv.id, nv.ho_ten, pb.id, pb.ten_phong_ban
            FROM public.nhan_vien nv
            LEFT JOIN public.phong_ban pb ON pb.id = nv.id_phong_ban
            WHERE nv.id = @userId
            LIMIT 1;
            """;

        await using var userCmd = dataSource.CreateCommand(userSql);
        userCmd.Parameters.AddWithValue("userId", userId);
        await using var userReader = await userCmd.ExecuteReaderAsync(cancellationToken);

        if (!await userReader.ReadAsync(cancellationToken)) return null;

        var id = userReader.GetInt64(0);
        var displayName = userReader.GetString(1);
        long? deptId = userReader.IsDBNull(2) ? null : userReader.GetInt64(2);
        string? deptName = userReader.IsDBNull(3) ? null : userReader.GetString(3);
        await userReader.CloseAsync();

        // Lấy danh sách công cụ active
        var toolsSql = deptId.HasValue
            ? """
              SELECT id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at
              FROM public.cong_cu_phong_ban
              WHERE id_phong_ban = @deptId AND trang_thai = 'Active'
              ORDER BY thu_tu_hien_thi ASC, id ASC;
              """
            : """
              SELECT id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at
              FROM public.cong_cu_phong_ban
              WHERE trang_thai = 'Active'
              ORDER BY thu_tu_hien_thi ASC, id ASC;
              """;

        await using var toolsCmd = dataSource.CreateCommand(toolsSql);
        if (deptId.HasValue)
        {
            toolsCmd.Parameters.AddWithValue("deptId", deptId.Value);
        }
        await using var toolsReader = await toolsCmd.ExecuteReaderAsync(cancellationToken);

        var tools = new List<DepartmentToolItem>();
        while (await toolsReader.ReadAsync(cancellationToken))
        {
            tools.Add(ReadTool(toolsReader));
        }

        return new UserToolsItem(id, displayName, deptId, deptName, tools);
    }

    private static DepartmentItem ReadDepartment(NpgsqlDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetString(4),
        reader.GetInt32(5),
        reader.GetInt32(6),
        reader.GetFieldValue<DateTimeOffset>(7),
        reader.GetFieldValue<DateTimeOffset>(8));

    private static DepartmentToolItem ReadTool(NpgsqlDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetInt64(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.GetInt32(6),
        reader.GetString(7),
        reader.GetFieldValue<DateTimeOffset>(8),
        reader.GetFieldValue<DateTimeOffset>(9));
}
