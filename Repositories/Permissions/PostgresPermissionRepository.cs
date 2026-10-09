using Npgsql;

namespace manage365.Repositories.Permissions;

public sealed class PostgresPermissionRepository(NpgsqlDataSource dataSource) : IPermissionRepository
{
    public async Task EnsureTablesAndSeedAsync(CancellationToken cancellationToken = default)
    {
        const string ddlSql = """
            CREATE SEQUENCE IF NOT EXISTS public.quyen_id_seq;

            CREATE TABLE IF NOT EXISTS public.quyen (
                id BIGINT PRIMARY KEY DEFAULT nextval('public.quyen_id_seq'),
                ma_quyen VARCHAR(100) NOT NULL UNIQUE,
                ten_quyen VARCHAR(200) NOT NULL,
                nhom_quyen VARCHAR(100) NOT NULL DEFAULT 'System',
                mo_ta TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS public.vai_tro_quyen (
                id_vai_tro BIGINT NOT NULL REFERENCES public.vai_tro(id) ON DELETE CASCADE,
                id_quyen BIGINT NOT NULL REFERENCES public.quyen(id) ON DELETE CASCADE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                PRIMARY KEY (id_vai_tro, id_quyen)
            );

            CREATE INDEX IF NOT EXISTS ix_quyen_nhom ON public.quyen(nhom_quyen);
            CREATE INDEX IF NOT EXISTS ix_vai_tro_quyen_vai_tro ON public.vai_tro_quyen(id_vai_tro);

            INSERT INTO public.quyen (id, ma_quyen, ten_quyen, nhom_quyen, mo_ta, created_at, updated_at)
            VALUES
                (nextval('public.quyen_id_seq'), 'USER_VIEW', 'Xem danh sách và chi tiết nhân viên', 'Users', 'Quyền xem thông tin hồ sơ nhân viên', NOW(), NOW()),
                (nextval('public.quyen_id_seq'), 'USER_MANAGE', 'Quản lý nhân viên (Thêm/Sửa/Xóa/Khóa)', 'Users', 'Toàn quyền thêm, chỉnh sửa, vô hiệu hóa tài khoản nhân sự', NOW(), NOW()),
                (nextval('public.quyen_id_seq'), 'ROLE_VIEW', 'Xem danh sách vai trò', 'Roles', 'Xem danh sách các vai trò hệ thống', NOW(), NOW()),
                (nextval('public.quyen_id_seq'), 'ROLE_MANAGE', 'Quản lý vai trò & cấp quyền', 'Roles', 'Thêm sửa xóa vai trò và gán quyền cho vai trò', NOW(), NOW()),
                (nextval('public.quyen_id_seq'), 'ATTENDANCE_CHECKIN', 'Chấm công ca làm việc', 'Attendance', 'Chấm công vào/ra ca bằng mã QR và GPS', NOW(), NOW()),
                (nextval('public.quyen_id_seq'), 'ATTENDANCE_VIEW', 'Xem lịch sử chấm công', 'Attendance', 'Xem nhật ký vào ra của bản thân hoặc cửa hàng', NOW(), NOW()),
                (nextval('public.quyen_id_seq'), 'ATTENDANCE_MANAGE', 'Quản trị ca & địa điểm chấm công', 'Attendance', 'Cấu hình tọa độ cửa hàng, duyệt ca chấm công', NOW(), NOW()),
                (nextval('public.quyen_id_seq'), 'REPORT_VIEW', 'Xem báo cáo & bảng lương', 'Reports', 'Xem doanh số, thống kê chấm công và lương', NOW(), NOW()),
                (nextval('public.quyen_id_seq'), 'SYSTEM_CONFIG', 'Cấu hình hệ thống', 'System', 'Quyền quản trị cấp cao hệ thống', NOW(), NOW())
            ON CONFLICT (ma_quyen) DO NOTHING;

            INSERT INTO public.vai_tro_quyen (id_vai_tro, id_quyen, created_at)
            SELECT vt.id, q.id, NOW()
            FROM public.vai_tro vt
            CROSS JOIN public.quyen q
            WHERE LOWER(vt.ten_vai_tro) = 'admin'
            ON CONFLICT (id_vai_tro, id_quyen) DO NOTHING;

            INSERT INTO public.vai_tro_quyen (id_vai_tro, id_quyen, created_at)
            SELECT vt.id, q.id, NOW()
            FROM public.vai_tro vt
            JOIN public.quyen q ON q.ma_quyen IN ('USER_VIEW', 'ROLE_VIEW', 'ATTENDANCE_CHECKIN', 'ATTENDANCE_VIEW', 'ATTENDANCE_MANAGE', 'REPORT_VIEW')
            WHERE LOWER(vt.ten_vai_tro) = 'manager'
            ON CONFLICT (id_vai_tro, id_quyen) DO NOTHING;

            INSERT INTO public.vai_tro_quyen (id_vai_tro, id_quyen, created_at)
            SELECT vt.id, q.id, NOW()
            FROM public.vai_tro vt
            JOIN public.quyen q ON q.ma_quyen IN ('ATTENDANCE_CHECKIN', 'ATTENDANCE_VIEW')
            WHERE LOWER(vt.ten_vai_tro) = 'employee'
            ON CONFLICT (id_vai_tro, id_quyen) DO NOTHING;
            """;

        await using var command = dataSource.CreateCommand(ddlSql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PermissionItem>> GetAllPermissionsAsync(
        string? group = null,
        CancellationToken cancellationToken = default)
    {
        var sql = string.IsNullOrWhiteSpace(group)
            ? """
              SELECT id, ma_quyen, ten_quyen, nhom_quyen, mo_ta, created_at, updated_at
              FROM public.quyen
              ORDER BY nhom_quyen ASC, id ASC;
              """
            : """
              SELECT id, ma_quyen, ten_quyen, nhom_quyen, mo_ta, created_at, updated_at
              FROM public.quyen
              WHERE LOWER(nhom_quyen) = LOWER(@group)
              ORDER BY nhom_quyen ASC, id ASC;
              """;

        await using var command = dataSource.CreateCommand(sql);
        if (!string.IsNullOrWhiteSpace(group))
        {
            command.Parameters.AddWithValue("group", group.Trim());
        }
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var list = new List<PermissionItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadPermission(reader));
        }

        return list;
    }

