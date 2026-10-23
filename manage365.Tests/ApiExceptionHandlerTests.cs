using System.Text.Json;
using manage365.Routes.API.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace manage365.Tests;

public sealed class ApiExceptionHandlerTests
{
    [Fact]
    public async Task HandleAsync_ReturnsSafeProblemDetailsWithTraceId()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.TraceIdentifier = "trace-test-123";
        context.Response.Body = new MemoryStream();
        var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(context, new InvalidOperationException("secret SQL details"), TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("internal_error", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("trace-test-123", body.RootElement.GetProperty("traceId").GetString());
        Assert.DoesNotContain("secret SQL details", body.RootElement.ToString());
    }
}
