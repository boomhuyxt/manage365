using System.ComponentModel.DataAnnotations;

namespace manage365.Routes.API.Users;

public sealed record UserListItemDto(
    long Id,
    string Email,
    string DisplayName,
    string Role,
    string EmployeeType,
    decimal HourlySalary,
    string? Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record UserDetailDto(
    long Id,
    string Email,
    string DisplayName,
    string Role,
    string EmployeeType,
    decimal HourlySalary,
    string? TaxCode,
    int? DependentsCount,
    string? Status,
    decimal? RatingScore,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PagedUsersResponse(
    IReadOnlyList<UserListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed class CreateUserRequest
{
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(255, ErrorMessage = "Email tối đa 255 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu không được để trống.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải từ 6 đến 100 ký tự.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Họ tên không được để trống.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ tên phải từ 2 đến 100 ký tự.")]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "Vai trò tối đa 50 ký tự.")]
    public string Role { get; set; } = "Employee";

    [Required(ErrorMessage = "Loại nhân viên không được để trống.")]
    [StringLength(50, ErrorMessage = "Loại nhân viên tối đa 50 ký tự.")]
    public string EmployeeType { get; set; } = "FullTime";

    [Range(0, 1_000_000_000, ErrorMessage = "Lương cơ bản giờ phải lớn hơn hoặc bằng 0.")]
    public decimal HourlySalary { get; set; } = 0;

    [StringLength(50, ErrorMessage = "Mã số thuế tối đa 50 ký tự.")]
    public string? TaxCode { get; set; }

    [Range(0, 50, ErrorMessage = "Số người phụ thuộc phải từ 0 đến 50.")]
    public int? DependentsCount { get; set; }

    [StringLength(50, ErrorMessage = "Trạng thái tối đa 50 ký tự.")]
    public string? Status { get; set; } = "Active";
}

public sealed class UpdateUserRequest
{
    [Required(ErrorMessage = "Họ tên không được để trống.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ tên phải từ 2 đến 100 ký tự.")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Loại nhân viên không được để trống.")]
    [StringLength(50, ErrorMessage = "Loại nhân viên tối đa 50 ký tự.")]
    public string EmployeeType { get; set; } = "FullTime";

    [Range(0, 1_000_000_000, ErrorMessage = "Lương cơ bản giờ phải lớn hơn hoặc bằng 0.")]
    public decimal HourlySalary { get; set; } = 0;

    [StringLength(50, ErrorMessage = "Mã số thuế tối đa 50 ký tự.")]
    public string? TaxCode { get; set; }

    [Range(0, 50, ErrorMessage = "Số người phụ thuộc phải từ 0 đến 50.")]
    public int? DependentsCount { get; set; }

    [StringLength(50, ErrorMessage = "Trạng thái tối đa 50 ký tự.")]
    public string? Status { get; set; } = "Active";
}

public sealed class AssignRoleRequest
{
    [Required(ErrorMessage = "Tên vai trò không được để trống.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Tên vai trò phải từ 2 đến 50 ký tự.")]
    public string Role { get; set; } = string.Empty;
}

public sealed class UpdateUserStatusRequest
{
    [Required(ErrorMessage = "Trạng thái không được để trống.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Trạng thái phải từ 2 đến 50 ký tự.")]
    public string Status { get; set; } = "Active";
}
