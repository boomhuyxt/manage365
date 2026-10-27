using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;

namespace manage365.Routes.API.LeaveRequests;

/// <summary>Checks current account state as tokens can outlive role/status changes.</summary>
public sealed class LeaveAccountFilter(NpgsqlDataSource source) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (!long.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id <= 0)
        {
            context.Result = new UnauthorizedResult();
            return;
        }
        await using var command = source.CreateCommand("""
            SELECT COALESCE(n.trang_thai,'Active'), v.ten_vai_tro
            FROM public.nhan_vien n LEFT JOIN public.vai_tro v ON v.id=n.id_vai_tro WHERE n.id=@id
            """);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(context.HttpContext.RequestAborted);
        var requiredRoles = context.ActionDescriptor.EndpointMetadata
            .OfType<AuthorizeAttribute>()
            .SelectMany(attribute => (attribute.Roles ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        if (!await reader.ReadAsync(context.HttpContext.RequestAborted) ||
            !string.Equals(reader.GetString(0), "Active", StringComparison.OrdinalIgnoreCase) ||
            (requiredRoles.Length > 0 && (reader.IsDBNull(1) ||
                !requiredRoles.Contains(reader.GetString(1), StringComparer.OrdinalIgnoreCase))))
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = 403, Title = "Tài khoản không hoạt động hoặc không còn quyền xử lý đơn.",
                Extensions = { ["code"] = "forbidden" }
            }) { StatusCode = 403 };
            return;
        }
        await reader.CloseAsync();
        await next();
    }
}
