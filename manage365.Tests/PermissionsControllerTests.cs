using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using manage365.Repositories.Permissions;
using manage365.Repositories.Roles;
using manage365.Routes.API.Auth;
using manage365.Routes.API.Permissions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace manage365.Tests;

public sealed class PermissionsControllerTests
{
    private readonly FakePermissionRepository _permissionRepo;
    private readonly FakeRoleRepoForPermissions _roleRepo;
    private readonly PermissionsController _controller;

    public PermissionsControllerTests()
    {
        _permissionRepo = new FakePermissionRepository();
        _roleRepo = new FakeRoleRepoForPermissions();
        _controller = new PermissionsController(_permissionRepo, _roleRepo)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, "101"),
                        new Claim(JwtRegisteredClaimNames.Email, "manager@manage365.io.vn"),
                        new Claim(ClaimTypes.Role, AppRoles.Manager)
                    ], "TestAuth"))
                }
            }
        };
    }

    [Fact]
    public async Task GetPermissions_ReturnsAllPermissions_WhenNoFilter()
    {
        var result = await _controller.GetPermissions(null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<List<PermissionDto>>(okResult.Value);
        Assert.NotEmpty(dtos);
        Assert.Contains(dtos, p => p.Code == "USER_VIEW");
    }

    [Fact]
    public async Task GetPermissions_FiltersByGroup_WhenGroupSpecified()
    {
        var result = await _controller.GetPermissions("Attendance", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<List<PermissionDto>>(okResult.Value);
        Assert.All(dtos, p => Assert.Equal("Attendance", p.Group));
    }

    [Fact]
    public async Task GetPermissionById_ReturnsOk_WhenExists()
    {
        var result = await _controller.GetPermissionById(1, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<PermissionDto>(okResult.Value);
        Assert.Equal(1, dto.Id);
    }

    [Fact]
    public async Task GetPermissionById_ReturnsNotFound_WhenDoesNotExist()
    {
        var result = await _controller.GetPermissionById(999, CancellationToken.None);

        var notFound = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task CreatePermission_ReturnsCreated_WhenValid()
    {
        var request = new CreatePermissionRequest
        {
            Code = "INVENTORY_EXPORT",
            Name = "Xuất kho sản phẩm",
            Group = "Inventory",
            Description = "Cho phép nhân viên tạo phiếu xuất kho"
        };

        var result = await _controller.CreatePermission(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<PermissionDto>(created.Value);
        Assert.Equal("INVENTORY_EXPORT", dto.Code);
        Assert.Equal("Xuất kho sản phẩm", dto.Name);
    }

    [Fact]
    public async Task CreatePermission_ReturnsConflict_WhenCodeAlreadyExists()
    {
        var request = new CreatePermissionRequest
        {
            Code = "USER_VIEW",
            Name = "Duplicate View",
            Group = "Users"
        };

        var result = await _controller.CreatePermission(request, CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task UpdatePermission_ReturnsOk_WhenValid()
    {
        var request = new UpdatePermissionRequest
        {
            Name = "Xem hồ sơ nhân sự mở rộng",
            Group = "Users",
            Description = "Mô tả mới"
        };

        var result = await _controller.UpdatePermission(1, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<PermissionDto>(okResult.Value);
        Assert.Equal("Xem hồ sơ nhân sự mở rộng", dto.Name);
    }

    [Fact]
    public async Task DeletePermission_ReturnsBadRequest_WhenDeletingCoreSystemPermission()
    {
        var result = await _controller.DeletePermission(1, CancellationToken.None); // ID 1 is USER_VIEW

        var badRequest = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task DeletePermission_ReturnsOk_WhenDeletingCustomPermission()
    {
        // Add custom permission
        var custom = await _permissionRepo.CreatePermissionAsync("CUSTOM_PERM", "Quyền tùy chỉnh", "Custom", null);
        Assert.NotNull(custom);

        var result = await _controller.DeletePermission(custom.Id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetPermissionsByRole_ReturnsOk_WhenRoleExists()
    {
        var result = await _controller.GetPermissionsByRole(2, CancellationToken.None); // Manager

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<RolePermissionsDto>(okResult.Value);
        Assert.Equal(2, dto.RoleId);
        Assert.NotEmpty(dto.Permissions);
    }

    [Fact]
    public async Task AssignPermissionsToRole_ReturnsOk_WhenValid()
    {
        var request = new AssignRolePermissionsRequest
        {
            PermissionIds = [1, 2]
        };

        var result = await _controller.AssignPermissionsToRole(2, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<RolePermissionsDto>(okResult.Value);
        Assert.Equal(2, dto.RoleId);
        Assert.Equal(2, dto.Permissions.Count);
    }

    [Fact]
    public async Task AssignPermissionsToRole_ReturnsBadRequest_WhenInvalidPermissionId()
    {
        var request = new AssignRolePermissionsRequest
        {
            PermissionIds = [1, 9999] // 9999 does not exist
        };

        var result = await _controller.AssignPermissionsToRole(2, request, CancellationToken.None);

        var badRequest = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task GetMyPermissions_ReturnsUserPermissions_Successfully()
    {
        var result = await _controller.GetMyPermissions(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<MyPermissionsResponse>(okResult.Value);
        Assert.Equal(101, response.UserId);
        Assert.Equal(AppRoles.Manager, response.Role);
        Assert.NotEmpty(response.PermissionCodes);
    }

    private sealed class FakePermissionRepository : IPermissionRepository
    {
        private readonly List<PermissionItem> _permissions =
        [
            new(1, "USER_VIEW", "Xem nhân viên", "Users", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(2, "USER_MANAGE", "Quản lý nhân viên", "Users", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(3, "ROLE_VIEW", "Xem vai trò", "Roles", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(4, "ROLE_MANAGE", "Quản lý vai trò", "Roles", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(5, "ATTENDANCE_CHECKIN", "Chấm công ca", "Attendance", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(6, "ATTENDANCE_VIEW", "Xem chấm công", "Attendance", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(7, "ATTENDANCE_MANAGE", "Quản lý ca", "Attendance", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(8, "REPORT_VIEW", "Xem báo cáo", "Reports", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(9, "SYSTEM_CONFIG", "Cấu hình hệ thống", "System", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        ];

        private readonly Dictionary<long, List<long>> _rolePermissions = new()
        {
            [1] = [1, 2, 3, 4, 5, 6, 7, 8, 9], // Admin
            [2] = [1, 3, 5, 6, 7, 8],          // Manager
            [3] = [5, 6]                       // Employee
        };

        public Task EnsureTablesAndSeedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<PermissionItem>> GetAllPermissionsAsync(string? group = null, CancellationToken cancellationToken = default)
        {
            var query = _permissions.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(group))
            {
                query = query.Where(p => p.Group.Equals(group.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            return Task.FromResult<IReadOnlyList<PermissionItem>>(query.ToList());
        }

        public Task<PermissionItem?> GetPermissionByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(_permissions.FirstOrDefault(p => p.Id == id));

        public Task<PermissionItem?> GetPermissionByCodeAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult(_permissions.FirstOrDefault(p => p.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase)));

        public Task<PermissionItem?> CreatePermissionAsync(string code, string name, string group, string? description, CancellationToken cancellationToken = default)
        {
            if (_permissions.Any(p => p.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult<PermissionItem?>(null);
            }

            var item = new PermissionItem(_permissions.Count + 1, code.Trim(), name.Trim(), group.Trim(), description, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            _permissions.Add(item);
            return Task.FromResult<PermissionItem?>(item);
        }

        public Task<PermissionItem?> UpdatePermissionAsync(long id, string name, string group, string? description, CancellationToken cancellationToken = default)
        {
            var idx = _permissions.FindIndex(p => p.Id == id);
            if (idx == -1) return Task.FromResult<PermissionItem?>(null);

            var existing = _permissions[idx];
            var updated = existing with { Name = name.Trim(), Group = group.Trim(), Description = description, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _permissions[idx] = updated;
            return Task.FromResult<PermissionItem?>(updated);
        }

        public Task<bool> DeletePermissionAsync(long id, CancellationToken cancellationToken = default)
        {
            var removed = _permissions.RemoveAll(p => p.Id == id) > 0;
            return Task.FromResult(removed);
        }

        public Task<IReadOnlyList<PermissionItem>> GetPermissionsByRoleIdAsync(long roleId, CancellationToken cancellationToken = default)
        {
            if (!_rolePermissions.TryGetValue(roleId, out var permIds))
            {
                return Task.FromResult<IReadOnlyList<PermissionItem>>([]);
            }

            var list = _permissions.Where(p => permIds.Contains(p.Id)).ToList();
            return Task.FromResult<IReadOnlyList<PermissionItem>>(list);
        }

        public Task<IReadOnlyList<PermissionItem>> GetPermissionsByUserIdAsync(long userId, CancellationToken cancellationToken = default)
        {
            // Simulate user 101 has role 2 (Manager)
            return GetPermissionsByRoleIdAsync(2, cancellationToken);
        }

        public Task<bool> AssignPermissionsToRoleAsync(long roleId, IEnumerable<long> permissionIds, CancellationToken cancellationToken = default)
        {
            _rolePermissions[roleId] = permissionIds.ToList();
            return Task.FromResult(true);
        }
    }

    private sealed class FakeRoleRepoForPermissions : IRoleRepository
    {
        private readonly List<RoleItem> _roles =
        [
            new(1, AppRoles.Admin, "Admin", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(2, AppRoles.Manager, "Manager", 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(3, AppRoles.Employee, "Employee", 5, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        ];

        public Task<IReadOnlyList<RoleItem>> GetAllRolesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RoleItem>>(_roles);
        public Task<RoleItem?> GetRoleByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(_roles.FirstOrDefault(r => r.Id == id));
        public Task<RoleItem?> GetRoleByNameAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(_roles.FirstOrDefault(r => r.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)));
        public Task<RoleItem?> CreateRoleAsync(string name, string? description, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RoleItem?> UpdateRoleAsync(long id, string name, string? description, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteRoleAsync(long id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> GetUserCountForRoleAsync(long roleId, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
