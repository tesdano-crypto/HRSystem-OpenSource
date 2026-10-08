using HRSystem.Application.Payroll;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.IntegrationTests;

public sealed partial class PayrollFoundationIntegrationTests
{
    [Fact]
    public async Task Initial_Batch_Creates_Current_Pointer_And_Duplicate_Is_Idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 8);

        var first = await fixture.Service.CreateInitialDraftAsync(periodId);
        var duplicate = await fixture.Service.CreateInitialDraftAsync(periodId);

        Assert.Equal(1, first.RevisionNumber);
        Assert.Equal(1, first.Created + first.NeedsSetup + first.PolicyPending +
            first.NeedsReview);
        Assert.Null(duplicate.RunId);
        Assert.Equal(1, duplicate.AlreadyCurrent);
        Assert.Single(await fixture.Db.PayrollRuns.ToListAsync());
        Assert.Single(await fixture.Db.PayrollEmployeeSnapshots.ToListAsync());
        var pointer = await fixture.Db.PayrollPeriodEmployeeCurrentSnapshots.SingleAsync();
        Assert.Equal((await fixture.Db.PayrollEmployeeSnapshots.SingleAsync()).Id,
            pointer.PayrollEmployeeSnapshotId);
    }

    [Fact]
    public async Task Employee_Recalculation_Appends_Snapshot_And_Atomically_Switches_Current()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 8);
        await fixture.Service.CreateInitialDraftAsync(periodId);
        var oldSnapshot = await fixture.Db.PayrollEmployeeSnapshots.AsNoTracking().SingleAsync();
        var oldFingerprint = oldSnapshot.TotalSourceFingerprint!.ToArray();

        var result = await fixture.Service.RecalculateEmployeeAsync(periodId,
            fixture.Employee.Id);

        Assert.Equal(2, result.RevisionNumber);
        Assert.Equal(2, await fixture.Db.PayrollEmployeeSnapshots.CountAsync());
        var pointer = await fixture.Db.PayrollPeriodEmployeeCurrentSnapshots.SingleAsync();
        Assert.NotEqual(oldSnapshot.Id, pointer.PayrollEmployeeSnapshotId);
        var persistedOld = await fixture.Db.PayrollEmployeeSnapshots.AsNoTracking()
            .SingleAsync(x => x.Id == oldSnapshot.Id);
        Assert.Equal(oldFingerprint, persistedOld.TotalSourceFingerprint);
    }

    [Fact]
    public async Task Changed_Only_Recalculation_Creates_Only_Changed_Employee_Snapshot()
    {
        await using var fixture = await Fixture.CreateAsync();
        var second = new Employee(Guid.NewGuid(), "PAY002", "第二位員工",
            fixture.Department.Id, new DateOnly(2020, 1, 1),
            new DateTimeOffset(2026, 8, 25, 1, 0, 0, TimeSpan.Zero));
        fixture.Db.Employees.Add(second);
        await fixture.Db.SaveChangesAsync();
        var plan = await fixture.Db.PayrollPlans.SingleAsync(x =>
            x.Code == "STANDARD_MONTHLY");
        await fixture.Service.AssignPlanAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            PayrollPlanId = plan.Id,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await fixture.Service.AssignPlanAsync(new()
        {
            EmployeeId = second.Id,
            PayrollPlanId = plan.Id,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 8);
        await fixture.Service.CreateInitialDraftAsync(periodId);
        var currentBefore = await fixture.Db.PayrollPeriodEmployeeCurrentSnapshots
            .AsNoTracking().ToDictionaryAsync(x => x.EmployeeId,
                x => x.PayrollEmployeeSnapshotId);
        var caseBonus = await fixture.Db.PayrollComponentDefinitions.SingleAsync(x =>
            x.Code == "CASE_BONUS");
        await fixture.Service.CreateAdjustmentAsync(new()
        {
            PayrollPeriodId = periodId,
            EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = caseBonus.Id,
            Amount = 1000,
            Direction = PayrollAdjustmentDirection.Earning,
            Reason = "版本測試"
        });

        var result = await fixture.Service.RecalculateChangedAsync(periodId);

        Assert.Single(result.Items, x => x.Outcome != PayrollBatchItemOutcome.AlreadyCurrent);
        Assert.Single(result.Items, x => x.Outcome == PayrollBatchItemOutcome.AlreadyCurrent);
        var currentAfter = await fixture.Db.PayrollPeriodEmployeeCurrentSnapshots
            .AsNoTracking().ToDictionaryAsync(x => x.EmployeeId,
                x => x.PayrollEmployeeSnapshotId);
        Assert.NotEqual(currentBefore[fixture.Employee.Id], currentAfter[fixture.Employee.Id]);
        Assert.Equal(currentBefore[second.Id], currentAfter[second.Id]);
        Assert.Equal(3, await fixture.Db.PayrollEmployeeSnapshots.CountAsync());
        var month = await fixture.Service.GetMonthAsync(periodId);
        Assert.Equal(2, month.CurrentEmployeeCount);
        Assert.Equal(month.Employees.Sum(x => x.Snapshot.GrossPay), month.GrossPay);
    }

    [Fact]
    public async Task Adjustment_Change_Marks_Current_Stale_And_Recalculation_Clears_It()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 8);
        await fixture.Service.CreateInitialDraftAsync(periodId);
        var caseBonus = await fixture.Db.PayrollComponentDefinitions.SingleAsync(x =>
            x.Code == "CASE_BONUS");
        var adjustmentId = await fixture.Service.CreateAdjustmentAsync(new()
        {
            PayrollPeriodId = periodId,
            EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = caseBonus.Id,
            Amount = 1000,
            Direction = PayrollAdjustmentDirection.Earning,
            Reason = "初始臨時項目"
        });
        Assert.True((await fixture.Db.PayrollPeriodEmployeeCurrentSnapshots
            .SingleAsync()).IsSourceChanged);

        await fixture.Service.RecalculateChangedAsync(periodId);
        Assert.False((await fixture.Db.PayrollPeriodEmployeeCurrentSnapshots
            .SingleAsync()).IsSourceChanged);
        var adjustment = await fixture.Db.PayrollAdjustments.AsNoTracking()
            .SingleAsync(x => x.Id == adjustmentId);
        await fixture.Service.UpdateAdjustmentAsync(adjustment.Id, 1500,
            adjustment.Direction, "更新臨時項目",
            Convert.ToBase64String(adjustment.RowVersion));
        Assert.True((await fixture.Db.PayrollPeriodEmployeeCurrentSnapshots
            .SingleAsync()).IsSourceChanged);
    }

    [Fact]
    public async Task Partial_Batch_Does_Not_Block_Configured_Employee()
    {
        await using var fixture = await Fixture.CreateAsync();
        var second = new Employee(Guid.NewGuid(), "PAY002", "未設定員工",
            fixture.Department.Id, new DateOnly(2020, 1, 1),
            new DateTimeOffset(2026, 8, 25, 1, 0, 0, TimeSpan.Zero));
        fixture.Db.Employees.Add(second);
        await fixture.Db.SaveChangesAsync();
        await fixture.AssignStandardAsync();
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 8);

        var result = await fixture.Service.CreateInitialDraftAsync(periodId);

        Assert.NotNull(result.RunId);
        Assert.True(result.NeedsSetup >= 1);
        Assert.Single(await fixture.Db.PayrollEmployeeSnapshots.ToListAsync());
        Assert.Single(await fixture.Db.PayrollPeriodEmployeeCurrentSnapshots.ToListAsync());
    }

    [Fact]
    public async Task Failed_Employee_Recalculation_Does_Not_Switch_Current_Pointer()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 8);
        await fixture.Service.CreateInitialDraftAsync(periodId);
        var pointerBefore = (await fixture.Db.PayrollPeriodEmployeeCurrentSnapshots
            .AsNoTracking().SingleAsync()).PayrollEmployeeSnapshotId;
        var plan = await fixture.Db.PayrollPlans.SingleAsync(x =>
            x.Code == "STANDARD_MONTHLY");
        fixture.Db.EmployeePayrollAssignments.Add(new EmployeePayrollAssignment(
            Guid.NewGuid(), fixture.Employee.Id, plan.Id,
            new DateOnly(2026, 8, 1)));
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.RecalculateEmployeeAsync(periodId,
            fixture.Employee.Id);

        Assert.Equal(PayrollBatchItemOutcome.NeedsSetup,
            Assert.Single(result.Items).Outcome);
        Assert.Null(result.RunId);
        Assert.Equal(pointerBefore,
            (await fixture.Db.PayrollPeriodEmployeeCurrentSnapshots
                .AsNoTracking().SingleAsync()).PayrollEmployeeSnapshotId);
        Assert.Single(await fixture.Db.PayrollEmployeeSnapshots.ToListAsync());
    }

    [Fact]
    public async Task Fifty_Employee_Batch_Uses_One_Run_And_One_Current_Pointer_Per_Employee()
    {
        await using var fixture = await Fixture.CreateAsync();
        var plan = await fixture.Db.PayrollPlans.SingleAsync(x =>
            x.Code == "STANDARD_MONTHLY");
        fixture.Db.EmployeePayrollAssignments.Add(new EmployeePayrollAssignment(
            Guid.NewGuid(), fixture.Employee.Id, plan.Id,
            new DateOnly(2026, 1, 1)));
        for (var index = 2; index <= 50; index++)
        {
            var employee = new Employee(Guid.NewGuid(), $"P{index:00000}",
                $"薪資測試員工 {index}", fixture.Department.Id,
                new DateOnly(2020, 1, 1),
                new DateTimeOffset(2026, 8, 25, 1, 0, 0, TimeSpan.Zero));
            fixture.Db.Employees.Add(employee);
            fixture.Db.EmployeePayrollAssignments.Add(new EmployeePayrollAssignment(
                Guid.NewGuid(), employee.Id, plan.Id, new DateOnly(2026, 1, 1)));
        }
        await fixture.Db.SaveChangesAsync();
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 8);

        var result = await fixture.Service.CreateInitialDraftAsync(periodId);

        Assert.NotNull(result.RunId);
        Assert.Equal(50, result.Items.Count);
        Assert.Equal(50, await fixture.Db.PayrollEmployeeSnapshots.CountAsync());
        Assert.Equal(50, await fixture.Db.PayrollPeriodEmployeeCurrentSnapshots.CountAsync());
        Assert.Single(await fixture.Db.PayrollRuns.ToListAsync());
    }
}
