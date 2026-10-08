using HRSystem.Application.Security;
using HRSystem.Domain.Common;
using HRSystem.Domain.Payroll;

namespace HRSystem.UnitTests;

public sealed class PayrollFoundationTests
{
    [Fact]
    public void Component_Definition_Normalizes_Code_And_Preserves_Typed_Metadata()
    {
        var item = new PayrollComponentDefinition(Guid.NewGuid(), " base_salary ", "底薪",
            PayrollComponentCategory.Earning, PayrollCalculationKind.FixedAmount,
            true, 10, new DateOnly(2026, 1, 1));
        Assert.Equal("BASE_SALARY", item.Code);
        Assert.Equal(PayrollComponentCategory.Earning, item.Category);
        Assert.Equal(PayrollCalculationKind.FixedAmount, item.CalculationKind);
    }

    [Fact]
    public void Component_Definition_Rejects_Invalid_Effective_Range() =>
        Assert.Throws<DomainValidationException>(() => new PayrollComponentDefinition(
            Guid.NewGuid(), "TEST", "測試", PayrollComponentCategory.Earning,
            PayrollCalculationKind.FixedAmount, true, 1, new DateOnly(2026, 2, 1),
            new DateOnly(2026, 1, 31)));

    [Theory]
    [InlineData(0, 0)] [InlineData(2, 0)] [InlineData(3, 2100)]
    [InlineData(11, 2100)] [InlineData(12, 4200)] [InlineData(35, 4200)]
    [InlineData(36, 6000)] [InlineData(71, 6000)] [InlineData(72, 7500)]
    public void Standard_Seniority_Tiers_Have_Deterministic_Boundaries(int months, decimal expected)
    {
        var tiers = StandardTiers();
        Assert.Equal(expected, Assert.Single(tiers, x => x.Contains(months)).Amount);
    }

    [Fact]
    public void Seniority_Tier_Overlap_Is_Rejected()
    {
        var component = Guid.NewGuid();
        var tiers = new[]
        {
            new PayrollSeniorityTier(Guid.NewGuid(), component, 0, 12, 0),
            new PayrollSeniorityTier(Guid.NewGuid(), component, 11, 24, 100)
        };
        Assert.Throws<DomainValidationException>(() => PayrollSeniorityTier.ValidateNoOverlap(tiers));
    }

    [Fact]
    public void Override_Supports_Replace_Add_And_Disable()
    {
        var employee = Guid.NewGuid(); var component = Guid.NewGuid(); var date = new DateOnly(2026, 1, 1);
        Assert.Equal(12000, new EmployeePayrollComponentOverride(Guid.NewGuid(), employee,
            component, 12000, PayrollOverrideMode.Replace, date).OverrideAmount);
        Assert.Equal(5000, new EmployeePayrollComponentOverride(Guid.NewGuid(), employee,
            component, 5000, PayrollOverrideMode.Add, date).OverrideAmount);
        Assert.Null(new EmployeePayrollComponentOverride(Guid.NewGuid(), employee,
            component, null, PayrollOverrideMode.Disable, date).OverrideAmount);
    }

    [Theory]
    [InlineData(PayrollOverrideMode.Replace)] [InlineData(PayrollOverrideMode.Add)]
    public void Monetary_Override_Requires_Amount(PayrollOverrideMode mode) =>
        Assert.Throws<DomainValidationException>(() => new EmployeePayrollComponentOverride(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, mode, new DateOnly(2026, 1, 1)));

    [Fact]
    public void Disable_Override_Rejects_Amount() =>
        Assert.Throws<DomainValidationException>(() => new EmployeePayrollComponentOverride(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, PayrollOverrideMode.Disable,
            new DateOnly(2026, 1, 1)));

