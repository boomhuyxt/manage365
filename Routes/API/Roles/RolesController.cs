using manage365.Repositories.Roles;
using manage365.Routes.API.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace manage365.Routes.API.Roles;

[ApiController]
[Tags("Roles")]
[Route("api/roles")]
public sealed class RolesController(IRoleRepository roleRepository) : ControllerBase
{
    private static readonly HashSet<string> SystemRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        AppRoles.Admin,
        AppRoles.Manager,
        AppRoles.Employee
    };

    /// <summary>
    /// Danh sách tất cả các vai trò trong hệ thống kèm số lượng nhân viên
    /// </summary>
    [HttpGet]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType<List<RoleDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<RoleDto>>> GetRoles(CancellationToken cancellationToken)
    {
        var roles = await roleRepository.GetAllRolesAsync(cancellationToken);
        var result = roles.Select(ToDto).ToList();
        return Ok(result);
    }

    /// <summary>
    /// Lấy thông tin chi tiết vai trò theo ID
    /// </summary>
    [HttpGet("{id:long}")]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType<RoleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoleDto>> GetRoleById(long id, CancellationToken cancellationToken)
    {
        var role = await roleRepository.GetRoleByIdAsync(id, cancellationToken);
        if (role is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy vai trò có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "role_not_found" });
        }

        return Ok(ToDto(role));
    }

    /// <summary>
    /// Tạo vai trò mới (chỉ Admin)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<RoleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoleDto>> CreateRole(
        [FromBody] CreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedName = request.Name.Trim();
        var existing = await roleRepository.GetRoleByNameAsync(normalizedName, cancellationToken);
        if (existing is not null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"Vai trò '{normalizedName}' đã tồn tại trong hệ thống.",
                extensions: new Dictionary<string, object?> { ["code"] = "role_duplicate" });
        }

        var created = await roleRepository.CreateRoleAsync(normalizedName, request.Description, cancellationToken);
        if (created is null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"Không thể tạo vai trò '{normalizedName}'.",
                extensions: new Dictionary<string, object?> { ["code"] = "role_creation_failed" });
        }

        var dto = ToDto(created);
        return CreatedAtAction(nameof(GetRoleById), new { id = dto.Id }, dto);
    }

    /// <summary>
    /// Cập nhật tên và mô tả vai trò (chỉ Admin)
    /// </summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<RoleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoleDto>> UpdateRole(
        long id,
        [FromBody] UpdateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await roleRepository.GetRoleByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy vai trò có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "role_not_found" });
        }

        var normalizedName = request.Name.Trim();

        // Không cho phép đổi tên vai trò hệ thống mặc định
        if (SystemRoles.Contains(existing.Name) &&
            !existing.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"Không thể đổi tên vai trò hệ thống mặc định '{existing.Name}'.",
                extensions: new Dictionary<string, object?> { ["code"] = "system_role_cannot_rename" });
        }

        // Kiểm tra trùng tên với vai trò khác
        var duplicate = await roleRepository.GetRoleByNameAsync(normalizedName, cancellationToken);
        if (duplicate is not null && duplicate.Id != id)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"Tên vai trò '{normalizedName}' đã được sử dụng.",
                extensions: new Dictionary<string, object?> { ["code"] = "role_name_in_use" });
        }

        var updated = await roleRepository.UpdateRoleAsync(id, normalizedName, request.Description, cancellationToken);
        if (updated is null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Cập nhật vai trò thất bại.",
                extensions: new Dictionary<string, object?> { ["code"] = "role_update_failed" });
        }

        return Ok(ToDto(updated));
    }

    /// <summary>
    /// Xóa vai trò (chỉ Admin, không xóa vai trò hệ thống và vai trò đang có nhân viên)
    /// </summary>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteRole(long id, CancellationToken cancellationToken)
    {
        var role = await roleRepository.GetRoleByIdAsync(id, cancellationToken);
        if (role is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy vai trò có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "role_not_found" });
        }

        if (SystemRoles.Contains(role.Name))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"Không được phép xóa vai trò hệ thống mặc định '{role.Name}'.",
                extensions: new Dictionary<string, object?> { ["code"] = "system_role_cannot_delete" });
        }

        var userCount = await roleRepository.GetUserCountForRoleAsync(id, cancellationToken);
        if (userCount > 0)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"Không thể xóa vai trò '{role.Name}' vì đang có {userCount} nhân viên đảm nhiệm.",
                extensions: new Dictionary<string, object?> { ["code"] = "role_in_use" });
        }

        var deleted = await roleRepository.DeleteRoleAsync(id, cancellationToken);
        if (!deleted)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Xóa vai trò không thành công.",
                extensions: new Dictionary<string, object?> { ["code"] = "role_delete_failed" });
        }

        return Ok(new { message = $"Đã xóa vai trò '{role.Name}' thành công." });
    }

    private static RoleDto ToDto(RoleItem role) => new(
        role.Id,
        role.Name,
        role.Description,
        role.UserCount,
        role.CreatedAtUtc,
        role.UpdatedAtUtc);
}
