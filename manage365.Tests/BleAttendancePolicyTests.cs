using manage365.Routes.API.Attendance;
using Xunit;

namespace manage365.Tests;

public sealed class BleAttendancePolicyTests
{
    [Theory]
    [InlineData("2026-10-28T12:00:00+07:00", false)]
    [InlineData("2026-10-28T13:15:00+07:00", true)]
    [InlineData("2026-10-28T14:00:00+07:00", true)]
    [InlineData("2026-10-28T22:00:00+07:00", false)]
    public void CheckInWindow_UsesShiftHoursAndEarlyAllowance(string timestamp, bool expected)
    {
        Assert.Equal(expected, BleAttendancePolicy.IsWithinCheckInWindow(Shift(42, "CHECK_IN"),
            DateTimeOffset.Parse(timestamp), TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"), 45));
    }

    [Fact]
    public void CheckInWindow_AcceptsOvernightShiftAfterMidnight()
    {
        var shift = Shift(42, "CHECK_IN") with { StartTime = "22:00", EndTime = "06:00", IsOvernight = true };
        Assert.True(BleAttendancePolicy.IsWithinCheckInWindow(shift, DateTimeOffset.Parse("2026-10-29T02:00:00+07:00"),
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"), 45));
    }
    [Fact]
    public void EvaluateCheckIn_ReturnsEligibleShift_WhenSignalIsStableAndOnlyOneShiftMatches()
    {
        var decision = BleAttendancePolicy.EvaluateCheckIn(
            [Shift(42, "CHECK_IN")], medianRssiDbm: -58, stablePresenceSeconds: 25,
            minimumRssiDbm: -72, minimumDwellSeconds: 20);

        Assert.True(decision.CanCheckIn);
        Assert.Equal(42, decision.ShiftAssignmentId);
        Assert.Null(decision.ErrorCode);
    }

    [Fact]
    public void EvaluateCheckIn_RejectsBriefPassBy()
    {
        var decision = BleAttendancePolicy.EvaluateCheckIn(
            [Shift(42, "CHECK_IN")], medianRssiDbm: -58, stablePresenceSeconds: 5,
            minimumRssiDbm: -72, minimumDwellSeconds: 20);

        Assert.False(decision.CanCheckIn);
        Assert.Equal("ble_dwell_not_met", decision.ErrorCode);
    }

    [Fact]
    public void EvaluateCheckIn_RejectsAmbiguousOverlappingShifts()
    {
        var decision = BleAttendancePolicy.EvaluateCheckIn(
            [Shift(42, "CHECK_IN"), Shift(43, "CHECK_IN")], medianRssiDbm: -58,
            stablePresenceSeconds: 25, minimumRssiDbm: -72, minimumDwellSeconds: 20);

        Assert.False(decision.CanCheckIn);
        Assert.Equal("shift_ambiguous", decision.ErrorCode);
    }

    [Fact]
    public void EvaluateCheckIn_RejectsWeakSignal()
    {
        var decision = BleAttendancePolicy.EvaluateCheckIn(
            [Shift(42, "CHECK_IN")], medianRssiDbm: -85, stablePresenceSeconds: 25,
            minimumRssiDbm: -72, minimumDwellSeconds: 20);

        Assert.False(decision.CanCheckIn);
        Assert.Equal("ble_signal_too_weak", decision.ErrorCode);
    }

    [Fact]
    public void EvaluateCheckIn_IgnoresCheckoutOnlyShift()
    {
        var decision = BleAttendancePolicy.EvaluateCheckIn(
            [Shift(42, "CHECK_OUT")], medianRssiDbm: -58, stablePresenceSeconds: 25,
            minimumRssiDbm: -72, minimumDwellSeconds: 20);

        Assert.False(decision.CanCheckIn);
        Assert.Equal("no_eligible_shift", decision.ErrorCode);
    }

    private static EligibleShiftDto Shift(long assignmentId, string action) => new(
        assignmentId, assignmentId, "Ca chiều", "28/10/2026", "14:00", "22:00", false,
        "Assigned", null, null, null, action, true, "Sẵn sàng");
}
