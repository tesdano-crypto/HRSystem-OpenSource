using System.Text.Json;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Payroll;
using HRSystem.Application.Security;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.IntegrationTests;

public sealed partial class PayrollFoundationIntegrationTests
{
    [Fact]
    public async Task Insurance_Workflow_Applies_Independent_Labor_And_Occupational_Enrollments()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.InsuranceForRole(RoleNames.Accounting);
        var request = Labor(fixture.Employee.Id, 45800,
            new DateOnly(2026, 7, 1));

        var preview = await service.PreviewLaborAsync(request);

        Assert.True(preview.CanApply);
        Assert.Contains(preview.Warnings, x => x.Contains("政策待確認"));
        var applied = await service.ApplyLaborAsync(request, preview.PreviewToken);
        var enrollment = await fixture.Db.EmployeeLaborInsuranceEnrollments
            .SingleAsync(x => x.Id == applied.EnrollmentId);
        Assert.Equal(45800, enrollment.MonthlyLaborInsuredSalary);
        var occupationalRequest = Occupational(fixture.Employee.Id, 72800,
            new DateOnly(2026, 7, 1));
        var occupationalPreview = await service.PreviewOccupationalAsync(
            occupationalRequest);
        var occupationalApplied = await service.ApplyOccupationalAsync(
            occupationalRequest, occupationalPreview.PreviewToken);
        var occupational = await fixture.Db.EmployeeOccupationalInsuranceEnrollments
            .SingleAsync(x => x.Id == occupationalApplied.EnrollmentId);
        Assert.Equal(72800, occupational.MonthlyInsuredSalary);
        var audit = fixture.Db.AuditLogs.Single(x =>
            x.Action == "EmployeeLaborInsuranceEnrollmentCreated");
        using var auditJson = JsonDocument.Parse(audit.NewValuesJson!);
        Assert.Equal("會計維護投保資料",
            auditJson.RootElement.GetProperty("reason").GetString());
        Assert.DoesNotContain("45800", audit.NewValuesJson);
        Assert.DoesNotContain("72800", audit.NewValuesJson);
    }

    [Fact]
    public async Task Insurance_Apply_Requires_Current_Preview_Token()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.InsuranceForRole(RoleNames.Admin);
        var request = Labor(fixture.Employee.Id, 45800,
            new DateOnly(2026, 7, 1));
        var preview = await service.PreviewLaborAsync(request);
        request.MonthlyLaborInsuredSalary = 48200;

        var error = await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            service.ApplyLaborAsync(request, preview.PreviewToken));

        Assert.Contains("重新預覽", error.Message);
        Assert.Empty(fixture.Db.EmployeeLaborInsuranceEnrollments);
    }

    [Fact]
    public async Task New_Effective_Row_Closes_Prior_History_And_Marks_Current_Payroll_Stale()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.InsuranceForRole(RoleNames.HR);
        var original = Labor(fixture.Employee.Id, 45800,
            new DateOnly(2026, 1, 1));
        var firstPreview = await service.PreviewLaborAsync(original);
        var first = await service.ApplyLaborAsync(original, firstPreview.PreviewToken);
        await fixture.AssignStandardAsync();
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 9);
        await fixture.Service.CreateInitialDraftAsync(periodId);

        var changed = Labor(fixture.Employee.Id, 48200,
            new DateOnly(2026, 9, 1));
        var preview = await service.PreviewLaborAsync(changed);
        var result = await service.ApplyLaborAsync(changed, preview.PreviewToken);

        Assert.Equal(first.EnrollmentId, result.SupersededEnrollmentId);
        Assert.Equal(new DateOnly(2026, 8, 31),
            (await fixture.Db.EmployeeLaborInsuranceEnrollments
                .SingleAsync(x => x.Id == first.EnrollmentId)).EffectiveTo);
        Assert.Equal(2, await fixture.Db.EmployeeLaborInsuranceEnrollments.CountAsync());
        Assert.True((await fixture.Db.PayrollPeriodEmployeeCurrentSnapshots
            .SingleAsync(x => x.PayrollPeriodId == periodId &&
                x.EmployeeId == fixture.Employee.Id)).IsSourceChanged);
        Assert.Contains(fixture.Db.AuditLogs, x =>
            x.Action == "EmployeeLaborInsuranceEnrollmentSuperseded");
    }

    [Fact]
    public async Task Health_Withdrawal_Date_Is_Exclusive_For_Monthly_Coverage()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.InsuranceForRole(RoleNames.Accounting);
        var request = new PreviewHealthInsuranceChangeRequest
        {
            EmployeeId = fixture.Employee.Id,
            IsEnrolled = true,
            MonthlyHealthInsuredAmount = 60800,
            DependentCount = 1,
            CoverageFrom = new DateOnly(2026, 7, 1),
            WithdrawalDate = new DateOnly(2026, 9, 1),
            Reason = "會計維護投保資料"
        };
        var preview = await service.PreviewHealthAsync(request);
        await service.ApplyHealthAsync(request, preview.PreviewToken);
        var enrollment = await fixture.Db.EmployeeHealthInsuranceEnrollments.SingleAsync();

        Assert.Equal(new DateOnly(2026, 8, 31), enrollment.EffectiveTo);
        Assert.Equal(PayrollCalculationStatus.PolicyPending,
            Health(enrollment, 2026, 7).CalculationStatus);
        Assert.Equal(PayrollCalculationStatus.PolicyPending,
            Health(enrollment, 2026, 8).CalculationStatus);
        Assert.Equal(PayrollCalculationStatus.Resolved,
            Health(enrollment, 2026, 9).CalculationStatus);
        Assert.Equal(0, Health(enrollment, 2026, 9).FinalEmployeeDeduction);
        Assert.Equal(1, enrollment.DependentCount);
    }

    [Fact]
    public async Task Historical_Inactive_Employee_And_MidMonth_Coverage_Are_Allowed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var historical = new Employee(Guid.NewGuid(), "HIST001", "歷史員工",
            fixture.Department.Id, new DateOnly(2026, 1, 1), fixture.Now,
            terminationDate: new DateOnly(2026, 7, 31));
        historical.Deactivate(fixture.Now);
        fixture.Db.Employees.Add(historical);
        await fixture.Db.SaveChangesAsync();
        var service = fixture.InsuranceForRole(RoleNames.HR);
        var request = Labor(historical.Id, 29500,
            new DateOnly(2026, 7, 13));

        var preview = await service.PreviewLaborAsync(request);
        await service.ApplyLaborAsync(request, preview.PreviewToken);

        Assert.Equal(new DateOnly(2026, 7, 13),
            (await fixture.Db.EmployeeLaborInsuranceEnrollments.SingleAsync(
                x => x.EmployeeId == historical.Id)).EffectiveFrom);
        Assert.Contains(await service.SearchEmployeesAsync(includeInactive: true),
            x => x.EmployeeId == historical.Id && !x.IsActive);
    }

    [Fact]
    public async Task Finalized_Payroll_Is_Reported_As_Protected_And_Not_Marked_Stale()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 9);
        await fixture.Service.CreateInitialDraftAsync(periodId);
        var period = await fixture.Db.PayrollPeriods.SingleAsync(x => x.Id == periodId);
        period.FinalizePeriod();
        await fixture.Db.SaveChangesAsync();
        var request = Labor(fixture.Employee.Id, 45800,
            new DateOnly(2026, 9, 1));
        var service = fixture.InsuranceForRole(RoleNames.Accounting);

        var preview = await service.PreviewLaborAsync(request);

        var affected = Assert.Single(preview.AffectedPeriods);
        Assert.True(affected.IsFinalizedProtected);
        Assert.False(affected.WillMarkSourceChanged);
        Assert.Contains(preview.Warnings, x => x.Contains("正式結算"));
        var applied = await service.ApplyLaborAsync(request, preview.PreviewToken);
        Assert.Equal(0, applied.CurrentSnapshotsMarkedSourceChanged);
        Assert.False((await fixture.Db.PayrollPeriodEmployeeCurrentSnapshots
            .SingleAsync(x => x.PayrollPeriodId == periodId &&
                x.EmployeeId == fixture.Employee.Id)).IsSourceChanged);
    }

    [Fact]
    public async Task Overlap_Is_Rejected_Gap_Is_Allowed_And_Roles_Are_Enforced()
    {
        await using var fixture = await Fixture.CreateAsync();
        var accounting = fixture.InsuranceForRole(RoleNames.Accounting);
        var first = Labor(fixture.Employee.Id, 30000,
            new DateOnly(2026, 1, 1));
        first.WithdrawalDate = new DateOnly(2026, 2, 1);
        var firstPreview = await accounting.PreviewLaborAsync(first);
        await accounting.ApplyLaborAsync(first, firstPreview.PreviewToken);

        var overlapping = Labor(fixture.Employee.Id, 32000,
            new DateOnly(2026, 1, 1));
        overlapping.WithdrawalDate = new DateOnly(2026, 3, 1);
        var invalid = await accounting.PreviewLaborAsync(overlapping);
        Assert.False(invalid.CanApply);
        Assert.Contains(invalid.Errors, x => x.Contains("重疊"));
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            accounting.ApplyLaborAsync(overlapping, invalid.PreviewToken));

        var gap = Labor(fixture.Employee.Id, 32000,
            new DateOnly(2026, 3, 1));
        var gapPreview = await accounting.PreviewLaborAsync(gap);
        Assert.True(gapPreview.CanApply);
        Assert.True(gapPreview.HasCoverageGap);

        Assert.NotEmpty(await fixture.InsuranceForRole(RoleNames.Admin)
            .SearchEmployeesAsync());
        Assert.NotEmpty(await fixture.InsuranceForRole(RoleNames.HR)
            .SearchEmployeesAsync());
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            fixture.InsuranceForRole(RoleNames.Owner).SearchEmployeesAsync());
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            fixture.InsuranceForRole(RoleNames.Employee).PreviewLaborAsync(gap));
    }

    [Fact]
    public async Task Occupational_Only_Workflow_Allows_Gap_Rejects_Overlap_And_Denies_Employee()
    {
        await using var fixture = await Fixture.CreateAsync();
        var accounting = fixture.InsuranceForRole(RoleNames.Accounting);
        var first = Occupational(fixture.Employee.Id, 45800,
            new DateOnly(2026, 1, 1));
        first.WithdrawalDate = new DateOnly(2026, 2, 1);
        var firstPreview = await accounting.PreviewOccupationalAsync(first);
        await accounting.ApplyOccupationalAsync(first, firstPreview.PreviewToken);

        var overlap = Occupational(fixture.Employee.Id, 72800,
            new DateOnly(2026, 1, 1));
        var invalid = await accounting.PreviewOccupationalAsync(overlap);
        Assert.False(invalid.CanApply);
        Assert.Contains(invalid.Errors, x => x.Contains("重疊"));

        var gap = Occupational(fixture.Employee.Id, 72800,
            new DateOnly(2026, 3, 1));
        var valid = await accounting.PreviewOccupationalAsync(gap);
        Assert.True(valid.CanApply);
        Assert.True(valid.HasCoverageGap);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            fixture.InsuranceForRole(RoleNames.Employee)
                .PreviewOccupationalAsync(gap));
    }

    [Fact]
    public async Task Occupational_Only_Employee_Does_Not_Require_Fake_Labor_Setup()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.InsuranceForRole(RoleNames.Accounting);
        var occupational = Occupational(fixture.Employee.Id, 72800,
            new DateOnly(2026, 8, 1));
        var occupationalPreview = await service.PreviewOccupationalAsync(
            occupational);
        await service.ApplyOccupationalAsync(occupational,
            occupationalPreview.PreviewToken);
        var health = new PreviewHealthInsuranceChangeRequest
        {
            EmployeeId = fixture.Employee.Id,
            IsEnrolled = true,
            MonthlyHealthInsuredAmount = 60800,
            DependentCount = 0,
            CoverageFrom = new DateOnly(2026, 8, 1),
            Reason = "會計維護投保資料"
        };
        var healthPreview = await service.PreviewHealthAsync(health);
        await service.ApplyHealthAsync(health, healthPreview.PreviewToken);

        var item = Assert.Single(await service.SearchEmployeesAsync());

        Assert.Equal(InsuranceEnrollmentDisplayStatus.NotConfigured,
            item.LaborStatus);
        Assert.Equal(InsuranceEnrollmentDisplayStatus.Enrolled,
            item.OccupationalStatus);
        Assert.False(item.NeedsSetup);
        Assert.Empty(fixture.Db.EmployeeLaborInsuranceEnrollments);
    }

    [Fact]
    public async Task Accidental_Labor_Cleanup_Is_Soft_And_Rejects_Duplicate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.InsuranceForRole(RoleNames.Admin);
        var request = Labor(fixture.Employee.Id, 45800,
            new DateOnly(2026, 8, 31));
        var preview = await service.PreviewLaborAsync(request);
        var result = await service.ApplyLaborAsync(request, preview.PreviewToken);

        await service.DeactivateLaborEnrollmentAsync(result.EnrollmentId,
            "Remove incorrectly created Labor enrollment before separating Occupational Accident Insurance");

        var row = await fixture.Db.EmployeeLaborInsuranceEnrollments
            .SingleAsync(x => x.Id == result.EnrollmentId);
        Assert.False(row.IsActive);
        Assert.Contains(fixture.Db.AuditLogs, x => x.Action ==
            "EmployeeLaborInsuranceEnrollmentDeactivated");
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            service.DeactivateLaborEnrollmentAsync(result.EnrollmentId,
                "duplicate cleanup"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Accidental_Labor_Cleanup_Rejects_Referenced_Or_MissingSetup_Calculation(
        bool enrollBeforeCalculation)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        var service = fixture.InsuranceForRole(RoleNames.Admin);
        var request = Labor(fixture.Employee.Id, 45800, new DateOnly(2026, 9, 1));
        Guid enrollmentId = default;
        if (enrollBeforeCalculation) enrollmentId = await Enroll();
        var period = await fixture.Service.CreatePeriodAsync(2026, 9);
        await fixture.Service.CreateInitialDraftAsync(period);
        if (!enrollBeforeCalculation) enrollmentId = await Enroll();
        var auditCount = await fixture.Db.AuditLogs.CountAsync();
        var snapshotCount = await fixture.Db.PayrollEmployeeSnapshots.CountAsync();

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            service.DeactivateLaborEnrollmentAsync(enrollmentId, "誤建資料檢視"));

        Assert.True((await fixture.Db.EmployeeLaborInsuranceEnrollments
            .SingleAsync(x => x.Id == enrollmentId)).IsActive);
        Assert.Equal(auditCount, await fixture.Db.AuditLogs.CountAsync());
        Assert.Equal(snapshotCount, await fixture.Db.PayrollEmployeeSnapshots.CountAsync());

        async Task<Guid> Enroll()
        {
            var preview = await service.PreviewLaborAsync(request);
            return (await service.ApplyLaborAsync(request, preview.PreviewToken)).EnrollmentId;
        }
    }

    [Fact]
    public async Task Occupational_Preview_Reads_Independent_Source_Without_Payroll_Writes()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        var service = fixture.InsuranceForRole(RoleNames.Accounting);
        var first = Occupational(fixture.Employee.Id, 72800, new DateOnly(2026, 7, 13));
        first.WithdrawalDate = new DateOnly(2026, 7, 21);
        var preview = await service.PreviewOccupationalAsync(first);
        var applied = await service.ApplyOccupationalAsync(first, preview.PreviewToken);
        var auditCount = await fixture.Db.AuditLogs.CountAsync();

        var payroll = await fixture.Service.PreviewFixedEarningsAsync(fixture.Employee.Id, 2026, 7);
        var readiness = Assert.IsType<PayrollOccupationalInsuranceDto>(payroll.OccupationalInsurance);
        Assert.Equal(applied.EnrollmentId, readiness.EnrollmentId);
        Assert.Equal(72800, readiness.MonthlyInsuredSalary);
        Assert.Equal(8, readiness.CoveredDays);
        Assert.Equal(PayrollCalculationStatus.PolicyPending, readiness.CalculationStatus);
        Assert.Equal(64, readiness.SourceFingerprint.Length);
        Assert.Equal(1, readiness.SourceFingerprintVersion);
        Assert.Empty(fixture.Db.EmployeeLaborInsuranceEnrollments);
        Assert.Empty(fixture.Db.PayrollRuns);
        Assert.Empty(fixture.Db.PayrollEmployeeSnapshots);
        Assert.Equal(auditCount, await fixture.Db.AuditLogs.CountAsync());
        var ownerPreview = await fixture.ForRole(RoleNames.Owner)
            .PreviewFixedEarningsAsync(fixture.Employee.Id, 2026, 7);
        Assert.Null(ownerPreview.OccupationalInsurance);
    }

    [Fact]
    public async Task Occupational_Supersession_Preserves_History_And_Emits_All_Audits()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.InsuranceForRole(RoleNames.Accounting);
        var first = Occupational(fixture.Employee.Id, 45800, new DateOnly(2026, 1, 1));
        first.WithdrawalDate = new DateOnly(2027, 1, 1);
        var firstPreview = await service.PreviewOccupationalAsync(first);
        var original = await service.ApplyOccupationalAsync(first, firstPreview.PreviewToken);
        var changed = Occupational(fixture.Employee.Id, 72800, new DateOnly(2026, 9, 1));
        changed.WithdrawalDate = new DateOnly(2027, 1, 1);
        var preview = await service.PreviewOccupationalAsync(changed);
        changed.MonthlyInsuredSalary = 76500;
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            service.ApplyOccupationalAsync(changed, preview.PreviewToken));
        changed.MonthlyInsuredSalary = 72800;
        await service.ApplyOccupationalAsync(changed, preview.PreviewToken);

        Assert.Equal(2, await fixture.Db.EmployeeOccupationalInsuranceEnrollments.CountAsync());
        Assert.Equal(new DateOnly(2026, 8, 31),
            (await fixture.Db.EmployeeOccupationalInsuranceEnrollments
                .SingleAsync(x => x.Id == original.EnrollmentId)).EffectiveTo);
        Assert.Equal(2, await fixture.Db.AuditLogs.CountAsync(x =>
            x.Action == "OccupationalInsuranceEnrollmentCreated"));
        Assert.Single(fixture.Db.AuditLogs.Where(x =>
            x.Action == "OccupationalInsuranceEnrollmentSuperseded"));
        Assert.Equal(2, await fixture.Db.AuditLogs.CountAsync(x =>
            x.Action == "OccupationalInsuranceEnrollmentEnded"));
        Assert.Empty(fixture.Db.EmployeeLaborInsuranceEnrollments);
        Assert.Empty(fixture.Db.EmployeeHealthInsuranceEnrollments);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            fixture.InsuranceForRole(RoleNames.Employee)
                .ApplyOccupationalAsync(changed, preview.PreviewToken));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            fixture.InsuranceForRole(RoleNames.Employee)
                .DeactivateLaborEnrollmentAsync(Guid.NewGuid(), "不得停用"));
    }

    private static PreviewLaborInsuranceChangeRequest Labor(Guid employeeId,
        decimal labor, DateOnly from) => new()
    {
        EmployeeId = employeeId,
        IsEnrolled = true,
        MonthlyLaborInsuredSalary = labor,
        CoverageFrom = from,
        Reason = "會計維護投保資料"
    };

    private static PreviewOccupationalInsuranceChangeRequest Occupational(
        Guid employeeId, decimal salary, DateOnly from) => new()
    {
        EmployeeId = employeeId,
        IsEnrolled = true,
        MonthlyInsuredSalary = salary,
        CoverageFrom = from,
        Reason = "會計維護投保資料"
    };

    private static HealthInsuranceCalculationResult Health(
        EmployeeHealthInsuranceEnrollment enrollment, int year, int month)
    {
        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = new DateOnly(year, month,
            DateTime.DaysInMonth(year, month));
        return HealthInsuranceEmployeeDeductionCalculator.Calculate(
            [enrollment], [], periodStart, periodEnd,
            enrollment.EffectiveFrom, null);
    }
}
