namespace manage365.Routes.API.Shifts;

/// <summary>A proposed interval in Vietnam local time, independent of a template.</summary>
public sealed record ShiftCoverageInterval(long EmployeeId, string ShiftDate, string StartTime, string EndTime,
    bool IsOvernight = false, long? ExcludeAssignmentId = null);
public sealed record ShiftCoverageRequest(List<ShiftCoverageInterval> Assignments);
public sealed record ShiftCoverageSegment(string Start, string End, List<long> CurrentEmployeeIds, List<long> ProjectedEmployeeIds);
public sealed record ShiftCoverageResponse(List<ShiftCoverageSegment> Segments);

/// <summary>Counts distinct employees on half-open intervals; never rejects different employees sharing hours.</summary>
public static class ShiftCoverage
{
    public static (DateTime Start, DateTime End) Bounds(ShiftCoverageInterval item)
    {
        if (item.EmployeeId <= 0 || item.ExcludeAssignmentId is <= 0)
            throw new ArgumentException("ID nhân viên/phân ca phải là số dương.");
        var date = ShiftInput.Date(item.ShiftDate);
        var start = ShiftInput.Time(item.StartTime);
        var end = ShiftInput.Time(item.EndTime);
        ShiftInput.Interval(start, end, item.IsOvernight);
        return (date.ToDateTime(start), date.AddDays(item.IsOvernight ? 1 : 0).ToDateTime(end));
    }

    public static ShiftCoverageResponse Calculate(List<ShiftCoverageInterval> proposals, List<ShiftAssignmentDto> assignments)
    {
        var proposed = proposals.Select(p => (p.EmployeeId, p.ExcludeAssignmentId, Range: Bounds(p))).ToList();
        var existing = assignments.Where(a => a.Status != "Cancelled")
            .Select(a => (a.EmployeeId, a.AssignmentId, Range: Bounds(new(a.EmployeeId, a.ShiftDate, a.StartTime, a.EndTime, a.IsOvernight))))
            .Where(a => proposed.Any(p => a.Range.Start < p.Range.End && p.Range.Start < a.Range.End)).ToList();
        var excluded = proposals.Where(p => p.ExcludeAssignmentId.HasValue).Select(p => p.ExcludeAssignmentId!.Value).ToHashSet();
        var boundaries = proposed.SelectMany(p => new[] { p.Range.Start, p.Range.End })
            .Concat(existing.SelectMany(a => new[] { a.Range.Start, a.Range.End })).Distinct().Order().ToList();
        var segments = new List<ShiftCoverageSegment>();
        for (var i = 0; i + 1 < boundaries.Count; i++)
        {
            var start = boundaries[i];
            var end = boundaries[i + 1];
            if (!proposed.Any(p => p.Range.Start < end && start < p.Range.End)) continue;
            var current = existing.Where(a => a.Range.Start < end && start < a.Range.End).ToList();
            var currentIds = current.Select(a => a.EmployeeId).Distinct().Order().ToList();
            var projectedIds = current.Where(a => !excluded.Contains(a.AssignmentId)).Select(a => a.EmployeeId)
                .Concat(proposed.Where(p => p.Range.Start < end && start < p.Range.End).Select(p => p.EmployeeId)).Distinct().Order().ToList();
            var startText = start.ToString("yyyy-MM-dd'T'HH:mm:ss");
            var endText = end.ToString("yyyy-MM-dd'T'HH:mm:ss");
            if (segments.LastOrDefault() is { } last && last.End == startText &&
                last.CurrentEmployeeIds.SequenceEqual(currentIds) && last.ProjectedEmployeeIds.SequenceEqual(projectedIds))
                segments[^1] = last with { End = endText };
            else
                segments.Add(new(startText, endText, currentIds, projectedIds));
        }
        return new(segments);
    }
}
