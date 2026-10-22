using System.Globalization;
using System.Security.Claims;
using manage365.Repositories.Shifts;
using manage365.Routes.API.Auth;
using manage365.Routes.API.Shifts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace manage365.Tests;

public sealed class ShiftSchedulingTests
{
    private readonly FakeShiftSchedulingRepository _repository;
    private readonly ShiftTemplatesController _templatesController;
    private readonly ShiftAssignmentsController _assignmentsController;

    public ShiftSchedulingTests()
    {
        _repository = new FakeShiftSchedulingRepository();
        _templatesController = new ShiftTemplatesController(_repository)
        {
            ControllerContext = CreateControllerContext(AppRoles.Admin)
        };
        _assignmentsController = new ShiftAssignmentsController(_repository)
        {
            ControllerContext = CreateControllerContext(AppRoles.Manager)
        };
    }

    [Fact]
    public async Task GetTemplates_ReturnsActiveTemplates_Successfully()
    {
        var result = await _templatesController.GetTemplates(true, TestContext.Current.CancellationToken);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<List<ShiftTemplateDto>>(okResult.Value);
        Assert.NotEmpty(list);
        Assert.Contains(list, t => t.Code == "CA_SANG");
    }

    [Fact]
    public async Task CreateTemplate_ReturnsCreated_WhenValid()
    {
        var req = new CreateShiftTemplateRequest(
            Code: "CA_PARTTIME_WEEKEND",
            Name: "Part-time Cuối Tuần",
            StartTime: "09:00",
            EndTime: "13:00",
            IsOvernight: false,
            Color: "#10b981",
            Description: "Ca part-time thứ 7 & chủ nhật");

        var result = await _templatesController.CreateTemplate(req, TestContext.Current.CancellationToken);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var created = Assert.IsType<ShiftTemplateDto>(createdResult.Value);
        Assert.Equal("CA_PARTTIME_WEEKEND", created.Code);
        Assert.Equal(4.0, created.DurationHours);
    }

