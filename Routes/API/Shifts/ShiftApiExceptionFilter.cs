using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;

namespace manage365.Routes.API.Shifts;

/// <summary>Maps domain and constraint errors without exposing SQL or connection details.</summary>
public sealed class ShiftApiExceptionFilter : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        var (status, code, message) = context.Exception switch
        {
            KeyNotFoundException ex => (404, "shift_resource_not_found", ex.Message),
            ArgumentException ex => (400, "invalid_shift_data", ex.Message),
            FormatException => (400, "invalid_shift_format", "Ngày hoặc giờ không hợp lệ."),
            InvalidOperationException ex => (409, "shift_conflict", ex.Message),
            PostgresException { SqlState: "23505" } => (409, "shift_duplicate", "Dữ liệu ca đã tồn tại."),
            PostgresException { SqlState: "23503" } => (409, "shift_reference_conflict", "Dữ liệu liên quan đã thay đổi. Vui lòng tải lại."),
            _ => (0, "", "")
        };
        if (status == 0) return;
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = status, Title = message,
            Extensions = { ["code"] = code, ["traceId"] = context.HttpContext.TraceIdentifier }
        }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
