using HRSystem.Domain.Common;
using HRSystem.Domain.ParentalLeave;
using HRSystem.Domain.Attendance;

namespace HRSystem.UnitTests;

public sealed class ParentalLeaveDomainTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 9, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Standard_Requires_At_Least_Six_Calendar_Months()
    {
        Assert.Equal(
            ParentalLeaveApplicationType.Standard,
            ParentalLeavePolicy.Classify(
                new DateOnly(2026, 9, 1),
                new DateOnly(2027, 2, 28)));
    }

    [Fact]
    public void Thirty_Days_To_Under_Six_Months_Is_ShortTerm()
    {
        Assert.Equal(
            ParentalLeaveApplicationType.ShortTerm30DaysOrMore,
            ParentalLeavePolicy.Classify(
                new DateOnly(2026, 9, 1),
                new DateOnly(2026, 9, 30)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(29)]
    public void One_To_TwentyNine_Days_Is_Daily(int days)
    {
        var start = new DateOnly(2026, 9, 1);
        Assert.Equal(
            ParentalLeaveApplicationType.DailyUnder30Days,
            ParentalLeavePolicy.Classify(start, start.AddDays(days - 1)));
    }

    [Fact]
    public void End_Before_Start_Is_Rejected()
    {
        Assert.Throws<DomainValidationException>(() =>
            ParentalLeavePolicy.CalendarDays(
                new DateOnly(2026, 9, 2),
                new DateOnly(2026, 9, 1)));
    }

    [Theory]
    [InlineData(ParentalLeaveApplicationType.Standard, ParentalLeaveNoticeType.Standard, 10)]
    [InlineData(ParentalLeaveApplicationType.ShortTerm30DaysOrMore, ParentalLeaveNoticeType.Standard, 10)]
    [InlineData(ParentalLeaveApplicationType.DailyUnder30Days, ParentalLeaveNoticeType.Standard, 5)]
    [InlineData(ParentalLeaveApplicationType.Standard, ParentalLeaveNoticeType.EmergencyCare, 1)]
    public void Notice_Rules_Are_Explicit(
        ParentalLeaveApplicationType type,
        ParentalLeaveNoticeType notice,
        int expected)
    {
        Assert.Equal(expected, ParentalLeavePolicy.RequiredNoticeDays(type, notice));
    }

    [Fact]
    public void Insufficient_Notice_Is_Rejected()
    {
        Assert.Throws<DomainValidationException>(() =>
            ParentalLeavePolicy.EnsureNotice(
                new DateOnly(2026, 8, 9),
                new DateOnly(2026, 8, 13),
                ParentalLeaveApplicationType.DailyUnder30Days,
                ParentalLeaveNoticeType.Standard,
                null));
    }

    [Fact]
    public void Emergency_Notice_Requires_Reason()
    {
        Assert.Throws<DomainValidationException>(() =>
            ParentalLeavePolicy.EnsureNotice(
                new DateOnly(2026, 8, 9),
                new DateOnly(2026, 8, 10),
                ParentalLeaveApplicationType.DailyUnder30Days,
                ParentalLeaveNoticeType.EmergencyCare,
                " "));
    }

    [Fact]
    public void Approved_Request_Can_Request_And_Approve_Cancellation()
    {
        var request = Submitted();
        request.Approve("manager", Now);
        request.RequestCancellation("日期變更", "employee", Now.AddMinutes(1));
        request.ApproveCancellation("manager", Now.AddMinutes(2));
        Assert.Equal(ParentalLeaveStatus.Cancelled, request.Status);
        Assert.False(ParentalLeavePolicy.CountsTowardUsage(request.Status));
    }

    [Fact]
    public void Cancellation_Can_Be_Rejected_Back_To_Approved()
    {
        var request = Submitted();
        request.Approve("manager", Now);
        request.RequestCancellation("日期變更", "employee", Now.AddMinutes(1));
        request.RejectCancellation("仍需留停", "manager", Now.AddMinutes(2));
        Assert.Equal(ParentalLeaveStatus.Approved, request.Status);
    }

    [Fact]
    public void Submitted_Request_Can_Be_Withdrawn_Without_Becoming_Draft()
    {
        var request = Submitted();
        request.Withdraw("employee", Now);
        Assert.Equal(ParentalLeaveStatus.Withdrawn, request.Status);
    }

    [Fact]
    public void Early_Return_Shortens_Effective_End_Only_After_Approval()
    {
        var request = Submitted();
        request.Approve("manager", Now);
        var original = request.EffectiveEndDate;
        request.RequestEarlyReturn(
            new DateOnly(2026, 10, 1),
            "提前復職",
            "employee",
            Now.AddMinutes(1));
        Assert.Equal(original, request.EffectiveEndDate);
        request.ApproveEarlyReturn("manager", Now.AddMinutes(2));
        Assert.Equal(new DateOnly(2026, 9, 30), request.EffectiveEndDate);
    }

    [Fact]
    public void Early_Return_Cannot_Be_Requested_Twice()
    {
        var request = Submitted();
        request.Approve("manager", Now);
        request.RequestEarlyReturn(
            new DateOnly(2026, 10, 1),
            "提前復職",
            "employee",
            Now);
        Assert.Throws<DomainValidationException>(() =>
            request.RequestEarlyReturn(
                new DateOnly(2026, 9, 15),
                "再次提出",
                "employee",
                Now));
    }

    [Fact]
    public void History_Rejects_Invalid_Transition()
    {
        Assert.Throws<DomainValidationException>(() =>
            new ParentalLeaveApprovalHistory(
                Guid.NewGuid(),
                Guid.NewGuid(),
                ParentalLeaveApprovalAction.Approved,
                "manager",
                "主管",
                null,
                Now,
                ParentalLeaveStatus.Draft,
                ParentalLeaveStatus.Approved));
    }

    [Fact]
    public void Effective_Status_Is_Derived_Without_Mutating_Stored_Status()
    {
        var request = Submitted();
        request.Approve("manager", Now);
        Assert.Equal(
            ParentalLeaveStatus.Completed,
            request.GetEffectiveStatus(new DateOnly(2027, 4, 1)));
        Assert.Equal(ParentalLeaveStatus.Approved, request.Status);
    }

    [Fact]
    public void Employment_Suspension_Remains_Authoritative_When_Adjustment_Times_Exist()
    {
        var workDate = new DateOnly(2026, 9, 1);
        var shift = new AttendanceShiftSnapshot(
            Guid.NewGuid(),
            "正常班",
            new TimeOnly(8, 0),
            new TimeOnly(8, 1),
            new TimeOnly(12, 0),
            new TimeOnly(13, 30),
            new TimeOnly(17, 30),
            480,
            false,
            false);
        var suspensionId = Guid.NewGuid();
        var raw = AttendanceDailyCalculator.Calculate(
            workDate,
            true,
            shift,
            [],
            [],
            suspensionId);

        var adjusted = AttendanceDailyCalculator.ApplyEffectiveTimes(
            workDate,
            shift,
            raw,
            workDate.ToDateTime(new TimeOnly(8, 0)),
            workDate.ToDateTime(new TimeOnly(17, 30)));

        Assert.True(adjusted.IsEmploymentSuspended);
        Assert.Equal(suspensionId, adjusted.EmploymentSuspensionSourceId);
        Assert.Equal(AttendanceDailyStatus.EmploymentSuspended, adjusted.Status);
        Assert.Equal(0, adjusted.RequiredAttendanceMinutes);
        Assert.Equal(0, adjusted.RecognizedWorkMinutes);
        Assert.Equal(0, adjusted.MissingMinutes);
        Assert.False(adjusted.IsLate);
        Assert.False(adjusted.IsEarlyLeave);
        Assert.False(adjusted.MissingClockIn);
        Assert.False(adjusted.MissingClockOut);
    }

    private static ParentalLeaveRequest Submitted()
    {
        var request = new ParentalLeaveRequest(
            Guid.NewGuid(),
            "PL-TEST-001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2025, 1, 1),
            new DateOnly(2026, 9, 1),
            new DateOnly(2027, 2, 28),
            "台北市測試路 1 號",
            "0900000000",
            true,
            ParentalLeaveNoticeType.Standard,
            null,
            null,
            "子女 A",
            "employee",
            Now);
        request.Submit(
            new DateOnly(2026, 8, 9),
            "employee",
            Now);
        return request;
    }
}