    public async Task<PermissionItem?> GetPermissionByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, ma_quyen, ten_quyen, nhom_quyen, mo_ta, created_at, updated_at
            FROM public.quyen
            WHERE id = @id
            LIMIT 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadPermission(reader) : null;
    }

    public async Task<PermissionItem?> GetPermissionByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, ma_quyen, ten_quyen, nhom_quyen, mo_ta, created_at, updated_at
            FROM public.quyen
            WHERE LOWER(ma_quyen) = LOWER(@code)
            LIMIT 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("code", code.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadPermission(reader) : null;
    }

    public async Task<PermissionItem?> CreatePermissionAsync(
        string code,
        string name,
        string group,
        string? description,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO public.quyen (id, ma_quyen, ten_quyen, nhom_quyen, mo_ta, created_at, updated_at)
            VALUES (nextval('public.quyen_id_seq'), @code, @name, @group, @description, NOW(), NOW())
            RETURNING id, ma_quyen, ten_quyen, nhom_quyen, mo_ta, created_at, updated_at;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("code", code.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.AddWithValue("group", group.Trim());
        command.Parameters.AddWithValue("description", (object?)description?.Trim() ?? DBNull.Value);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadPermission(reader) : null;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }
    }

    public async Task<PermissionItem?> UpdatePermissionAsync(
        long id,
        string name,
        string group,
        string? description,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE public.quyen
            SET ten_quyen = @name,
                nhom_quyen = @group,
                mo_ta = @description,
                updated_at = NOW()
            WHERE id = @id
            RETURNING id, ma_quyen, ten_quyen, nhom_quyen, mo_ta, created_at, updated_at;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.AddWithValue("group", group.Trim());
        command.Parameters.AddWithValue("description", (object?)description?.Trim() ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPermission(reader) : null;
    }

    public async Task<bool> DeletePermissionAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM public.quyen WHERE id = @id;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<IReadOnlyList<PermissionItem>> GetPermissionsByRoleIdAsync(
        long roleId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT q.id, q.ma_quyen, q.ten_quyen, q.nhom_quyen, q.mo_ta, q.created_at, q.updated_at
            FROM public.quyen q
            JOIN public.vai_tro_quyen vtq ON vtq.id_quyen = q.id
            WHERE vtq.id_vai_tro = @roleId
            ORDER BY q.nhom_quyen ASC, q.id ASC;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("roleId", roleId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var list = new List<PermissionItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadPermission(reader));
        }

        return list;
    }

    public async Task<IReadOnlyList<PermissionItem>> GetPermissionsByUserIdAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT DISTINCT q.id, q.ma_quyen, q.ten_quyen, q.nhom_quyen, q.mo_ta, q.created_at, q.updated_at
            FROM public.nhan_vien nv
            JOIN public.vai_tro_quyen vtq ON vtq.id_vai_tro = nv.id_vai_tro
            JOIN public.quyen q ON q.id = vtq.id_quyen
            WHERE nv.id = @userId
            ORDER BY q.nhom_quyen ASC, q.id ASC;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var list = new List<PermissionItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadPermission(reader));
        }

        return list;
    }

    public async Task<bool> AssignPermissionsToRoleAsync(
        long roleId,
        IEnumerable<long> permissionIds,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            // Xóa phân quyền cũ của vai trò
            const string deleteSql = "DELETE FROM public.vai_tro_quyen WHERE id_vai_tro = @roleId;";
            await using var delCmd = new NpgsqlCommand(deleteSql, conn, tx);
            delCmd.Parameters.AddWithValue("roleId", roleId);
            await delCmd.ExecuteNonQueryAsync(cancellationToken);

            // Gán danh sách quyền mới
            const string insertSql = """
                INSERT INTO public.vai_tro_quyen (id_vai_tro, id_quyen, created_at)
                VALUES (@roleId, @permId, NOW())
                ON CONFLICT (id_vai_tro, id_quyen) DO NOTHING;
                """;

            foreach (var permId in permissionIds)
            {
                await using var insCmd = new NpgsqlCommand(insertSql, conn, tx);
                insCmd.Parameters.AddWithValue("roleId", roleId);
                insCmd.Parameters.AddWithValue("permId", permId);
                await insCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static PermissionItem ReadPermission(NpgsqlDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetFieldValue<DateTimeOffset>(5),
        reader.GetFieldValue<DateTimeOffset>(6));
}
