using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using manage365.Repositories.Departments;
using manage365.Routes.API.Auth;
using manage365.Routes.API.Departments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace manage365.Tests;

public sealed class DepartmentsControllerTests
{
    private readonly FakeDepartmentRepository _departmentRepo;
    private readonly DepartmentsController _controller;

    public DepartmentsControllerTests()
    {
        _departmentRepo = new FakeDepartmentRepository();
        _controller = new DepartmentsController(_departmentRepo)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, "201"),
                        new Claim(JwtRegisteredClaimNames.Email, "warehouse_staff@manage365.io.vn"),
                        new Claim(ClaimTypes.Role, AppRoles.Employee)
                    ], "TestAuth"))
                }
            }
        };
    }

    [Fact]
    public async Task GetDepartments_ReturnsAll_Successfully()
    {
        var result = await _controller.GetDepartments(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<List<DepartmentDto>>(okResult.Value);
        Assert.NotEmpty(list);
        Assert.Contains(list, d => d.Code == "WAREHOUSE");
    }

    [Fact]
    public async Task GetDepartmentById_ReturnsOk_WhenExists()
    {
        var result = await _controller.GetDepartmentById(1, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<DepartmentDetailDto>(okResult.Value);
        Assert.Equal(1, detail.Id);
        Assert.Equal("WAREHOUSE", detail.Code);
        Assert.NotEmpty(detail.Tools);
    }

    [Fact]
    public async Task GetDepartmentById_ReturnsNotFound_WhenDoesNotExist()
    {
        var result = await _controller.GetDepartmentById(999, CancellationToken.None);

        var notFound = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task CreateDepartment_ReturnsCreated_WhenValid()
    {
        var request = new CreateDepartmentRequest
        {
            Code = "CUSTOMER_CARE",
            Name = "Bộ phận Chăm sóc khách hàng",
            Description = "Tiếp nhận phản hồi và hỗ trợ đổi trả"
        };

        var result = await _controller.CreateDepartment(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<DepartmentDto>(created.Value);
        Assert.Equal("CUSTOMER_CARE", dto.Code);
        Assert.Equal("Bộ phận Chăm sóc khách hàng", dto.Name);
    }

    [Fact]
    public async Task CreateDepartment_ReturnsConflict_WhenDuplicateCode()
    {
        var request = new CreateDepartmentRequest
        {
            Code = "WAREHOUSE",
            Name = "Trùng mã kho"
        };

        var result = await _controller.CreateDepartment(request, CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task UpdateDepartment_ReturnsOk_WhenValid()
    {
        var request = new UpdateDepartmentRequest
        {
            Name = "Kho tổng phân phối",
            Description = "Mô tả mới",
            Status = "Active"
        };

        var result = await _controller.UpdateDepartment(1, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<DepartmentDto>(okResult.Value);
        Assert.Equal("Kho tổng phân phối", dto.Name);
    }

    [Fact]
    public async Task DeleteDepartment_ReturnsConflict_WhenHasEmployees()
    {
        var result = await _controller.DeleteDepartment(1, CancellationToken.None); // Warehouse has employees

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task DeleteDepartment_ReturnsOk_WhenNoEmployees()
    {
        // Tạo phòng ban mới không có nhân viên
        var dept = await _departmentRepo.CreateDepartmentAsync("EMPTY_DEPT", "Phòng ban trống", null);
        Assert.NotNull(dept);

        var result = await _controller.DeleteDepartment(dept.Id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetDepartmentTools_ReturnsTools_Successfully()
    {
        var result = await _controller.GetDepartmentTools(1, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var tools = Assert.IsAssignableFrom<List<DepartmentToolDto>>(okResult.Value);
        Assert.NotEmpty(tools);
        Assert.Contains(tools, t => t.Code == "TOOL_STOCK_IN");
    }

    [Fact]
    public async Task CreateDepartmentTool_ReturnsCreated_WhenValid()
    {
        var request = new CreateDepartmentToolRequest
        {
            Code = "TOOL_TEMPERATURE_LOG",
            Name = "Ghi nhận nhiệt độ kho lạnh",
            Icon = "thermostat",
            RoutePath = "/warehouse/temp-log",
            DisplayOrder = 5
        };

        var result = await _controller.CreateDepartmentTool(1, request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<DepartmentToolDto>(created.Value);
        Assert.Equal("TOOL_TEMPERATURE_LOG", dto.Code);
    }

    [Fact]
    public async Task CreateDepartmentTool_ReturnsConflict_WhenDuplicateCode()
    {
        var request = new CreateDepartmentToolRequest
        {
            Code = "TOOL_STOCK_IN",
            Name = "Duplicate stock in"
        };

        var result = await _controller.CreateDepartmentTool(1, request, CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task UpdateDepartmentTool_ReturnsOk_WhenValid()
    {
        var request = new UpdateDepartmentToolRequest
        {
            Name = "Nhập kho siêu thị",
            Icon = "inbox",
            RoutePath = "/warehouse/stock-in-v2",
            DisplayOrder = 1,
            Status = "Active"
        };

        var result = await _controller.UpdateDepartmentTool(1, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<DepartmentToolDto>(okResult.Value);
        Assert.Equal("Nhập kho siêu thị", dto.Name);
    }

    [Fact]
    public async Task DeleteDepartmentTool_ReturnsOk_WhenValid()
    {
        var result = await _controller.DeleteDepartmentTool(1, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task AssignEmployeeToDepartment_ReturnsOk_WhenValid()
    {
        var request = new AssignEmployeeDepartmentRequest { DepartmentId = 2 };

        var result = await _controller.AssignEmployeeToDepartment(201, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetMyTools_ReturnsCurrentUserDepartmentTools()
    {
        var result = await _controller.GetMyTools(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<MyDepartmentToolsResponse>(okResult.Value);
        Assert.Equal(201, response.UserId);
        Assert.Equal(1, response.DepartmentId);
        Assert.Equal("Bộ phận Kho vận", response.DepartmentName);
        Assert.NotEmpty(response.Tools);
    }

    private sealed class FakeDepartmentRepository : IDepartmentRepository
    {
        private readonly List<DepartmentItem> _departments =
        [
            new(1, "WAREHOUSE", "Bộ phận Kho vận", "Quản lý kho", "Active", 3, 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(2, "POS_SALES", "Bộ phận Bán hàng / Thu ngân", "Bán hàng POS", "Active", 5, 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(3, "HR_ADMIN", "Bộ phận Nhân sự & Quản trị", "Nhân sự", "Active", 1, 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        ];

        private readonly List<DepartmentToolItem> _tools =
        [
            new(1, 1, "TOOL_STOCK_IN", "Nhập kho", "move_to_inbox", "/warehouse/stock-in", 1, "Active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(2, 1, "TOOL_STOCK_OUT", "Xuất kho", "outbox", "/warehouse/stock-out", 2, "Active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(3, 2, "TOOL_POS_CHECKOUT", "Quầy POS", "point_of_sale", "/pos/checkout", 1, "Active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        ];

        public Task EnsureTablesAndSeedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<DepartmentItem>> GetAllDepartmentsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DepartmentItem>>(_departments.ToList());

        public Task<DepartmentDetailItem?> GetDepartmentByIdAsync(long id, CancellationToken cancellationToken = default)
        {
            var dept = _departments.FirstOrDefault(d => d.Id == id);
            if (dept is null) return Task.FromResult<DepartmentDetailItem?>(null);

            var tools = _tools.Where(t => t.DepartmentId == id).ToList();
            return Task.FromResult<DepartmentDetailItem?>(new DepartmentDetailItem(
                dept.Id, dept.Code, dept.Name, dept.Description, dept.Status, dept.EmployeeCount, tools, dept.CreatedAtUtc, dept.UpdatedAtUtc
            ));
        }

        public Task<DepartmentItem?> GetDepartmentByCodeAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult(_departments.FirstOrDefault(d => d.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase)));

        public Task<DepartmentItem?> CreateDepartmentAsync(string code, string name, string? description, CancellationToken cancellationToken = default)
        {
            if (_departments.Any(d => d.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult<DepartmentItem?>(null);
            }

            var item = new DepartmentItem(_departments.Count + 1, code.Trim(), name.Trim(), description, "Active", 0, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            _departments.Add(item);
            return Task.FromResult<DepartmentItem?>(item);
        }

        public Task<DepartmentItem?> UpdateDepartmentAsync(long id, string name, string? description, string? status, CancellationToken cancellationToken = default)
        {
            var idx = _departments.FindIndex(d => d.Id == id);
            if (idx == -1) return Task.FromResult<DepartmentItem?>(null);

            var existing = _departments[idx];
            var updated = existing with { Name = name.Trim(), Description = description, Status = status ?? existing.Status, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _departments[idx] = updated;
            return Task.FromResult<DepartmentItem?>(updated);
        }

        public Task<bool> DeleteDepartmentAsync(long id, CancellationToken cancellationToken = default)
        {
            var removed = _departments.RemoveAll(d => d.Id == id) > 0;
            _tools.RemoveAll(t => t.DepartmentId == id);
            return Task.FromResult(removed);
        }

        public Task<IReadOnlyList<DepartmentToolItem>> GetDepartmentToolsAsync(long departmentId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DepartmentToolItem>>(_tools.Where(t => t.DepartmentId == departmentId).ToList());

        public Task<DepartmentToolItem?> GetToolByIdAsync(long toolId, CancellationToken cancellationToken = default)
            => Task.FromResult(_tools.FirstOrDefault(t => t.Id == toolId));

        public Task<DepartmentToolItem?> GetToolByCodeAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult(_tools.FirstOrDefault(t => t.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase)));

        public Task<DepartmentToolItem?> CreateToolAsync(long departmentId, string code, string name, string? icon, string? routePath, int displayOrder, CancellationToken cancellationToken = default)
        {
            if (_tools.Any(t => t.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult<DepartmentToolItem?>(null);
            }

            var item = new DepartmentToolItem(_tools.Count + 1, departmentId, code.Trim(), name.Trim(), icon, routePath, displayOrder, "Active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            _tools.Add(item);
            return Task.FromResult<DepartmentToolItem?>(item);
        }

        public Task<DepartmentToolItem?> UpdateToolAsync(long toolId, string name, string? icon, string? routePath, int displayOrder, string? status, CancellationToken cancellationToken = default)
        {
            var idx = _tools.FindIndex(t => t.Id == toolId);
            if (idx == -1) return Task.FromResult<DepartmentToolItem?>(null);

            var existing = _tools[idx];
            var updated = existing with { Name = name.Trim(), Icon = icon, RoutePath = routePath, DisplayOrder = displayOrder, Status = status ?? existing.Status, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _tools[idx] = updated;
            return Task.FromResult<DepartmentToolItem?>(updated);
        }

        public Task<bool> DeleteToolAsync(long toolId, CancellationToken cancellationToken = default)
        {
            var removed = _tools.RemoveAll(t => t.Id == toolId) > 0;
            return Task.FromResult(removed);
        }

        public Task<bool> AssignEmployeeToDepartmentAsync(long employeeId, long? departmentId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<UserToolsItem?> GetToolsByUserIdAsync(long userId, CancellationToken cancellationToken = default)
        {
            // Simulate user 201 belongs to Warehouse (Department 1)
            var userTools = _tools.Where(t => t.DepartmentId == 1).ToList();
            return Task.FromResult<UserToolsItem?>(new UserToolsItem(userId, "Nguyen Van Kho", 1, "Bộ phận Kho vận", userTools));
        }
    }
}
