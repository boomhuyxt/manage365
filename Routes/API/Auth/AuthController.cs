using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace manage365.Routes.API.Auth;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IUserStore userStore,
    IPasswordHasher passwordHasher,
    IJwtTokenService tokenService) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public ActionResult<AuthResponse> Register(RegisterRequest request)
    {
        var email = InMemoryUserStore.NormalizeEmail(request.Email);
        var user = new User(
            Guid.NewGuid(),
            email,
            request.DisplayName.Trim(),
            passwordHasher.Hash(request.Password),
            DateTimeOffset.UtcNow);

        if (!userStore.TryAdd(user))
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Email is already registered."
            });
        }

        var response = CreateAuthResponse(user);
        return CreatedAtAction(nameof(Me), response);
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public ActionResult<AuthResponse> Login(LoginRequest request)
    {
        var email = InMemoryUserStore.NormalizeEmail(request.Email);
        var user = userStore.FindByEmail(email);

        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Invalid email or password."
            });
        }

        return Ok(CreateAuthResponse(user));
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

        if (!Guid.TryParse(subject, out var userId) || email is null || displayName is null)
        {
            return Unauthorized();
        }

        return Ok(new UserResponse(userId, email, displayName));
    }

    private AuthResponse CreateAuthResponse(User user)
    {
        var token = tokenService.CreateAccessToken(user);
        return new AuthResponse(
            token.Value,
            "Bearer",
            token.ExpiresAtUtc,
            new UserResponse(user.Id, user.Email, user.DisplayName));
    }
}
