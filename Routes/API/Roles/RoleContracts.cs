using System.ComponentModel.DataAnnotations;

namespace manage365.Routes.API.Roles;

public sealed record RoleDto(
    long Id,
    string Name,
    string? Description,
    int UserCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed class CreateRoleRequest
{
    [Required(ErrorMessage = "Tên vai trò không được để trống.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Tên vai trò phải từ 2 đến 50 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(255, ErrorMessage = "Mô tả không được vượt quá 255 ký tự.")]
    public string? Description { get; set; }
}

public sealed class UpdateRoleRequest
{
    [Required(ErrorMessage = "Tên vai trò không được để trống.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Tên vai trò phải từ 2 đến 50 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(255, ErrorMessage = "Mô tả không được vượt quá 255 ký tự.")]
    public string? Description { get; set; }
}
