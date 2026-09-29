using manage365.Configuration;
using Xunit;

namespace manage365.Tests;

public sealed class LocalEnvFileTests
{
    [Fact]
    public void Parse_SupportsQuotedValuesAndIgnoresComments()
    {
        var values = LocalEnvFile.Parse(
        [
            "# local secrets",
            "Smtp__Password='abcd efgh ijkl mnop'",
            "Smtp__Username=sender@gmail.com"
        ]);

        Assert.Equal("abcd efgh ijkl mnop", values["Smtp__Password"]);
        Assert.Equal("sender@gmail.com", values["Smtp__Username"]);
    }
}
