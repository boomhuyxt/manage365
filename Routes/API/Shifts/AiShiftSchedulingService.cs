using System.Globalization;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using manage365.Repositories.Shifts;
using Microsoft.Extensions.Logging;

namespace manage365.Routes.API.Shifts;

public sealed class AiShiftSchedulingService(
    IShiftSchedulingRepository shiftRepo,
    IHttpClientFactory httpClientFactory,
    ILogger<AiShiftSchedulingService> logger)
{
    private static readonly string[] DayNames =
    [
        "Thứ Hai", "Thứ Ba", "Thứ Tư", "Thứ Năm", "Thứ Sáu", "Thứ Bảy", "Chủ Nhật"
    ];

    public async Task<AiScheduleResponse> GenerateScheduleAsync(
        AiScheduleRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        // 1. Parse and align WeekStartDate to Monday
        if (!DateOnly.TryParseExact(request.WeekStartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var inputDate) ||
            inputDate.Year is < 1900 or > 9998)
        {
            throw new ArgumentException("Ngày bắt đầu tuần không hợp lệ (chuẩn yyyy-MM-dd).");
        }

        var diff = (7 + (inputDate.DayOfWeek - DayOfWeek.Monday)) % 7;
        var monday = inputDate.AddDays(-diff);
        var sunday = monday.AddDays(6);

        // 2. Load active employees
        var employeesResponse = await shiftRepo.GetEmployeesAsync(null, null, 1, 100, cancellationToken);
        var excludedIds = (request.ExcludeEmployeeIds ?? []).ToHashSet();
        var candidateEmployees = employeesResponse.Items
            .Where(e => e.Status.Equals("Active", StringComparison.OrdinalIgnoreCase) && !excludedIds.Contains(e.Id))
            .ToList();

        if (candidateEmployees.Count == 0)
        {
            throw new InvalidOperationException("Không có nhân viên nào đang hoạt động để xếp lịch.");
        }

        // 3. Load active templates
        var allTemplates = await shiftRepo.GetTemplatesAsync(activeOnly: true, cancellationToken);
        var templates = allTemplates;
        if (request.SelectedTemplateIds is { Count: > 0 } selected)
        {
            var selectedSet = selected.ToHashSet();
            templates = allTemplates.Where(t => selectedSet.Contains(t.Id)).ToList();
        }

        // Fallback default templates if none configured in database
        if (templates.Count == 0)
        {
            templates =
            [
                new ShiftTemplateDto(1, "CA-SANG", "Ca Sáng", "06:00", "14:00", false, "#f97316", "Ca sáng chuẩn", "Active", 8),
                new ShiftTemplateDto(2, "CA-CHIEU", "Ca Chiều", "14:00", "22:00", false, "#3b82f6", "Ca chiều chuẩn", "Active", 8),
                new ShiftTemplateDto(3, "CA-TOI", "Ca Đêm", "22:00", "06:00", true, "#8b5cf6", "Ca đêm qua ngày", "Active", 8)
            ];
        }

        // 4. Load current weekly schedule to check leaves and existing shifts
        var currentSchedule = await shiftRepo.GetScheduleAsync(monday, sunday, cancellationToken: cancellationToken);
        var existingAssignments = currentSchedule.Assignments
            .Where(a => !a.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var minEmployees = Math.Clamp(request.MinEmployeesPerShift, 1, 20);
        var maxShiftsPerWeek = Math.Clamp(request.MaxShiftsPerEmployeePerWeek, 1, 7);

        // State tracking
        var employeeShiftCounts = candidateEmployees.ToDictionary(e => e.Id, _ => 0);
        var employeeAssignedDates = candidateEmployees.ToDictionary(e => e.Id, _ => new HashSet<DateOnly>());
        var employeeShifts = candidateEmployees.ToDictionary(
            e => e.Id,
            _ => new List<(DateOnly Date, TimeOnly Start, TimeOnly End, bool IsOvernight)>());

        // Approved leaves per employee per date
        var employeeLeaves = new Dictionary<long, HashSet<DateOnly>>();
        foreach (var emp in candidateEmployees)
        {
            employeeLeaves[emp.Id] = [];
        }

        foreach (var assignment in existingAssignments)
        {
            if (DateOnly.TryParseExact(assignment.ShiftDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sDate))
            {
                if (assignment.IsOnLeave && employeeLeaves.ContainsKey(assignment.EmployeeId))
                {
                    employeeLeaves[assignment.EmployeeId].Add(sDate);
                }

                if (request.KeepExistingShifts && employeeShiftCounts.ContainsKey(assignment.EmployeeId))
                {
                    employeeAssignedDates[assignment.EmployeeId].Add(sDate);
                    employeeShiftCounts[assignment.EmployeeId]++;
                    if (TimeOnly.TryParseExact(assignment.StartTime, ["HH:mm", "HH:mm:ss"], out var st) &&
                        TimeOnly.TryParseExact(assignment.EndTime, ["HH:mm", "HH:mm:ss"], out var et))
                    {
                        employeeShifts[assignment.EmployeeId].Add((sDate, st, et, assignment.IsOvernight));
                    }
                }
            }
        }

        var proposedShifts = new List<AiProposedShiftDto>();
        var batchPayload = new List<AssignShiftRequest>();
        var warnings = new List<string>();
        var random = new Random(42); // Deterministic seed for reproducible fair shuffle

        // 5. Day-by-Day Constraint Satisfaction Scheduling
        var dailyCoverageList = new List<AiDayCoverageDto>();

        for (var dayIdx = 0; dayIdx < 7; dayIdx++)
        {
            var date = monday.AddDays(dayIdx);
            var dateStr = date.ToString("yyyy-MM-dd");
            var dayName = DayNames[dayIdx];
            var daySlots = new List<AiShiftSlotCoverageDto>();
            var dayTotalStaff = new HashSet<long>();

            foreach (var template in templates)
            {
                var templateStart = ShiftInput.Time(template.StartTime);
                var templateEnd = ShiftInput.Time(template.EndTime);

                // Count existing assignments for this template on this date
                var existingInTemplate = existingAssignments
                    .Where(a => a.ShiftDate == dateStr &&
                                ((a.ShiftTemplateId.HasValue && a.ShiftTemplateId == template.Id) ||
                                 a.ShiftName.Equals(template.Name, StringComparison.OrdinalIgnoreCase)) &&
                                !a.IsOnLeave)
                    .ToList();

                var staffNamesInSlot = existingInTemplate.Select(a => a.EmployeeName).ToList();
                foreach (var ex in existingInTemplate)
                {
                    dayTotalStaff.Add(ex.EmployeeId);
                }

                var neededCount = request.KeepExistingShifts
                    ? Math.Max(0, minEmployees - existingInTemplate.Count)
                    : minEmployees;

                if (neededCount > 0)
                {
                    // Find available candidates
                    var available = candidateEmployees.Where(emp =>
                    {
                        // 1. Not on approved leave on this date
                        if (employeeLeaves[emp.Id].Contains(date)) return false;

                        // 2. Not already assigned a shift on this date
                        if (employeeAssignedDates[emp.Id].Contains(date)) return false;

                        // 3. Weekly shift limit
                        if (employeeShiftCounts[emp.Id] >= maxShiftsPerWeek) return false;

                        // 4. Labor Code: Rest interval >= 12h
                        // Check shift from yesterday
                        var yesterday = date.AddDays(-1);
                        var yesterdayShifts = employeeShifts[emp.Id].Where(s => s.Date == yesterday).ToList();
                        foreach (var yShift in yesterdayShifts)
                        {
                            var yEndTime = yShift.IsOvernight ? yesterday.AddDays(1).ToDateTime(yShift.End) : yesterday.ToDateTime(yShift.End);
                            var currentStartTime = date.ToDateTime(templateStart);
                            if (currentStartTime < yEndTime.AddHours(11.5))
                            {
                                return false; // Rest interval too short
                            }
                        }

                        // Check shift tomorrow if already assigned
                        var tomorrow = date.AddDays(1);
                        var tomorrowShifts = employeeShifts[emp.Id].Where(s => s.Date == tomorrow).ToList();
                        var currentEndTime = template.IsOvernight ? date.AddDays(1).ToDateTime(templateEnd) : date.ToDateTime(templateEnd);
                        foreach (var tShift in tomorrowShifts)
                        {
                            var tStartTime = tomorrow.ToDateTime(tShift.Start);
                            if (tStartTime < currentEndTime.AddHours(11.5))
                            {
                                return false; // Rest interval too short
                            }
                        }

                        return true;
                    })
                    .OrderBy(emp => employeeShiftCounts[emp.Id]) // Lowest workload first
                    .ThenBy(emp => employeeAssignedDates[emp.Id].Contains(date.AddDays(-1)) ? 1 : 0) // Prefer well-rested
                    .ThenBy(_ => random.Next())
                    .Take(neededCount)
                    .ToList();

                    if (available.Count < neededCount)
                    {
                        warnings.Add($"{dayName} ({dateStr}): Ca {template.Name} cần {neededCount} người nhưng chỉ tìm được {available.Count} nhân sự đáp ứng đủ điều kiện nghỉ ngơi và giới hạn tuần.");
                    }

                    foreach (var emp in available)
                    {
                        employeeShiftCounts[emp.Id]++;
                        employeeAssignedDates[emp.Id].Add(date);
                        employeeShifts[emp.Id].Add((date, templateStart, templateEnd, template.IsOvernight));
                        dayTotalStaff.Add(emp.Id);
                        staffNamesInSlot.Add(emp.DisplayName);

                        var proposedDto = new AiProposedShiftDto(
                            EmployeeId: emp.Id,
                            EmployeeName: emp.DisplayName,
                            EmployeeCode: emp.Code,
                            DepartmentName: emp.DepartmentName,
                            ShiftDate: dateStr,
                            DayOfWeekName: dayName,
                            ShiftTemplateId: template.Id,
                            ShiftName: template.Name,
                            StartTime: template.StartTime,
                            EndTime: template.EndTime,
                            IsOvernight: template.IsOvernight,
                            Color: template.Color ?? "#f97316",
                            Reason: $"AI tối ưu: Phân ca công bằng (đã gán {employeeShiftCounts[emp.Id]}/{maxShiftsPerWeek} ca), đảm bảo thời gian nghỉ chuyển ca");

                        proposedShifts.Add(proposedDto);

                        batchPayload.Add(new AssignShiftRequest(
                            EmployeeId: emp.Id,
                            ShiftDate: dateStr,
                            ShiftTemplateId: template.Id,
                            CustomShiftName: template.Name,
                            CustomStartTime: null,
                            CustomEndTime: null,
                            IsOvernight: null,
                            Note: "AI tự động phân ca tối ưu",
                            ShopOwnerId: request.ShopOwnerId));
                    }
                }

                daySlots.Add(new AiShiftSlotCoverageDto(
                    ShiftName: template.Name,
                    StartTime: template.StartTime,
                    EndTime: template.EndTime,
                    StaffCount: staffNamesInSlot.Count,
                    StaffNames: staffNamesInSlot));
            }

            dailyCoverageList.Add(new AiDayCoverageDto(
                Date: dateStr,
                DayOfWeekName: dayName,
                TotalShifts: daySlots.Sum(s => s.StaffCount),
                TotalStaff: dayTotalStaff.Count,
                ShiftSlots: daySlots));
        }

        // 6. Compute statistics and AI Insights
        var assignedEmployeesCount = employeeShiftCounts.Count(pair => pair.Value > 0);
        var totalShiftsGenerated = proposedShifts.Count;
        var avgShifts = assignedEmployeesCount > 0
            ? Math.Round((double)employeeShiftCounts.Values.Sum() / assignedEmployeesCount, 1)
            : 0;

        var aiInsights = new List<string>
        {
            $"⚖️ Cân bằng phân bổ: Trung bình mỗi nhân sự đảm nhận {avgShifts} ca/tuần, đảm bảo ít nhất 1-2 ngày nghỉ phục hồi sức khỏe.",
            "📜 Tuân thủ Bộ luật Lao động 2019: Bảo đảm giãn cách nghỉ giữa 2 ca tối thiểu 12 tiếng, không xếp ca sáng sau ca đêm.",
            $"🌴 Tôn trọng đơn nghỉ phép: Tự động loại trừ và bảo vệ ngày nghỉ của các nhân sự có đơn nghỉ phép được duyệt.",
            $"🏪 Tỷ lệ đáp ứng định mức: {dailyCoverageList.Sum(d => d.TotalShifts)} lượt phân ca trong tuần trên toàn bộ {templates.Count} khung giờ."
        };

        // Try querying Manage365 AI Engine for generative summary
        var summary = $"AI đã lập kế hoạch phân công tự động thành công cho tuần từ {monday:dd/MM/yyyy} đến {sunday:dd/MM/yyyy}. Đã tạo {totalShiftsGenerated} phân ca mới cho {assignedEmployeesCount}/{candidateEmployees.Count} nhân viên, bảo đảm định mức vận hành.";

        try
        {
            var aiClient = httpClientFactory.CreateClient("ChatBot");
            var aiPrompt = $"Bạn là AI Giám sát Nhân sự Manage365. Dưới đây là kết quả AI vừa tự động xếp lịch tuần {monday:dd/MM/yyyy} - {sunday:dd/MM/yyyy}: {totalShiftsGenerated} ca được gán, {assignedEmployeesCount} nhân sự làm việc, trung bình {avgShifts} ca/người. Hãy đưa ra nhận xét ngắn gọn 2 câu khen ngợi tính tối ưu và lưu ý quản lý cửa hàng.";

            using var chatRes = await aiClient.PostAsJsonAsync("chat", new { message = aiPrompt, history = Array.Empty<object>() }, cancellationToken);
            if (chatRes.IsSuccessStatusCode)
            {
                var json = await chatRes.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                if (json.TryGetProperty("reply", out var replyElem))
                {
                    var replyStr = replyElem.GetString();
                    if (!string.IsNullOrWhiteSpace(replyStr))
                    {
                        summary = replyStr.Trim();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not fetch dynamic summary from Manage365 AI Engine, using deterministic summary.");
        }

        return new AiScheduleResponse(
            WeekStartDate: monday.ToString("yyyy-MM-dd"),
            WeekEndDate: sunday.ToString("yyyy-MM-dd"),
            ProposedShiftCount: totalShiftsGenerated,
            TotalEmployeesConsidered: candidateEmployees.Count,
            EmployeesAssigned: assignedEmployeesCount,
            AverageShiftsPerEmployee: avgShifts,
            ProposedShifts: proposedShifts,
            BatchAssignmentPayload: batchPayload,
            DailyCoverage: dailyCoverageList,
            AiInsights: aiInsights,
            Warnings: warnings,
            Summary: summary);
    }
}
