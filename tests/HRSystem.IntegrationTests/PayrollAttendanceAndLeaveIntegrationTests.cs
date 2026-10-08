using HRSystem.Domain.Attendance;
using HRSystem.Application.Overtime;
using HRSystem.Domain.Overtime;
using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.IntegrationTests;

public sealed partial class PayrollFoundationIntegrationTests
{
    [Fact]
    public async Task Draft_Batch_Uses_Final_Attendance_And_Persists_P3_Evidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        await SeedCompleteJulyAttendanceAsync(fixture, new DateOnly(2026, 7, 10),
            includePersonalLeave: true);

        var runId = await fixture.Service.CreateDraftAsync(
            await fixture.Service.CreatePeriodAsync(2026, 7));
        var employee = Assert.Single((await fixture.Service.GetRunAsync(runId)).Employees);
        var allowance = Assert.Single(employee.Components,
            x => x.Code == "ATTENDANCE_ALLOWANCE");
        Assert.Equal(1933, allowance.ResolvedAmount);
        Assert.Equal(1, allowance.AttendanceAllowance!.IneligibleDays);
        var day = Assert.Single(allowance.AttendanceAllowance.Evidence);
        Assert.True(day.Reasons.HasFlag(AttendanceAllowanceIneligibilityReason.Late));
        Assert.True(day.Reasons.HasFlag(AttendanceAllowanceIneligibilityReason.ApprovedLeave));

