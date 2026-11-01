using System.Net;
using System.Security.Claims;
using System.Text.Json;
using manage365.Repositories.Shifts;
using manage365.Routes.API.Shifts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace manage365.Tests;

public sealed class AiShiftSchedulingServiceTests
{
    [Fact]
    public async Task GenerateScheduleAsync_SchedulesShifts_FairlyAcrossActiveEmployees()
    {
        var fakeRepo = new FakeShiftSchedulingRepository
        {
            Employees =
            [
                new ScheduleEmployeeDto(1, "NV-0001", "Nguyễn Văn A", 1, "Bán hàng", "Active"),
                new ScheduleEmployeeDto(2, "NV-0002", "Trần Thị B", 1, "Bán hàng", "Active"),
                new ScheduleEmployeeDto(3, "NV-0003", "Lê Văn C", 1, "Bán hàng", "Active")
            ],
            Templates =
            [
                new ShiftTemplateDto(10, "SANG", "Ca Sáng", "06:00", "14:00", false, "#f97316", "Ca sáng", "Active", 8),
                new ShiftTemplateDto(20, "CHIEU", "Ca Chiều", "14:00", "22:00", false, "#3b82f6", "Ca chiều", "Active", 8)
            ],
            Schedule = new WeeklyScheduleResponse("2026-11-02", "2026-11-08", 0, 0, [], [])
        };

        var factory = new FakeHttpClientFactory();
        var service = new AiShiftSchedulingService(fakeRepo, factory, NullLogger<AiShiftSchedulingService>.Instance);

        var request = new AiScheduleRequest(
            WeekStartDate: "2026-11-02",
            MinEmployeesPerShift: 1,
            MaxShiftsPerEmployeePerWeek: 5,
            KeepExistingShifts: true);

        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "Manager")], "test"));

        var response = await service.GenerateScheduleAsync(request, user, TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal("2026-11-02", response.WeekStartDate);
        Assert.Equal("2026-11-08", response.WeekEndDate);
        Assert.True(response.ProposedShiftCount > 0);
        Assert.Equal(3, response.TotalEmployeesConsidered);
        Assert.Equal(3, response.EmployeesAssigned);
        Assert.NotEmpty(response.AiInsights);
        Assert.NotEmpty(response.BatchAssignmentPayload);

        // Verify that no employee worked more than max 5 shifts
        var shiftsPerEmployee = response.BatchAssignmentPayload.GroupBy(a => a.EmployeeId).ToDictionary(g => g.Key, g => g.Count());
        foreach (var count in shiftsPerEmployee.Values)
        {
            Assert.True(count <= 5, $"Nhân viên bị xếp quá 5 ca: {count}");
        }
    }

    [Fact]
    public async Task GenerateScheduleAsync_RespectsApprovedLeave()
    {
        var fakeRepo = new FakeShiftSchedulingRepository
        {
            Employees =
            [
                new ScheduleEmployeeDto(1, "NV-0001", "Nguyễn Văn A", 1, "Bán hàng", "Active"),
                new ScheduleEmployeeDto(2, "NV-0002", "Trần Thị B", 1, "Bán hàng", "Active")
            ],
            Templates =
            [
                new ShiftTemplateDto(10, "SANG", "Ca Sáng", "06:00", "14:00", false, "#f97316", "Ca sáng", "Active", 8)
            ],
            // Employee 1 is on approved leave on 2026-11-02
            Schedule = new WeeklyScheduleResponse(
                "2026-11-02",
                "2026-11-08",
                1,
                1,
                [
                    new ShiftAssignmentDto(
                        AssignmentId: 999,
                        ShiftId: 101,
                        EmployeeId: 1,
                        EmployeeName: "Nguyễn Văn A",
                        EmployeeCode: "NV-0001",
                        DepartmentName: "Bán hàng",
                        ShiftTemplateId: 10,
                        ShiftName: "Ca Sáng",
                        ShiftDate: "2026-11-02",
                        StartTime: "06:00",
                        EndTime: "14:00",
                        IsOvernight: false,
                        Color: "#f97316",
                        Status: "Approved",
                        Note: "Đơn nghỉ phép",
                        Attendance: null,
                        IsOnLeave: true,
                        LeaveStatus: "Approved")
                ],
                [])
        };

        var factory = new FakeHttpClientFactory();
        var service = new AiShiftSchedulingService(fakeRepo, factory, NullLogger<AiShiftSchedulingService>.Instance);

        var request = new AiScheduleRequest(
            WeekStartDate: "2026-11-02",
            MinEmployeesPerShift: 1,
            MaxShiftsPerEmployeePerWeek: 6);

        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "Manager")], "test"));

        var response = await service.GenerateScheduleAsync(request, user, TestContext.Current.CancellationToken);

        // Employee 1 MUST NOT be scheduled on 2026-11-02 because they are on leave
        var emp1OnLeaveDay = response.BatchAssignmentPayload
            .Any(a => a.EmployeeId == 1 && a.ShiftDate == "2026-11-02");

        Assert.False(emp1OnLeaveDay, "Nhân viên đang nghỉ phép không được phép bị xếp ca vào ngày nghỉ.");
    }

    [Fact]
    public async Task AiGenerateSchedule_ControllerReturnsOk_WhenValid()
    {
        var fakeRepo = new FakeShiftSchedulingRepository
        {
            Employees =
            [
                new ScheduleEmployeeDto(1, "NV-0001", "Nguyễn Văn A", 1, "Bán hàng", "Active")
            ],
            Templates =
            [
                new ShiftTemplateDto(10, "SANG", "Ca Sáng", "06:00", "14:00", false, "#f97316", "Ca sáng", "Active", 8)
            ],
            Schedule = new WeeklyScheduleResponse("2026-11-02", "2026-11-08", 0, 0, [], [])
        };

        var factory = new FakeHttpClientFactory();
        var service = new AiShiftSchedulingService(fakeRepo, factory, NullLogger<AiShiftSchedulingService>.Instance);
        var controller = new ShiftAssignmentsController(fakeRepo, service)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Manager"), new Claim("sub", "1")], "test"))
                }
            }
        };

        var result = await controller.AiGenerateSchedule(
            new AiScheduleRequest(WeekStartDate: "2026-11-02"),
            TestContext.Current.CancellationToken);

        var okResult = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result);
        var response = Assert.IsType<AiScheduleResponse>(okResult.Value);
        Assert.Equal("2026-11-02", response.WeekStartDate);
        Assert.True(response.ProposedShiftCount > 0);
    }

    private sealed class FakeShiftSchedulingRepository : IShiftSchedulingRepository
    {
        public List<ScheduleEmployeeDto> Employees { get; set; } = [];
        public List<ShiftTemplateDto> Templates { get; set; } = [];
        public WeeklyScheduleResponse Schedule { get; set; } = new("2026-11-02", "2026-11-08", 0, 0, [], []);

        public Task<ScheduleEmployeesResponse> GetEmployeesAsync(string? search, long? departmentId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScheduleEmployeesResponse(Employees, 1, 100, Employees.Count));

        public Task<List<ShiftTemplateDto>> GetTemplatesAsync(bool activeOnly = true, CancellationToken cancellationToken = default) =>
            Task.FromResult(Templates);

        public Task<WeeklyScheduleResponse> GetScheduleAsync(DateOnly startDate, DateOnly endDate, long? departmentId = null, long? employeeId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Schedule);

        public Task EnsureTablesAndSeedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ShiftTemplateDto?> GetTemplateByIdAsync(long id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ShiftTemplateDto> CreateTemplateAsync(CreateShiftTemplateRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ShiftTemplateDto?> UpdateTemplateAsync(long id, UpdateShiftTemplateRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteTemplateAsync(long id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ShiftAssignmentDto?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ValidateShiftConflictResponse> ValidateConflictAsync(long employeeId, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime, bool isOvernight, long? excludeAssignmentId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ShiftAssignmentDto> AssignShiftAsync(AssignShiftRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ShiftAssignmentDto?> UpdateAssignmentAsync(long assignmentId, UpdateShiftAssignmentRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BatchAssignShiftResult> BatchAssignShiftsAsync(BatchAssignShiftRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BatchAssignShiftResult> CopyWeeklyScheduleAsync(DateOnly sourceStartDate, DateOnly targetStartDate, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var handler = new FakeHttpHandler();
            return new HttpClient(handler)
            {
                BaseAddress = new Uri("https://manage365.click/api/")
            };
        }
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(new { reply = "AI nhận xét: Lịch phân bổ tốt, đảm bảo thời gian nghỉ." });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
