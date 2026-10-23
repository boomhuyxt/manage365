using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using manage365.Repositories.LeaveRequests;
using manage365.Routes.API.Auth;
using manage365.Routes.API.LeaveRequests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace manage365.Tests;

public sealed class FakeLeaveRequestRepository : ILeaveRequestRepository
{
    public List<LeaveRequestDto> Items { get; } = [];
    public Dictionary<string, (int, string)> IdempotencyStore { get; } = [];

    public Task EnsureTablesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<LeaveOptionsResponse> GetOptionsAsync(bool isAdmin, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new LeaveOptionsResponse(
            Units: LeaveUnits.All,
            ReasonCodes: LeaveReasonCodes.All,
            PayrollTreatments: isAdmin ? LeavePayrollTreatments.All : null,
            MinReasonLength: 10,
            MaxReasonLength: 2000,
            MaxTechnicalDays: 366,
            LongLeaveThresholdDays: 30,
            LateSubmissionWindowDays: 30));
    }

    public Task<LeaveRequestPreviewResponse> GeneratePreviewAsync(
        long employeeId,
        CreateLeaveRequestInput input,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (input.Unit == LeaveUnits.Shift && !input.ShiftAssignmentId.HasValue)
        {
            throw new LeaveValidationException("invalid_unit_fields", "Với đơn vị Shift, shiftAssignmentId là bắt buộc.");
        }
        if (input.Unit == LeaveUnits.Day && string.IsNullOrWhiteSpace(input.Date))
        {
            throw new LeaveValidationException("invalid_unit_fields", "Với đơn vị Day, date là bắt buộc.");
        }
        if (input.Unit == LeaveUnits.DateRange && (string.IsNullOrWhiteSpace(input.StartDate) || string.IsNullOrWhiteSpace(input.EndDate)))
        {
            throw new LeaveValidationException("invalid_unit_fields", "Với đơn vị DateRange, startDate và endDate là bắt buộc.");
        }

        var expires = nowUtc.AddMinutes(10);
        return Task.FromResult(new LeaveRequestPreviewResponse(
            PreviewToken: "test_token_123",
            ExpiresAtUtc: expires,
            RequestedPeriod: "Ca sáng ngày 2026-11-03",
            EffectiveIntervals:
            [
                new(nowUtc.AddDays(1), nowUtc.AddDays(1).AddHours(8), "2026-11-03 08:00", "2026-11-03 16:00")
            ],
            AffectedAssignments:
            [
                new(1, 10, "Ca Sáng", "2026-11-03", "08:00", "16:00", false, 480, false)
            ],
            ScheduledLeaveMinutes: 480,
            CalendarDays: input.Unit == LeaveUnits.Day ? 1 : null,
            ScheduleBasis: LeaveScheduleBases.Available,
            IsLateSubmission: false,
            IsLongLeave: false,
            HasExpandedCoverage: false,
            Warnings: [],
            HasConflict: false,
            ConflictReason: null,
            ExistingVersion: null));
    }

    public Task<LeaveRequestPreviewResponse> GenerateReviewPreviewAsync(
        long leaveRequestId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default,
        long? adminId = null)
    {
        var existing = Items.FirstOrDefault(i => i.Id == leaveRequestId)
            ?? throw new LeaveNotFoundException("leave_request_not_found", "Không tìm thấy đơn.");

        return Task.FromResult(new LeaveRequestPreviewResponse(
            PreviewToken: "review_token_123",
            ExpiresAtUtc: nowUtc.AddMinutes(10),
            RequestedPeriod: existing.RequestedPeriod,
            EffectiveIntervals: existing.EffectiveIntervals,
            AffectedAssignments: [],
            ScheduledLeaveMinutes: existing.ScheduledLeaveMinutes,
            CalendarDays: existing.CalendarDays,
            ScheduleBasis: existing.ScheduleBasis,
            IsLateSubmission: existing.IsLateSubmission,
            IsLongLeave: (existing.CalendarDays ?? 0) > 30,
            HasExpandedCoverage: false,
            Warnings: [],
            HasConflict: false,
            ConflictReason: null,
            ExistingVersion: existing.Version));
    }

    public Task<LeaveRequestDto> CreateLeaveRequestAsync(
        long employeeId,
        CreateLeaveRequestCommand command,
        string? idempotencyKey,
        string? requestHash,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var id = Items.Count + 1;
        var dto = new LeaveRequestDto(
            Id: id,
            Version: 1,
            EmployeeId: employeeId,
            EmployeeName: "Test Employee",
            EmployeeCode: $"NV-{employeeId:D4}",
            EmployeeTypeSnapshot: "FullTime",
            DepartmentName: "Kho Vận",
            Unit: command.Unit,
            ReasonCode: command.ReasonCode,
            Reason: command.Reason,
            IsEmergency: command.IsEmergency,
            IsLateSubmission: false,
            LateSubmissionReason: command.LateSubmissionReason,
            ShiftAssignmentId: command.ShiftAssignmentId,
            Date: command.Date,
            StartDate: command.StartDate,
            EndDate: command.EndDate,
            CalendarDays: command.Unit == LeaveUnits.Day ? 1 : null,
            ScheduledLeaveMinutes: 480,
            AffectedAssignmentCount: 1,
            ScheduleBasis: LeaveScheduleBases.Available,
            Status: LeaveStatuses.Pending,
            PayrollTreatment: LeavePayrollTreatments.Unresolved,
            PayrollReadiness: LeavePayrollReadinesses.NeedsReview,
            CreatedAtUtc: nowUtc,
            UpdatedAtUtc: nowUtc,
            ReviewedById: null,
            ReviewedByName: null,
            ReviewedAtUtc: null,
            ReviewNote: null,
            RejectionReason: null,
            RevocationReason: null,
            CancelledAtUtc: null,
            RevokedAtUtc: null,
            RequestedPeriod: "Ca sáng",
            EffectiveIntervals:
            [
                new(nowUtc.AddDays(1), nowUtc.AddDays(1).AddHours(8), "2026-11-03 08:00", "2026-11-03 16:00")
            ]);

        Items.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<LeaveRequestDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Items.FirstOrDefault(i => i.Id == id));
    }

    public Task<LeaveRequestDetailDto?> GetDetailByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return Task.FromResult<LeaveRequestDetailDto?>(null);

        return Task.FromResult<LeaveRequestDetailDto?>(new LeaveRequestDetailDto(
            Request: item,
            AffectedAssignments: [],
            DayAllocations: [],
            History:
            [
                new(1, item.EmployeeId, item.EmployeeName, "CREATED", null, "Pending", "Gửi đơn", item.CreatedAtUtc)
            ]));
    }

    public Task<PagedLeaveRequestsResponse> GetMyLeaveRequestsAsync(
        long employeeId,
        string? status,
        string? unit,
        DateOnly? fromDate,
        DateOnly? toDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var list = Items.Where(i => i.EmployeeId == employeeId).ToList();
        return Task.FromResult(new PagedLeaveRequestsResponse(list, 1, pageSize, list.Count, 1));
    }

    public Task<PagedLeaveRequestsResponse> GetLeaveRequestsAdminAsync(
        string? status,
        string? unit,
        string? reasonCode,
        string? payrollTreatment,
        long? employeeId,
        long? departmentId,
        DateOnly? fromDate,
        DateOnly? toDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PagedLeaveRequestsResponse(Items, 1, pageSize, Items.Count, 1));
    }

    public Task<LeaveRequestDto> CancelLeaveRequestAsync(
        long leaveRequestId,
        long employeeId,
        CancelLeaveRequestCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var item = Items.FirstOrDefault(i => i.Id == leaveRequestId)
            ?? throw new LeaveNotFoundException();

        if (item.EmployeeId != employeeId) throw new LeaveNotFoundException();
        if (item.Status != LeaveStatuses.Pending) throw new LeaveConflictException("invalid_leave_status", "Chỉ hủy đơn Pending.");
        if (item.Version != command.Version) throw new LeaveConflictException("stale_version", "Stale version.");

        var updated = item with { Status = LeaveStatuses.Cancelled, Version = item.Version + 1, CancelledAtUtc = nowUtc };
        Items[Items.IndexOf(item)] = updated;
        return Task.FromResult(updated);
    }

    public Task<LeaveRequestDto> ApproveLeaveRequestAsync(
        long leaveRequestId,
        long adminId,
        ApproveLeaveRequestCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var item = Items.FirstOrDefault(i => i.Id == leaveRequestId)
            ?? throw new LeaveNotFoundException();

        if (item.EmployeeId == adminId) throw new LeaveForbiddenException("self_review_not_allowed", "Admin không được tự duyệt đơn của mình.");
        if (item.Status != LeaveStatuses.Pending) throw new LeaveConflictException("invalid_leave_status", "Chỉ duyệt đơn Pending.");
        if (item.Version != command.Version) throw new LeaveConflictException("stale_version", "Stale version.");

        var updated = item with
        {
            Status = LeaveStatuses.Approved,
            Version = item.Version + 1,
            ReviewedById = adminId,
            ReviewedByName = "Admin User",
            ReviewedAtUtc = nowUtc,
            ReviewNote = command.ReviewNote,
            PayrollTreatment = command.PayrollTreatment
        };
        Items[Items.IndexOf(item)] = updated;
        return Task.FromResult(updated);
    }

    public Task<LeaveRequestDto> RejectLeaveRequestAsync(
        long leaveRequestId,
        long adminId,
        RejectLeaveRequestCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var item = Items.FirstOrDefault(i => i.Id == leaveRequestId)
            ?? throw new LeaveNotFoundException();

        if (item.EmployeeId == adminId) throw new LeaveForbiddenException("self_review_not_allowed", "Admin không được tự từ chối đơn của mình.");
        if (item.Status != LeaveStatuses.Pending) throw new LeaveConflictException("invalid_leave_status", "Chỉ từ chối đơn Pending.");

        var updated = item with
        {
            Status = LeaveStatuses.Rejected,
            Version = item.Version + 1,
            ReviewedById = adminId,
            ReviewedByName = "Admin User",
            ReviewedAtUtc = nowUtc,
            RejectionReason = command.Reason
        };
        Items[Items.IndexOf(item)] = updated;
        return Task.FromResult(updated);
    }

    public Task<LeaveRequestDto> RevokeLeaveRequestAsync(
        long leaveRequestId,
        long adminId,
        RevokeLeaveRequestCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var item = Items.FirstOrDefault(i => i.Id == leaveRequestId)
            ?? throw new LeaveNotFoundException();

        if (item.Status != LeaveStatuses.Approved) throw new LeaveConflictException("invalid_leave_status", "Chỉ thu hồi đơn Approved.");

        var updated = item with
        {
            Status = LeaveStatuses.Revoked,
            Version = item.Version + 1,
            RevokedAtUtc = nowUtc,
            RevocationReason = command.Reason
        };
        Items[Items.IndexOf(item)] = updated;
        return Task.FromResult(updated);
    }

    public Task<PayrollLeaveSummaryResponse> GetPayrollSummaryAsync(
        DateOnly fromDate,
        DateOnly toDate,
        long? employeeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PayrollLeaveSummaryResponse([], fromDate.ToString("yyyy-MM-dd"), toDate.ToString("yyyy-MM-dd"), 1, pageSize, 0, 0));
    }

    public Task<bool> HasApprovedLeaveConflictAsync(long employeeId, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<(int StatusCode, string ResponseJson)?> GetIdempotencyRecordAsync(long employeeId, string key, string endpoint, CancellationToken cancellationToken = default)
    {
        var fullKey = $"{employeeId}:{key}:{endpoint}";
        if (IdempotencyStore.TryGetValue(fullKey, out var tuple))
        {
            return Task.FromResult<(int, string)?>(tuple);
        }
        return Task.FromResult<(int, string)?>(null);
    }

    public Task SaveIdempotencyRecordAsync(long employeeId, string key, string endpoint, string requestHash, string responseJson, int statusCode, CancellationToken cancellationToken = default)
    {
        var fullKey = $"{employeeId}:{key}:{endpoint}";
        IdempotencyStore[fullKey] = (statusCode, responseJson);
        return Task.CompletedTask;
    }
}

