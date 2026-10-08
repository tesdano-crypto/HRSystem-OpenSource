using HRSystem.Application.Attendance;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class LeaveAttendanceRecalculationEngineTests
{
    private static readonly DateOnly WorkDate = new(2026, 8, 3);
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Recalculation_Preserves_Current_Adjustment_And_Raw_Evidence()
    {
        await using var db = TestDb.Create();
        var department = new Department(
            Guid.NewGuid(), "LEAVE-ENGINE", "Leave Engine", Now);
        var employee = new Employee(
            Guid.NewGuid(),
            "EMP9601",
            "Leave Engine Employee",
            department.Id,
            new DateOnly(2026, 1, 1),
            Now);
        var shift = NormalShift();
        var assignment = new EmployeeShiftAssignment(
            Guid.NewGuid(),
            employee.Id,
            shift.Id,
            new DateOnly(2026, 1, 1),
            null,
            Now);
        var leaveType = new LeaveType(
            Guid.NewGuid(),
            "AL",
            "Annual Leave",
            LeaveUnit.Hour,
            0.5m,
            true,
            false,
            1,
            Now);
        db.AddRange(
            department,
            employee,
            shift,
            assignment,
            leaveType,
            RawEvent(employee.Id, 1, 8, 0),
            RawEvent(employee.Id, 2, 17, 30));
        await db.SaveChangesAsync();
        var engine = new AttendanceRecalculationEngine(
            db,
            new FixedTimeProvider(Now));
        await engine.RecalculateRangeAsync(
            WorkDate,
            WorkDate,
            employee.Id);
        await db.SaveChangesAsync();
        await engine.RecalculateRangeAsync(
            WorkDate,
            WorkDate,
            employee.Id);
        await db.SaveChangesAsync();

        var daily = await db.DailyAttendanceResults.SingleAsync();
        var adjustmentId = Guid.NewGuid();
        var adjustedClockIn = Local(8, 30);
        var adjustedClockOut = Local(17, 30);
        var rawCalculation = AttendanceDailyCalculator.Calculate(
            WorkDate,
            true,
            Snapshot(shift),
            [
                new AttendancePunchCandidate(Guid.NewGuid(), Local(8, 0)),
                new AttendancePunchCandidate(Guid.NewGuid(), Local(17, 30))
            ]);
        var adjustedCalculation =
            AttendanceDailyCalculator.ApplyEffectiveTimes(
                WorkDate,
                Snapshot(shift),
                rawCalculation,
                adjustedClockIn,
                adjustedClockOut);
        var adjustment = new AttendanceAdjustment(
            adjustmentId,
            daily.Id,
            employee.Id,
            WorkDate,
            1,
            AttendanceAdjustmentAction.Created,
            daily.EffectiveClockInLocalTime,
            daily.EffectiveClockOutLocalTime,
            adjustedClockIn,
            adjustedClockOut,
            AttendanceAdjustmentReason.ManagerApproved,
            "Preserve current adjustment during leave recalculation.",
            "unit-test-admin",
            Now,
            null);
        db.AttendanceAdjustments.Add(adjustment);
        daily.ApplyAdjustment(
            adjustment.Id,
            adjustedClockIn,
            adjustedClockOut,
            adjustedCalculation,
            true,
            Now);
        var leave = new LeaveRequest(
            Guid.NewGuid(),
            "LR-ENGINE-001",
            employee.Id,
            leaveType.Id,
            Taipei(8, 0),
            Taipei(12, 0),
            4m,
            "Morning leave",
            "unit-test-employee",
            Now);
        leave.Submit("unit-test-employee", Now);
        leave.Approve("unit-test-manager", Now);
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        db.ClearTrackedChanges();

        await engine.RecalculateRangeAsync(
            WorkDate,
            WorkDate,
            employee.Id);
        await db.SaveChangesAsync();
        await engine.RecalculateRangeAsync(
            WorkDate,
            WorkDate,
            employee.Id);
        await db.SaveChangesAsync();

        var persisted = await db.DailyAttendanceResults
            .AsNoTracking()
            .Include(item => item.LeaveSegments)
            .SingleAsync();
        Assert.Equal(adjustmentId, persisted.CurrentAdjustmentId);
        Assert.True(persisted.IsAdjusted);
        Assert.Equal(adjustedClockIn, persisted.EffectiveClockInLocalTime);
        Assert.Equal(adjustedClockOut, persisted.EffectiveClockOutLocalTime);
        Assert.Equal(240, persisted.ApprovedLeaveMinutes);
        Assert.Equal(240, persisted.RequiredAttendanceMinutes);
        Assert.Equal(LeaveCoverageStatus.Partial,
            persisted.LeaveCoverageStatus);
        Assert.Single(persisted.LeaveSegments);
        Assert.Equal(1, await db.DailyAttendanceResults.CountAsync());
        Assert.Equal(2, await db.AttendanceRawEvents.CountAsync());
        Assert.Equal(1, await db.AttendanceAdjustments.CountAsync());
    }

    private static AttendanceShift NormalShift() =>
        new(
            Guid.NewGuid(),
            "LEAVE-NORMAL",
            "Leave Normal",
            new TimeOnly(8, 0),
            new TimeOnly(8, 1),
            new TimeOnly(12, 0),
            new TimeOnly(13, 30),
            new TimeOnly(17, 30),
            480,
            false,
            false,
            Now);

    private static AttendanceShiftSnapshot Snapshot(AttendanceShift shift) =>
        new(
            shift.Id,
            shift.Name,
            shift.ScheduledStartTime,
            shift.LateThresholdTime,
            shift.LunchBreakStartTime,
            shift.LunchBreakEndTime,
            shift.ScheduledEndTime,
            shift.ExpectedWorkMinutes,
            shift.IsLunchPunchRequired,
            shift.IsOvernightShift);

    private static AttendanceRawEvent RawEvent(
        Guid employeeId,
        long externalEventId,
        int hour,
        int minute) =>
        new(
            Guid.NewGuid(),
            AttendanceSourceSystems.BioWebTa,
            externalEventId,
            employeeId,
            "LEAVE-ENGINE-PIN",
            "LEAVE-ENGINE-DEVICE",
            Local(hour, minute),
            null,
            null,
            Local(hour, minute),
            Now);

    private static DateTime Local(int hour, int minute) =>
        DateTime.SpecifyKind(
            WorkDate.ToDateTime(new TimeOnly(hour, minute)),
            DateTimeKind.Unspecified);

    private static DateTimeOffset Taipei(int hour, int minute) =>
        new(
            WorkDate.Year,
            WorkDate.Month,
            WorkDate.Day,
            hour,
            minute,
            0,
            TimeSpan.FromHours(8));

    private sealed class FixedTimeProvider(DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