    [Fact]
    public async Task CreateTemplate_ReturnsBadRequest_WhenTimesInvalid()
    {
        var req = new CreateShiftTemplateRequest(
            Code: "CA_INVALID",
            Name: "Ca Bị Lỗi Giờ",
            StartTime: "14:00",
            EndTime: "10:00",
            IsOvernight: false); // Kết thúc trước bắt đầu nhưng không bật qua đêm

        var result = await _templatesController.CreateTemplate(req, TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Fact]
    public async Task AssignShift_ReturnsCreated_WhenValid()
    {
        var req = new AssignShiftRequest(
            EmployeeId: 101,
            ShiftDate: "2026-10-12",
            ShiftTemplateId: 1, // Ca Sáng 06:00 - 14:00
            Note: "Trực quầy thu ngân 1");

        var result = await _assignmentsController.AssignShift(req, TestContext.Current.CancellationToken);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var assignment = Assert.IsType<ShiftAssignmentDto>(createdResult.Value);
        Assert.Equal(101, assignment.EmployeeId);
        Assert.Equal("06:00", assignment.StartTime);
        Assert.Equal("14:00", assignment.EndTime);
    }

    [Fact]
    public async Task AssignShift_ReturnsConflict_WhenSameEmployeeHasOverlappingShift()
    {
        // Gán ca sáng 06:00 - 14:00
        await _assignmentsController.AssignShift(new AssignShiftRequest(
            EmployeeId: 102,
            ShiftDate: "2026-10-12",
            CustomShiftName: "Ca Sáng",
            CustomStartTime: "06:00",
            CustomEndTime: "14:00",
            IsOvernight: false), TestContext.Current.CancellationToken);

        // Gán tiếp ca trùng giờ 10:00 - 16:00 cho cùng nhân viên 102
        var overlapReq = new AssignShiftRequest(
            EmployeeId: 102,
            ShiftDate: "2026-10-12",
            CustomShiftName: "Ca Chồng Giờ",
            CustomStartTime: "10:00",
            CustomEndTime: "16:00",
            IsOvernight: false);

        var result = await _assignmentsController.AssignShift(overlapReq, TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task AssignShift_Succeeds_WhenShiftsDoNotOverlap_ForSameEmployee()
    {
        // Nhân viên làm ca sáng 06:00 - 10:00
        await _assignmentsController.AssignShift(new AssignShiftRequest(
            EmployeeId: 103,
            ShiftDate: "2026-10-13",
            CustomShiftName: "Ca Sáng Sớm",
            CustomStartTime: "06:00",
            CustomEndTime: "10:00",
            IsOvernight: false), TestContext.Current.CancellationToken);

        // Nhân viên làm tiếp ca tối 18:00 - 22:00 cùng ngày (ca gãy hợp lệ)
        var eveningReq = new AssignShiftRequest(
            EmployeeId: 103,
            ShiftDate: "2026-10-13",
            CustomShiftName: "Ca Tối",
            CustomStartTime: "18:00",
            CustomEndTime: "22:00",
            IsOvernight: false);

        var result = await _assignmentsController.AssignShift(eveningReq, TestContext.Current.CancellationToken);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var assignment = Assert.IsType<ShiftAssignmentDto>(createdResult.Value);
        Assert.Equal("18:00", assignment.StartTime);
    }

    [Fact]
    public async Task AssignShift_AllowsMultipleEmployees_InSameOrOverlappingTimeSlot()
    {
        // Nhân viên 201 làm ca 02:00 - 23:00
        var emp1Req = new AssignShiftRequest(
            EmployeeId: 201,
            ShiftDate: "2026-10-20",
            CustomShiftName: "Ca Dài 1",
            CustomStartTime: "02:00",
            CustomEndTime: "23:00",
            IsOvernight: false);
        var res1 = await _assignmentsController.AssignShift(emp1Req, TestContext.Current.CancellationToken);
        Assert.IsType<CreatedAtActionResult>(res1.Result);

        // Nhân viên 202 làm ca 02:00 - 23:00 cùng ngày (trùng hoàn toàn ca với NV 201)
        var emp2Req = new AssignShiftRequest(
            EmployeeId: 202,
            ShiftDate: "2026-10-20",
            CustomShiftName: "Ca Dài 2",
            CustomStartTime: "02:00",
            CustomEndTime: "23:00",
            IsOvernight: false);
        var res2 = await _assignmentsController.AssignShift(emp2Req, TestContext.Current.CancellationToken);
        Assert.IsType<CreatedAtActionResult>(res2.Result);

        // Nhân viên 203 làm ca 08:00 - 17:00 (chồng lấn giờ trong khoảng 02:00 - 23:00)
        var emp3Req = new AssignShiftRequest(
            EmployeeId: 203,
            ShiftDate: "2026-10-20",
            CustomShiftName: "Ca Giờ Hành Chính",
            CustomStartTime: "08:00",
            CustomEndTime: "17:00",
            IsOvernight: false);
        var res3 = await _assignmentsController.AssignShift(emp3Req, TestContext.Current.CancellationToken);
        Assert.IsType<CreatedAtActionResult>(res3.Result);
    }

    [Fact]
    public async Task ValidateConflict_DetectsShortRestGap_Warning()
    {
        // Đã có ca chiều 14:00 - 22:00 ngày 2026-10-14
        await _assignmentsController.AssignShift(new AssignShiftRequest(
            EmployeeId: 104,
            ShiftDate: "2026-10-14",
            CustomShiftName: "Ca Chiều",
            CustomStartTime: "14:00",
            CustomEndTime: "22:00",
            IsOvernight: false), TestContext.Current.CancellationToken);

        // Kiểm tra ca sáng hôm sau 06:00 - 14:00 ngày 2026-10-15 (nghỉ đúng 8h)
        var valReq = new ValidateShiftConflictRequest(
            EmployeeId: 104,
            ShiftDate: "2026-10-15",
            StartTime: "05:00", // Chỉ được nghỉ 7h từ 22:00 hôm trước đến 05:00 hôm sau
            EndTime: "13:00",
            IsOvernight: false);

        var valResult = await _assignmentsController.ValidateConflict(valReq, TestContext.Current.CancellationToken);

        var okResult = Assert.IsType<OkObjectResult>(valResult.Result);
        var response = Assert.IsType<ValidateShiftConflictResponse>(okResult.Value);
        Assert.False(response.HasConflict); // Không overlap
        Assert.NotEmpty(response.Warnings); // Nhưng có cảnh báo nghỉ ít hơn 8h
        Assert.Contains(response.Warnings, w => w.Contains("khuyến nghị tối thiểu 8 giờ"));
    }

    [Fact]
    public async Task BatchAssignShifts_ExecutesMultipleAssignments()
    {
        var batchReq = new BatchAssignShiftRequest([
            new AssignShiftRequest(105, "2026-10-16", 1),
            new AssignShiftRequest(106, "2026-10-16", 2)
        ]);

        var result = await _assignmentsController.BatchAssignShifts(batchReq, TestContext.Current.CancellationToken);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<BatchAssignShiftResult>(okResult.Value);
        Assert.Equal(2, response.TotalRequested);
        Assert.Equal(2, response.SuccessCount);
        Assert.Equal(0, response.FailedCount);
    }

    [Fact]
    public async Task GetSchedule_ReturnsMatrix_AndCoverageWarnings()
    {
        var result = await _assignmentsController.GetSchedule(
            "2026-10-12",
            "2026-10-18",
            null,
            null,
            TestContext.Current.CancellationToken);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<WeeklyScheduleResponse>(okResult.Value);
        Assert.Equal("2026-10-12", response.StartDate);
        Assert.Equal("2026-10-18", response.EndDate);
    }

    private static ControllerContext CreateControllerContext(string role)
    {
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "1"),
                    new Claim(ClaimTypes.Role, role)
                ], "TestAuth"))
            }
        };
    }
}

