using System.Text;
using Npgsql;

namespace manage365.Repositories.Users;

public sealed class PostgresUserManagementRepository(NpgsqlDataSource dataSource) : IUserManagementRepository
{
    public async Task<PagedUserResult<UserManagementItem>> GetUsersAsync(
        UserFilterParams filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        var whereClauses = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var searchPattern = $"%{filter.Search.Trim().ToLowerInvariant()}%";
            whereClauses.Add("(LOWER(nv.ho_ten) LIKE @search OR LOWER(nv.email) LIKE @search)");
            parameters.Add(new NpgsqlParameter("search", searchPattern));
        }

        if (!string.IsNullOrWhiteSpace(filter.Role))
        {
            whereClauses.Add("LOWER(COALESCE(vt.ten_vai_tro, 'Employee')) = LOWER(@role)");
            parameters.Add(new NpgsqlParameter("role", filter.Role.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(filter.EmployeeType))
        {
            whereClauses.Add("LOWER(nv.loai_nhan_vien) = LOWER(@employeeType)");
            parameters.Add(new NpgsqlParameter("employeeType", filter.EmployeeType.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            whereClauses.Add("LOWER(COALESCE(nv.trang_thai, 'Active')) = LOWER(@status)");
            parameters.Add(new NpgsqlParameter("status", filter.Status.Trim()));
        }

        var whereSql = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : string.Empty;

        var countSql = $"""
            SELECT COUNT(*)
            FROM public.nhan_vien AS nv
            LEFT JOIN public.vai_tro AS vt ON vt.id = nv.id_vai_tro
            {whereSql};
            """;

        await using var countCommand = dataSource.CreateCommand(countSql);
        foreach (var param in parameters)
        {
            countCommand.Parameters.Add(new NpgsqlParameter(param.ParameterName, param.Value));
        }

        var totalCountObj = await countCommand.ExecuteScalarAsync(cancellationToken);
        var totalCount = Convert.ToInt32(totalCountObj);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var selectSql = $"""
            SELECT nv.id, nv.email, nv.ho_ten, COALESCE(vt.ten_vai_tro, 'Employee'),
                   nv.loai_nhan_vien, nv.luong_co_ban_gio, COALESCE(nv.trang_thai, 'Active'),
                   nv.created_at, nv.updated_at
            FROM public.nhan_vien AS nv
            LEFT JOIN public.vai_tro AS vt ON vt.id = nv.id_vai_tro
            {whereSql}
            ORDER BY nv.id DESC
            LIMIT @limit OFFSET @offset;
            """;

        await using var selectCommand = dataSource.CreateCommand(selectSql);
        foreach (var param in parameters)
        {
            selectCommand.Parameters.Add(new NpgsqlParameter(param.ParameterName, param.Value));
        }
        selectCommand.Parameters.AddWithValue("limit", pageSize);
        selectCommand.Parameters.AddWithValue("offset", offset);

        await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
        var items = new List<UserManagementItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new UserManagementItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDecimal(5),
                reader.IsDBNull(6) ? "Active" : reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetFieldValue<DateTimeOffset>(8)));
        }

        return new PagedUserResult<UserManagementItem>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<UserManagementDetail?> GetUserByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT nv.id, nv.email, nv.ho_ten, COALESCE(vt.ten_vai_tro, 'Employee'),
                   nv.loai_nhan_vien, nv.luong_co_ban_gio, nv.ma_so_thue, nv.so_nguoi_phu_thuoc,
                   COALESCE(nv.trang_thai, 'Active'), nv.diem_danh_gia, nv.created_at, nv.updated_at
            FROM public.nhan_vien AS nv
            LEFT JOIN public.vai_tro AS vt ON vt.id = nv.id_vai_tro
            WHERE nv.id = @id
            LIMIT 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadUserDetail(reader) : null;
    }

