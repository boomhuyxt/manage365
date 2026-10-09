using System.IdentityModel.Tokens.Jwt;
using manage365.Repositories.Departments;
using manage365.Routes.API.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace manage365.Routes.API.Departments;

[ApiController]
[Tags("Departments")]
[Route("api/departments")]
public sealed class DepartmentsController(IDepartmentRepository departmentRepository) : ControllerBase
{
    /// <summary>
    /// Danh sách tất cả các phòng ban trong hệ thống kèm số lượng nhân viên và số lượng công cụ
    /// </summary>
    [HttpGet]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType<List<DepartmentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<DepartmentDto>>> GetDepartments(CancellationToken cancellationToken)
    {
        var items = await departmentRepository.GetAllDepartmentsAsync(cancellationToken);
        var result = items.Select(ToDto).ToList();
        return Ok(result);
    }

    /// <summary>
    /// Lấy chi tiết thông tin phòng ban kèm danh sách các công cụ trực thuộc
    /// </summary>
    [HttpGet("{id:long}")]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType<DepartmentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DepartmentDetailDto>> GetDepartmentById(long id, CancellationToken cancellationToken)
    {
        var detail = await departmentRepository.GetDepartmentByIdAsync(id, cancellationToken);
        if (detail is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy phòng ban có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "department_not_found" });
        }

        return Ok(ToDetailDto(detail));
    }

    /// <summary>
    /// Tạo mới một phòng ban (chỉ Quản trị viên - Admin)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartmentDto>> CreateDepartment(
        [FromBody] CreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var existing = await departmentRepository.GetDepartmentByCodeAsync(normalizedCode, cancellationToken);
        if (existing is not null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"Mã phòng ban '{normalizedCode}' đã tồn tại trong hệ thống.",
                extensions: new Dictionary<string, object?> { ["code"] = "department_duplicate" });
        }

        var created = await departmentRepository.CreateDepartmentAsync(
            normalizedCode,
            request.Name.Trim(),
            request.Description?.Trim(),
            cancellationToken);

        if (created is null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Tạo phòng ban không thành công.",
                extensions: new Dictionary<string, object?> { ["code"] = "department_creation_failed" });
        }

        var dto = ToDto(created);
        return CreatedAtAction(nameof(GetDepartmentById), new { id = dto.Id }, dto);
    }

    /// <summary>
    /// Cập nhật thông tin phòng ban (chỉ Quản trị viên - Admin)
    /// </summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DepartmentDto>> UpdateDepartment(
        long id,
        [FromBody] UpdateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await departmentRepository.GetDepartmentByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy phòng ban có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "department_not_found" });
        }

        var updated = await departmentRepository.UpdateDepartmentAsync(
            id,
            request.Name.Trim(),
            request.Description?.Trim(),
            request.Status?.Trim(),
            cancellationToken);

        if (updated is null)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Cập nhật phòng ban thất bại.",
                extensions: new Dictionary<string, object?> { ["code"] = "department_update_failed" });
        }

        return Ok(ToDto(updated));
    }

    /// <summary>
    /// Xóa phòng ban (chỉ Quản trị viên - Admin, chặn xóa nếu phòng ban đang có nhân viên)
    /// </summary>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteDepartment(long id, CancellationToken cancellationToken)
    {
        var existing = await departmentRepository.GetDepartmentByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy phòng ban có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "department_not_found" });
        }

        if (existing.EmployeeCount > 0)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"Không thể xóa phòng ban '{existing.Name}' vì đang có {existing.EmployeeCount} nhân viên trực thuộc. Hãy điều chuyển nhân viên sang phòng ban khác trước.",
                extensions: new Dictionary<string, object?> { ["code"] = "department_in_use" });
        }

        var deleted = await departmentRepository.DeleteDepartmentAsync(id, cancellationToken);
        if (!deleted)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Xóa phòng ban không thành công.",
                extensions: new Dictionary<string, object?> { ["code"] = "department_delete_failed" });
        }

        return Ok(new { message = $"Đã xóa phòng ban '{existing.Name}' thành công." });
    }

    /// <summary>
    /// Lấy danh sách công cụ nghiệp vụ của một phòng ban
    /// </summary>
    [HttpGet("{id:long}/tools")]
    [Authorize]
    [ProducesResponseType<List<DepartmentToolDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<DepartmentToolDto>>> GetDepartmentTools(long id, CancellationToken cancellationToken)
    {
        var dept = await departmentRepository.GetDepartmentByIdAsync(id, cancellationToken);
        if (dept is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy phòng ban có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "department_not_found" });
        }

        var tools = await departmentRepository.GetDepartmentToolsAsync(id, cancellationToken);
        return Ok(tools.Select(ToToolDto).ToList());
    }

    /// <summary>
    /// Thêm công cụ nghiệp vụ mới cho phòng ban (chỉ Quản trị viên - Admin)
    /// </summary>
    [HttpPost("{id:long}/tools")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<DepartmentToolDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartmentToolDto>> CreateDepartmentTool(
        long id,
        [FromBody] CreateDepartmentToolRequest request,
        CancellationToken cancellationToken)
    {
        var dept = await departmentRepository.GetDepartmentByIdAsync(id, cancellationToken);
        if (dept is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy phòng ban có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "department_not_found" });
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var existing = await departmentRepository.GetToolByCodeAsync(normalizedCode, cancellationToken);
        if (existing is not null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"Mã công cụ '{normalizedCode}' đã tồn tại trong hệ thống.",
                extensions: new Dictionary<string, object?> { ["code"] = "tool_duplicate" });
        }

        var created = await departmentRepository.CreateToolAsync(
            id,
            normalizedCode,
            request.Name.Trim(),
            request.Icon?.Trim(),
            request.RoutePath?.Trim(),
            request.DisplayOrder,
            cancellationToken);

        if (created is null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Tạo công cụ không thành công.",
                extensions: new Dictionary<string, object?> { ["code"] = "tool_creation_failed" });
        }

        return CreatedAtAction(nameof(GetDepartmentTools), new { id }, ToToolDto(created));
    }

    /// <summary>
    /// Cập nhật công cụ nghiệp vụ (chỉ Quản trị viên - Admin)
    /// </summary>
    [HttpPut("tools/{toolId:long}")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<DepartmentToolDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DepartmentToolDto>> UpdateDepartmentTool(
        long toolId,
        [FromBody] UpdateDepartmentToolRequest request,
        CancellationToken cancellationToken)
    {
        var tool = await departmentRepository.GetToolByIdAsync(toolId, cancellationToken);
        if (tool is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy công cụ có ID {toolId}.",
                extensions: new Dictionary<string, object?> { ["code"] = "tool_not_found" });
        }

        var updated = await departmentRepository.UpdateToolAsync(
            toolId,
            request.Name.Trim(),
            request.Icon?.Trim(),
            request.RoutePath?.Trim(),
            request.DisplayOrder,
            request.Status?.Trim(),
            cancellationToken);

        if (updated is null)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Cập nhật công cụ thất bại.",
                extensions: new Dictionary<string, object?> { ["code"] = "tool_update_failed" });
        }

        return Ok(ToToolDto(updated));
    }

    /// <summary>
    /// Xóa công cụ nghiệp vụ khỏi phòng ban (chỉ Quản trị viên - Admin)
    /// </summary>
    [HttpDelete("tools/{toolId:long}")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDepartmentTool(long toolId, CancellationToken cancellationToken)
    {
        var tool = await departmentRepository.GetToolByIdAsync(toolId, cancellationToken);
        if (tool is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy công cụ có ID {toolId}.",
                extensions: new Dictionary<string, object?> { ["code"] = "tool_not_found" });
        }

        var deleted = await departmentRepository.DeleteToolAsync(toolId, cancellationToken);
        if (!deleted)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Xóa công cụ không thành công.",
                extensions: new Dictionary<string, object?> { ["code"] = "tool_delete_failed" });
        }

        return Ok(new { message = $"Đã xóa công cụ '{tool.Name}' ({tool.Code}) thành công." });
    }

    /// <summary>
    /// Điều chuyển / gán nhân viên vào phòng ban (chỉ Quản trị viên - Admin)
    /// </summary>
    [HttpPut("employees/{employeeId:long}")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignEmployeeToDepartment(
        long employeeId,
        [FromBody] AssignEmployeeDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DepartmentId.HasValue)
        {
            var dept = await departmentRepository.GetDepartmentByIdAsync(request.DepartmentId.Value, cancellationToken);
            if (dept is null)
            {
                return Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: $"Không tìm thấy phòng ban có ID {request.DepartmentId.Value}.",
                    extensions: new Dictionary<string, object?> { ["code"] = "department_not_found" });
            }
        }

        var assigned = await departmentRepository.AssignEmployeeToDepartmentAsync(employeeId, request.DepartmentId, cancellationToken);
        if (!assigned)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy nhân viên có ID {employeeId}.",
                extensions: new Dictionary<string, object?> { ["code"] = "employee_not_found" });
        }

        return Ok(new { message = "Cập nhật phòng ban cho nhân viên thành công." });
    }

    /// <summary>
    /// Lấy danh sách các công cụ nghiệp vụ của nhân viên đang đăng nhập để hiển thị Menu App/Web
    /// </summary>
    [HttpGet("my-tools")]
    [Authorize]
    [ProducesResponseType<MyDepartmentToolsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MyDepartmentToolsResponse>> GetMyTools(CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!long.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var userTools = await departmentRepository.GetToolsByUserIdAsync(userId, cancellationToken);
        if (userTools is null)
        {
            return Unauthorized();
        }

        return Ok(new MyDepartmentToolsResponse(
            userTools.UserId,
            userTools.DisplayName,
            userTools.DepartmentId,
            userTools.DepartmentName,
            userTools.Tools.Select(ToToolDto).ToList()));
    }

    private static DepartmentDto ToDto(DepartmentItem item) => new(
        item.Id,
        item.Code,
        item.Name,
        item.Description,
        item.Status,
        item.EmployeeCount,
        item.ToolCount,
        item.CreatedAtUtc,
        item.UpdatedAtUtc);

    private static DepartmentDetailDto ToDetailDto(DepartmentDetailItem item) => new(
        item.Id,
        item.Code,
        item.Name,
        item.Description,
        item.Status,
        item.EmployeeCount,
        item.Tools.Select(ToToolDto).ToList(),
        item.CreatedAtUtc,
        item.UpdatedAtUtc);

    private static DepartmentToolDto ToToolDto(DepartmentToolItem tool) => new(
        tool.Id,
        tool.DepartmentId,
        tool.Code,
        tool.Name,
        tool.Icon,
        tool.RoutePath,
        tool.DisplayOrder,
        tool.Status,
        tool.CreatedAtUtc,
        tool.UpdatedAtUtc);
}
