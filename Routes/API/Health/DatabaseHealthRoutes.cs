using Npgsql;

namespace manage365.Routes.API.Health;

public static class DatabaseHealthRoutes
{
    public static IEndpointRouteBuilder MapDatabaseHealthRoutes(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/health/database", CheckDatabaseAsync)
            .WithName("CheckDatabase")
            .WithTags("Health")
            .AllowAnonymous()
            .Produces<DatabaseHealthResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> CheckDatabaseAsync(
        NpgsqlDataSource dataSource,
        ILogger<DatabaseHealthLog> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);

            return Results.Ok(new DatabaseHealthResponse("healthy"));
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            logger.LogError(exception, "PostgreSQL health check failed.");

            return Results.Problem(
                title: "Database unavailable",
                detail: "The application could not connect to PostgreSQL.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private sealed record DatabaseHealthResponse(string Status);

    private sealed class DatabaseHealthLog;
}