public sealed class LeaveRequestsControllerTests
{
    private readonly FakeLeaveRequestRepository _repository;
    private readonly LeaveRequestsController _controller;

    public LeaveRequestsControllerTests()
    {
        _repository = new FakeLeaveRequestRepository();
        _controller = new LeaveRequestsController(_repository)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, "101"),
                        new Claim(JwtRegisteredClaimNames.Email, "staff@manage365.io.vn"),
                        new Claim(ClaimTypes.Role, AppRoles.Employee)
                    ], "TestAuth"))
                }
            }
        };
    }

    [Fact]
    public async Task GetOptions_ReturnsUnitsAndReasonCodes()
    {
        var result = await _controller.GetOptions(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var options = Assert.IsType<LeaveOptionsResponse>(ok.Value);

        Assert.Contains(LeaveUnits.Shift, options.Units);
        Assert.Contains(LeaveUnits.Day, options.Units);
        Assert.Contains(LeaveUnits.DateRange, options.Units);
        Assert.Contains(LeaveReasonCodes.Personal, options.ReasonCodes);
    }

    [Fact]
    public async Task Preview_ValidInput_ReturnsPreviewResponse()
    {
        var input = new CreateLeaveRequestInput
        {
            Unit = LeaveUnits.Day,
            Date = "2026-11-03",
            ReasonCode = LeaveReasonCodes.Personal,
            Reason = "Nghỉ để giải quyết việc gia đình"
        };

        var result = await _controller.Preview(input, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var preview = Assert.IsType<LeaveRequestPreviewResponse>(ok.Value);

        Assert.NotNull(preview.PreviewToken);
        Assert.Equal(480, preview.ScheduledLeaveMinutes);
    }

    [Fact]
    public async Task Create_ValidCommand_Returns201Created()
    {
        var command = new CreateLeaveRequestCommand
        {
            Unit = LeaveUnits.Day,
            Date = "2026-11-03",
            ReasonCode = LeaveReasonCodes.Personal,
            Reason = "Nghỉ để giải quyết việc gia đình",
            PreviewToken = "test_token_123"
        };

        var result = await _controller.Create(command, null, CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<LeaveRequestDto>(created.Value);

        Assert.Equal(LeaveStatuses.Pending, dto.Status);
        Assert.Equal(101, dto.EmployeeId);
    }

    [Fact]
    public async Task Cancel_WhenPending_ReturnsCancelled()
    {
        var created = await _repository.CreateLeaveRequestAsync(101, new CreateLeaveRequestCommand
        {
            Unit = LeaveUnits.Day,
            Date = "2026-11-03",
            ReasonCode = LeaveReasonCodes.Personal,
            Reason = "Nghỉ việc gia đình",
            PreviewToken = "token"
        }, null, null, DateTimeOffset.UtcNow);

        var cancelCmd = new CancelLeaveRequestCommand { Version = 1, Note = "Đổi ý không nghỉ nữa" };
        var result = await _controller.Cancel(created.Id, cancelCmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<LeaveRequestDto>(ok.Value);
        Assert.Equal(LeaveStatuses.Cancelled, dto.Status);
    }

    [Fact]
    public async Task Approve_WhenAdminApprovingEmployee_ReturnsApproved()
    {
        var created = await _repository.CreateLeaveRequestAsync(101, new CreateLeaveRequestCommand
        {
            Unit = LeaveUnits.Day,
            Date = "2026-11-03",
            ReasonCode = LeaveReasonCodes.Personal,
            Reason = "Nghỉ việc gia đình",
            PreviewToken = "token"
        }, null, null, DateTimeOffset.UtcNow);

        // Switch user context to Admin (id = 999)
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, "999"),
            new Claim(ClaimTypes.Role, AppRoles.Admin)
        ], "TestAuth"));

        var approveCmd = new ApproveLeaveRequestCommand
        {
            Version = 1,
            PreviewToken = "review_token",
            PayrollTreatment = LeavePayrollTreatments.PaidLeave,
            ReviewNote = "Đã duyệt cho nhân viên"
        };

        var result = await _controller.Approve(created.Id, approveCmd, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<LeaveRequestDto>(ok.Value);

        Assert.Equal(LeaveStatuses.Approved, dto.Status);
        Assert.Equal(999, dto.ReviewedById);
        Assert.Equal(LeavePayrollTreatments.PaidLeave, dto.PayrollTreatment);
    }

    [Fact]
    public async Task Approve_WhenAdminSelfReviewing_ThrowsSelfReviewForbidden()
    {
        var created = await _repository.CreateLeaveRequestAsync(999, new CreateLeaveRequestCommand
        {
            Unit = LeaveUnits.Day,
            Date = "2026-11-03",
            ReasonCode = LeaveReasonCodes.Personal,
            Reason = "Nghỉ việc gia đình",
            PreviewToken = "token"
        }, null, null, DateTimeOffset.UtcNow);

        // User is Admin with id = 999
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, "999"),
            new Claim(ClaimTypes.Role, AppRoles.Admin)
        ], "TestAuth"));

        var approveCmd = new ApproveLeaveRequestCommand
        {
            Version = 1,
            PreviewToken = "review_token",
            PayrollTreatment = LeavePayrollTreatments.PaidLeave
        };

        await Assert.ThrowsAsync<LeaveForbiddenException>(() =>
            _controller.Approve(created.Id, approveCmd, CancellationToken.None));
    }

    [Fact]
    public async Task Reject_WhenAdminWithReason_ReturnsRejected()
    {
        var created = await _repository.CreateLeaveRequestAsync(101, new CreateLeaveRequestCommand
        {
            Unit = LeaveUnits.Day,
            Date = "2026-11-03",
            ReasonCode = LeaveReasonCodes.Personal,
            Reason = "Nghỉ việc gia đình",
            PreviewToken = "token"
        }, null, null, DateTimeOffset.UtcNow);

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, "999"),
            new Claim(ClaimTypes.Role, AppRoles.Admin)
        ], "TestAuth"));

        var rejectCmd = new RejectLeaveRequestCommand { Version = 1, Reason = "Cửa hàng đang thiếu người trực" };
        var result = await _controller.Reject(created.Id, rejectCmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<LeaveRequestDto>(ok.Value);
        Assert.Equal(LeaveStatuses.Rejected, dto.Status);
        Assert.Equal("Cửa hàng đang thiếu người trực", dto.RejectionReason);
    }

    [Fact]
    public async Task Revoke_WhenApproved_ReturnsRevoked()
    {
        var created = await _repository.CreateLeaveRequestAsync(101, new CreateLeaveRequestCommand
        {
            Unit = LeaveUnits.Day,
            Date = "2026-11-03",
            ReasonCode = LeaveReasonCodes.Personal,
            Reason = "Nghỉ việc gia đình",
            PreviewToken = "token"
        }, null, null, DateTimeOffset.UtcNow);

        await _repository.ApproveLeaveRequestAsync(created.Id, 999, new ApproveLeaveRequestCommand
        {
            Version = 1,
            PreviewToken = "token",
            PayrollTreatment = LeavePayrollTreatments.PaidLeave
        }, DateTimeOffset.UtcNow);

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, "999"),
            new Claim(ClaimTypes.Role, AppRoles.Admin)
        ], "TestAuth"));

        var revokeCmd = new RevokeLeaveRequestCommand { Version = 2, Reason = "Kế hoạch thay đổi, nhân viên đồng ý đi làm lại" };
        var result = await _controller.Revoke(created.Id, revokeCmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<LeaveRequestDto>(ok.Value);
        Assert.Equal(LeaveStatuses.Revoked, dto.Status);
    }

    [Fact]
    public async Task GetById_WhenUnauthorizedEmployeeAccessingOther_Returns404()
    {
        var created = await _repository.CreateLeaveRequestAsync(202, new CreateLeaveRequestCommand
        {
            Unit = LeaveUnits.Day,
            Date = "2026-11-03",
            ReasonCode = LeaveReasonCodes.Personal,
            Reason = "Nghỉ việc gia đình",
            PreviewToken = "token"
        }, null, null, DateTimeOffset.UtcNow);

        // Current user is 101, accessing 202's request
        var result = await _controller.GetById(created.Id, CancellationToken.None);
        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
    }
}
