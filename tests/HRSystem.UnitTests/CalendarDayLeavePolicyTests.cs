using HRSystem.Application.LeaveRequests;
using HRSystem.Domain.Common;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;

namespace HRSystem.UnitTests;

public sealed class CalendarDayLeavePolicyTests
{
    private static readonly DateOnly StartDate = new(2026, 8, 10);
    private static readonly DateTimeOffset StartUtc =
        new(2026, 8, 9, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Maternity_Is_Exactly_56_Inclusive_Calendar_Days()
    {
        var range = CalendarDayLeavePolicy.Resolve(
            CalendarDayLeavePolicy.MaternityCode,
            StartDate,
            null,
            null);

        Assert.Equal(56, range.CalendarDayCount);
        Assert.Equal(new DateOnly(2026, 10, 4), range.EndDate);
    }

    [Theory]
    [InlineData(PregnancyDurationCategory.ThreeMonthsOrMore, 28)]
    [InlineData(PregnancyDurationCategory.TwoToUnderThreeMonths, 7)]
    [InlineData(PregnancyDurationCategory.UnderTwoMonths, 5)]
    public void Miscarriage_Derives_End_Date_From_Category(
        PregnancyDurationCategory category,
        int expectedDays)
    {
        var range = CalendarDayLeavePolicy.Resolve(
            CalendarDayLeavePolicy.MiscarriageCode,
            StartDate,
            null,
            category);

        Assert.Equal(expectedDays, range.CalendarDayCount);
        Assert.Equal(StartDate.AddDays(expectedDays - 1), range.EndDate);
        Assert.Equal(category, range.PregnancyDurationCategory);
    }

    [Fact]
    public void Miscarriage_Requires_Structured_Category() =>
        Assert.Throws<DomainValidationException>(() =>
            CalendarDayLeavePolicy.Resolve(
                CalendarDayLeavePolicy.MiscarriageCode,
                StartDate,
                null,
                null));

    [Fact]
    public void Bed_Rest_Uses_User_Selected_Inclusive_Range()
    {
        var range = CalendarDayLeavePolicy.Resolve(
            CalendarDayLeavePolicy.PregnancyBedRestCode,
            StartDate,
            StartDate.AddDays(89),
            null);

        Assert.Equal(90, range.CalendarDayCount);
        Assert.Equal(StartDate.AddDays(89), range.EndDate);
    }

    [Fact]
    public void Bed_Rest_Rejects_End_Before_Start() =>
        Assert.Throws<DomainValidationException>(() =>
            CalendarDayLeavePolicy.Resolve(
                CalendarDayLeavePolicy.PregnancyBedRestCode,
                StartDate,
                StartDate.AddDays(-1),
                null));

    [Fact]
    public void Maternity_Rejects_Miscarriage_Category() =>
        Assert.Throws<DomainValidationException>(() =>
            CalendarDayLeavePolicy.Resolve(
                CalendarDayLeavePolicy.MaternityCode,
                StartDate,
                null,
                PregnancyDurationCategory.UnderTwoMonths));

    [Fact]
    public void Calendar_Day_Request_Can_Exceed_31_Days()
    {
        var request = NewCalendarRequest(56, 320m);

        Assert.Equal(LeaveRequestStatus.Draft, request.Status);
        Assert.Equal(320m, request.DurationHours);
    }

    [Fact]
    public void Calendar_Day_Request_Can_Have_Zero_Attendance_Hours()
    {
        var request = NewCalendarRequest(1, 0m);

        Assert.Equal(0m, request.DurationHours);
    }

    [Fact]
    public void Working_Schedule_Request_Still_Rejects_Over_31_Days() =>
        Assert.Throws<DomainValidationException>(() => new LeaveRequest(
            Guid.NewGuid(),
            "LR-WORKING-31",
            Guid.NewGuid(),
            Guid.NewGuid(),
            StartUtc,
            StartUtc.AddDays(32),
            8m,
            "測試",
            "unit-test-user",
            StartUtc));

    [Fact]
    public void Working_Schedule_Request_Still_Rejects_Zero_Hours() =>
        Assert.Throws<DomainValidationException>(() => new LeaveRequest(
            Guid.NewGuid(),
            "LR-WORKING-ZERO",
            Guid.NewGuid(),
            Guid.NewGuid(),
            StartUtc,
            StartUtc.AddHours(1),
            0m,
            "測試",
            "unit-test-user",
            StartUtc));

    [Fact]
    public async Task Calculator_Exempts_Calendar_Days_From_31_Day_Limit()
    {
        await using var db = TestDb.Create();
        var now = DateTimeOffset.UtcNow;
        var department = new Department(
            Guid.NewGuid(), "CAL-D", "Calendar Department", now);
        var employee = new Employee(
            Guid.NewGuid(), "CAL0001", "Calendar Employee",
            department.Id, new DateOnly(2026, 1, 1), now);
        var shift = new HRSystem.Domain.Attendance.AttendanceShift(
            Guid.NewGuid(), "CAL-S", "Calendar Shift",
            new TimeOnly(8, 0), new TimeOnly(8, 1),
            new TimeOnly(12, 0), new TimeOnly(13, 30),
            new TimeOnly(17, 30), 480, false, false, now);
        db.AddRange(
            department,
            employee,
            shift,
            new HRSystem.Domain.Attendance.EmployeeShiftAssignment(
                Guid.NewGuid(), employee.Id, shift.Id,
                new DateOnly(2026, 1, 1), null, now));
        await db.SaveChangesAsync();

        var result = await new LeaveDurationCalculator(db).CalculateAsync(
            employee.Id,
            StartUtc,
            StartUtc.AddDays(56),
            LeaveCalculationMode.CalendarDays);

        Assert.True(result.CanSubmit);
        Assert.Equal(320m, result.DurationHours);
    }

    [Fact]
    public async Task Calendar_Day_Weekend_Only_Is_Valid_With_Zero_Attendance_Hours()
    {
        await using var db = TestDb.Create();
        var now = DateTimeOffset.UtcNow;
        var department = new Department(
            Guid.NewGuid(), "WKND-D", "Weekend Department", now);
        var employee = new Employee(
            Guid.NewGuid(), "WKND0001", "Weekend Employee",
            department.Id, new DateOnly(2026, 1, 1), now);
        db.AddRange(department, employee);
        await db.SaveChangesAsync();

        var saturdayStart = new DateTimeOffset(
            2026, 8, 7, 16, 0, 0, TimeSpan.Zero);
        var result = await new LeaveDurationCalculator(db).CalculateAsync(
            employee.Id,
            saturdayStart,
            saturdayStart.AddDays(1),
            LeaveCalculationMode.CalendarDays);

        Assert.True(result.CanSubmit);
        Assert.Equal(0m, result.DurationHours);
    }

    private static LeaveRequest NewCalendarRequest(
        int calendarDays,
        decimal durationHours) => new(
        Guid.NewGuid(),
        $"LR-CALENDAR-{calendarDays}",
        Guid.NewGuid(),
        Guid.NewGuid(),
        StartUtc,
        StartUtc.AddDays(calendarDays),
        durationHours,
        "測試",
        "unit-test-user",
        StartUtc,
        LeaveCalculationMode.CalendarDays,
        CalendarDayLeavePolicy.PregnancyBedRestCode,
        null);
}