    [Fact]
    public void Payroll_Period_Uses_Calendar_Month()
    {
        var period = new PayrollPeriod(Guid.NewGuid(), 2028, 2);
        Assert.Equal(new DateOnly(2028, 2, 1), period.PeriodStart);
        Assert.Equal(new DateOnly(2028, 2, 29), period.PeriodEnd);
        Assert.Equal(PayrollPeriodStatus.Open, period.Status);
    }

    [Theory] [InlineData(1999, 12)] [InlineData(2026, 0)] [InlineData(2026, 13)]
    public void Payroll_Period_Rejects_Invalid_Year_Or_Month(int year, int month) =>
        Assert.Throws<DomainValidationException>(() => new PayrollPeriod(Guid.NewGuid(), year, month));

    [Fact]
    public void Adjustment_Uses_Positive_Amount_And_Explicit_Direction()
    {
        var item = new PayrollAdjustment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 2300, PayrollAdjustmentDirection.Earning, "案件", "admin",
            DateTimeOffset.UtcNow);
        Assert.Equal(2300, item.Amount); Assert.Equal(PayrollAdjustmentDirection.Earning, item.Direction);
    }

    [Fact]
    public void Adjustment_Rejects_NonPositive_Amount() =>
        Assert.Throws<DomainValidationException>(() => new PayrollAdjustment(Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0,
            PayrollAdjustmentDirection.Deduction, "其他扣款", "admin", DateTimeOffset.UtcNow));

    [Fact]
    public void Snapshot_Copies_Historical_Identity_And_Component_Amounts()
    {
        var snapshot = new PayrollEmployeeSnapshot(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(),
            "E001", "歷史姓名", null, "歷史部門", new DateOnly(2020, 1, 1), null,
            Guid.NewGuid(), "STANDARD_MONTHLY", DateTimeOffset.UtcNow, PayrollEmployeeSetupStatus.Ready);
        snapshot.Components.Add(new PayrollEmployeeSnapshotComponent(Guid.NewGuid(), snapshot.Id,
            Guid.NewGuid(), "BASE_SALARY", "底薪", PayrollComponentCategory.Earning,
            PayrollSnapshotSourceType.PayrollPlan, Guid.NewGuid(), 29500, null, 29500,
            PayrollCalculationStatus.Resolved, new DateOnly(2026, 8, 31)));
        Assert.Equal("歷史姓名", snapshot.EmployeeName);
        Assert.Equal(29500, Assert.Single(snapshot.Components).ResolvedAmount);
    }

    [Fact]
    public void Pending_Component_Does_Not_Use_Zero_Fallback()
    {
        var line = new PayrollEmployeeSnapshotComponent(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "ATTENDANCE_ALLOWANCE", "出席補貼",
            PayrollComponentCategory.Earning, PayrollSnapshotSourceType.RulePending,
            Guid.NewGuid(), 2000, null, null, PayrollCalculationStatus.Pending,
            new DateOnly(2026, 8, 31));
        Assert.Null(line.ResolvedAmount); Assert.Equal(PayrollCalculationStatus.Pending, line.CalculationStatus);
    }

    [Theory]
    [InlineData(RoleNames.Admin, true)]
    [InlineData(RoleNames.Accounting, true)]
    [InlineData(RoleNames.HR, false)]
    [InlineData(RoleNames.Owner, false)]
    [InlineData(RoleNames.Manager, false)]
    [InlineData(RoleNames.Employee, false)]
    public void PayrollManage_Is_Granted_Only_To_Configured_Roles(string role, bool expected) =>
        Assert.Equal(expected, RolePermissions.HasPermission([role], PolicyNames.PayrollManage));

    private static PayrollSeniorityTier[] StandardTiers()
    {
        var id = Guid.NewGuid();
        return
        [
            new(Guid.NewGuid(), id, 0, 3, 0), new(Guid.NewGuid(), id, 3, 12, 2100),
            new(Guid.NewGuid(), id, 12, 36, 4200), new(Guid.NewGuid(), id, 36, 72, 6000),
            new(Guid.NewGuid(), id, 72, null, 7500)
        ];
    }
}
