using System.Security.Cryptography;
using System.Text;
using manage365.Routes.API.LeaveRequests;

namespace manage365.Repositories.LeaveRequests;

public sealed partial class PostgresLeaveRequestRepository
{
    private sealed record InternalShiftAssignment(
        long AssignmentId,
        long ShiftId,
        string ShiftName,
        DateOnly ShiftDate,
        TimeSpan StartTime,
        TimeSpan EndTime,
        bool IsOvernight,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        int ScheduledMinutes);

    private sealed record PreviewCachePayload(
        string PreviewToken,
        long EmployeeId,
        long? LeaveRequestId,
        string RequestHash,
        string Unit,
        string ReasonCode,
        string Reason,
        bool IsEmergency,
        bool IsLateSubmission,
        string? LateSubmissionReason,
        long? ShiftAssignmentId,
        string? Date,
        string? StartDate,
        string? EndDate,
        int? CalendarDays,
        int ScheduledLeaveMinutes,
        string ScheduleBasis,
        bool IsLongLeave,
        bool HasExpandedCoverage,
        string RequestedPeriod,
        IReadOnlyList<LeaveIntervalDto> EffectiveIntervals,
        IReadOnlyList<AffectedAssignmentDto> AffectedAssignments,
        IReadOnlyList<LeaveDayAllocationDto> DayAllocations,
        int? ExistingVersion,
        DateTimeOffset ExpiresAtUtc,
        long? AdminId = null);

    private static DateTimeOffset ConvertLocalToUtc(DateTime localDateTime)
    {
        var unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, VietnamTimeZone);
    }

    private static DateTime ConvertUtcToLocal(DateTimeOffset utc)
    {
        return TimeZoneInfo.ConvertTime(utc, VietnamTimeZone).DateTime;
    }

    private static string ComputeRequestHash(CreateLeaveRequestInput input)
    {
        var raw = System.Text.Json.JsonSerializer.Serialize(input);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static List<LeaveIntervalDto> MergeIntervals(List<(DateTimeOffset Start, DateTimeOffset End)> intervals)
    {
        if (intervals.Count == 0) return [];

        var sorted = intervals.OrderBy(x => x.Start).ToList();
        var merged = new List<(DateTimeOffset Start, DateTimeOffset End)>();

        var current = sorted[0];
        for (int i = 1; i < sorted.Count; i++)
        {
            var next = sorted[i];
            if (next.Start <= current.End)
            {
                current = (current.Start, next.End > current.End ? next.End : current.End);
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }
        merged.Add(current);

        return merged.Select(m => new LeaveIntervalDto(
            StartUtc: m.Start,
            EndUtc: m.End,
            StartLocal: ConvertUtcToLocal(m.Start).ToString("yyyy-MM-dd HH:mm"),
            EndLocal: ConvertUtcToLocal(m.End).ToString("yyyy-MM-dd HH:mm"))).ToList();
    }

    private static List<LeaveDayAllocationDto> CalculateDayAllocations(
        IReadOnlyList<AffectedAssignmentDto> affectedAssignments)
    {
        var dict = new Dictionary<DateOnly, int>();

        foreach (var assignment in affectedAssignments)
        {
            var shiftDate = DateOnly.ParseExact(assignment.ShiftDate, "yyyy-MM-dd");
            var start = TimeSpan.Parse(assignment.StartTime);
            var end = TimeSpan.Parse(assignment.EndTime);

            if (!assignment.IsOvernight)
            {
                var mins = (int)(end - start).TotalMinutes;
                dict[shiftDate] = dict.GetValueOrDefault(shiftDate) + mins;
            }
            else
            {
                // Day 1: start to 24:00
                var day1Mins = (int)(TimeSpan.FromHours(24) - start).TotalMinutes;
                dict[shiftDate] = dict.GetValueOrDefault(shiftDate) + day1Mins;

                // Day 2: 00:00 to end
                var day2 = shiftDate.AddDays(1);
                var day2Mins = (int)end.TotalMinutes;
                dict[day2] = dict.GetValueOrDefault(day2) + day2Mins;
            }
        }

        return dict.OrderBy(k => k.Key)
            .Select(kv => new LeaveDayAllocationDto(kv.Key.ToString("yyyy-MM-dd"), kv.Value))
            .ToList();
    }
}
