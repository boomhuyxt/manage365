using System.ComponentModel.DataAnnotations;

namespace manage365.Routes.API.Departments;

public sealed record DepartmentDto(
    long Id,
    string Code,
    string Name,
    string? Description,
    string Status,
    int EmployeeCount,
    int ToolCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record DepartmentToolDto(
    long Id,
    long DepartmentId,
    string Code,
    string Name,
    string? Icon,
    string? RoutePath,
    int DisplayOrder,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record DepartmentDetailDto(
    long Id,
    string Code,
    string Name,
    string? Description,
    string Status,
    int EmployeeCount,
    IReadOnlyList<DepartmentToolDto> Tools,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record MyDepartmentToolsResponse(
    long UserId,
    string DisplayName,
    long? DepartmentId,
    string? DepartmentName,
    IReadOnlyList<DepartmentToolDto> Tools);

public sealed class CreateDepartmentRequest
{
    [Required(ErrorMessage = "Mã phòng ban không được để trống.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Mã phòng ban phải từ 2 đến 50 ký tự.")]
    [RegularExpression(@"^[A-Z0-9_]+$", ErrorMessage = "Mã phòng ban chỉ được chứa chữ in hoa, chữ số và dấu gạch dưới (ví dụ: WAREHOUSE).")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên phòng ban không được để trống.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Tên phòng ban phải từ 2 đến 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Mô tả tối đa 500 ký tự.")]
    public string? Description { get; set; }
}

public sealed class UpdateDepartmentRequest
{
    [Required(ErrorMessage = "Tên phòng ban không được để trống.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Tên phòng ban phải từ 2 đến 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Mô tả tối đa 500 ký tự.")]
    public string? Description { get; set; }

    [StringLength(50, ErrorMessage = "Trạng thái tối đa 50 ký tự.")]
    public string? Status { get; set; } = "Active";
}

public sealed class CreateDepartmentToolRequest
{
    [Required(ErrorMessage = "Mã công cụ không được để trống.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Mã công cụ phải từ 2 đến 100 ký tự.")]
    [RegularExpression(@"^[A-Z0-9_]+$", ErrorMessage = "Mã công cụ chỉ được chứa chữ in hoa, chữ số và dấu gạch dưới (ví dụ: TOOL_STOCK_IN).")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên công cụ không được để trống.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Tên công cụ phải từ 2 đến 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Mã icon tối đa 100 ký tự.")]
    public string? Icon { get; set; }

    [StringLength(200, ErrorMessage = "Đường dẫn màn hình tối đa 200 ký tự.")]
    public string? RoutePath { get; set; }

    [Range(0, 1000, ErrorMessage = "Thứ tự hiển thị phải từ 0 đến 1000.")]
    public int DisplayOrder { get; set; } = 0;
}

public sealed class UpdateDepartmentToolRequest
{
    [Required(ErrorMessage = "Tên công cụ không được để trống.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Tên công cụ phải từ 2 đến 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Mã icon tối đa 100 ký tự.")]
    public string? Icon { get; set; }

    [StringLength(200, ErrorMessage = "Đường dẫn màn hình tối đa 200 ký tự.")]
    public string? RoutePath { get; set; }

    [Range(0, 1000, ErrorMessage = "Thứ tự hiển thị phải từ 0 đến 1000.")]
    public int DisplayOrder { get; set; } = 0;

    [StringLength(50, ErrorMessage = "Trạng thái tối đa 50 ký tự.")]
    public string? Status { get; set; } = "Active";
}

public sealed class AssignEmployeeDepartmentRequest
{
    public long? DepartmentId { get; set; }
}