    public async Task<UserManagementDetail?> GetUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT nv.id, nv.email, nv.ho_ten, COALESCE(vt.ten_vai_tro, 'Employee'),
                   nv.loai_nhan_vien, nv.luong_co_ban_gio, nv.ma_so_thue, nv.so_nguoi_phu_thuoc,
                   COALESCE(nv.trang_thai, 'Active'), nv.diem_danh_gia, nv.created_at, nv.updated_at
            FROM public.nhan_vien AS nv
            LEFT JOIN public.vai_tro AS vt ON vt.id = nv.id_vai_tro
            WHERE LOWER(nv.email) = LOWER(@email)
            LIMIT 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("email", normalizedEmail);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadUserDetail(reader) : null;
    }

    public async Task<UserManagementDetail?> CreateUserAsync(CreateUserData data, CancellationToken cancellationToken = default)
    {
        const string sql = """
            WITH target_role AS (
                SELECT id FROM public.vai_tro WHERE LOWER(ten_vai_tro) = LOWER(@role)
                UNION ALL
                SELECT id FROM public.vai_tro WHERE ten_vai_tro = 'Employee'
                LIMIT 1
            )
            INSERT INTO public.nhan_vien
                (id, id_vai_tro, ho_ten, email, mat_khau, loai_nhan_vien, luong_co_ban_gio,
                 ma_so_thue, so_nguoi_phu_thuoc, trang_thai, created_at, updated_at)
            SELECT nextval('public.nhan_vien_id_seq'), tr.id, @displayName, @email, @passwordHash,
                   @employeeType, @hourlySalary, @taxCode, @dependentsCount, @status, NOW(), NOW()
            FROM target_role tr
            RETURNING id;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("role", data.Role.Trim());
        command.Parameters.AddWithValue("displayName", data.DisplayName.Trim());
        command.Parameters.AddWithValue("email", data.Email.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("passwordHash", data.PasswordHash);
        command.Parameters.AddWithValue("employeeType", data.EmployeeType.Trim());
        command.Parameters.AddWithValue("hourlySalary", data.HourlySalary);
        command.Parameters.AddWithValue("taxCode", (object?)data.TaxCode?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("dependentsCount", (object?)data.DependentsCount ?? DBNull.Value);
        command.Parameters.AddWithValue("status", (object?)data.Status?.Trim() ?? "Active");

        try
        {
            var createdId = await command.ExecuteScalarAsync(cancellationToken);
            if (createdId is null) return null;
            return await GetUserByIdAsync(Convert.ToInt64(createdId), cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }
    }

    public async Task<UserManagementDetail?> UpdateUserAsync(long id, UpdateUserData data, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE public.nhan_vien
            SET ho_ten = @displayName,
                loai_nhan_vien = @employeeType,
                luong_co_ban_gio = @hourlySalary,
                ma_so_thue = @taxCode,
                so_nguoi_phu_thuoc = @dependentsCount,
                trang_thai = @status,
                updated_at = NOW()
            WHERE id = @id;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("displayName", data.DisplayName.Trim());
        command.Parameters.AddWithValue("employeeType", data.EmployeeType.Trim());
        command.Parameters.AddWithValue("hourlySalary", data.HourlySalary);
        command.Parameters.AddWithValue("taxCode", (object?)data.TaxCode?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("dependentsCount", (object?)data.DependentsCount ?? DBNull.Value);
        command.Parameters.AddWithValue("status", (object?)data.Status?.Trim() ?? "Active");

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (rows == 0) return null;

        return await GetUserByIdAsync(id, cancellationToken);
    }

    public async Task<UserManagementDetail?> UpdateRoleAsync(long id, string roleName, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE public.nhan_vien
            SET id_vai_tro = (
                    SELECT id FROM public.vai_tro
                    WHERE LOWER(ten_vai_tro) = LOWER(@roleName)
                    LIMIT 1
                ),
                updated_at = NOW()
            WHERE id = @id;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("roleName", roleName.Trim());

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (rows == 0) return null;

        return await GetUserByIdAsync(id, cancellationToken);
    }

    public async Task<UserManagementDetail?> UpdateStatusAsync(long id, string status, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE public.nhan_vien
            SET trang_thai = @status,
                updated_at = NOW()
            WHERE id = @id;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("status", status.Trim());

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (rows == 0) return null;

        return await GetUserByIdAsync(id, cancellationToken);
    }

    public async Task<bool> DeleteUserAsync(long id, CancellationToken cancellationToken = default)
    {
        try
        {
            const string sql = "DELETE FROM public.nhan_vien WHERE id = @id;";
            await using var command = dataSource.CreateCommand(sql);
            command.Parameters.AddWithValue("id", id);
            var rows = await command.ExecuteNonQueryAsync(cancellationToken);
            return rows > 0;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            // Nếu có dữ liệu liên kết như chấm công, chuyển trạng thái sang Inactive (soft delete)
            const string softDeleteSql = """
                UPDATE public.nhan_vien
                SET trang_thai = 'Inactive',
                    updated_at = NOW()
                WHERE id = @id;
                """;
            await using var softCmd = dataSource.CreateCommand(softDeleteSql);
            softCmd.Parameters.AddWithValue("id", id);
            var rows = await softCmd.ExecuteNonQueryAsync(cancellationToken);
            return rows > 0;
        }
    }

    private static UserManagementDetail ReadUserDetail(NpgsqlDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetDecimal(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetInt32(7),
        reader.IsDBNull(8) ? "Active" : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetDecimal(9),
        reader.GetFieldValue<DateTimeOffset>(10),
        reader.GetFieldValue<DateTimeOffset>(11));
}
