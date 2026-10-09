using System.IdentityModel.Tokens.Jwt;
using manage365.Repositories.Auth;
using manage365.Repositories.Roles;
using manage365.Repositories.Users;
using manage365.Routes.API.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace manage365.Routes.API.Users;

[ApiController]
[Tags("Users", "Employees")]
[Route("api/users")]
[Route("api/employees")]
public sealed class UsersController(
    IUserManagementRepository userRepository,
    IRoleRepository roleRepository,
    IPasswordHasher passwordHasher) : ControllerBase
{
    /// <summary>
    /// Danh sách nhân viên phân trang kèm bộ lọc tìm kiếm (Quản lý và Quản trị viên)
    /// </summary>
    [HttpGet]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType<PagedUsersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedUsersResponse>> GetUsers(
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] string? employeeType = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var filter = new UserFilterParams(search, role, employeeType, status, page, pageSize);
        var result = await userRepository.GetUsersAsync(filter, cancellationToken);

        var dtos = result.Items.Select(u => new UserListItemDto(
            u.Id,
            u.Email,
            u.DisplayName,
            u.Role,
            u.EmployeeType,
            u.HourlySalary,
            u.Status,
            u.CreatedAtUtc,
            u.UpdatedAtUtc)).ToList();

        return Ok(new PagedUsersResponse(dtos, result.Page, result.PageSize, result.TotalCount, result.TotalPages));
    }

    /// <summary>
    /// Lấy chi tiết thông tin nhân viên theo ID
    /// </summary>
    [HttpGet("{id:long}")]
    [Authorize]
    [ProducesResponseType<UserDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDetailDto>> GetUserById(long id, CancellationToken cancellationToken)
    {
        if (!IsAdminOrManager() && GetCurrentUserId() != id)
        {
            return Forbid();
        }

        var user = await userRepository.GetUserByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy nhân viên có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "user_not_found" });
        }

        return Ok(ToDetailDto(user));
    }

    /// <summary>
    /// Tạo nhân viên mới (chỉ Quản trị viên)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<UserDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDetailDto>> CreateUser(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var existing = await userRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken);
        if (existing is not null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"Email '{normalizedEmail}' đã được đăng ký trong hệ thống.",
                extensions: new Dictionary<string, object?> { ["code"] = "email_already_exists" });
        }

        // Kiểm tra vai trò hợp lệ nếu có chỉ định
        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            var role = await roleRepository.GetRoleByNameAsync(request.Role.Trim(), cancellationToken);
            if (role is null)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: $"Vai trò '{request.Role}' không tồn tại trong hệ thống.",
                    extensions: new Dictionary<string, object?> { ["code"] = "role_not_found" });
            }
        }

        var passwordHash = passwordHasher.Hash(request.Password);
        var createData = new CreateUserData(
            normalizedEmail,
            request.DisplayName.Trim(),
            passwordHash,
            string.IsNullOrWhiteSpace(request.Role) ? AppRoles.Employee : request.Role.Trim(),
            request.EmployeeType.Trim(),
            request.HourlySalary,
            request.TaxCode?.Trim(),
            request.DependentsCount,
            string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status.Trim());

        var created = await userRepository.CreateUserAsync(createData, cancellationToken);
        if (created is null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Không thể tạo nhân viên mới do xung đột dữ liệu.",
                extensions: new Dictionary<string, object?> { ["code"] = "user_creation_conflict" });
        }

        var dto = ToDetailDto(created);
        return CreatedAtAction(nameof(GetUserById), new { id = dto.Id }, dto);
    }

    /// <summary>
    /// Cập nhật thông tin nhân viên (chỉ Quản trị viên)
    /// </summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<UserDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDetailDto>> UpdateUser(
        long id,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await userRepository.GetUserByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy nhân viên có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "user_not_found" });
        }

        var updateData = new UpdateUserData(
            request.DisplayName.Trim(),
            request.EmployeeType.Trim(),
            request.HourlySalary,
            request.TaxCode?.Trim(),
            request.DependentsCount,
            request.Status?.Trim() ?? existing.Status ?? "Active");

        var updated = await userRepository.UpdateUserAsync(id, updateData, cancellationToken);
        if (updated is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Cập nhật nhân viên không thành công.",
                extensions: new Dictionary<string, object?> { ["code"] = "user_update_failed" });
        }

        return Ok(ToDetailDto(updated));
    }

    /// <summary>
    /// Phân quyền / gán vai trò cho nhân viên (chỉ Quản trị viên)
    /// </summary>
    [HttpPut("{id:long}/role")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<UserDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDetailDto>> AssignRole(
        long id,
        [FromBody] AssignRoleRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetUserByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy nhân viên có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "user_not_found" });
        }

        var role = await roleRepository.GetRoleByNameAsync(request.Role.Trim(), cancellationToken);
        if (role is null)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"Vai trò '{request.Role}' không tồn tại.",
                extensions: new Dictionary<string, object?> { ["code"] = "role_not_found" });
        }

        var updated = await userRepository.UpdateRoleAsync(id, role.Name, cancellationToken);
        if (updated is null)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Gán vai trò thất bại.",
                extensions: new Dictionary<string, object?> { ["code"] = "role_assignment_failed" });
        }

        return Ok(ToDetailDto(updated));
    }

    /// <summary>
    /// Cập nhật trạng thái nhân viên (chỉ Quản trị viên)
    /// </summary>
    [HttpPut("{id:long}/status")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType<UserDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDetailDto>> UpdateStatus(
        long id,
        [FromBody] UpdateUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetUserByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy nhân viên có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "user_not_found" });
        }

        var updated = await userRepository.UpdateStatusAsync(id, request.Status.Trim(), cancellationToken);
        if (updated is null)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Cập nhật trạng thái thất bại.",
                extensions: new Dictionary<string, object?> { ["code"] = "status_update_failed" });
        }

        return Ok(ToDetailDto(updated));
    }

    /// <summary>
    /// Xóa hoặc vô hiệu hóa nhân viên (chỉ Quản trị viên)
    /// </summary>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUser(long id, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() == id)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Quản trị viên không thể tự xóa tài khoản của chính mình.",
                extensions: new Dictionary<string, object?> { ["code"] = "cannot_delete_self" });
        }

        var user = await userRepository.GetUserByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy nhân viên có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "user_not_found" });
        }

        var deleted = await userRepository.DeleteUserAsync(id, cancellationToken);
        if (!deleted)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Xóa nhân viên không thành công.",
                extensions: new Dictionary<string, object?> { ["code"] = "user_delete_failed" });
        }

        return Ok(new { message = $"Đã xử lý xóa/vô hiệu hóa nhân viên '{user.DisplayName}'." });
    }

    private static UserDetailDto ToDetailDto(UserManagementDetail user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role,
        user.EmployeeType,
        user.HourlySalary,
        user.TaxCode,
        user.DependentsCount,
        user.Status,
        user.RatingScore,
        user.CreatedAtUtc,
        user.UpdatedAtUtc);

    private bool IsAdminOrManager()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        return role is AppRoles.Admin or AppRoles.Manager;
    }

    private long? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return long.TryParse(subject, out var id) ? id : null;
    }
}
