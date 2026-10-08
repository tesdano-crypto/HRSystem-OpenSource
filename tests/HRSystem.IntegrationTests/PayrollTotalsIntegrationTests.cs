using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.IntegrationTests;

public sealed partial class PayrollFoundationIntegrationTests
{
    [Fact]
    public async Task Draft_Persists_Known_Totals_Blockers_And_Fingerprint()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();

        var runId = await fixture.Service.CreateDraftAsync(
            await fixture.Service.CreatePeriodAsync(2026, 8));
        var employee = Assert.Single((await fixture.Service.GetRunAsync(runId)).Employees);
        var stored = await fixture.Db.PayrollEmployeeSnapshots.AsNoTracking()
            .SingleAsync(x => x.Id == employee.Id);

        Assert.True(employee.GrossPay > 0);
        Assert.True(employee.TotalDeductions >= 0);
        Assert.Null(employee.NetPay);
        Assert.NotEqual(PayrollCalculationStatus.Resolved,
            employee.TotalCalculationStatus);
        Assert.NotEmpty(employee.TotalBlockingEvidence!);
        Assert.Equal(employee.TotalBlockingEvidence!.Count,
            stored.BlockingComponentCount);
        Assert.Equal(PayrollTotalFingerprintV1.Version,
            stored.TotalSourceFingerprintVersion);
        Assert.Equal(32, stored.TotalSourceFingerprint!.Length);
        Assert.NotNull(stored.TotalsCalculatedAtUtc);
        Assert.Equal(stored.BlockingComponentCount,
            await fixture.Db.PayrollTotalBlockingEvidence.CountAsync(
                x => x.PayrollEmployeeSnapshotId == stored.Id));
    }

    [Fact]
    public async Task Total_Blocking_Evidence_Is_Append_Only()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssignStandardAsync();
        await fixture.Service.CreateDraftAsync(
            await fixture.Service.CreatePeriodAsync(2026, 8));
        var evidence = await fixture.Db.PayrollTotalBlockingEvidence.FirstAsync();

        fixture.Db.Entry(evidence).Property(x => x.ComponentStatus).CurrentValue =
            PayrollCalculationStatus.Resolved;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Historical_PreP6_Snapshot_Remains_NotCalculated_Not_SourceChanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var periodId = await fixture.Service.CreatePeriodAsync(2026, 8);
        var run = new PayrollRun(Guid.NewGuid(), periodId, 1,
            PayrollRunTrigger.InitialBatch, "payroll-admin",
            new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero));
        run.EmployeeSnapshots.Add(new PayrollEmployeeSnapshot(Guid.NewGuid(), periodId, run.Id,
            fixture.Employee.Id, fixture.Employee.EmployeeNumber,
            fixture.Employee.ChineseName, fixture.Department.Id, fixture.Department.Name,
            fixture.Employee.HireDate, fixture.Employee.TerminationDate,
            null, null, new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero),
            PayrollEmployeeSetupStatus.Ready));
        fixture.Db.PayrollRuns.Add(run);
        await fixture.Db.SaveChangesAsync();

        var dto = Assert.Single((await fixture.Service.GetRunAsync(run.Id)).Employees);

        Assert.Equal(PayrollCalculationStatus.NotCalculated,
            dto.TotalCalculationStatus);
        Assert.True(dto.IsTotalSourceCurrent);
        Assert.Null(dto.NetPay);
        Assert.Empty(dto.TotalBlockingEvidence!);
    }
}
