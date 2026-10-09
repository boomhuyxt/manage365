using Npgsql;

namespace manage365.Repositories.Roles;

public sealed class PostgresRoleRepository(NpgsqlDataSource dataSource) : IRoleRepository
{
    public async Task<IReadOnlyList<RoleItem>> GetAllRolesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT vt.id, vt.ten_vai_tro, vt.mo_ta,
                   COUNT(nv.id)::int AS user_count,
                   vt.created_at, vt.updated_at
            FROM public.vai_tro AS vt
            LEFT JOIN public.nhan_vien AS nv ON nv.id_vai_tro = vt.id
            GROUP BY vt.id, vt.ten_vai_tro, vt.mo_ta, vt.created_at, vt.updated_at
            ORDER BY vt.id ASC;
            """;

        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var list = new List<RoleItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadRole(reader));
        }

        return list;
    }

    public async Task<RoleItem?> GetRoleByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT vt.id, vt.ten_vai_tro, vt.mo_ta,
                   COUNT(nv.id)::int AS user_count,
                   vt.created_at, vt.updated_at
            FROM public.vai_tro AS vt
            LEFT JOIN public.nhan_vien AS nv ON nv.id_vai_tro = vt.id
            WHERE vt.id = @id
            GROUP BY vt.id, vt.ten_vai_tro, vt.mo_ta, vt.created_at, vt.updated_at
            LIMIT 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadRole(reader) : null;
    }

    public async Task<RoleItem?> GetRoleByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT vt.id, vt.ten_vai_tro, vt.mo_ta,
                   COUNT(nv.id)::int AS user_count,
                   vt.created_at, vt.updated_at
            FROM public.vai_tro AS vt
            LEFT JOIN public.nhan_vien AS nv ON nv.id_vai_tro = vt.id
            WHERE LOWER(vt.ten_vai_tro) = LOWER(@name)
            GROUP BY vt.id, vt.ten_vai_tro, vt.mo_ta, vt.created_at, vt.updated_at
            LIMIT 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("name", name.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadRole(reader) : null;
    }

    public async Task<RoleItem?> CreateRoleAsync(string name, string? description, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO public.vai_tro (id, ten_vai_tro, mo_ta, created_at, updated_at)
            VALUES (nextval('public.vai_tro_id_seq'), @name, @description, NOW(), NOW())
            RETURNING id, ten_vai_tro, mo_ta, 0, created_at, updated_at;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.AddWithValue("description", (object?)description?.Trim() ?? DBNull.Value);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadRole(reader) : null;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }
    }

    public async Task<RoleItem?> UpdateRoleAsync(long id, string name, string? description, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE public.vai_tro
            SET ten_vai_tro = @name,
                mo_ta = @description,
                updated_at = NOW()
            WHERE id = @id
            RETURNING id, ten_vai_tro, mo_ta,
                      (SELECT COUNT(*)::int FROM public.nhan_vien WHERE id_vai_tro = @id) AS user_count,
                      created_at, updated_at;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.AddWithValue("description", (object?)description?.Trim() ?? DBNull.Value);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadRole(reader) : null;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }
    }

    public async Task<bool> DeleteRoleAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM public.vai_tro WHERE id = @id;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        return rowsAffected > 0;
    }

    public async Task<int> GetUserCountForRoleAsync(long roleId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT COUNT(*)::int FROM public.nhan_vien WHERE id_vai_tro = @roleId;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("roleId", roleId);
        var count = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(count);
    }

    private static RoleItem ReadRole(NpgsqlDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.GetInt32(3),
        reader.GetFieldValue<DateTimeOffset>(4),
        reader.GetFieldValue<DateTimeOffset>(5));
}
