using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Payroll;
using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.IntegrationTests;

public sealed partial class PayrollFoundationIntegrationTests
{
    [Fact]
    public async Task Labor_Insurance_Setting_Rejects_Overlapping_Periods()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.CreateLaborInsuranceEnrollmentAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            Status = LaborInsuranceEnrollmentStatus.Enrolled,
            MonthlyInsuredSalary = 30000,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            fixture.Service.CreateLaborInsuranceEnrollmentAsync(new()
            {
                EmployeeId = fixture.Employee.Id,
                Status = LaborInsuranceEnrollmentStatus.Enrolled,
                MonthlyInsuredSalary = 32000,
                EffectiveFrom = new DateOnly(2026, 8, 1)
            }));
    }

    [Fact]
    public async Task Preview_Resolves_Labor_Insurance_From_Batched_Setting_And_Policy()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        await AddInsuranceAsync(fixture);

        var preview = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 8);

        var component = Assert.Single(preview.Components,
            x => x.Code == "LABOR_INSURANCE");
        Assert.Equal(660, component.ResolvedAmount);
        Assert.Equal(PayrollCalculationStatus.Resolved, component.CalculationStatus);
        Assert.Equal(30000, component.LaborInsurance!.MonthlyInsuredSalary);
        Assert.Equal(2, component.LaborInsurance.Contributions.Count);
        Assert.Equal(PayrollCalculationStatus.NeedsSetup,
            Assert.Single(preview.Components, x => x.Code == "HEALTH_INSURANCE").CalculationStatus);
    }

    [Fact]
    public async Task Preview_Prorates_MidMonth_Labor_And_Employment_With_Shared_Thirty_Day_Policy()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        await fixture.Service.CreateLaborInsuranceEnrollmentAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            Status = LaborInsuranceEnrollmentStatus.Enrolled,
            MonthlyInsuredSalary = 30000,
            EffectiveFrom = new DateOnly(2026, 8, 13)
        });
        fixture.Db.LaborInsuranceRatePolicies.Add(new LaborInsuranceRatePolicy(
            Guid.NewGuid(), "synthetic-30-day",
            LaborInsuranceCoverage.OrdinaryAccident | LaborInsuranceCoverage.Employment,
            .10m, .01m, .20m,
            LaborInsuranceContributionPeriodPolicy.ThirtyDayProrated,
            new DateOnly(2026, 1, 1)));
        await fixture.Db.SaveChangesAsync();

        var preview = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 8);

        var component = Assert.Single(preview.Components,
            x => x.Code == "LABOR_INSURANCE");
        Assert.Equal(PayrollCalculationStatus.Resolved, component.CalculationStatus);
        Assert.Equal(396, component.ResolvedAmount);
        Assert.Equal(360, Assert.Single(component.LaborInsurance!.Contributions,
            x => x.Kind == LaborInsuranceContributionKind.OrdinaryAccident)
            .RawEmployeeAmount);
        Assert.Equal(36, Assert.Single(component.LaborInsurance.Contributions,
            x => x.Kind == LaborInsuranceContributionKind.Employment)
            .RawEmployeeAmount);
    }

    [Fact]
    public async Task Draft_Persists_Immutable_Labor_Insurance_Evidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        await AddInsuranceAsync(fixture);

        var runId = await fixture.Service.CreateDraftAsync(
            await fixture.Service.CreatePeriodAsync(2026, 8));
        var component = Assert.Single(Assert.Single(
            (await fixture.Service.GetRunAsync(runId)).Employees).Components,
            x => x.Code == "LABOR_INSURANCE");

        Assert.Equal(660, component.ResolvedAmount);
        Assert.Equal("synthetic-2026", component.LaborInsurance!.PolicyVersion);
        Assert.Equal(32, (await fixture.Db.PayrollLaborInsuranceSnapshots
            .SingleAsync()).SourceFingerprint.Length);
        Assert.Equal(2, await fixture.Db.PayrollLaborInsuranceContributionEvidence.CountAsync());
    }

    [Fact]
    public async Task Missing_Production_Policy_Is_Explicitly_Policy_Pending()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        await fixture.Service.CreateLaborInsuranceEnrollmentAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            Status = LaborInsuranceEnrollmentStatus.Enrolled,
            MonthlyInsuredSalary = 30000,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });

        var preview = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 8);
        var labor = Assert.Single(preview.Components,
            x => x.Code == "LABOR_INSURANCE");
        Assert.Equal(PayrollCalculationStatus.PolicyPending, labor.CalculationStatus);
        Assert.Null(labor.ResolvedAmount);
    }

    [Fact]
    public async Task Explicit_Not_Enrolled_Drafts_Resolved_Zero_Without_Policy()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        await fixture.Service.CreateLaborInsuranceEnrollmentAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            Status = LaborInsuranceEnrollmentStatus.NotEnrolled,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });

        var preview = await fixture.Service.PreviewFixedEarningsAsync(
            fixture.Employee.Id, 2026, 8);
        var labor = Assert.Single(preview.Components,
            x => x.Code == "LABOR_INSURANCE");
        Assert.Equal(PayrollCalculationStatus.Resolved, labor.CalculationStatus);
        Assert.Equal(0, labor.ResolvedAmount);
    }

    private static async Task AddInsuranceAsync(Fixture fixture)
    {
        await fixture.Service.CreateLaborInsuranceEnrollmentAsync(new()
        {
            EmployeeId = fixture.Employee.Id,
            Status = LaborInsuranceEnrollmentStatus.Enrolled,
            MonthlyInsuredSalary = 30000,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        fixture.Db.LaborInsuranceRatePolicies.Add(new LaborInsuranceRatePolicy(
            Guid.NewGuid(), "synthetic-2026",
            LaborInsuranceCoverage.OrdinaryAccident | LaborInsuranceCoverage.Employment,
            .10m, .01m, .20m,
            LaborInsuranceContributionPeriodPolicy.FullPeriodOnly,
            new DateOnly(2026, 1, 1)));
        await fixture.Db.SaveChangesAsync();
    }
}
