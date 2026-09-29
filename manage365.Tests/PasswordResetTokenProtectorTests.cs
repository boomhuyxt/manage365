using manage365.Routes.API.Auth.PasswordReset;
using Xunit;

namespace manage365.Tests;

public sealed class PasswordResetTokenProtectorTests
{
    private readonly PasswordResetTokenProtector _protector = new("unit-test-key-with-at-least-32-bytes");

    [Fact]
    public void CreateCode_ReturnsSixDigits()
    {
        var code = _protector.CreateCode();

        Assert.Matches("^[0-9]{6}$", code);
    }

    [Fact]
    public void HashCode_NormalizesEmailAndIsDeterministic()
    {
        var first = _protector.HashCode(" Employee@Retail365.com ", "123456");
        var second = _protector.HashCode("employee@retail365.com", "123456");

        Assert.Equal(first, second);
    }

    [Fact]
    public void CreateResetToken_ReturnsDifferentOpaqueTokens()
    {
        var first = _protector.CreateResetToken();
        var second = _protector.CreateResetToken();

        Assert.NotEqual(first, second);
        Assert.DoesNotContain("=", first);
    }

    [Fact]
    public void HashResetToken_IsDeterministic()
    {
        const string token = "opaque-token";

        Assert.Equal(_protector.HashResetToken(token), _protector.HashResetToken(token));
    }
}
