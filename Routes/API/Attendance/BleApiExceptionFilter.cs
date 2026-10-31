using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;

namespace manage365.Routes.API.Attendance;

public sealed class BleApiExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var error = context.Exception as BleAttendanceException;
        if (context.Exception is PostgresException { SqlState: "42P01" or "42703" })
            error = new("ble_schema_missing", "BLE chưa được cài đặt đầy đủ. Cần áp dụng các migration 013–018.", 503);
        if (error is null) return;
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = error.StatusCode, Title = error.Message,
            Extensions = { ["code"] = error.Code, ["traceId"] = context.HttpContext.TraceIdentifier }
        }) { StatusCode = error.StatusCode };
        context.ExceptionHandled = true;
    }
}
