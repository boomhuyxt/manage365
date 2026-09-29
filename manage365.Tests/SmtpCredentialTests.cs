using manage365.Routes.API.Auth.PasswordReset;
using Xunit;

namespace manage365.Tests;

public sealed class SmtpCredentialTests
{
    [Fact]
    public void NormalizePassword_RemovesWhitespaceForGmail()
    {
        var password = SmtpCredential.NormalizePassword(
            "smtp.gmail.com",
            "abcd efgh ijkl mnop");

        Assert.Equal("abcdefghijklmnop", password);
    }

    [Fact]
    public void NormalizePassword_PreservesWhitespaceForOtherProviders()
    {
        var password = SmtpCredential.NormalizePassword(
            "smtp.example.com",
            "password with spaces");

        Assert.Equal("password with spaces", password);
    }
}
