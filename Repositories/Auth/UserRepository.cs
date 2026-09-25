using manage365.Routes.API.Auth;
using Npgsql;

namespace manage365.Repositories.Auth;

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<User?> TryAddAsync(NewUser user, CancellationToken cancellationToken = default);
}

public sealed record NewUser(
    string Email,
    string DisplayName,
    string PasswordHash,
    string EmployeeType);

public sealed class PostgresUserRepository(NpgsqlDataSource dataSource) : IUserRepository
{
    private const string EmployeeRole = AppRoles.Employee;

    public async Task<User?> FindByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT nv.id, nv.email, nv.ho_ten, nv.mat_khau,
                   COALESCE(vt.ten_vai_tro, 'Employee'), nv.created_at
            FROM public.nhan_vien AS nv
            LEFT JOIN public.vai_tro AS vt ON vt.id = nv.id_vai_tro
            WHERE nv.email = @email
            LIMIT 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("email", normalizedEmail);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    public async Task<User?> TryAddAsync(
        NewUser user,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            WITH employee_role AS (
                INSERT INTO public.vai_tro (id, ten_vai_tro, mo_ta)
                VALUES (nextval('public.vai_tro_id_seq'), @role, 'Default employee role')
                ON CONFLICT (ten_vai_tro)
                DO UPDATE SET ten_vai_tro = EXCLUDED.ten_vai_tro
                RETURNING id
            )
            INSERT INTO public.nhan_vien
                (id, id_vai_tro, ho_ten, email, mat_khau, loai_nhan_vien, luong_co_ban_gio)
            SELECT nextval('public.nhan_vien_id_seq'), id, @displayName, @email,
                   @passwordHash, @employeeType, 0
            FROM employee_role
            RETURNING id, email, ho_ten, mat_khau, @role, created_at;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("role", EmployeeRole);
        command.Parameters.AddWithValue("displayName", user.DisplayName);
        command.Parameters.AddWithValue("email", user.Email);
        command.Parameters.AddWithValue("passwordHash", user.PasswordHash);
        command.Parameters.AddWithValue("employeeType", user.EmployeeType);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation &&
            exception.ConstraintName == "nhan_vien_email_key")
        {
            return null;
        }
    }

    private static User ReadUser(NpgsqlDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetFieldValue<DateTimeOffset>(5));
}
