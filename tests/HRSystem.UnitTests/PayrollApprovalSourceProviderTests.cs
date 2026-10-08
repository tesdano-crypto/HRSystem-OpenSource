using System.Security.Cryptography;
using HRSystem.Application.Approvals;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.Payroll;

namespace HRSystem.UnitTests;

public sealed class PayrollApprovalSourceProviderTests
{
    [Fact]
    public async Task Month_Snapshot_Uses_Current_Set_And_Changes_After_Pointer_Switch()
    {
        await using var db = TestDb.Create();
        var seed = Seed(db, resolved: true, gross: 40000, deductions: 3000);
        AddNonPayMonthEmployee(db, seed.Employee.DepartmentId);
        await db.SaveChangesAsync();
        var provider = new PayrollApprovalSourceProvider(db);

        var first = await provider.GetSnapshotAsync(seed.Period.Id);
        var run2 = new PayrollRun(Guid.NewGuid(), seed.Period.Id, 2,
            PayrollRunTrigger.EmployeeRecalculation, "accounting",
            new DateTimeOffset(2026, 8, 28, 2, 0, 0, TimeSpan.Zero));
        var snapshot2 = Snapshot(seed.Period.Id, run2.Id, seed.Employee,
            42000, 3000, resolved: true);
        run2.EmployeeSnapshots.Add(snapshot2);
        db.PayrollRuns.Add(run2);
        seed.Pointer.SwitchTo(snapshot2,
            new DateTimeOffset(2026, 8, 28, 2, 1, 0, TimeSpan.Zero));
        await db.SaveChangesAsync();

        var second = await provider.GetSnapshotAsync(seed.Period.Id);
        var repeated = await provider.GetSnapshotAsync(seed.Period.Id);

        Assert.Equal(PayrollApprovalSourceProvider.FingerprintVersion,
            first.FingerprintVersion);
        Assert.False(first.Fingerprint.SequenceEqual(second.Fingerprint));
        Assert.Equal(second.Fingerprint, repeated.Fingerprint);
        Assert.Equal(nameof(PayrollPeriod), second.SourceEntityType);
        Assert.Equal(seed.Period.Id.ToString(), second.SourceEntityId);
        Assert.Equal("1", Assert.Single(second.Summary,
            x => x.Label == "員工人數").Value);
        var serialized = System.Text.Json.JsonSerializer.Serialize(second.Summary);
        Assert.DoesNotContain(seed.Employee.EmployeeNumber, serialized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(seed.Employee.ChineseName, serialized,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceChanged_Or_Unresolved_Current_Snapshot_Cannot_Be_Submitted()
    {
        await using var unresolvedDb = TestDb.Create();
        var unresolved = Seed(unresolvedDb, resolved: false, gross: 0, deductions: 0);
        await unresolvedDb.SaveChangesAsync();
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            new PayrollApprovalSourceProvider(unresolvedDb)
                .GetSnapshotAsync(unresolved.Period.Id));

        await using var staleDb = TestDb.Create();
        var stale = Seed(staleDb, resolved: true, gross: 40000, deductions: 3000);
        stale.Pointer.MarkSourceChanged(DateTimeOffset.UtcNow);
        await staleDb.SaveChangesAsync();
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            new PayrollApprovalSourceProvider(staleDb)
                .GetSnapshotAsync(stale.Period.Id));
    }

    private static (PayrollPeriod Period, Employee Employee,
        PayrollPeriodEmployeeCurrentSnapshot Pointer) Seed(
        Infrastructure.Persistence.HRSystemDbContext db, bool resolved,
        decimal gross, decimal deductions)
    {
        var now = new DateTimeOffset(2026, 8, 28, 1, 0, 0, TimeSpan.Zero);
        var department = new Department(Guid.NewGuid(), "PAY", "薪資部", now);
        var employee = new Employee(Guid.NewGuid(), "EMP0001", "測試員工",
            department.Id, new DateOnly(2020, 1, 1), now);
        var period = new PayrollPeriod(Guid.NewGuid(), 2026, 8);
        var run = new PayrollRun(Guid.NewGuid(), period.Id, 1,
            PayrollRunTrigger.InitialBatch, "accounting", now);
        var snapshot = Snapshot(period.Id, run.Id, employee, gross, deductions,
            resolved);
        run.EmployeeSnapshots.Add(snapshot);
        period.Runs.Add(run);
        var pointer = new PayrollPeriodEmployeeCurrentSnapshot(period.Id,
            employee.Id, snapshot.Id, now);
        period.CurrentSnapshots.Add(pointer);
        db.Departments.Add(department);
        db.Employees.Add(employee);
        db.PayrollPeriods.Add(period);
        return (period, employee, pointer);
    }

    private static PayrollEmployeeSnapshot Snapshot(Guid periodId, Guid runId,
        Employee employee, decimal gross, decimal deductions, bool resolved)
    {
        var snapshot = new PayrollEmployeeSnapshot(Guid.NewGuid(), periodId, runId,
            employee.Id, employee.EmployeeNumber, employee.ChineseName, null,
            "薪資部", employee.HireDate, null, null, null,
            new DateTimeOffset(2026, 8, 28, 1, 0, 0, TimeSpan.Zero),
            PayrollEmployeeSetupStatus.Ready);
        if (resolved)
            snapshot.ApplyTotals(new PayrollTotalCalculationResult(gross, deductions,
                gross - deductions, PayrollCalculationStatus.Resolved, [],
                SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{gross}:{deductions}"))),
                new DateTimeOffset(2026, 8, 28, 1, 1, 0, TimeSpan.Zero));
        return snapshot;
    }

    private static void AddNonPayMonthEmployee(
        Infrastructure.Persistence.HRSystemDbContext db, Guid departmentId)
    {
        var now = new DateTimeOffset(2026, 8, 28, 1, 0, 0, TimeSpan.Zero);
        var employee = new Employee(Guid.NewGuid(), "EMP0016", "週期員工",
            departmentId, new DateOnly(2020, 1, 1), now);
        db.Employees.Add(employee);
        db.EmployeePayrollPayCycles.Add(new EmployeePayrollPayCycle(Guid.NewGuid(),
            employee.Id, PayrollPayCycleType.SemiannualFixed,
            new DateOnly(2025, 12, 1), anchorPayMonth: new DateOnly(2025, 12, 1),
            fixedPaymentAmount: 4000, reason: "test"));
    }
}