        var deduction = Assert.Single(employee.Components,
            x => x.Code == "LEAVE_DEDUCTION");
        Assert.Equal(492, deduction.ResolvedAmount);
        Assert.Equal(240, deduction.LeaveDeduction!.PersonalLeaveMinutes);
        Assert.Single(deduction.LeaveDeduction.Evidence);
        Assert.Single(fixture.Db.PayrollAttendanceAllowanceSnapshots);
        Assert.Single(fixture.Db.PayrollLeaveDeductionSnapshots);
        Assert.Single(fixture.Db.PayrollAttendanceAllowanceEvidence);
        Assert.Single(fixture.Db.PayrollLeaveDeductionEvidence);
    }

    [Fact]
    public async Task Corrected_Final_Result_Is_Current_Truth_And_Old_Anomaly_Is_Not_Read()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        await SeedCompleteJulyAttendanceAsync(fixture, null, false);

        var preview = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 7);
        var allowance = Assert.Single(preview.Components,
            x => x.Code == "ATTENDANCE_ALLOWANCE");
        Assert.Equal(2000, allowance.ResolvedAmount);
        Assert.Empty(allowance.AttendanceAllowance!.Evidence);
    }

    [Fact]
    public async Task Missing_Final_Date_Is_NeedsReview_And_Does_Not_Grant_Full_Allowance()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        var preview = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 7);
        var allowance = Assert.Single(preview.Components,
            x => x.Code == "ATTENDANCE_ALLOWANCE");
        Assert.Equal(PayrollCalculationStatus.NeedsReview, allowance.CalculationStatus);
        Assert.Null(allowance.ResolvedAmount);
    }

    [Fact]
    public async Task Preview_Uses_Current_Confirmed_Recognition_And_Full_Month_Base()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        await SeedCompleteJulyAttendanceAsync(fixture, null, false);
        var date = new DateOnly(2026, 7, 8);
        var request = new OvertimeRequest(Guid.NewGuid(), fixture.Employee.Id,
            date.ToDateTime(new TimeOnly(17, 30)),
            date.ToDateTime(new TimeOnly(20, 30)), "P4 test", "employee",
            new DateTimeOffset(2026, 7, 8, 8, 0, 0, TimeSpan.Zero));
        request.Submit("employee", DateTimeOffset.UtcNow);
        request.Approve(OvertimeReviewReason.ApprovedAsRequested, null,
            "admin", DateTimeOffset.UtcNow);
        fixture.Db.OvertimeRequests.Add(request);
        await fixture.Db.SaveChangesAsync();
        var attendance = await fixture.Db.DailyAttendanceResults
            .SingleAsync(x => x.EmployeeId == fixture.Employee.Id && x.WorkDate == date);
        var source = OvertimeRecognitionPolicy.Build(request.Id, request.EmployeeId,
            request.OvertimeDate, request.Status, request.RowVersion,
            request.PlannedStartAt, request.PlannedEndAt, attendance.Id,
            attendance.RowVersion, attendance.ScheduledEndTimeSnapshot,
            attendance.IsOvernightShiftSnapshot == true,
            attendance.EffectiveClockOutLocalTime, attendance.MissingClockIn,
            attendance.MissingClockOut);
        var recognition = new OvertimeRecognition(Guid.NewGuid(), request.Id,
            request.EmployeeId, request.OvertimeDate, request.PlannedStartAt,
            request.PlannedEndAt, source.ObservedClockOutAt,
            source.SuggestedStartAt, source.SuggestedEndAt, source.SuggestedMinutes,
            source.InitialStatus, source.Fingerprint, DateTimeOffset.UtcNow);
        recognition.Confirm(date.ToDateTime(new TimeOnly(17, 30)),
            date.ToDateTime(new TimeOnly(20, 30)),
            OvertimeRecognitionReason.ActualAttendanceConfirmed, null,
            source.Fingerprint, "admin", DateTimeOffset.UtcNow);
        fixture.Db.OvertimeRecognitions.Add(recognition);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var preview = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 7);
        Assert.NotNull(preview.OvertimePay);
        Assert.Equal(PayrollCalculationStatus.Resolved,
            preview.OvertimePay.CalculationStatus);
        Assert.Equal(180, preview.OvertimePay.TotalRecognizedMinutes);
        Assert.Equal(120, preview.OvertimePay.Buckets.Single(x =>
            x.Bucket == OvertimePayBucket.FirstTwoHours).Minutes);
        Assert.Equal(60, preview.OvertimePay.Buckets.Single(x =>
            x.Bucket == OvertimePayBucket.AfterTwoHours).Minutes);
        Assert.DoesNotContain(preview.OvertimePay.IncludedComponents,
            x => x.Code == "CERTIFICATE_ALLOWANCE");
    }

    private static async Task SeedCompleteJulyAttendanceAsync(
        Fixture fixture, DateOnly? lateDate, bool includePersonalLeave)
    {
        var now = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var shift = new AttendanceShift(Guid.NewGuid(), "PAY", "薪資測試班",
            new TimeOnly(8, 0), new TimeOnly(8, 1), new TimeOnly(12, 0),
            new TimeOnly(13, 30), new TimeOnly(17, 30), 480, false, false, now);
        fixture.Db.AttendanceShifts.Add(shift);
        var snapshot = new AttendanceShiftSnapshot(shift.Id, shift.Name,
            shift.ScheduledStartTime, shift.LateThresholdTime,
            shift.LunchBreakStartTime, shift.LunchBreakEndTime,
            shift.ScheduledEndTime, shift.ExpectedWorkMinutes,
            shift.IsLunchPunchRequired, shift.IsOvernightShift);
        var leaveDate = new DateOnly(2026, 7, 10);
        for (var day = 1; day <= 31; day++)
        {
            var date = new DateOnly(2026, 7, day);
            var clockIn = lateDate == date
                ? new DateTime(2026, 7, day, 9, 0, 0)
                : new DateTime(2026, 7, day, 8, 0, 0);
            var calculation = AttendanceDailyCalculator.Calculate(date, true, snapshot,
            [
                new AttendancePunchCandidate(Guid.NewGuid(), clockIn),
                new AttendancePunchCandidate(Guid.NewGuid(),
                    new DateTime(2026, 7, day, 17, 30, 0))
            ]);
            var result = new DailyAttendanceResult(Guid.NewGuid(), fixture.Employee.Id,
                date, now);
            result.Recalculate(true, AttendanceCalendarClassification.WorkingDay,
                shift, calculation, "payroll-test", now);
            if (includePersonalLeave && date == leaveDate)
            {
                result.ReplaceLeaveSegments([
                    new DailyAttendanceLeaveSegment(Guid.NewGuid(), result.Id,
                        Guid.NewGuid(), Guid.NewGuid(), "PERSONAL", "事假",
                        now, now.AddHours(4), 240, now)
                ]);
            }
            fixture.Db.DailyAttendanceResults.Add(result);
        }
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
    }
}
