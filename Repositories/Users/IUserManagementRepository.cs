namespace manage365.Repositories.Users;

public sealed record UserManagementItem(
    long Id,
    string Email,
    string DisplayName,
    string Role,
    string EmployeeType,
    decimal HourlySalary,
    string? Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record UserManagementDetail(
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

public sealed record UserFilterParams(
    string? Search,
    string? Role,
    string? EmployeeType,
    string? Status,
    int Page,
    int PageSize);

public sealed record PagedUserResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record CreateUserData(
    string Email,
    string DisplayName,
    string PasswordHash,
    string Role,
    string EmployeeType,
    decimal HourlySalary,
    string? TaxCode,
    int? DependentsCount,
    string? Status);

public sealed record UpdateUserData(
    string DisplayName,
    string EmployeeType,
    decimal HourlySalary,
    string? TaxCode,
    int? DependentsCount,
    string? Status);

public interface IUserManagementRepository
{
    Task<PagedUserResult<UserManagementItem>> GetUsersAsync(UserFilterParams filter, CancellationToken cancellationToken = default);
    Task<UserManagementDetail?> GetUserByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<UserManagementDetail?> GetUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<UserManagementDetail?> CreateUserAsync(CreateUserData data, CancellationToken cancellationToken = default);
    Task<UserManagementDetail?> UpdateUserAsync(long id, UpdateUserData data, CancellationToken cancellationToken = default);
    Task<UserManagementDetail?> UpdateRoleAsync(long id, string roleName, CancellationToken cancellationToken = default);
    Task<UserManagementDetail?> UpdateStatusAsync(long id, string status, CancellationToken cancellationToken = default);
    Task<bool> DeleteUserAsync(long id, CancellationToken cancellationToken = default);
}
