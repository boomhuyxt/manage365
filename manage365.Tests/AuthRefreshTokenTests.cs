using manage365.Repositories.Auth;
using manage365.Routes.API.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

namespace manage365.Tests;

public sealed class AuthRefreshTokenTests
{
    private static readonly User TestUser = new(
        42,
        "employee@manage365.io.vn",
        "Nguyen Van A",
        "hashed-password",
        AppRoles.Employee,
        DateTimeOffset.UtcNow);

    private readonly IOptions<JwtOptions> _jwtOptions = Options.Create(new JwtOptions
    {
        Issuer = "manage365-test",
        Audience = "manage365-test-audience",
        Key = "super-secret-key-that-is-at-least-32-bytes-long!",
        AccessTokenMinutes = 60,
        RefreshTokenLifetimeDays = 30
    });

    [Fact]
    public void JwtTokenService_GeneratesAndHashesRefreshToken_Consistently()
    {
        var tokenService = new JwtTokenService(_jwtOptions);

        var token1 = tokenService.GenerateRefreshToken();
        var token2 = tokenService.GenerateRefreshToken();

        Assert.False(string.IsNullOrWhiteSpace(token1));
        Assert.False(string.IsNullOrWhiteSpace(token2));
        Assert.NotEqual(token1, token2);

        var hash1 = tokenService.HashRefreshToken(token1);
        var hash2 = tokenService.HashRefreshToken(token1);

        Assert.Equal(64, hash1.Length);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public async Task AuthController_Login_GeneratesBothAccessTokenAndRefreshToken()
    {
        var userRepo = new FakeUserRepository(TestUser);
        var tokenRepo = new FakeRefreshTokenRepository();
        var passwordHasher = new FakePasswordHasher(verifyResult: true);
        var tokenService = new JwtTokenService(_jwtOptions);

        var controller = new AuthController(userRepo, passwordHasher, tokenService, tokenRepo, _jwtOptions)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.Login(new LoginRequest
        {
            Email = TestUser.Email,
            Password = "valid-password"
        }, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var authResponse = Assert.IsType<AuthResponse>(okResult.Value);

        Assert.False(string.IsNullOrWhiteSpace(authResponse.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(authResponse.RefreshToken));
        Assert.NotNull(authResponse.RefreshTokenExpiresAtUtc);
        Assert.Equal(TestUser.Email, authResponse.User.Email);
        Assert.Single(tokenRepo.Tokens);
    }

    [Fact]
    public async Task AuthController_Refresh_SlidesExpirationAndIssuesNewAccessToken()
    {
        var userRepo = new FakeUserRepository(TestUser);
        var tokenRepo = new FakeRefreshTokenRepository();
        var tokenService = new JwtTokenService(_jwtOptions);
        var passwordHasher = new FakePasswordHasher(verifyResult: true);

        var rawRefreshToken = "sample-valid-refresh-token";
        var tokenHash = tokenService.HashRefreshToken(rawRefreshToken);
        var initialExpiresAt = DateTimeOffset.UtcNow.AddDays(5);

        await tokenRepo.SaveRefreshTokenAsync(TestUser.Id, tokenHash, initialExpiresAt);

        var controller = new AuthController(userRepo, passwordHasher, tokenService, tokenRepo, _jwtOptions);

        var result = await controller.Refresh(new RefreshTokenRequest
        {
            RefreshToken = rawRefreshToken
        }, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var authResponse = Assert.IsType<AuthResponse>(okResult.Value);

        Assert.False(string.IsNullOrWhiteSpace(authResponse.AccessToken));
        Assert.Equal(rawRefreshToken, authResponse.RefreshToken);
        Assert.True(authResponse.RefreshTokenExpiresAtUtc > initialExpiresAt);
        Assert.Equal(TestUser.Id, authResponse.User.Id);
    }

    [Fact]
    public async Task AuthController_Refresh_FailsWhenTokenIsExpiredOrNotFound()
    {
        var userRepo = new FakeUserRepository(TestUser);
        var tokenRepo = new FakeRefreshTokenRepository();
        var tokenService = new JwtTokenService(_jwtOptions);
        var passwordHasher = new FakePasswordHasher(verifyResult: true);

        var controller = new AuthController(userRepo, passwordHasher, tokenService, tokenRepo, _jwtOptions);

        var result = await controller.Refresh(new RefreshTokenRequest
        {
            RefreshToken = "non-existent-or-expired-token"
        }, CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(unauthorized.Value);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.Status);
    }

    [Fact]
    public async Task AuthController_Revoke_MarksTokenAsRevoked()
    {
        var userRepo = new FakeUserRepository(TestUser);
        var tokenRepo = new FakeRefreshTokenRepository();
        var tokenService = new JwtTokenService(_jwtOptions);
        var passwordHasher = new FakePasswordHasher(verifyResult: true);

        var rawRefreshToken = "token-to-revoke";
        var tokenHash = tokenService.HashRefreshToken(rawRefreshToken);
        await tokenRepo.SaveRefreshTokenAsync(TestUser.Id, tokenHash, DateTimeOffset.UtcNow.AddDays(30));

        var controller = new AuthController(userRepo, passwordHasher, tokenService, tokenRepo, _jwtOptions);

        var result = await controller.Revoke(new RevokeTokenRequest
        {
            RefreshToken = rawRefreshToken
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);

        // Attempting to refresh after revoking must fail with 401
        var refreshResult = await controller.Refresh(new RefreshTokenRequest
        {
            RefreshToken = rawRefreshToken
        }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(refreshResult.Result);
    }

    private sealed class FakeUserRepository(User? user = null) : IUserRepository
    {
        public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
            => Task.FromResult(user != null && user.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase) ? user : null);

        public Task<User?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(user != null && user.Id == id ? user : null);

        public Task<User?> TryAddAsync(NewUser newUser, CancellationToken cancellationToken = default)
            => Task.FromResult(user);
    }

    private sealed class FakePasswordHasher(bool verifyResult) : IPasswordHasher
    {
        public string Hash(string password) => "hashed-" + password;
        public bool Verify(string password, string hash) => verifyResult;
    }

    private sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
    {
        public readonly List<UserRefreshToken> Tokens = [];

        public Task EnsureTableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveRefreshTokenAsync(
            long userId,
            string tokenHash,
            DateTimeOffset expiresAtUtc,
            string? deviceInfo = null,
            CancellationToken cancellationToken = default)
        {
            Tokens.Add(new UserRefreshToken(
                Tokens.Count + 1,
                userId,
                tokenHash,
                expiresAtUtc,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                null,
                deviceInfo));
            return Task.CompletedTask;
        }

        public Task<UserRefreshToken?> SlideExpirationAsync(
            string tokenHash,
            DateTimeOffset newExpiresAtUtc,
            CancellationToken cancellationToken = default)
        {
            var index = Tokens.FindIndex(t => t.TokenHash == tokenHash && t.RevokedAtUtc == null && t.ExpiresAtUtc > DateTimeOffset.UtcNow);
            if (index == -1) return Task.FromResult<UserRefreshToken?>(null);

            var existing = Tokens[index];
            var updated = existing with
            {
                ExpiresAtUtc = newExpiresAtUtc,
                LastUsedAtUtc = DateTimeOffset.UtcNow
            };
            Tokens[index] = updated;
            return Task.FromResult<UserRefreshToken?>(updated);
        }

        public Task<UserRefreshToken?> FindValidTokenAsync(
            string tokenHash,
            CancellationToken cancellationToken = default)
        {
            var match = Tokens.FirstOrDefault(t => t.TokenHash == tokenHash && t.RevokedAtUtc == null && t.ExpiresAtUtc > DateTimeOffset.UtcNow);
            return Task.FromResult<UserRefreshToken?>(match);
        }

        public Task RevokeTokenAsync(
            string tokenHash,
            CancellationToken cancellationToken = default)
        {
            var index = Tokens.FindIndex(t => t.TokenHash == tokenHash);
            if (index != -1)
            {
                Tokens[index] = Tokens[index] with { RevokedAtUtc = DateTimeOffset.UtcNow };
            }
            return Task.CompletedTask;
        }

        public Task RevokeAllUserTokensAsync(
            long userId,
            CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < Tokens.Count; i++)
            {
                if (Tokens[i].UserId == userId)
                {
                    Tokens[i] = Tokens[i] with { RevokedAtUtc = DateTimeOffset.UtcNow };
                }
            }
            return Task.CompletedTask;
        }
    }
}
