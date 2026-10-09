using manage365.Repositories.Auth;
using manage365.Routes.API.Auth;
using manage365.Routes.API.Auth.PasswordReset;
using Microsoft.Extensions.Options;
using Xunit;

namespace manage365.Tests;

public sealed class PasswordResetServiceTests
{
    private static readonly User ExistingUser = new(
        7,
        "employee@retail365.com",
        "Retail Employee",
        "old-hash",
        AppRoles.Employee,
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task RequestCode_DoesNotSendEmailForUnknownAccount()
    {
        var emailSender = new FakeEmailSender();
        var service = CreateService(new FakeUserRepository(), new FakeResetRepository(), emailSender);

        await service.RequestCodeAsync("missing@retail365.com", CancellationToken.None);

        Assert.Null(emailSender.Code);
    }

    [Fact]
    public async Task RequestCode_SendsSixDigitCodeForExistingAccount()
    {
        var emailSender = new FakeEmailSender();
        var service = CreateService(new FakeUserRepository(ExistingUser), new FakeResetRepository(), emailSender);

        await service.RequestCodeAsync(ExistingUser.Email, CancellationToken.None);

        Assert.Matches("^[0-9]{6}$", emailSender.Code!);
    }

    [Fact]
    public async Task VerifyCode_ReturnsOpaqueTokenWhenRepositoryAcceptsCode()
    {
        var service = CreateService(new FakeUserRepository(ExistingUser), new FakeResetRepository(), new FakeEmailSender());

        var result = await service.VerifyCodeAsync(ExistingUser.Email, "123456", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Token.Length >= 32);
    }

    [Fact]
    public async Task ResetPassword_HashesPasswordBeforePersistence()
    {
        var repository = new FakeResetRepository();
        var service = CreateService(new FakeUserRepository(ExistingUser), repository, new FakeEmailSender());

        var reset = await service.ResetPasswordAsync(
            ExistingUser.Email,
            "opaque-reset-token-with-more-than-32-characters",
            "NewPassword123!",
            CancellationToken.None);

        Assert.True(reset);
        Assert.Equal("hashed:NewPassword123!", repository.PasswordHash);
    }

    private static PasswordResetService CreateService(
        IUserRepository userRepository,
        IPasswordResetRepository resetRepository,
        IPasswordResetEmailSender emailSender) => new(
            userRepository,
            resetRepository,
            new PasswordResetTokenProtector("unit-test-key-with-at-least-32-bytes"),
            emailSender,
            new FakePasswordHasher(),
            Options.Create(new PasswordResetOptions()));

    private sealed class FakeUserRepository(User? user = null) : IUserRepository
    {
        public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
            Task.FromResult(user?.Email == normalizedEmail ? user : null);

        public Task<User?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult(user?.Id == id ? user : null);

        public Task<User?> TryAddAsync(NewUser newUser, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);
    }

    private sealed class FakeResetRepository : IPasswordResetRepository
    {
        public string? PasswordHash { get; private set; }

        public Task<bool> SaveCodeAsync(long userId, string codeHash, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task InvalidateCodeAsync(long userId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> VerifyCodeAsync(
            long userId,
            string codeHash,
            string resetTokenHash,
            DateTimeOffset resetTokenExpiresAtUtc,
            int maxAttempts,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> ResetPasswordAsync(
            long userId,
            string resetTokenHash,
            string passwordHash,
            CancellationToken cancellationToken)
        {
            PasswordHash = passwordHash;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeEmailSender : IPasswordResetEmailSender
    {
        public string? Code { get; private set; }

        public Task SendCodeAsync(
            string recipientEmail,
            string displayName,
            string code,
            int lifetimeMinutes,
            CancellationToken cancellationToken)
        {
            Code = code;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";
        public bool Verify(string password, string encodedHash) => encodedHash == Hash(password);
    }
}
