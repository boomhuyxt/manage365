using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace manage365.Routes.API.LeaveRequests;

public class LeaveApiException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }
    public IDictionary<string, string[]>? ValidationErrors { get; }

    public LeaveApiException(int statusCode, string errorCode, string message, IDictionary<string, string[]>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ValidationErrors = errors;
    }
}

public sealed class LeaveValidationException : LeaveApiException
{
    public LeaveValidationException(string errorCode, string message, IDictionary<string, string[]>? errors = null)
        : base(400, errorCode, message, errors)
    {
    }
}

public sealed class LeaveNotFoundException : LeaveApiException
{
    public LeaveNotFoundException(string errorCode = "leave_request_not_found", string message = "Không tìm thấy đơn nghỉ phép.")
        : base(404, errorCode, message)
    {
    }
}

public sealed class LeaveForbiddenException : LeaveApiException
{
    public LeaveForbiddenException(string errorCode, string message)
        : base(403, errorCode, message)
    {
    }
}

public sealed class LeaveConflictException : LeaveApiException
{
    public LeaveConflictException(string errorCode, string message)
        : base(409, errorCode, message)
    {
    }
}

public sealed class LeaveApiExceptionFilter : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is LeaveApiException leaveEx)
        {
            if (leaveEx.ValidationErrors is { Count: > 0 })
            {
                var validationDetails = new ValidationProblemDetails(leaveEx.ValidationErrors)
                {
                    Status = leaveEx.StatusCode,
                    Title = leaveEx.Message,
                    Detail = leaveEx.Message,
                    Instance = context.HttpContext.Request.Path
                };
                validationDetails.Extensions["code"] = leaveEx.ErrorCode;
                validationDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                context.Result = new ObjectResult(validationDetails) { StatusCode = leaveEx.StatusCode };
            }
            else
            {
                var problemDetails = new ProblemDetails
                {
                    Status = leaveEx.StatusCode,
                    Title = leaveEx.Message,
                    Detail = leaveEx.Message,
                    Instance = context.HttpContext.Request.Path
                };
                problemDetails.Extensions["code"] = leaveEx.ErrorCode;
                problemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                context.Result = new ObjectResult(problemDetails) { StatusCode = leaveEx.StatusCode };
            }
            context.ExceptionHandled = true;
            return;
        }

        if (context.Exception is KeyNotFoundException notFound)
        {
            var problem = new ProblemDetails
            {
                Status = 404,
                Title = notFound.Message,
                Detail = notFound.Message,
                Instance = context.HttpContext.Request.Path
            };
            problem.Extensions["code"] = "leave_request_not_found";
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            context.Result = new ObjectResult(problem) { StatusCode = 404 };
            context.ExceptionHandled = true;
            return;
        }

        if (context.Exception is ArgumentException argEx)
        {
            var problem = new ProblemDetails
            {
                Status = 400,
                Title = argEx.Message,
                Detail = argEx.Message,
                Instance = context.HttpContext.Request.Path
            };
            problem.Extensions["code"] = "validation_error";
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            context.Result = new ObjectResult(problem) { StatusCode = 400 };
            context.ExceptionHandled = true;
            return;
        }

        if (context.Exception is InvalidOperationException opEx)
        {
            var problem = new ProblemDetails
            {
                Status = 409,
                Title = opEx.Message,
                Detail = opEx.Message,
                Instance = context.HttpContext.Request.Path
            };
            problem.Extensions["code"] = "leave_conflict";
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            context.Result = new ObjectResult(problem) { StatusCode = 409 };
            context.ExceptionHandled = true;
        }
    }
}
