using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using manage365.Repositories.Auth;
using manage365.Repositories.Roles;
using manage365.Repositories.Users;
using manage365.Routes.API.Auth;
using manage365.Routes.API.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace manage365.Tests;

public sealed class UsersControllerTests
{
    private readonly FakeUserManagementRepository _userRepo;
    private readonly FakeRoleRepoForUsers _roleRepo;
    private readonly FakePasswordHasherForUsers _hasher;
    private readonly UsersController _controller;

    public UsersControllerTests()
    {
        _userRepo = new FakeUserManagementRepository();
        _roleRepo = new FakeRoleRepoForUsers();
        _hasher = new FakePasswordHasherForUsers();
        _controller = new UsersController(_userRepo, _roleRepo, _hasher)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, "999"),
                        new Claim(ClaimTypes.Role, AppRoles.Admin),
                        new Claim(JwtRegisteredClaimNames.Email, "admin@manage365.io.vn"),
                        new Claim(JwtRegisteredClaimNames.Name, "System Admin")
                    ], "TestAuth"))
                }
            }
        };
    }

    [Fact]
    public async Task GetUsers_ReturnsPagedUsers_Successfully()
    {
        var result = await _controller.GetUsers(page: 1, pageSize: 10, cancellationToken: CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PagedUsersResponse>(okResult.Value);
        Assert.Equal(2, response.TotalCount);
        Assert.Equal(2, response.Items.Count);
        Assert.Equal(1, response.Page);
    }

    [Fact]
    public async Task GetUserById_ReturnsOk_WhenUserExists()
    {
        var result = await _controller.GetUserById(1, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<UserDetailDto>(okResult.Value);
        Assert.Equal(1, dto.Id);
        Assert.Equal("admin@manage365.io.vn", dto.Email);
    }

    [Fact]
    public async Task GetUserById_ReturnsNotFound_WhenUserDoesNotExist()
    {
        var result = await _controller.GetUserById(404, CancellationToken.None);

        var notFound = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task CreateUser_ReturnsCreated_WhenValid()
    {
        var request = new CreateUserRequest
        {
            Email = "staff2@manage365.io.vn",
            Password = "Password123!",
            DisplayName = "Nguyen Van B",
            Role = AppRoles.Employee,
            EmployeeType = "FullTime",
            HourlySalary = 30000,
            TaxCode = "9988776655",
            DependentsCount = 1,
            Status = "Active"
        };

        var result = await _controller.CreateUser(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<UserDetailDto>(createdResult.Value);
        Assert.Equal("staff2@manage365.io.vn", dto.Email);
        Assert.Equal("Nguyen Van B", dto.DisplayName);
        Assert.Equal(30000, dto.HourlySalary);
    }

    [Fact]
    public async Task CreateUser_ReturnsConflict_WhenEmailAlreadyExists()
    {
        var request = new CreateUserRequest
        {
            Email = "admin@manage365.io.vn",
            Password = "Password123!",
            DisplayName = "Duplicate Admin",
            Role = AppRoles.Admin,
            EmployeeType = "FullTime",
            HourlySalary = 50000
        };

        var result = await _controller.CreateUser(request, CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task CreateUser_ReturnsBadRequest_WhenRoleDoesNotExist()
    {
        var request = new CreateUserRequest
        {
            Email = "newuser@manage365.io.vn",
            Password = "Password123!",
            DisplayName = "New User",
            Role = "NonExistentRole",
            EmployeeType = "PartTime",
            HourlySalary = 20000
        };

        var result = await _controller.CreateUser(request, CancellationToken.None);

        var badRequest = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_ReturnsOk_WhenValid()
    {
        var request = new UpdateUserRequest
        {
            DisplayName = "Admin Updated Name",
            EmployeeType = "FullTime",
            HourlySalary = 55000,
            TaxCode = "1234567890",
            DependentsCount = 2,
            Status = "Active"
        };

        var result = await _controller.UpdateUser(1, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<UserDetailDto>(okResult.Value);
        Assert.Equal("Admin Updated Name", dto.DisplayName);
        Assert.Equal(55000, dto.HourlySalary);
    }

    [Fact]
    public async Task AssignRole_ReturnsOk_WhenRoleExists()
    {
        var request = new AssignRoleRequest { Role = AppRoles.Manager };

        var result = await _controller.AssignRole(2, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<UserDetailDto>(okResult.Value);
        Assert.Equal(AppRoles.Manager, dto.Role);
    }

    [Fact]
    public async Task AssignRole_ReturnsBadRequest_WhenRoleDoesNotExist()
    {
        var request = new AssignRoleRequest { Role = "UnknownRole" };

        var result = await _controller.AssignRole(2, request, CancellationToken.None);

        var badRequest = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_ReturnsOk_WhenValid()
    {
        var request = new UpdateUserStatusRequest { Status = "Inactive" };

        var result = await _controller.UpdateStatus(2, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<UserDetailDto>(okResult.Value);
        Assert.Equal("Inactive", dto.Status);
    }

    [Fact]
    public async Task DeleteUser_ReturnsBadRequest_WhenAttemptingToDeleteSelf()
    {
        var result = await _controller.DeleteUser(999, CancellationToken.None); // Current user sub is 999

        var badRequest = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_ReturnsOk_WhenAdminDeletesOtherUser()
    {
        var result = await _controller.DeleteUser(2, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    private sealed class FakeUserManagementRepository : IUserManagementRepository
    {
        private readonly List<UserManagementDetail> _users =
        [
            new(1, "admin@manage365.io.vn", "System Admin", AppRoles.Admin, "FullTime", 50000, null, 0, "Active", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(2, "staff@manage365.io.vn", "Staff Member", AppRoles.Employee, "PartTime", 25000, null, 0, "Active", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        ];

        public Task<PagedUserResult<UserManagementItem>> GetUsersAsync(UserFilterParams filter, CancellationToken cancellationToken = default)
        {
            var items = _users.Select(u => new UserManagementItem(
                u.Id, u.Email, u.DisplayName, u.Role, u.EmployeeType, u.HourlySalary, u.Status, u.CreatedAtUtc, u.UpdatedAtUtc
            )).ToList();

            return Task.FromResult(new PagedUserResult<UserManagementItem>(items, 1, 10, items.Count, 1));
        }

        public Task<UserManagementDetail?> GetUserByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(_users.FirstOrDefault(u => u.Id == id));

        public Task<UserManagementDetail?> GetUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
            => Task.FromResult(_users.FirstOrDefault(u => u.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase)));

        public Task<UserManagementDetail?> CreateUserAsync(CreateUserData data, CancellationToken cancellationToken = default)
        {
            if (_users.Any(u => u.Email.Equals(data.Email, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult<UserManagementDetail?>(null);
            }

            var item = new UserManagementDetail(
                _users.Count + 1, data.Email, data.DisplayName, data.Role, data.EmployeeType, data.HourlySalary,
                data.TaxCode, data.DependentsCount, data.Status, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow
            );
            _users.Add(item);
            return Task.FromResult<UserManagementDetail?>(item);
        }

        public Task<UserManagementDetail?> UpdateUserAsync(long id, UpdateUserData data, CancellationToken cancellationToken = default)
        {
            var idx = _users.FindIndex(u => u.Id == id);
            if (idx == -1) return Task.FromResult<UserManagementDetail?>(null);

            var existing = _users[idx];
            var updated = existing with
            {
                DisplayName = data.DisplayName,
                EmployeeType = data.EmployeeType,
                HourlySalary = data.HourlySalary,
                TaxCode = data.TaxCode,
                DependentsCount = data.DependentsCount,
                Status = data.Status,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _users[idx] = updated;
            return Task.FromResult<UserManagementDetail?>(updated);
        }

        public Task<UserManagementDetail?> UpdateRoleAsync(long id, string roleName, CancellationToken cancellationToken = default)
        {
            var idx = _users.FindIndex(u => u.Id == id);
            if (idx == -1) return Task.FromResult<UserManagementDetail?>(null);

            var existing = _users[idx];
            var updated = existing with { Role = roleName, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _users[idx] = updated;
            return Task.FromResult<UserManagementDetail?>(updated);
        }

        public Task<UserManagementDetail?> UpdateStatusAsync(long id, string status, CancellationToken cancellationToken = default)
        {
            var idx = _users.FindIndex(u => u.Id == id);
            if (idx == -1) return Task.FromResult<UserManagementDetail?>(null);

            var existing = _users[idx];
            var updated = existing with { Status = status, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _users[idx] = updated;
            return Task.FromResult<UserManagementDetail?>(updated);
        }

        public Task<bool> DeleteUserAsync(long id, CancellationToken cancellationToken = default)
        {
            var removed = _users.RemoveAll(u => u.Id == id) > 0;
            return Task.FromResult(removed);
        }
    }

    private sealed class FakeRoleRepoForUsers : IRoleRepository
    {
        private readonly List<RoleItem> _roles =
        [
            new(1, AppRoles.Admin, "Admin", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(2, AppRoles.Manager, "Manager", 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(3, AppRoles.Employee, "Employee", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        ];

        public Task<IReadOnlyList<RoleItem>> GetAllRolesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RoleItem>>(_roles);
        public Task<RoleItem?> GetRoleByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(_roles.FirstOrDefault(r => r.Id == id));
        public Task<RoleItem?> GetRoleByNameAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(_roles.FirstOrDefault(r => r.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)));
        public Task<RoleItem?> CreateRoleAsync(string name, string? description, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RoleItem?> UpdateRoleAsync(long id, string name, string? description, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteRoleAsync(long id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> GetUserCountForRoleAsync(long roleId, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakePasswordHasherForUsers : IPasswordHasher
    {
        public string Hash(string password) => "hashed-" + password;
        public bool Verify(string password, string hash) => true;
    }
}
