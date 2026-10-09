using System.ComponentModel.DataAnnotations;

namespace manage365.Routes.API.Permissions;

public sealed record PermissionDto(
    long Id,
    string Code,
    string Name,
    string Group,
    string? Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record RolePermissionsDto(
    long RoleId,
    string RoleName,
    IReadOnlyList<PermissionDto> Permissions);

public sealed record MyPermissionsResponse(
    long UserId,
    string Email,
    string Role,
    IReadOnlyList<string> PermissionCodes,
    IReadOnlyList<PermissionDto> Details);

public sealed class CreatePermissionRequest
{
    [Required(ErrorMessage = "Mã quyền không được để trống.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Mã quyền phải từ 2 đến 100 ký tự.")]
    [RegularExpression(@"^[A-Z0-9_]+$", ErrorMessage = "Mã quyền chỉ được chứa chữ in hoa, chữ số và dấu gạch dưới (ví dụ: USER_VIEW).")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên quyền không được để trống.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Tên quyền phải từ 2 đến 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhóm quyền không được để trống.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Nhóm quyền phải từ 2 đến 100 ký tự.")]
    public string Group { get; set; } = "General";

    [StringLength(500, ErrorMessage = "Mô tả tối đa 500 ký tự.")]
    public string? Description { get; set; }
}

public sealed class UpdatePermissionRequest
{
    [Required(ErrorMessage = "Tên quyền không được để trống.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Tên quyền phải từ 2 đến 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhóm quyền không được để trống.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Nhóm quyền phải từ 2 đến 100 ký tự.")]
    public string Group { get; set; } = "General";

    [StringLength(500, ErrorMessage = "Mô tả tối đa 500 ký tự.")]
    public string? Description { get; set; }
}

public sealed class AssignRolePermissionsRequest
{
    [Required(ErrorMessage = "Danh sách ID quyền không được để trống.")]
    public List<long> PermissionIds { get; set; } = [];
}
