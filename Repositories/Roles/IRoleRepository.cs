namespace manage365.Repositories.Roles;

public sealed record RoleItem(
    long Id,
    string Name,
    string? Description,
    int UserCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public interface IRoleRepository
{
    Task<IReadOnlyList<RoleItem>> GetAllRolesAsync(CancellationToken cancellationToken = default);
    Task<RoleItem?> GetRoleByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<RoleItem?> GetRoleByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<RoleItem?> CreateRoleAsync(string name, string? description, CancellationToken cancellationToken = default);
    Task<RoleItem?> UpdateRoleAsync(long id, string name, string? description, CancellationToken cancellationToken = default);
    Task<bool> DeleteRoleAsync(long id, CancellationToken cancellationToken = default);
    Task<int> GetUserCountForRoleAsync(long roleId, CancellationToken cancellationToken = default);
}
