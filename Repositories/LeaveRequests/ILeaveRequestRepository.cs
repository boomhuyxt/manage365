using manage365.Routes.API.LeaveRequests;

namespace manage365.Repositories.LeaveRequests;

public interface ILeaveRequestRepository
{
    Task EnsureTablesAsync(CancellationToken cancellationToken = default);

    Task<LeaveOptionsResponse> GetOptionsAsync(bool isAdmin, CancellationToken cancellationToken = default);

    Task<LeaveRequestPreviewResponse> GeneratePreviewAsync(
        long employeeId,
        CreateLeaveRequestInput input,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<LeaveRequestPreviewResponse> GenerateReviewPreviewAsync(
        long leaveRequestId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default,
        long? adminId = null);

    Task<LeaveRequestDto> CreateLeaveRequestAsync(
        long employeeId,
        CreateLeaveRequestCommand command,
        string? idempotencyKey,
        string? requestHash,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<LeaveRequestDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<LeaveRequestDetailDto?> GetDetailByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<PagedLeaveRequestsResponse> GetMyLeaveRequestsAsync(
        long employeeId,
        string? status,
        string? unit,
        DateOnly? fromDate,
        DateOnly? toDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedLeaveRequestsResponse> GetLeaveRequestsAdminAsync(
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
        CancellationToken cancellationToken = default);

    Task<LeaveRequestDto> CancelLeaveRequestAsync(
        long leaveRequestId,
        long employeeId,
        CancelLeaveRequestCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<LeaveRequestDto> ApproveLeaveRequestAsync(
        long leaveRequestId,
        long adminId,
        ApproveLeaveRequestCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<LeaveRequestDto> RejectLeaveRequestAsync(
        long leaveRequestId,
        long adminId,
        RejectLeaveRequestCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<LeaveRequestDto> RevokeLeaveRequestAsync(
        long leaveRequestId,
        long adminId,
        RevokeLeaveRequestCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<PayrollLeaveSummaryResponse> GetPayrollSummaryAsync(
        DateOnly fromDate,
        DateOnly toDate,
        long? employeeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<bool> HasApprovedLeaveConflictAsync(
        long employeeId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default);

    Task<(int StatusCode, string ResponseJson)?> GetIdempotencyRecordAsync(
        long employeeId,
        string key,
        string endpoint,
        CancellationToken cancellationToken = default);

    Task SaveIdempotencyRecordAsync(
        long employeeId,
        string key,
        string endpoint,
        string requestHash,
        string responseJson,
        int statusCode,
        CancellationToken cancellationToken = default);
}
