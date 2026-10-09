using manage365.Repositories.Roles;
using manage365.Routes.API.Auth;
using manage365.Routes.API.Roles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace manage365.Tests;

public sealed class RolesControllerTests
{
    private readonly FakeRoleRepository _roleRepo;
    private readonly RolesController _controller;

    public RolesControllerTests()
    {
        _roleRepo = new FakeRoleRepository();
        _controller = new RolesController(_roleRepo)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Fact]
    public async Task GetRoles_ReturnsAllRoles_WithSuccess()
    {
        var result = await _controller.GetRoles(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var roles = Assert.IsAssignableFrom<List<RoleDto>>(okResult.Value);
        Assert.Equal(3, roles.Count);
        Assert.Contains(roles, r => r.Name == AppRoles.Admin);
        Assert.Contains(roles, r => r.Name == AppRoles.Employee);
    }

    [Fact]
    public async Task GetRoleById_ReturnsOk_WhenRoleExists()
    {
        var result = await _controller.GetRoleById(1, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var role = Assert.IsType<RoleDto>(okResult.Value);
        Assert.Equal("Admin", role.Name);
    }

    [Fact]
    public async Task GetRoleById_ReturnsNotFound_WhenRoleDoesNotExist()
    {
        var result = await _controller.GetRoleById(999, CancellationToken.None);

        var notFound = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task CreateRole_ReturnsCreated_WhenValid()
    {
        var request = new CreateRoleRequest
        {
            Name = "ShiftLeader",
            Description = "Trưởng ca cửa hàng"
        };

        var result = await _controller.CreateRole(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<RoleDto>(createdResult.Value);
        Assert.Equal("ShiftLeader", dto.Name);
        Assert.Equal("Trưởng ca cửa hàng", dto.Description);
    }

    [Fact]
    public async Task CreateRole_ReturnsConflict_WhenRoleAlreadyExists()
    {
        var request = new CreateRoleRequest
        {
            Name = "Admin",
            Description = "Duplicate admin"
        };

        var result = await _controller.CreateRole(request, CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task UpdateRole_ReturnsBadRequest_WhenRenamingSystemRole()
    {
        var request = new UpdateRoleRequest
        {
            Name = "SuperAdmin",
            Description = "Trying to rename built-in Admin"
        };

        var result = await _controller.UpdateRole(1, request, CancellationToken.None);

        var badRequest = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task UpdateRole_ReturnsOk_WhenUpdatingCustomRole()
    {
        // Add custom role
        await _roleRepo.CreateRoleAsync("Cashier", "Thu ngân quầy");
        var custom = await _roleRepo.GetRoleByNameAsync("Cashier");
        Assert.NotNull(custom);

        var request = new UpdateRoleRequest
        {
            Name = "SeniorCashier",
            Description = "Thu ngân chính"
        };

        var result = await _controller.UpdateRole(custom.Id, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<RoleDto>(okResult.Value);
        Assert.Equal("SeniorCashier", dto.Name);
        Assert.Equal("Thu ngân chính", dto.Description);
    }

    [Fact]
    public async Task DeleteRole_ReturnsBadRequest_WhenDeletingSystemRole()
    {
        var result = await _controller.DeleteRole(1, CancellationToken.None); // Admin

        var badRequest = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task DeleteRole_ReturnsConflict_WhenRoleInUse()
    {
        // Custom role with user count > 0
        var role = await _roleRepo.CreateRoleAsync("Lead", "Lead team");
        Assert.NotNull(role);
        _roleRepo.SetUserCount(role.Id, 2);

        var result = await _controller.DeleteRole(role.Id, CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task DeleteRole_ReturnsOk_WhenSuccessfullyDeleted()
    {
        var role = await _roleRepo.CreateRoleAsync("Trainee", "Nhân viên thực tập");
        Assert.NotNull(role);

        var result = await _controller.DeleteRole(role.Id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    private sealed class FakeRoleRepository : IRoleRepository
    {
        private readonly List<RoleItem> _roles =
        [
            new(1, AppRoles.Admin, "System Administrator", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(2, AppRoles.Manager, "Store Manager", 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(3, AppRoles.Employee, "Store Employee", 5, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        ];

        private readonly Dictionary<long, int> _userCounts = new()
        {
            [1] = 1,
            [2] = 2,
            [3] = 5
        };

        public void SetUserCount(long roleId, int count) => _userCounts[roleId] = count;

        public Task<IReadOnlyList<RoleItem>> GetAllRolesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RoleItem>>(_roles.ToList());

        public Task<RoleItem?> GetRoleByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(_roles.FirstOrDefault(r => r.Id == id));

        public Task<RoleItem?> GetRoleByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(_roles.FirstOrDefault(r => r.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)));

        public Task<RoleItem?> CreateRoleAsync(string name, string? description, CancellationToken cancellationToken = default)
        {
            if (_roles.Any(r => r.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult<RoleItem?>(null);
            }

            var item = new RoleItem(_roles.Count + 1, name.Trim(), description, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            _roles.Add(item);
            _userCounts[item.Id] = 0;
            return Task.FromResult<RoleItem?>(item);
        }

        public Task<RoleItem?> UpdateRoleAsync(long id, string name, string? description, CancellationToken cancellationToken = default)
        {
            var idx = _roles.FindIndex(r => r.Id == id);
            if (idx == -1) return Task.FromResult<RoleItem?>(null);

            var existing = _roles[idx];
            var updated = existing with { Name = name.Trim(), Description = description, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _roles[idx] = updated;
            return Task.FromResult<RoleItem?>(updated);
        }

        public Task<bool> DeleteRoleAsync(long id, CancellationToken cancellationToken = default)
        {
            var removed = _roles.RemoveAll(r => r.Id == id) > 0;
            _userCounts.Remove(id);
            return Task.FromResult(removed);
        }

        public Task<int> GetUserCountForRoleAsync(long roleId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_userCounts.TryGetValue(roleId, out var count) ? count : 0);
        }
    }
}
