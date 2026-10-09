namespace manage365.Repositories.Permissions;

public sealed record PermissionItem(
    long Id,
    string Code,
    string Name,
    string Group,
    string? Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record RolePermissionsItem(
    long RoleId,
    string RoleName,
    IReadOnlyList<PermissionItem> Permissions);

public interface IPermissionRepository
{
    Task EnsureTablesAndSeedAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PermissionItem>> GetAllPermissionsAsync(string? group = null, CancellationToken cancellationToken = default);
    Task<PermissionItem?> GetPermissionByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<PermissionItem?> GetPermissionByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<PermissionItem?> CreatePermissionAsync(string code, string name, string group, string? description, CancellationToken cancellationToken = default);
    Task<PermissionItem?> UpdatePermissionAsync(long id, string name, string group, string? description, CancellationToken cancellationToken = default);
    Task<bool> DeletePermissionAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PermissionItem>> GetPermissionsByRoleIdAsync(long roleId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PermissionItem>> GetPermissionsByUserIdAsync(long userId, CancellationToken cancellationToken = default);
    Task<bool> AssignPermissionsToRoleAsync(long roleId, IEnumerable<long> permissionIds, CancellationToken cancellationToken = default);
}
