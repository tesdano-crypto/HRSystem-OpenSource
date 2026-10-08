using HRSystem.Application.Common.Exceptions;
using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.IntegrationTests;

public sealed partial class PayrollFoundationIntegrationTests
{
    [Fact]
    public async Task Health_Insurance_Setting_Rejects_Overlapping_Periods()
    {
        await using var fixture = await Fixture.CreateAsync();
        await AddHealthEnrollmentAsync(fixture);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            fixture.Service.CreateHealthInsuranceEnrollmentAsync(new()
            {
                EmployeeId = fixture.Employee.Id,
                Status = HealthInsuranceEnrollmentStatus.Enrolled,
                MonthlyInsuredAmount = 32000,
                DependentCount = 1,
                EffectiveFrom = new DateOnly(2026, 8, 1)
            }));
    }

    [Fact]
    public async Task Preview_Resolves_Health_Insurance_Independently_From_Labor()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        await AddHealthAsync(fixture);
        var preview = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 8);
        var health = Assert.Single(preview.Components, x => x.Code == "HEALTH_INSURANCE");
        Assert.Equal(1396, health.ResolvedAmount);
        Assert.Equal(2, health.HealthInsurance!.ActualDependentCount);
        Assert.Equal(3, health.HealthInsurance.ContributionUnits);
        Assert.Equal(PayrollCalculationStatus.NeedsSetup,
            Assert.Single(preview.Components, x => x.Code == "LABOR_INSURANCE").CalculationStatus);
    }

    [Fact]
    public async Task Preview_Charges_Full_Health_Month_For_MidMonth_Enrollment()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        await fixture.Service.CreateHealthInsuranceEnrollmentAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            Status = HealthInsuranceEnrollmentStatus.Enrolled,
            MonthlyInsuredAmount = 30000,
            DependentCount = 2,
            EffectiveFrom = new DateOnly(2026, 8, 13)
        });
        await AddHealthPolicyAsync(fixture);

        var health = Assert.Single((await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 8)).Components,
            x => x.Code == "HEALTH_INSURANCE");

        Assert.Equal(PayrollCalculationStatus.Resolved, health.CalculationStatus);
        Assert.Equal(1396, health.ResolvedAmount);
    }

    [Fact]
    public async Task Preview_Uses_Historical_Health_Rows_To_Resolve_Later_Month_As_Not_Covered()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        fixture.Db.EmployeeHealthInsuranceEnrollments.Add(
            new EmployeeHealthInsuranceEnrollment(Guid.NewGuid(), fixture.Employee.Id,
                HealthInsuranceEnrollmentStatus.Enrolled, 60800, 1,
                new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31)));
        await AddHealthPolicyAsync(fixture);

        var health = Assert.Single((await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 9)).Components,
            x => x.Code == "HEALTH_INSURANCE");

        Assert.Equal(PayrollCalculationStatus.Resolved, health.CalculationStatus);
        Assert.Equal(0, health.ResolvedAmount);
    }

    [Fact]
    public async Task Preview_Classifies_MidMonth_Health_TransferOut_As_NeedsReview()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        fixture.Db.EmployeeHealthInsuranceEnrollments.Add(
            new EmployeeHealthInsuranceEnrollment(Guid.NewGuid(), fixture.Employee.Id,
                HealthInsuranceEnrollmentStatus.Enrolled, 30000, 0,
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20)));
        await AddHealthPolicyAsync(fixture);

        var health = Assert.Single((await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 8)).Components,
            x => x.Code == "HEALTH_INSURANCE");

        Assert.Equal(PayrollCalculationStatus.NeedsReview,
            health.CalculationStatus);
        Assert.Null(health.ResolvedAmount);
    }

    [Fact]
    public async Task Draft_Persists_Health_Snapshot_And_Evidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        await AddHealthAsync(fixture);
        var runId = await fixture.Service.CreateDraftAsync(
            await fixture.Service.CreatePeriodAsync(2026, 8));
        var component = Assert.Single(Assert.Single(
            (await fixture.Service.GetRunAsync(runId)).Employees).Components,
            x => x.Code == "HEALTH_INSURANCE");
        Assert.Equal(1396, component.ResolvedAmount);
        Assert.Equal("synthetic-health-2026", component.HealthInsurance!.PolicyVersion);
        Assert.Equal(32, (await fixture.Db.PayrollHealthInsuranceSnapshots
            .SingleAsync()).SourceFingerprint.Length);
        Assert.Equal(1, await fixture.Db.PayrollHealthInsuranceEvidence.CountAsync());
    }

    [Fact]
    public async Task Missing_Health_Policy_Is_Policy_Pending_And_NotEnrolled_Is_Zero()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        await AddHealthEnrollmentAsync(fixture);
        var pending = Assert.Single((await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 8)).Components, x => x.Code == "HEALTH_INSURANCE");
        Assert.Equal(PayrollCalculationStatus.PolicyPending, pending.CalculationStatus);
        Assert.Null(pending.ResolvedAmount);

        await using var second = await Fixture.CreateAsync();
        await second.AssignStandardAsync();
        await second.Service.CreateHealthInsuranceEnrollmentAsync(new()
        {
            EmployeeId = second.Employee.Id,
            Status = HealthInsuranceEnrollmentStatus.NotEnrolled,
            EffectiveFrom = new DateOnly(2020, 1, 1)
        });
        var zero = Assert.Single((await second.Service.PreviewFixedEarningsAsync(
            second.Employee.Id, 2026, 8)).Components, x => x.Code == "HEALTH_INSURANCE");
        Assert.Equal(0, zero.ResolvedAmount);
        Assert.Equal(PayrollCalculationStatus.Resolved, zero.CalculationStatus);
    }

    [Fact]
    public async Task Health_Audit_Contains_No_Insured_Amount_Or_Dependent_Count()
    {
        await using var fixture = await Fixture.CreateAsync();
        await AddHealthEnrollmentAsync(fixture);
        var audit = await fixture.Db.AuditLogs.SingleAsync(x =>
            x.Action == "EmployeeHealthInsuranceEnrollmentCreated");
        Assert.DoesNotContain("30000", audit.NewValuesJson ?? string.Empty);
        Assert.DoesNotContain("DependentCount\":2", audit.NewValuesJson ?? string.Empty);
    }

    private static async Task AddHealthEnrollmentAsync(Fixture fixture)
    {
        await fixture.Service.CreateHealthInsuranceEnrollmentAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            Status = HealthInsuranceEnrollmentStatus.Enrolled,
            MonthlyInsuredAmount = 30000,
            DependentCount = 2,
            EffectiveFrom = new DateOnly(2020, 1, 1)
        });
    }

    private static async Task AddHealthAsync(Fixture fixture)
    {
        await AddHealthEnrollmentAsync(fixture);
        await AddHealthPolicyAsync(fixture);
    }

    private static async Task AddHealthPolicyAsync(Fixture fixture)
    {
        fixture.Db.HealthInsuranceRatePolicies.Add(new HealthInsuranceRatePolicy(
            Guid.NewGuid(), "synthetic-health-2026", .0517m, .30m, 3,
            HealthInsuranceDependentBillingRule.EmployeeAndCappedDependents,
            HealthInsuranceContributionPeriodPolicy.FullPeriodOnly,
            new DateOnly(2026, 1, 1)));
        await fixture.Db.SaveChangesAsync();
    }
}
