using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using manage365.Repositories.Permissions;
using manage365.Repositories.Roles;
using manage365.Routes.API.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace manage365.Routes.API.Permissions;

[ApiController]
[Tags("Permissions")]
[Route("api/permissions")]
public sealed class PermissionsController(
    IPermissionRepository permissionRepository,
    IRoleRepository roleRepository) : ControllerBase
{
    private static readonly HashSet<string> SystemPermissionCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "USER_VIEW",
        "USER_MANAGE",
        "ROLE_VIEW",
        "ROLE_MANAGE",
        "ATTENDANCE_CHECKIN",
        "ATTENDANCE_VIEW",
        "ATTENDANCE_MANAGE",
        "REPORT_VIEW",
        "SYSTEM_CONFIG"
    };

    /// <summary>
    /// Danh sách tất cả các quyền trong hệ thống (có thể lọc theo nhóm quyền)
    /// </summary>
    [HttpGet]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType<List<PermissionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<PermissionDto>>> GetPermissions(
        [FromQuery] string? group = null,
        CancellationToken cancellationToken = default)
    {
        var items = await permissionRepository.GetAllPermissionsAsync(group, cancellationToken);
        var result = items.Select(ToDto).ToList();
        return Ok(result);
    }

    /// <summary>
    /// Lấy chi tiết một quyền theo ID
    /// </summary>
    [HttpGet("{id:long}")]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType<PermissionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PermissionDto>> GetPermissionById(long id, CancellationToken cancellationToken)
    {
        var permission = await permissionRepository.GetPermissionByIdAsync(id, cancellationToken);
        if (permission is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy quyền có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "permission_not_found" });
        }

        return Ok(ToDto(permission));
    }

    /// <summary>
    /// Tạo quyền mới (chỉ Quản trị viên - Admin)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<PermissionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PermissionDto>> CreatePermission(
        [FromBody] CreatePermissionRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var existing = await permissionRepository.GetPermissionByCodeAsync(normalizedCode, cancellationToken);
        if (existing is not null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"Mã quyền '{normalizedCode}' đã tồn tại trong hệ thống.",
                extensions: new Dictionary<string, object?> { ["code"] = "permission_duplicate" });
        }

        var created = await permissionRepository.CreatePermissionAsync(
            normalizedCode,
            request.Name.Trim(),
            request.Group.Trim(),
            request.Description?.Trim(),
            cancellationToken);

        if (created is null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Tạo quyền không thành công.",
                extensions: new Dictionary<string, object?> { ["code"] = "permission_creation_failed" });
        }

        var dto = ToDto(created);
        return CreatedAtAction(nameof(GetPermissionById), new { id = dto.Id }, dto);
    }

    /// <summary>
    /// Cập nhật tên, nhóm và mô tả của quyền (chỉ Quản trị viên - Admin)
    /// </summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<PermissionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PermissionDto>> UpdatePermission(
        long id,
        [FromBody] UpdatePermissionRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await permissionRepository.GetPermissionByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy quyền có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "permission_not_found" });
        }

        var updated = await permissionRepository.UpdatePermissionAsync(
            id,
            request.Name.Trim(),
            request.Group.Trim(),
            request.Description?.Trim(),
            cancellationToken);

        if (updated is null)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Cập nhật quyền thất bại.",
                extensions: new Dictionary<string, object?> { ["code"] = "permission_update_failed" });
        }

        return Ok(ToDto(updated));
    }

    /// <summary>
    /// Xóa quyền (chỉ Quản trị viên - Admin, không xóa quyền hệ thống mặc định)
    /// </summary>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePermission(long id, CancellationToken cancellationToken)
    {
        var permission = await permissionRepository.GetPermissionByIdAsync(id, cancellationToken);
        if (permission is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy quyền có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "permission_not_found" });
        }

        if (SystemPermissionCodes.Contains(permission.Code))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"Không được phép xóa quyền hệ thống cốt lõi '{permission.Code}'.",
                extensions: new Dictionary<string, object?> { ["code"] = "system_permission_cannot_delete" });
        }

        var deleted = await permissionRepository.DeletePermissionAsync(id, cancellationToken);
        if (!deleted)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Xóa quyền không thành công.",
                extensions: new Dictionary<string, object?> { ["code"] = "permission_delete_failed" });
        }

        return Ok(new { message = $"Đã xóa quyền '{permission.Name}' ({permission.Code}) thành công." });
    }

    /// <summary>
    /// Lấy danh sách các quyền được gán cho một vai trò
    /// </summary>
    [HttpGet("roles/{roleId:long}")]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType<RolePermissionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RolePermissionsDto>> GetPermissionsByRole(
        long roleId,
        CancellationToken cancellationToken)
    {
        var role = await roleRepository.GetRoleByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy vai trò có ID {roleId}.",
                extensions: new Dictionary<string, object?> { ["code"] = "role_not_found" });
        }

        var permissions = await permissionRepository.GetPermissionsByRoleIdAsync(roleId, cancellationToken);
        return Ok(new RolePermissionsDto(role.Id, role.Name, permissions.Select(ToDto).ToList()));
    }

    /// <summary>
    /// Gán hoặc cập nhật danh sách quyền cho vai trò (chỉ Quản trị viên - Admin)
    /// </summary>
    [HttpPost("roles/{roleId:long}")]
    [HttpPut("roles/{roleId:long}")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<RolePermissionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RolePermissionsDto>> AssignPermissionsToRole(
        long roleId,
        [FromBody] AssignRolePermissionsRequest request,
        CancellationToken cancellationToken)
    {
        var role = await roleRepository.GetRoleByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy vai trò có ID {roleId}.",
                extensions: new Dictionary<string, object?> { ["code"] = "role_not_found" });
        }

        // Kiểm tra danh sách permission ID hợp lệ
        var allPermissions = await permissionRepository.GetAllPermissionsAsync(null, cancellationToken);
        var validIds = allPermissions.Select(p => p.Id).ToHashSet();
        var invalid = request.PermissionIds.Where(id => !validIds.Contains(id)).ToList();
        if (invalid.Count > 0)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"Các ID quyền sau không tồn tại trong hệ thống: {string.Join(", ", invalid)}.",
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_permission_ids" });
        }

        await permissionRepository.AssignPermissionsToRoleAsync(roleId, request.PermissionIds.Distinct(), cancellationToken);
        var updatedPermissions = await permissionRepository.GetPermissionsByRoleIdAsync(roleId, cancellationToken);

        return Ok(new RolePermissionsDto(role.Id, role.Name, updatedPermissions.Select(ToDto).ToList()));
    }

    /// <summary>
    /// Lấy danh sách các quyền của tài khoản hiện tại đang đăng nhập
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<MyPermissionsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MyPermissionsResponse>> GetMyPermissions(CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var email = User.FindFirst(JwtRegisteredClaimNames.Email)?.Value ?? string.Empty;
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        if (!long.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var permissions = await permissionRepository.GetPermissionsByUserIdAsync(userId, cancellationToken);
        var codes = permissions.Select(p => p.Code).ToList();
        var dtos = permissions.Select(ToDto).ToList();

        return Ok(new MyPermissionsResponse(userId, email, role, codes, dtos));
    }

    private static PermissionDto ToDto(PermissionItem item) => new(
        item.Id,
        item.Code,
        item.Name,
        item.Group,
        item.Description,
        item.CreatedAtUtc,
        item.UpdatedAtUtc);
}
