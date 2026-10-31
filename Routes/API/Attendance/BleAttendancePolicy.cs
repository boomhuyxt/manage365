namespace manage365.Routes.API.Attendance;

public sealed record BleCheckInDecision(bool CanCheckIn, long? ShiftAssignmentId, string? ErrorCode);

public static class BleAttendancePolicy
{
    public static bool IsWithinCheckInWindow(EligibleShiftDto shift, DateTimeOffset now, TimeZoneInfo timeZone, int earlyMinutes)
    {
        if (!DateOnly.TryParseExact(shift.ShiftDate, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date) ||
            !TimeOnly.TryParseExact(shift.StartTime, "HH:mm", out var startTime) ||
            !TimeOnly.TryParseExact(shift.EndTime, "HH:mm", out var endTime)) return false;
        var start = date.ToDateTime(startTime);
        var end = date.ToDateTime(endTime).AddDays(shift.IsOvernight ? 1 : 0);
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone).DateTime;
        return localNow >= start.AddMinutes(-Math.Max(0, earlyMinutes)) && localNow < end;
    }

    public static BleCheckInDecision EvaluateCheckIn(
        IReadOnlyCollection<EligibleShiftDto> shifts,
        int medianRssiDbm,
        int stablePresenceSeconds,
        int minimumRssiDbm,
        int minimumDwellSeconds)
    {
        if (medianRssiDbm < minimumRssiDbm)
        {
            return new(false, null, "ble_signal_too_weak");
        }

        if (stablePresenceSeconds < minimumDwellSeconds)
        {
            return new(false, null, "ble_dwell_not_met");
        }

        var eligible = shifts
            .Where(shift => shift.CanPerformAction && shift.AllowedAction == "CHECK_IN" && shift.ShiftAssignmentId.HasValue)
            .ToArray();
        return eligible.Length switch
        {
            1 => new(true, eligible[0].ShiftAssignmentId, null),
            > 1 => new(false, null, "shift_ambiguous"),
            _ => new(false, null, "no_eligible_shift")
        };
    }
}
