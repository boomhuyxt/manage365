using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using manage365.Repositories.Shifts;
using manage365.Routes.API.Shifts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace manage365.Tests;

public sealed class ShiftSchedulingHttpTests
{
    [Fact]
    public async Task HttpPipeline_EnforcesPermissions_AndSharesBatchDataWithCoverage()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(ShiftAssignmentsController).Assembly);
        builder.Services.AddSingleton<IShiftSchedulingRepository, FakeShiftSchedulingRepository>();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync(ct);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var preview = new ShiftCoverageRequest([new(102, "2026-10-19", "08:00", "17:00")]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("api/shift-assignments/coverage", preview, ct)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Role", "Employee");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("api/shift-assignments/coverage", preview, ct)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Manager");
        var batch = await client.PostAsJsonAsync("api/shift-assignments/batch", new BatchAssignShiftRequest([
            new(101, "2026-10-19", CustomStartTime: "02:00", CustomEndTime: "23:00"),
            new(102, "2026-10-19", CustomStartTime: "08:00", CustomEndTime: "12:00"),
            new(102, "2026-10-19", CustomStartTime: "17:00", CustomEndTime: "21:00")]), ct);
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        Assert.Equal(3, (await batch.Content.ReadFromJsonAsync<BatchAssignShiftResult>(ct))!.SuccessCount);
        var response = await client.PostAsJsonAsync("api/shift-assignments/coverage", preview, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ShiftCoverageResponse>(ct))!;
        Assert.Equal([2, 1], result.Segments.Select(s => s.CurrentEmployeeIds.Count));
        Assert.All(result.Segments, segment => Assert.Equal([101L, 102L], segment.ProjectedEmployeeIds));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("api/shift-assignments/coverage", new ShiftCoverageRequest([]), ct)).StatusCode);
        await app.StopAsync(ct);
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (role.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Role, role), new("sub", "101")], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new(principal, Scheme.Name)));
        }
    }
}
