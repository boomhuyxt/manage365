using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace manage365.Routes.API.Errors;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        var (status, code, title) = exception switch
        {
            PostgresException { SqlState: "23505" } => (StatusCodes.Status409Conflict, "duplicate_conflict", "Dữ liệu đã tồn tại."),
            PostgresException { SqlState: "23503" } => (StatusCodes.Status409Conflict, "reference_conflict", "Dữ liệu liên quan đã thay đổi."),
            DbException => (StatusCodes.Status503ServiceUnavailable, "database_unavailable", "Dịch vụ dữ liệu hiện không khả dụng."),
            TimeoutException => (StatusCodes.Status504GatewayTimeout, "request_timeout", "Yêu cầu đã hết thời gian chờ."),
            _ => (StatusCodes.Status500InternalServerError, "internal_error", "Đã xảy ra lỗi nội bộ.")
        };

        logger.LogError(exception,
            "Unhandled API exception. TraceId: {TraceId}, Path: {Path}, ErrorCode: {ErrorCode}",
            httpContext.TraceIdentifier,
            httpContext.Request.Path,
            code);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, problem, cancellationToken: cancellationToken);
        return true;
    }
}
