using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Payroll;
using HRSystem.Application.Security;
using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.IntegrationTests;

public sealed partial class PayrollFoundationIntegrationTests
{
    [Fact]
    public async Task Legacy_Adjustments_Require_PayrollManage_And_Reject_Exact_Duplicate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 7);
        var component = await fixture.Db.PayrollComponentDefinitions.SingleAsync(x =>
            x.Code == PayrollLegacyAdjustmentComponents.AttendanceAllowanceCode);
        const string reason = "2026/07 Legacy Transition — source payroll sheet attendance allowance";

        var id = await fixture.Service.CreateAdjustmentAsync(new()
        {
            PayrollPeriodId = periodId,
            EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = component.Id,
            Amount = 1933,
            Direction = PayrollAdjustmentDirection.Earning,
            Reason = $"  {reason}  "
        });

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            fixture.Service.CreateAdjustmentAsync(new()
            {
                PayrollPeriodId = periodId,
                EmployeeId = fixture.Employee.Id,
                ComponentDefinitionId = component.Id,
                Amount = 1933,
                Direction = PayrollAdjustmentDirection.Earning,
                Reason = reason
            }));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            fixture.ForRole(RoleNames.HR).CreateAdjustmentAsync(new()
            {
                PayrollPeriodId = periodId,
                EmployeeId = fixture.Employee.Id,
                ComponentDefinitionId = component.Id,
                Amount = 1,
                Direction = PayrollAdjustmentDirection.Earning,
                Reason = "unauthorized"
            }));

        var saved = Assert.Single(await fixture.Db.PayrollAdjustments.ToListAsync());
        Assert.Equal(id, saved.Id);
        Assert.Equal(reason, saved.Reason);
        Assert.Contains(fixture.Db.AuditLogs,
            x => x.Action == "PayrollAdjustmentCreated" && x.EntityId == id.ToString());
    }

    [Fact]
    public async Task Normal_Attendance_And_Overtime_Components_Remain_NonManual()
    {
        await using var fixture = await Fixture.CreateAsync();
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 7);
        var components = await fixture.Db.PayrollComponentDefinitions
            .Where(x => x.Code == "ATTENDANCE_ALLOWANCE" ||
                x.Code == "OVERTIME_FIRST_2H")
            .ToDictionaryAsync(x => x.Code);

        foreach (var component in components.Values)
        {
            await Assert.ThrowsAsync<ApplicationValidationException>(() =>
                fixture.Service.CreateAdjustmentAsync(new()
                {
                    PayrollPeriodId = periodId,
                    EmployeeId = fixture.Employee.Id,
                    ComponentDefinitionId = component.Id,
                    Amount = 1,
                    Direction = PayrollAdjustmentDirection.Earning,
                    Reason = "不得人工建立正式計算項目"
                }));
        }

        Assert.Equal(PayrollCalculationKind.RuleBased,
            components["ATTENDANCE_ALLOWANCE"].CalculationKind);
        Assert.Equal(PayrollCalculationKind.ExternalCalculated,
            components["OVERTIME_FIRST_2H"].CalculationKind);
        Assert.Empty(fixture.Db.PayrollAdjustments);
    }

    [Fact]
    public async Task Legacy_Earnings_Enter_Gross_Without_Fake_Evidence_And_Create_Revision()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 7);
        await fixture.Service.CreateInitialDraftAsync(periodId);
        var oldPointer = await PayrollCurrentSnapshotSet.Query(fixture.Db)
            .SingleAsync(x => x.PayrollPeriodId == periodId);
        var oldSnapshot = oldPointer.PayrollEmployeeSnapshot;
        var oldEmployeeFingerprint = PayrollEmployeeCalculationFingerprintV1.Calculate(oldSnapshot);
        var oldMonthFingerprint = PayrollCurrentSnapshotSet.Fingerprint(periodId, [oldPointer]);
        var oldRaw = await fixture.Db.AttendanceRawEvents.CountAsync();
        var oldDaily = await fixture.Db.DailyAttendanceResults.CountAsync();
        var oldAttendanceAdjustments = await fixture.Db.AttendanceAdjustments.CountAsync();
        var oldOvertimeRequests = await fixture.Db.OvertimeRequests.CountAsync();
        var oldRecognitions = await fixture.Db.OvertimeRecognitions.CountAsync();
        var oldAttendance = Assert.Single(oldSnapshot.Components,
            x => x.ComponentCode == "ATTENDANCE_ALLOWANCE");
        var oldRecognizedMinutes = oldSnapshot.OvertimePaySnapshot!.TotalRecognizedMinutes;
        var legacy = await fixture.Db.PayrollComponentDefinitions
            .Where(x => x.Code == PayrollLegacyAdjustmentComponents.AttendanceAllowanceCode ||
                x.Code == PayrollLegacyAdjustmentComponents.OvertimePayCode)
            .ToDictionaryAsync(x => x.Code);

        await fixture.Service.CreateAdjustmentAsync(new()
        {
            PayrollPeriodId = periodId,
            EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = legacy[PayrollLegacyAdjustmentComponents.AttendanceAllowanceCode].Id,
            Amount = 1933,
            Direction = PayrollAdjustmentDirection.Earning,
            Reason = "legacy attendance authority"
        });
        var stalePointer = await PayrollCurrentSnapshotSet.Query(fixture.Db)
            .SingleAsync(x => x.PayrollPeriodId == periodId);
        Assert.True(stalePointer.IsSourceChanged);
        Assert.NotEqual(Convert.ToHexString(oldMonthFingerprint),
            Convert.ToHexString(PayrollCurrentSnapshotSet.Fingerprint(periodId, [stalePointer])));
        await fixture.Service.CreateAdjustmentAsync(new()
        {
            PayrollPeriodId = periodId,
            EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = legacy[PayrollLegacyAdjustmentComponents.OvertimePayCode].Id,
            Amount = 7326,
            Direction = PayrollAdjustmentDirection.Earning,
            Reason = "legacy overtime authority"
        });

        var recalculated = await fixture.Service.RecalculateEmployeeAsync(
            periodId, fixture.Employee.Id);
        var current = await PayrollCurrentSnapshotSet.Query(fixture.Db)
            .SingleAsync(x => x.PayrollPeriodId == periodId);
        var snapshot = current.PayrollEmployeeSnapshot;

        Assert.Equal(2, recalculated.RevisionNumber);
        Assert.Equal(2, await fixture.Db.PayrollEmployeeSnapshots.CountAsync());
        Assert.NotEqual(oldSnapshot.Id, snapshot.Id);
        Assert.False(current.IsSourceChanged);
        Assert.Equal(oldSnapshot.GrossPay + 1933 + 7326, snapshot.GrossPay);
        Assert.Contains(snapshot.Components, x =>
            x.ComponentCode == PayrollLegacyAdjustmentComponents.AttendanceAllowanceCode &&
            x.SourceType == PayrollSnapshotSourceType.ManualAdjustment &&
            x.ResolvedAmount == 1933);
        Assert.Contains(snapshot.Components, x =>
            x.ComponentCode == PayrollLegacyAdjustmentComponents.OvertimePayCode &&
            x.SourceType == PayrollSnapshotSourceType.ManualAdjustment &&
            x.ResolvedAmount == 7326);
        var currentAttendance = Assert.Single(snapshot.Components,
            x => x.ComponentCode == "ATTENDANCE_ALLOWANCE");
        Assert.Equal(oldAttendance.CalculationStatus, currentAttendance.CalculationStatus);
        Assert.Equal(oldAttendance.ResolvedAmount, currentAttendance.ResolvedAmount);
        Assert.Equal(oldRecognizedMinutes,
            snapshot.OvertimePaySnapshot!.TotalRecognizedMinutes);
        Assert.NotEqual(Convert.ToHexString(oldEmployeeFingerprint),
            Convert.ToHexString(PayrollEmployeeCalculationFingerprintV1.Calculate(snapshot)));
        Assert.NotEqual(Convert.ToHexString(oldMonthFingerprint),
            Convert.ToHexString(PayrollCurrentSnapshotSet.Fingerprint(periodId, [current])));
        Assert.Equal(oldRaw, await fixture.Db.AttendanceRawEvents.CountAsync());
        Assert.Equal(oldDaily, await fixture.Db.DailyAttendanceResults.CountAsync());
        Assert.Equal(oldAttendanceAdjustments,
            await fixture.Db.AttendanceAdjustments.CountAsync());
        Assert.Equal(oldOvertimeRequests, await fixture.Db.OvertimeRequests.CountAsync());
        Assert.Equal(oldRecognitions, await fixture.Db.OvertimeRecognitions.CountAsync());
    }
}