internal sealed class FakeShiftSchedulingRepository : IShiftSchedulingRepository
{
    private readonly List<ShiftTemplateDto> _templates =
    [
        new ShiftTemplateDto(1, "CA_SANG", "Ca Sáng", "06:00", "14:00", false, "#f59e0b", "Ca sáng", "Active", 8.0),
        new ShiftTemplateDto(2, "CA_CHIEU", "Ca Chiều", "14:00", "22:00", false, "#3b82f6", "Ca chiều", "Active", 8.0),
        new ShiftTemplateDto(3, "CA_DEM", "Ca Đêm", "22:00", "06:00", true, "#8b5cf6", "Ca đêm", "Active", 8.0)
    ];

    private readonly List<ShiftAssignmentDto> _assignments = [];
    private long _nextAssignmentId = 1;
    private long _nextTemplateId = 4;

    public Task<ScheduleEmployeesResponse> GetEmployeesAsync(string? search, long? departmentId, int page, int pageSize, CancellationToken cancellationToken = default)
        => Task.FromResult(new ScheduleEmployeesResponse([], page, pageSize, 0));

    public Task EnsureTablesAndSeedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<List<ShiftTemplateDto>> GetTemplatesAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var list = activeOnly ? _templates.Where(t => t.Status == "Active").ToList() : [.. _templates];
        return Task.FromResult(list);
    }

    public Task<ShiftTemplateDto?> GetTemplateByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_templates.FirstOrDefault(t => t.Id == id));
    }

    public Task<ShiftTemplateDto> CreateTemplateAsync(CreateShiftTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var start = TimeSpan.Parse(request.StartTime, CultureInfo.InvariantCulture);
        var end = TimeSpan.Parse(request.EndTime, CultureInfo.InvariantCulture);
        if (!request.IsOvernight && end <= start)
        {
            throw new ArgumentException("Giờ kết thúc phải lớn hơn giờ bắt đầu.");
        }

        var duration = (request.IsOvernight || end <= start) ? (end - start + TimeSpan.FromDays(1)).TotalHours : (end - start).TotalHours;
        var dto = new ShiftTemplateDto(_nextTemplateId++, request.Code, request.Name, request.StartTime, request.EndTime, request.IsOvernight, request.Color ?? "#f97316", request.Description, "Active", duration);
        _templates.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<ShiftTemplateDto?> UpdateTemplateAsync(long id, UpdateShiftTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var index = _templates.FindIndex(t => t.Id == id);
        if (index < 0) return Task.FromResult<ShiftTemplateDto?>(null);

        var start = TimeSpan.Parse(request.StartTime, CultureInfo.InvariantCulture);
        var end = TimeSpan.Parse(request.EndTime, CultureInfo.InvariantCulture);
        if (!request.IsOvernight && end <= start)
        {
            throw new ArgumentException("Giờ kết thúc phải lớn hơn giờ bắt đầu.");
        }

        var duration = (request.IsOvernight || end <= start) ? (end - start + TimeSpan.FromDays(1)).TotalHours : (end - start).TotalHours;
        var updated = _templates[index] with
        {
            Name = request.Name,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            IsOvernight = request.IsOvernight,
            Color = request.Color ?? "#f97316",
            Description = request.Description,
            Status = request.Status ?? "Active",
            DurationHours = duration
        };
        _templates[index] = updated;
        return Task.FromResult<ShiftTemplateDto?>(updated);
    }

    public Task<bool> DeleteTemplateAsync(long id, CancellationToken cancellationToken = default)
    {
        var t = _templates.FirstOrDefault(x => x.Id == id);
        if (t is null) return Task.FromResult(false);
        var index = _templates.IndexOf(t);
        _templates[index] = t with { Status = "Inactive" };
        return Task.FromResult(true);
    }

    public Task<WeeklyScheduleResponse> GetScheduleAsync(DateOnly startDate, DateOnly endDate, long? departmentId = null, long? employeeId = null, CancellationToken cancellationToken = default)
    {
        var list = _assignments.Where(a =>
        {
            var d = DateOnly.Parse(a.ShiftDate, CultureInfo.InvariantCulture);
            return d >= startDate && d <= endDate &&
                   (!employeeId.HasValue || a.EmployeeId == employeeId.Value);
        }).ToList();

        var warnings = new List<string>();
        for (var d = startDate; d <= endDate; d = d.AddDays(1))
        {
            if (!list.Any(a => DateOnly.Parse(a.ShiftDate, CultureInfo.InvariantCulture) == d))
            {
                warnings.Add($"Ngày {d:dd/MM/yyyy}: Chưa có nhân viên nào trực.");
            }
        }

        return Task.FromResult(new WeeklyScheduleResponse(
            startDate.ToString("yyyy-MM-dd"),
            endDate.ToString("yyyy-MM-dd"),
            list.Count,
            list.Select(a => a.EmployeeId).Distinct().Count(),
            list,
            warnings));
    }

    public Task<ShiftAssignmentDto?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_assignments.FirstOrDefault(a => a.AssignmentId == assignmentId));
    }

    public Task<ValidateShiftConflictResponse> ValidateConflictAsync(long employeeId, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime, bool isOvernight, long? excludeAssignmentId = null, CancellationToken cancellationToken = default)
    {
        var proposedStart = shiftDate.ToDateTime(startTime);
        var proposedEnd = isOvernight ? shiftDate.AddDays(1).ToDateTime(endTime) : shiftDate.ToDateTime(endTime);

        var conflicts = new List<ConflictingShiftDto>();
        var warnings = new List<string>();

        var existingList = _assignments
            .Where(a => a.EmployeeId == employeeId && (!excludeAssignmentId.HasValue || a.AssignmentId != excludeAssignmentId.Value))
            .ToList();

        foreach (var ex in existingList)
        {
            var exDate = DateOnly.Parse(ex.ShiftDate, CultureInfo.InvariantCulture);
            var exStart = exDate.ToDateTime(TimeOnly.Parse(ex.StartTime, CultureInfo.InvariantCulture));
            var exEnd = ex.IsOvernight ? exDate.AddDays(1).ToDateTime(TimeOnly.Parse(ex.EndTime, CultureInfo.InvariantCulture)) : exDate.ToDateTime(TimeOnly.Parse(ex.EndTime, CultureInfo.InvariantCulture));

            if (proposedStart < exEnd && exStart < proposedEnd)
            {
                conflicts.Add(new ConflictingShiftDto(ex.AssignmentId, ex.ShiftName, ex.ShiftDate, ex.StartTime, ex.EndTime));
            }
            else
            {
                double gap = -1;
                if (proposedStart >= exEnd) gap = (proposedStart - exEnd).TotalHours;
                else if (exStart >= proposedEnd) gap = (exStart - proposedEnd).TotalHours;

                if (gap is >= 0 and < 8.0)
                {
                    warnings.Add($"Khoảng cách nghỉ giữa ca [{ex.ShiftName} ngày {ex.ShiftDate}] và ca mới chỉ là {gap:F1} giờ (khuyến nghị tối thiểu 8 giờ để đảm bảo sức khỏe nhân viên).");
                }
            }
        }

        if (conflicts.Count > 0)
        {
            return Task.FromResult(new ValidateShiftConflictResponse(true, "OVERLAP", "Xung đột trùng giờ làm việc.", warnings, conflicts));
        }

        return Task.FromResult(new ValidateShiftConflictResponse(false, "NONE", "Hợp lệ", warnings, []));
    }

    public Task<ShiftAssignmentDto> AssignShiftAsync(AssignShiftRequest request, CancellationToken cancellationToken = default)
    {
        var shiftDate = DateOnly.Parse(request.ShiftDate, CultureInfo.InvariantCulture);
        string name;
        string start;
        string end;
        bool overnight;
        long? templateId = null;

        if (request.ShiftTemplateId.HasValue)
        {
            var t = _templates.FirstOrDefault(x => x.Id == request.ShiftTemplateId.Value)
                ?? throw new InvalidOperationException("Không tìm thấy mẫu ca.");
            templateId = t.Id;
            name = t.Name;
            start = t.StartTime;
            end = t.EndTime;
            overnight = t.IsOvernight;
        }
        else
        {
            name = request.CustomShiftName ?? "Ca Tùy Biến";
            start = request.CustomStartTime!;
            end = request.CustomEndTime!;
            overnight = request.IsOvernight ?? false;
        }

        var validation = ValidateConflictAsync(request.EmployeeId, shiftDate, TimeOnly.Parse(start, CultureInfo.InvariantCulture), TimeOnly.Parse(end, CultureInfo.InvariantCulture), overnight, null, cancellationToken).Result;
        if (validation.HasConflict)
        {
            throw new InvalidOperationException(validation.Message);
        }

        var dto = new ShiftAssignmentDto(
            AssignmentId: _nextAssignmentId++,
            ShiftId: 500 + _nextAssignmentId,
            EmployeeId: request.EmployeeId,
            EmployeeName: $"Nhân viên {request.EmployeeId}",
            EmployeeCode: $"NV-{request.EmployeeId:D4}",
            DepartmentName: "Bán hàng",
            ShiftTemplateId: templateId,
            ShiftName: name,
            ShiftDate: request.ShiftDate,
            StartTime: start,
            EndTime: end,
            IsOvernight: overnight,
            Color: "#f97316",
            Status: "Approved",
            Note: request.Note,
            Attendance: null);

        _assignments.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<ShiftAssignmentDto?> UpdateAssignmentAsync(long assignmentId, UpdateShiftAssignmentRequest request, CancellationToken cancellationToken = default)
    {
        var index = _assignments.FindIndex(a => a.AssignmentId == assignmentId);
        if (index < 0) return Task.FromResult<ShiftAssignmentDto?>(null);

        var cur = _assignments[index];
        var updated = cur with
        {
            ShiftName = request.ShiftName ?? cur.ShiftName,
            StartTime = request.StartTime ?? cur.StartTime,
            EndTime = request.EndTime ?? cur.EndTime,
            IsOvernight = request.IsOvernight ?? cur.IsOvernight,
            Status = request.Status ?? cur.Status,
            Note = request.Note ?? cur.Note
        };
        _assignments[index] = updated;
        return Task.FromResult<ShiftAssignmentDto?>(updated);
    }

    public Task<bool> DeleteAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        var item = _assignments.FirstOrDefault(a => a.AssignmentId == assignmentId);
        if (item is null) return Task.FromResult(false);
        _assignments.Remove(item);
        return Task.FromResult(true);
    }

    public Task<BatchAssignShiftResult> BatchAssignShiftsAsync(BatchAssignShiftRequest request, CancellationToken cancellationToken = default)
    {
        var created = new List<ShiftAssignmentDto>();
        var errors = new List<string>();
        foreach (var item in request.Assignments)
        {
            try
            {
                created.Add(AssignShiftAsync(item, cancellationToken).Result);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }

        return Task.FromResult(new BatchAssignShiftResult(request.Assignments.Count, created.Count, errors.Count, errors, created));
    }

    public Task<BatchAssignShiftResult> CopyWeeklyScheduleAsync(DateOnly sourceStartDate, DateOnly targetStartDate, CancellationToken cancellationToken = default)
    {
        var offset = targetStartDate.DayNumber - sourceStartDate.DayNumber;
        var batch = _assignments.Select(a => new AssignShiftRequest(
            a.EmployeeId,
            DateOnly.Parse(a.ShiftDate, CultureInfo.InvariantCulture).AddDays(offset).ToString("yyyy-MM-dd"),
            a.ShiftTemplateId,
            a.ShiftName,
            a.StartTime,
            a.EndTime,
            a.IsOvernight,
            "Sao chép")).ToList();

        return BatchAssignShiftsAsync(new BatchAssignShiftRequest(batch), cancellationToken);
    }
}
