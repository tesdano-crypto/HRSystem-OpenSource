using System.Net;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Payroll;
using HRSystem.Application.Security;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.Payroll;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.IntegrationTests;

public sealed partial class PayrollFoundationIntegrationTests
{
    [Theory]
    [InlineData("/admin/payroll")]
    [InlineData("/admin/payroll/settings")]
    [InlineData("/admin/payroll/employees")]
    public async Task Admin_Payroll_Routes_Load(string route)
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Employee")]
    public async Task Non_Admin_Cannot_Open_Payroll_Routes(string role)
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);
        var response = await client.GetAsync("/admin/payroll");
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Payroll_View_And_Manage_Are_Enforced_Independently()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.NotNull(await fixture.ForRole(RoleNames.HR).GetPeriodsAsync());
        Assert.Equal(fixture.Employee.Id,
            (await fixture.ForRole(RoleNames.HR)
                .GetEmployeeAsync(fixture.Employee.Id)).Employee.EmployeeId);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            fixture.ForRole(RoleNames.HR).CreatePeriodAsync(2026, 10));
        Assert.NotNull(await fixture.ForRole(RoleNames.Owner).GetPeriodsAsync());
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            fixture.ForRole(RoleNames.Owner).CreatePeriodAsync(2026, 10));
        Assert.NotEqual(Guid.Empty,
            await fixture.ForRole(RoleNames.Accounting).CreatePeriodAsync(2026, 10));
        Assert.Equal(fixture.Employee.Id,
            (await fixture.ForRole(RoleNames.Accounting)
                .GetEmployeeAsync(fixture.Employee.Id)).Employee.EmployeeId);
    }

    [Fact]
    public async Task Payroll_View_Does_Not_Expose_Insurance_Administration_Details()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ForRole(RoleNames.HR).CreateLaborInsuranceEnrollmentAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            Status = LaborInsuranceEnrollmentStatus.Enrolled,
            MonthlyInsuredSalary = 30000,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });

        var ownerView = await fixture.ForRole(RoleNames.Owner)
            .GetEmployeeAsync(fixture.Employee.Id);

        Assert.Empty(ownerView.LaborInsuranceEnrollments);
        Assert.Empty(ownerView.HealthInsuranceEnrollments);
    }

    [Fact]
    public async Task Seeded_Settings_Have_Standard_And_Custom_Plans()
    {
        await using var fixture = await Fixture.CreateAsync();
        var settings = await fixture.Service.GetSettingsAsync();
        Assert.Equal(18, settings.Components.Count);
        Assert.Contains(settings.Components, x => x.Code == "PERIODIC_FIXED_PAY");
        Assert.Contains(settings.Components, x =>
            x.Code == PayrollLegacyAdjustmentComponents.AttendanceAllowanceCode &&
            x.Category == PayrollComponentCategory.Earning &&
            x.CalculationKind == PayrollCalculationKind.ManualAdjustment);
        Assert.Contains(settings.Components, x =>
            x.Code == PayrollLegacyAdjustmentComponents.OvertimePayCode &&
            x.Category == PayrollComponentCategory.Earning &&
            x.CalculationKind == PayrollCalculationKind.ManualAdjustment);
        var standard = Assert.Single(settings.Plans, x => x.Code == "STANDARD_MONTHLY");
        Assert.Equal(29500, Assert.Single(standard.Components, x => x.Code == "BASE_SALARY").DefaultAmount);
        Assert.Equal(PayrollProrationKind.Monthly30Day,
            Assert.Single(standard.Components, x => x.Code == "BASE_SALARY").ProrationKind);
        Assert.Equal(2000, Assert.Single(standard.Components, x => x.Code == "MEAL_ALLOWANCE").DefaultAmount);
        Assert.Equal(PayrollProrationKind.Monthly30Day,
            Assert.Single(standard.Components, x => x.Code == "MEAL_ALLOWANCE").ProrationKind);
        Assert.Equal(2000, Assert.Single(standard.Components, x => x.Code == "ATTENDANCE_ALLOWANCE").DefaultAmount);
        var performance = Assert.Single(standard.Components, x => x.Code == "PERFORMANCE");
        Assert.Equal(5, performance.Tiers.Count);
        Assert.Equal(PayrollProrationKind.PendingPolicy, performance.ProrationKind);
        Assert.Contains(settings.Plans, x => x.Code == "CUSTOM_FIXED");
        Assert.Contains(settings.Plans, x => x.Code == "PERIODIC_FIXED");
    }

    [Fact]
    public async Task Assignment_And_Override_Overlaps_Are_Denied()
    {
        await using var fixture = await Fixture.CreateAsync();
        var plan = await fixture.Db.PayrollPlans.SingleAsync(x => x.Code == "STANDARD_MONTHLY");
        await fixture.Service.AssignPlanAsync(new() { EmployeeId = fixture.Employee.Id,
            PayrollPlanId = plan.Id, EffectiveFrom = new DateOnly(2026, 1, 1) });
        await Assert.ThrowsAsync<ApplicationValidationException>(() => fixture.Service.AssignPlanAsync(new()
        { EmployeeId = fixture.Employee.Id, PayrollPlanId = plan.Id, EffectiveFrom = new DateOnly(2026, 8, 1) }));
        var performance = await fixture.Db.PayrollComponentDefinitions.SingleAsync(x => x.Code == "PERFORMANCE");
        await fixture.Service.CreateOverrideAsync(new() { EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = performance.Id, Mode = PayrollOverrideMode.Replace,
            Amount = 12000, EffectiveFrom = new DateOnly(2026, 8, 1) });
        await Assert.ThrowsAsync<ApplicationValidationException>(() => fixture.Service.CreateOverrideAsync(new()
        { EmployeeId = fixture.Employee.Id, ComponentDefinitionId = performance.Id,
            Mode = PayrollOverrideMode.Disable, EffectiveFrom = new DateOnly(2026, 9, 1) }));
    }

    [Fact]
    public async Task Draft_Snapshots_Fixed_Override_Seniority_And_Pending_Lines()
    {
        await using var fixture = await Fixture.CreateAsync(new DateOnly(2018, 1, 1));
        var plan = await fixture.Db.PayrollPlans.SingleAsync(x => x.Code == "STANDARD_MONTHLY");
        await fixture.Service.AssignPlanAsync(new() { EmployeeId = fixture.Employee.Id,
            PayrollPlanId = plan.Id, EffectiveFrom = new DateOnly(2026, 1, 1) });
        var performance = await fixture.Db.PayrollComponentDefinitions.SingleAsync(x => x.Code == "PERFORMANCE");
        await fixture.Service.CreateOverrideAsync(new() { EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = performance.Id, Mode = PayrollOverrideMode.Replace,
            Amount = 12000, EffectiveFrom = new DateOnly(2026, 8, 1) });
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 8);
        var runId = await fixture.Service.CreateDraftAsync(periodId);
        var run = await fixture.Service.GetRunAsync(runId);
        var snapshot = Assert.Single(run.Employees);
        Assert.Equal(29500, Assert.Single(snapshot.Components, x => x.Code == "BASE_SALARY").ResolvedAmount);
        Assert.Equal(12000, Assert.Single(snapshot.Components, x => x.Code == "PERFORMANCE").ResolvedAmount);
        var attendance = Assert.Single(snapshot.Components, x => x.Code == "ATTENDANCE_ALLOWANCE");
        Assert.Equal(2000, attendance.StandardAmount); Assert.Null(attendance.ResolvedAmount);
        Assert.Equal(PayrollCalculationStatus.NeedsReview, attendance.CalculationStatus);
        Assert.DoesNotContain(fixture.Db.AuditLogs, x =>
            (x.OldValuesJson ?? "").Contains("29500", StringComparison.Ordinal) ||
            (x.NewValuesJson ?? "").Contains("29500", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_Assignment_Rolls_Back_Whole_Draft()
    {
        await using var fixture = await Fixture.CreateAsync();
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 8);
        await Assert.ThrowsAsync<ApplicationValidationException>(() => fixture.Service.CreateDraftAsync(periodId));
        Assert.Empty(fixture.Db.PayrollRuns);
        Assert.Empty(fixture.Db.PayrollEmployeeSnapshots);
        Assert.Equal(PayrollPeriodStatus.Open, (await fixture.Db.PayrollPeriods.FindAsync(periodId))!.Status);
    }

    [Fact]
    public async Task Custom_Fixed_Plan_Requires_And_Snapshots_Explicit_Base_Override()
    {
        await using var fixture = await Fixture.CreateAsync();
        var plan = await fixture.Db.PayrollPlans.SingleAsync(x => x.Code == "CUSTOM_FIXED");
        var baseSalary = await fixture.Db.PayrollComponentDefinitions.SingleAsync(x => x.Code == "BASE_SALARY");
        await fixture.Service.AssignPlanAsync(new() { EmployeeId = fixture.Employee.Id,
            PayrollPlanId = plan.Id, EffectiveFrom = new DateOnly(2026, 1, 1) });
        await fixture.Service.CreateOverrideAsync(new() { EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = baseSalary.Id, Mode = PayrollOverrideMode.Replace,
            Amount = 50000, EffectiveFrom = new DateOnly(2026, 1, 1) });
        var run = await fixture.Service.GetRunAsync(await fixture.Service.CreateDraftAsync(
            await fixture.Service.CreatePeriodAsync(2026, 8)));
        Assert.Equal(50000, Assert.Single(Assert.Single(run.Employees).Components,
            x => x.Code == "BASE_SALARY").ResolvedAmount);
    }

    [Fact]
    public async Task Custom_Fixed_Missing_Base_Override_Blocks_Draft_Without_Implicit_Zero()
    {
        await using var fixture = await Fixture.CreateAsync();
        var plan = await fixture.Db.PayrollPlans.SingleAsync(x => x.Code == "CUSTOM_FIXED");
        await fixture.Service.AssignPlanAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            PayrollPlanId = plan.Id,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 8);

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            fixture.Service.CreateDraftAsync(periodId));

        Assert.Empty(fixture.Db.PayrollRuns);
        Assert.Empty(fixture.Db.PayrollEmployeeSnapshots);
    }

    [Fact]
    public async Task Direct_Fixed_Component_Override_Is_Added_Without_Employee_Code_Logic()
    {
        await using var fixture = await Fixture.CreateAsync();
        var plan = await fixture.Db.PayrollPlans.SingleAsync(x => x.Code == "STANDARD_MONTHLY");
        var job = await fixture.Db.PayrollComponentDefinitions.SingleAsync(x => x.Code == "JOB_ALLOWANCE");
        await fixture.Service.AssignPlanAsync(new() { EmployeeId = fixture.Employee.Id,
            PayrollPlanId = plan.Id, EffectiveFrom = new DateOnly(2026, 1, 1) });
        await fixture.Service.CreateOverrideAsync(new() { EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = job.Id, Mode = PayrollOverrideMode.Replace,
            Amount = 3000, EffectiveFrom = new DateOnly(2026, 1, 1) });
        var run = await fixture.Service.GetRunAsync(await fixture.Service.CreateDraftAsync(
            await fixture.Service.CreatePeriodAsync(2026, 8)));
        Assert.Equal(3000, Assert.Single(Assert.Single(run.Employees).Components,
            x => x.Code == "JOB_ALLOWANCE").ResolvedAmount);
    }

    [Fact]
    public async Task July_Mid_Month_Preview_Proration_Matches_Approved_Base_And_Meal_Regression()
    {
        await using var fixture = await Fixture.CreateAsync(new DateOnly(2026, 7, 13));
        await fixture.AssignStandardAsync();

        var preview = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 7);

        var salary = Assert.Single(preview.Components, x => x.Code == "BASE_SALARY");
        Assert.Equal(29500, salary.FullMonthlyAmount);
        Assert.Equal(19, salary.PayableDays);
        Assert.Equal(18683.333333m, salary.RawProratedAmount);
        Assert.Equal(18683, salary.ResolvedAmount);
        var meal = Assert.Single(preview.Components, x => x.Code == "MEAL_ALLOWANCE");
        Assert.Equal(1266.666667m, meal.RawProratedAmount);
        Assert.Equal(1267, meal.ResolvedAmount);
        Assert.Equal(19950, preview.FixedEarningsSubtotal);
    }

    [Fact]
    public async Task Partial_Month_Performance_Resolves_Full_Amount_But_Remains_Policy_Pending()
    {
        await using var fixture = await Fixture.CreateAsync(
            new DateOnly(2020, 1, 1), new DateOnly(2026, 7, 20));
        await fixture.AssignStandardAsync();

        var preview = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 7);

        var performance = Assert.Single(preview.Components, x => x.Code == "PERFORMANCE");
        Assert.Equal(7500, performance.FullMonthlyAmount);
        Assert.Equal(20, performance.PayableDays);
        Assert.Equal(PayrollProrationKind.PendingPolicy, performance.ProrationKind);
        Assert.Equal(PayrollCalculationStatus.PolicyPending, performance.CalculationStatus);
        Assert.Null(performance.RawProratedAmount);
        Assert.Null(performance.ResolvedAmount);
    }

    [Fact]
    public async Task Partial_Month_Direct_Job_Allowance_Is_Policy_Pending_Not_Zero()
    {
        await using var fixture = await Fixture.CreateAsync(new DateOnly(2026, 7, 13));
        await fixture.AssignStandardAsync();
        var job = await fixture.Db.PayrollComponentDefinitions.SingleAsync(
            x => x.Code == "JOB_ALLOWANCE");
        await fixture.Service.CreateOverrideAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = job.Id,
            Mode = PayrollOverrideMode.Replace,
            Amount = 3000,
            EffectiveFrom = new DateOnly(2026, 7, 1)
        });

        var preview = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 7);

        var allowance = Assert.Single(preview.Components, x => x.Code == "JOB_ALLOWANCE");
        Assert.Equal(3000, allowance.FullMonthlyAmount);
        Assert.Equal(PayrollCalculationStatus.PolicyPending, allowance.CalculationStatus);
        Assert.Null(allowance.ResolvedAmount);
    }

    [Fact]
    public async Task Base_Override_Precedence_Is_Applied_Before_Shared_Proration()
    {
        await using var fixture = await Fixture.CreateAsync(new DateOnly(2026, 7, 13));
        await fixture.AssignStandardAsync();
        var salary = await fixture.Db.PayrollComponentDefinitions.SingleAsync(
            x => x.Code == "BASE_SALARY");
        await fixture.Service.CreateOverrideAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = salary.Id,
            Mode = PayrollOverrideMode.Replace,
            Amount = 50000,
            EffectiveFrom = new DateOnly(2026, 7, 1)
        });

        var preview = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 7);

        var component = Assert.Single(preview.Components, x => x.Code == "BASE_SALARY");
        Assert.Equal(50000, component.FullMonthlyAmount);
        Assert.Equal(31667, component.ResolvedAmount);
        Assert.Equal(PayrollSnapshotSourceType.EmployeeOverride, component.SourceType);
    }

    [Fact]
    public async Task Add_Disable_And_Future_Overrides_Preserve_P1_Precedence()
    {
        await using var fixture = await Fixture.CreateAsync(new DateOnly(2020, 1, 1));
        await fixture.AssignStandardAsync();
        var performance = await fixture.Db.PayrollComponentDefinitions.SingleAsync(
            x => x.Code == "PERFORMANCE");
        var meal = await fixture.Db.PayrollComponentDefinitions.SingleAsync(
            x => x.Code == "MEAL_ALLOWANCE");
        await fixture.Service.CreateOverrideAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = performance.Id,
            Mode = PayrollOverrideMode.Add,
            Amount = 2000,
            EffectiveFrom = new DateOnly(2026, 8, 1)
        });
        await fixture.Service.CreateOverrideAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = meal.Id,
            Mode = PayrollOverrideMode.Disable,
            EffectiveFrom = new DateOnly(2026, 8, 1)
        });

        var july = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 7);
        Assert.Equal(7500, Assert.Single(july.Components,
            x => x.Code == "PERFORMANCE").ResolvedAmount);
        Assert.Equal(2000, Assert.Single(july.Components,
            x => x.Code == "MEAL_ALLOWANCE").ResolvedAmount);

        var august = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 8);
        Assert.Equal(9500, Assert.Single(august.Components,
            x => x.Code == "PERFORMANCE").ResolvedAmount);
        var disabledMeal = Assert.Single(august.Components, x => x.Code == "MEAL_ALLOWANCE");
        Assert.Equal(PayrollCalculationStatus.Disabled, disabledMeal.CalculationStatus);
        Assert.Null(disabledMeal.ResolvedAmount);
    }

    [Theory]
    [InlineData(50000)]
    [InlineData(20000)]
    [InlineData(5000)]
    [InlineData(41000)]
    public async Task Custom_Fixed_Base_Uses_Explicit_Override_Without_Standard_Leakage(
        decimal amount)
    {
        await using var fixture = await Fixture.CreateAsync();
        var plan = await fixture.Db.PayrollPlans.SingleAsync(x => x.Code == "CUSTOM_FIXED");
        var salary = await fixture.Db.PayrollComponentDefinitions.SingleAsync(
            x => x.Code == "BASE_SALARY");
        await fixture.Service.AssignPlanAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            PayrollPlanId = plan.Id,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await fixture.Service.CreateOverrideAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = salary.Id,
            Mode = PayrollOverrideMode.Replace,
            Amount = amount,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });

        var preview = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 8);

        var component = Assert.Single(preview.Components, x => x.Code == "BASE_SALARY");
        Assert.Equal(amount, component.FullMonthlyAmount);
        Assert.Equal(amount, component.ResolvedAmount);
        Assert.NotEqual(29500, component.ResolvedAmount);
    }

    [Fact]
    public async Task Draft_Persists_Proration_Evidence_And_Excludes_Unresolved_From_Fixed_Subtotal()
    {
        await using var fixture = await Fixture.CreateAsync(new DateOnly(2026, 7, 13));
        await fixture.AssignStandardAsync();
        var runId = await fixture.Service.CreateDraftAsync(
            await fixture.Service.CreatePeriodAsync(2026, 7));

        var employee = Assert.Single((await fixture.Service.GetRunAsync(runId)).Employees);
        var salary = Assert.Single(employee.Components, x => x.Code == "BASE_SALARY");
        Assert.Equal(PayrollProrationKind.Monthly30Day, salary.ProrationKind);
        Assert.Equal(19, salary.PayableDays);
        Assert.Equal(18683, salary.ResolvedAmount);
        Assert.Equal(PayrollCalculationStatus.PolicyPending,
            Assert.Single(employee.Components, x => x.Code == "PERFORMANCE").CalculationStatus);
        Assert.Equal(PayrollCalculationStatus.Resolved,
            Assert.Single(employee.Components, x => x.Code == "OVERTIME_FIRST_2H").CalculationStatus);
        Assert.Equal(19950, employee.FixedEarningsSubtotal);
    }

    [Fact]
    public async Task Later_Employee_And_Plan_Changes_Do_Not_Mutate_Draft_Snapshot()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        var runId = await fixture.Service.CreateDraftAsync(
            await fixture.Service.CreatePeriodAsync(2026, 8));
        var component = await fixture.Db.PayrollPlanComponents
            .Include(x => x.ComponentDefinition)
            .SingleAsync(x => x.PayrollPlan.Code == "STANDARD_MONTHLY" &&
                x.ComponentDefinition.Code == "BASE_SALARY");
        var firstTwoRate = await fixture.Db.OvertimePayRatePolicies.SingleAsync(
            x => x.Bucket == OvertimePayBucket.FirstTwoHours);
        var certificate = await fixture.Db.PayrollComponentDefinitions.SingleAsync(
            x => x.Code == "CERTIFICATE_ALLOWANCE");
        fixture.Db.Entry(component).Property(x => x.DefaultAmount).CurrentValue = 31000;
        fixture.Db.Entry(firstTwoRate).Property(x => x.Multiplier).CurrentValue = 9m;
        fixture.Db.Entry(certificate).Property(x =>
            x.IncludeInOvertimeHourlyBase).CurrentValue = true;
        fixture.Employee.Update("變更後姓名", fixture.Department.Id,
            fixture.Employee.HireDate, null, null, fixture.Employee.TerminationDate,
            null, null, DateTimeOffset.UtcNow);
        await fixture.Db.SaveChangesAsync();

        var snapshot = Assert.Single((await fixture.Service.GetRunAsync(runId)).Employees);
        Assert.Equal("薪資測試員工", snapshot.EmployeeName);
        Assert.Equal(29500, Assert.Single(snapshot.Components,
            x => x.Code == "BASE_SALARY").FullMonthlyAmount);
        Assert.Equal(1.34m, snapshot.OvertimePay!.Buckets.Single(
            x => x.Bucket == OvertimePayBucket.FirstTwoHours).Multiplier);
        Assert.DoesNotContain(snapshot.OvertimePay.IncludedComponents,
            x => x.Code == "CERTIFICATE_ALLOWANCE");
    }

    [Fact]
    public async Task Manual_Adjustment_Requires_Compatible_Component_And_Open_Period()
    {
        await using var fixture = await Fixture.CreateAsync();
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 8);
        var caseBonus = await fixture.Db.PayrollComponentDefinitions.SingleAsync(x => x.Code == "CASE_BONUS");
        var id = await fixture.Service.CreateAdjustmentAsync(new() { PayrollPeriodId = periodId,
            EmployeeId = fixture.Employee.Id, ComponentDefinitionId = caseBonus.Id, Amount = 2300,
            Direction = PayrollAdjustmentDirection.Earning, Reason = "測試案件" });
        Assert.NotEqual(Guid.Empty, id);
        var baseSalary = await fixture.Db.PayrollComponentDefinitions.SingleAsync(x => x.Code == "BASE_SALARY");
        await Assert.ThrowsAsync<ApplicationValidationException>(() => fixture.Service.CreateAdjustmentAsync(new()
        { PayrollPeriodId = periodId, EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = baseSalary.Id, Amount = 1,
            Direction = PayrollAdjustmentDirection.Earning, Reason = "不得使用" }));
    }

    [Fact]
    public async Task Snapshot_Is_Protected_From_Update_And_Delete()
    {
        await using var fixture = await Fixture.CreateAsync();
        var snapshot = new PayrollEmployeeSnapshot(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), fixture.Employee.Id,
            fixture.Employee.EmployeeNumber, fixture.Employee.ChineseName, fixture.Department.Id,
            fixture.Department.Name, fixture.Employee.HireDate, null, null, null,
            DateTimeOffset.UtcNow, PayrollEmployeeSetupStatus.Ready);
        fixture.Db.Attach(snapshot); fixture.Db.Entry(snapshot).State = EntityState.Deleted;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Overtime_Evidence_Snapshot_Is_Append_Only()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        var runId = await fixture.Service.CreateDraftAsync(
            await fixture.Service.CreatePeriodAsync(2026, 8));
        var evidence = await fixture.Db.PayrollOvertimePaySnapshots.SingleAsync(
            x => x.EmployeeSnapshot.PayrollRunId == runId);

        fixture.Db.Entry(evidence).Property(x =>
            x.TotalRecognizedMinutes).CurrentValue = 999;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Semiannual_Preview_Excludes_NonPay_Month_And_Resolves_Pay_Month()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.CreatePayCycleAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            Type = PayrollPayCycleType.SemiannualFixed,
            EffectiveFrom = new DateOnly(2025, 12, 1),
            AnchorPayMonth = new DateOnly(2025, 12, 1),
            FixedPaymentAmount = 4000,
            Reason = "Payroll special pay cycle setup"
        });

        var july = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 7);
        Assert.Equal(PayrollParticipationStatus.NonPayMonth,
            july.ParticipationStatus);
        Assert.Equal("本月不發薪", july.ParticipationMessage);
        Assert.Empty(july.Components);

        var december = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 12);
        Assert.Equal(PayrollParticipationStatus.Eligible,
            december.ParticipationStatus);
        Assert.Equal(4000, Assert.Single(december.Components,
            x => x.Code == "PERIODIC_FIXED_PAY").ResolvedAmount);
        Assert.Equal(4000, december.FixedEarningsSubtotal);
    }

    [Fact]
    public async Task Periodic_Accrual_Preview_Excludes_July_And_Aggregates_December()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.CreatePayCycleAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            Type = PayrollPayCycleType.PeriodicAccruedFixed,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            AnchorPayMonth = new DateOnly(2025, 12, 1),
            MonthlyFixedAmount = 4000,
            CycleMonths = 6,
            PaymentTiming = PayrollPeriodicPaymentTiming.CycleStart,
            Reason = "periodic accrual test"
        });

        var july = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 7);
        Assert.Equal(PayrollParticipationStatus.NonPayMonth,
            july.ParticipationStatus);
        Assert.Empty(july.Components);

        var december = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 12);
        var periodic = Assert.Single(december.Components,
            x => x.Code == "PERIODIC_FIXED_PAY");
        Assert.Equal(24000, periodic.ResolvedAmount);
        Assert.Equal(new DateOnly(2026, 12, 1),
            periodic.PeriodicAccrual!.CoveredFrom);
        Assert.Equal(new DateOnly(2027, 5, 31),
            periodic.PeriodicAccrual.CoveredTo);
        Assert.Equal(6, periodic.PeriodicAccrual.Months.Count);

        var periodId = await fixture.Service.CreatePeriodAsync(2026, 12);
        var batch = await fixture.Service.CreateInitialDraftAsync(periodId);
        Assert.Single(batch.Items);
        Assert.NotNull(batch.Items[0].SnapshotId);
        var evidence = await fixture.Db.PayrollPeriodicAccrualSnapshots
            .Include(x => x.Months).SingleAsync();
        Assert.Equal(24000, evidence.TotalAmount);
        Assert.Equal(6, evidence.Months.Count);
    }

    [Fact]
    public async Task Labor_And_Occupational_Enrollments_Persist_Independently()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.CreateLaborInsuranceEnrollmentAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            Status = LaborInsuranceEnrollmentStatus.Enrolled,
            MonthlyLaborInsuredSalary = 45800,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        var insurance = fixture.InsuranceForRole(RoleNames.Accounting);
        var request = Occupational(fixture.Employee.Id, 72800,
            new DateOnly(2026, 1, 1));
        var preview = await insurance.PreviewOccupationalAsync(request);
        await insurance.ApplyOccupationalAsync(request, preview.PreviewToken);

        var detail = await fixture.Service.GetEmployeeAsync(fixture.Employee.Id);
        var enrollment = Assert.Single(detail.LaborInsuranceEnrollments);
        Assert.Equal(45800, enrollment.MonthlyLaborInsuredSalary);
        var occupational = Assert.Single(
            detail.OccupationalInsuranceEnrollments!);
        Assert.Equal(72800, occupational.MonthlyInsuredSalary);
    }

    [Fact]
    public async Task PayCycle_Overlap_Is_Rejected_And_Requires_PayrollManage()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new CreatePayrollPayCycleRequest
        {
            EmployeeId = fixture.Employee.Id,
            Type = PayrollPayCycleType.Monthly,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Reason = "test"
        };
        await fixture.Service.CreatePayCycleAsync(request);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            fixture.Service.CreatePayCycleAsync(request));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            fixture.ForRole(RoleNames.HR).CreatePayCycleAsync(new()
            {
                EmployeeId = fixture.Employee.Id,
                EffectiveFrom = new DateOnly(2027, 1, 1),
                Reason = "denied"
            }));
        Assert.Contains(fixture.Db.AuditLogs,
            x => x.Action == "PayrollPayCycleCreated");
    }

    [Fact]
    public async Task Effective_Dated_Custom_Fixed_Remains_41000_Then_Changes_To_12000()
    {
        await using var fixture = await Fixture.CreateAsync();
        var plan = await fixture.Db.PayrollPlans.SingleAsync(
            x => x.Code == "CUSTOM_FIXED");
        var salary = await fixture.Db.PayrollComponentDefinitions.SingleAsync(
            x => x.Code == "BASE_SALARY");
        await fixture.Service.AssignPlanAsync(new()
        {
            EmployeeId = fixture.Employee.Id, PayrollPlanId = plan.Id,
            EffectiveFrom = new DateOnly(2026, 7, 1),
            EffectiveTo = new DateOnly(2026, 8, 31)
        });
        await fixture.Service.CreateOverrideAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = salary.Id,
            Mode = PayrollOverrideMode.Replace, Amount = 41000,
            EffectiveFrom = new DateOnly(2026, 7, 1),
            EffectiveTo = new DateOnly(2026, 8, 31)
        });
        await fixture.Service.AssignPlanAsync(new()
        {
            EmployeeId = fixture.Employee.Id, PayrollPlanId = plan.Id,
            EffectiveFrom = new DateOnly(2026, 9, 1)
        });
        await fixture.Service.CreateOverrideAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            ComponentDefinitionId = salary.Id,
            Mode = PayrollOverrideMode.Replace, Amount = 12000,
            EffectiveFrom = new DateOnly(2026, 9, 1),
            ReasonCode = "part-time"
        });

        foreach (var (year, month, expected) in new[]
        {
            (2026, 7, 41000m), (2026, 8, 41000m),
            (2026, 9, 12000m), (2026, 10, 12000m),
            (2026, 7, 41000m)
        })
        {
            var preview = await fixture.Service.PreviewFixedEarningsAsync(
                fixture.Employee.Id, year, month);
            Assert.Equal(expected, Assert.Single(preview.Components,
                x => x.Code == "BASE_SALARY").ResolvedAmount);
        }
    }

    [Fact]
    public async Task Batch_Uses_Lifecycle_And_PayCycle_Eligibility()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        var inactive = new Employee(Guid.NewGuid(), "TEST001", "停用測試",
            fixture.Department.Id, new DateOnly(2020, 1, 1), fixture.Now);
        inactive.Deactivate(fixture.Now);
        var periodic = new Employee(Guid.NewGuid(), "PAY016", "週期員工",
            fixture.Department.Id, new DateOnly(2020, 1, 1), fixture.Now);
        fixture.Db.Employees.AddRange(inactive, periodic);
        fixture.Db.EmployeePayrollPayCycles.Add(new EmployeePayrollPayCycle(
            Guid.NewGuid(), periodic.Id, PayrollPayCycleType.SemiannualFixed,
            new DateOnly(2025, 12, 1), anchorPayMonth: new DateOnly(2025, 12, 1),
            fixedPaymentAmount: 4000, reason: "test"));
        await fixture.Db.SaveChangesAsync();

        var periodId = await fixture.Service.CreatePeriodAsync(2026, 7);
        var batch = await fixture.Service.CreateInitialDraftAsync(periodId);
        Assert.Single(batch.Items);
        var month = await fixture.Service.GetMonthAsync(periodId);
        Assert.Equal(1, month.EligibleEmployeeCount);
        Assert.Equal(1, month.CurrentEmployeeCount);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(HRSystemDbContext db, PayrollService service, Department department,
            Employee employee, DateTimeOffset now)
        { Db = db; Service = service; Department = department; Employee = employee; Now = now; }
        public HRSystemDbContext Db { get; }
        public PayrollService Service { get; }
        public Department Department { get; }
        public Employee Employee { get; }
        public DateTimeOffset Now { get; }
        public static async Task<Fixture> CreateAsync(
            DateOnly? hireDate = null, DateOnly? terminationDate = null)
        {
            var options = new DbContextOptionsBuilder<HRSystemDbContext>()
                .UseInMemoryDatabase($"Payroll-{Guid.NewGuid()}",
                    database => database.EnableNullChecks(false)).Options;
            var db = new HRSystemDbContext(options); await db.Database.EnsureCreatedAsync();
            var now = new DateTimeOffset(2026, 8, 25, 1, 0, 0, TimeSpan.Zero);
            var department = new Department(Guid.NewGuid(), "PAY", "薪資測試部", now);
            var employee = new Employee(Guid.NewGuid(), "PAY001", "薪資測試員工",
                department.Id, hireDate ?? new DateOnly(2020, 1, 1), now,
                terminationDate: terminationDate);
            db.Departments.Add(department); db.Employees.Add(employee); await db.SaveChangesAsync();
            return new Fixture(db,
                new PayrollService(db, new PayrollUser(RoleNames.Admin), new FixedTimeProvider(now)),
                department, employee, now);
        }
        public PayrollService ForRole(string role) =>
            new(Db, new PayrollUser(role), new FixedTimeProvider(Now));
        public InsuranceManagementService InsuranceForRole(string role) =>
            new(Db, new PayrollUser(role), new FixedTimeProvider(Now));
        public async Task AssignStandardAsync()
        {
            var plan = await Db.PayrollPlans.SingleAsync(x => x.Code == "STANDARD_MONTHLY");
            await Service.AssignPlanAsync(new()
            {
                EmployeeId = Employee.Id,
                PayrollPlanId = plan.Id,
                EffectiveFrom = new DateOnly(2026, 1, 1)
            });
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); }
    }

    private sealed class PayrollUser(string assignedRole) : ICurrentUser
    {
        public string? UserId => "payroll-admin"; public Guid? EmployeeId => null;
        public string? DisplayName => "Payroll Admin"; public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true; public bool IsInRole(string role) => role == assignedRole;
        public bool HasPermission(string policy) => RolePermissions.HasPermission([assignedRole], policy);
    }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
}
