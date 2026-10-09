using System.IdentityModel.Tokens.Jwt;
using manage365.Repositories.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace manage365.Routes.API.Auth;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenService tokenService,
    IRefreshTokenRepository refreshTokenRepository,
    IOptions<JwtOptions> jwtOptions) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var user = await userRepository.TryAddAsync(new NewUser(
            email,
            request.DisplayName.Trim(),
            passwordHasher.Hash(request.Password),
            "FullTime"), cancellationToken);

        if (user is null)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Email is already registered."
            });
        }

        var response = await CreateAuthResponseAsync(user, cancellationToken);
        return CreatedAtAction(nameof(Me), response);
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var user = await userRepository.FindByEmailAsync(email, cancellationToken);

        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Invalid email or password."
            });
        }

        var response = await CreateAuthResponseAsync(user, cancellationToken);
        return Ok(response);
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Refresh token is required."
            });
        }

        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
        var slidingDays = Math.Max(1, jwtOptions.Value.RefreshTokenLifetimeDays);
        var newExpiresAt = DateTimeOffset.UtcNow.AddDays(slidingDays);

        var updatedToken = await refreshTokenRepository.SlideExpirationAsync(tokenHash, newExpiresAt, cancellationToken);
        if (updatedToken is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Invalid or expired refresh token."
            });
        }

        var user = await userRepository.FindByIdAsync(updatedToken.UserId, cancellationToken);
        if (user is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "User not found."
            });
        }

        var accessToken = tokenService.CreateAccessToken(user);
        var response = new AuthResponse(
            accessToken.Value,
            request.RefreshToken.Trim(),
            "Bearer",
            accessToken.ExpiresAtUtc,
            updatedToken.ExpiresAtUtc,
            new UserResponse(user.Id, user.Email, user.DisplayName, user.Role));

        return Ok(response);
    }

    [HttpPost("revoke")]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Revoke(
        RevokeTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
            await refreshTokenRepository.RevokeTokenAsync(tokenHash, cancellationToken);
        }

        return Ok(new { message = "Logged out successfully." });
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<UserResponse> Me()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var email = User.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
        var displayName = User.FindFirst(JwtRegisteredClaimNames.Name)?.Value;
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        if (!long.TryParse(subject, out var userId) || email is null || displayName is null || role is null)
        {
            return Unauthorized();
        }

        return Ok(new UserResponse(userId, email, displayName, role));
    }

    private async Task<AuthResponse> CreateAuthResponseAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var accessToken = tokenService.CreateAccessToken(user);
        var rawRefreshToken = tokenService.GenerateRefreshToken();
        var tokenHash = tokenService.HashRefreshToken(rawRefreshToken);
        var slidingDays = Math.Max(1, jwtOptions.Value.RefreshTokenLifetimeDays);
        var refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(slidingDays);
        var deviceInfo = Request.Headers.UserAgent.ToString();

        await refreshTokenRepository.SaveRefreshTokenAsync(
            user.Id,
            tokenHash,
            refreshExpiresAt,
            string.IsNullOrWhiteSpace(deviceInfo) ? null : deviceInfo,
            cancellationToken);

        return new AuthResponse(
            accessToken.Value,
            rawRefreshToken,
            "Bearer",
            accessToken.ExpiresAtUtc,
            refreshExpiresAt,
            new UserResponse(user.Id, user.Email, user.DisplayName, user.Role));
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
