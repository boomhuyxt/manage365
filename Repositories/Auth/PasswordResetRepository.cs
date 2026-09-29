using Npgsql;

namespace manage365.Repositories.Auth;

public interface IPasswordResetRepository
{
    Task<bool> SaveCodeAsync(long userId, string codeHash, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken);
    Task InvalidateCodeAsync(long userId, CancellationToken cancellationToken);
    Task<bool> VerifyCodeAsync(
        long userId,
        string codeHash,
        string resetTokenHash,
        DateTimeOffset resetTokenExpiresAtUtc,
        int maxAttempts,
        CancellationToken cancellationToken);
    Task<bool> ResetPasswordAsync(
        long userId,
        string resetTokenHash,
        string passwordHash,
        CancellationToken cancellationToken);
}

public sealed class PostgresPasswordResetRepository(NpgsqlDataSource dataSource) : IPasswordResetRepository
{
    public async Task<bool> SaveCodeAsync(
        long userId,
        string codeHash,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO public.password_reset_codes
                (user_id, code_hash, expires_at)
            VALUES (@userId, @codeHash, @expiresAt)
            ON CONFLICT (user_id) DO UPDATE SET
                code_hash = EXCLUDED.code_hash,
                expires_at = EXCLUDED.expires_at,
                failed_attempts = 0,
                verified_at = NULL,
                reset_token_hash = NULL,
                reset_token_expires_at = NULL,
                used_at = NULL,
                created_at = now()
            WHERE password_reset_codes.created_at <= now() - interval '60 seconds'
            RETURNING id;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("codeHash", codeHash);
        command.Parameters.AddWithValue("expiresAt", expiresAtUtc);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task InvalidateCodeAsync(long userId, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM public.password_reset_codes WHERE user_id = @userId;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("userId", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> VerifyCodeAsync(
        long userId,
        string codeHash,
        string resetTokenHash,
        DateTimeOffset resetTokenExpiresAtUtc,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH candidate AS (
                SELECT id, code_hash
                FROM public.password_reset_codes
                WHERE user_id = @userId
                  AND used_at IS NULL
                  AND verified_at IS NULL
                  AND expires_at > now()
                  AND failed_attempts < @maxAttempts
                FOR UPDATE
            ), updated AS (
                UPDATE public.password_reset_codes AS prc
                SET failed_attempts = prc.failed_attempts +
                        CASE WHEN candidate.code_hash = @codeHash THEN 0 ELSE 1 END,
                    verified_at = CASE
                        WHEN candidate.code_hash = @codeHash THEN now()
                        ELSE prc.verified_at
                    END,
                    reset_token_hash = CASE
                        WHEN candidate.code_hash = @codeHash THEN @resetTokenHash
                        ELSE prc.reset_token_hash
                    END,
                    reset_token_expires_at = CASE
                        WHEN candidate.code_hash = @codeHash THEN @resetTokenExpiresAt
                        ELSE prc.reset_token_expires_at
                    END
                FROM candidate
                WHERE prc.id = candidate.id
                RETURNING candidate.code_hash = @codeHash AS verified
            )
            SELECT COALESCE((SELECT verified FROM updated), false);
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("codeHash", codeHash);
        command.Parameters.AddWithValue("resetTokenHash", resetTokenHash);
        command.Parameters.AddWithValue("resetTokenExpiresAt", resetTokenExpiresAtUtc);
        command.Parameters.AddWithValue("maxAttempts", maxAttempts);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> ResetPasswordAsync(
        long userId,
        string resetTokenHash,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH valid_reset AS (
                SELECT id
                FROM public.password_reset_codes
                WHERE user_id = @userId
                  AND reset_token_hash = @resetTokenHash
                  AND verified_at IS NOT NULL
                  AND reset_token_expires_at > now()
                  AND used_at IS NULL
                FOR UPDATE
            ), updated_user AS (
                UPDATE public.nhan_vien
                SET mat_khau = @passwordHash
                WHERE id = @userId AND EXISTS (SELECT 1 FROM valid_reset)
                RETURNING id
            ), consumed AS (
                UPDATE public.password_reset_codes
                SET used_at = now()
                WHERE id IN (SELECT id FROM valid_reset)
                  AND EXISTS (SELECT 1 FROM updated_user)
                RETURNING id
            )
            SELECT EXISTS (SELECT 1 FROM consumed);
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("resetTokenHash", resetTokenHash);
        command.Parameters.AddWithValue("passwordHash", passwordHash);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }
}
