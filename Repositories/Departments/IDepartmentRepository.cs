namespace manage365.Repositories.Departments;

public sealed record DepartmentItem(
    long Id,
    string Code,
    string Name,
    string? Description,
    string Status,
    int EmployeeCount,
    int ToolCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record DepartmentToolItem(
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

public sealed record DepartmentDetailItem(
    long Id,
    string Code,
    string Name,
    string? Description,
    string Status,
    int EmployeeCount,
    IReadOnlyList<DepartmentToolItem> Tools,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record UserToolsItem(
    long UserId,
    string DisplayName,
    long? DepartmentId,
    string? DepartmentName,
    IReadOnlyList<DepartmentToolItem> Tools);

public interface IDepartmentRepository
{
    Task EnsureTablesAndSeedAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DepartmentItem>> GetAllDepartmentsAsync(CancellationToken cancellationToken = default);
    Task<DepartmentDetailItem?> GetDepartmentByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<DepartmentItem?> GetDepartmentByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<DepartmentItem?> CreateDepartmentAsync(string code, string name, string? description, CancellationToken cancellationToken = default);
    Task<DepartmentItem?> UpdateDepartmentAsync(long id, string name, string? description, string? status, CancellationToken cancellationToken = default);
    Task<bool> DeleteDepartmentAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DepartmentToolItem>> GetDepartmentToolsAsync(long departmentId, CancellationToken cancellationToken = default);
    Task<DepartmentToolItem?> GetToolByIdAsync(long toolId, CancellationToken cancellationToken = default);
    Task<DepartmentToolItem?> GetToolByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<DepartmentToolItem?> CreateToolAsync(long departmentId, string code, string name, string? icon, string? routePath, int displayOrder, CancellationToken cancellationToken = default);
    Task<DepartmentToolItem?> UpdateToolAsync(long toolId, string name, string? icon, string? routePath, int displayOrder, string? status, CancellationToken cancellationToken = default);
    Task<bool> DeleteToolAsync(long toolId, CancellationToken cancellationToken = default);

    Task<bool> AssignEmployeeToDepartmentAsync(long employeeId, long? departmentId, CancellationToken cancellationToken = default);
    Task<UserToolsItem?> GetToolsByUserIdAsync(long userId, CancellationToken cancellationToken = default);
}
