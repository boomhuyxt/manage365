using manage365.Routes.API.Shifts;

namespace manage365.Repositories.Shifts;

public interface IShiftSchedulingRepository
{
    Task<ScheduleEmployeesResponse> GetEmployeesAsync(string? search, long? departmentId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task EnsureTablesAndSeedAsync(CancellationToken cancellationToken = default);

    Task<List<ShiftTemplateDto>> GetTemplatesAsync(bool activeOnly = true, CancellationToken cancellationToken = default);
    Task<ShiftTemplateDto?> GetTemplateByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<ShiftTemplateDto> CreateTemplateAsync(CreateShiftTemplateRequest request, CancellationToken cancellationToken = default);
    Task<ShiftTemplateDto?> UpdateTemplateAsync(long id, UpdateShiftTemplateRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteTemplateAsync(long id, CancellationToken cancellationToken = default);

    Task<WeeklyScheduleResponse> GetScheduleAsync(
        DateOnly startDate,
        DateOnly endDate,
        long? departmentId = null,
        long? employeeId = null,
        CancellationToken cancellationToken = default);

    Task<ShiftAssignmentDto?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default);

    Task<ValidateShiftConflictResponse> ValidateConflictAsync(
        long employeeId,
        DateOnly shiftDate,
        TimeOnly startTime,
        TimeOnly endTime,
        bool isOvernight,
        long? excludeAssignmentId = null,
        CancellationToken cancellationToken = default);

    Task<ShiftAssignmentDto> AssignShiftAsync(AssignShiftRequest request, CancellationToken cancellationToken = default);
    Task<ShiftAssignmentDto?> UpdateAssignmentAsync(long assignmentId, UpdateShiftAssignmentRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default);

    Task<BatchAssignShiftResult> BatchAssignShiftsAsync(BatchAssignShiftRequest request, CancellationToken cancellationToken = default);
    Task<BatchAssignShiftResult> CopyWeeklyScheduleAsync(DateOnly sourceStartDate, DateOnly targetStartDate, CancellationToken cancellationToken = default);
}
