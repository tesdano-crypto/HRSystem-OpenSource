using System.Security.Cryptography;
using HRSystem.Domain.Common;
using HRSystem.Domain.Payroll;

namespace HRSystem.UnitTests;

public sealed class PayrollRevisionTests
{
    [Fact]
    public void Employee_Calculation_Fingerprint_Is_Deterministic_And_Ignores_Run_Identity()
    {
        var periodId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var first = Snapshot(periodId, Guid.NewGuid(), employeeId,
            componentId, sourceId, 42000);
        var second = Snapshot(periodId, Guid.NewGuid(), employeeId,
            componentId, sourceId, 42000);

        Assert.Equal(PayrollEmployeeCalculationFingerprintV1.Calculate(first),
            PayrollEmployeeCalculationFingerprintV1.Calculate(second));
        Assert.NotEqual(
            Convert.ToHexString(PayrollEmployeeCalculationFingerprintV1.Calculate(first)),
            Convert.ToHexString(PayrollEmployeeCalculationFingerprintV1.Calculate(
                Snapshot(periodId, Guid.NewGuid(), employeeId,
                    componentId, sourceId, 42001))));
    }

    [Fact]
    public void Month_Fingerprint_Is_Deterministic_And_Changes_When_Current_Snapshot_Switches()
    {
        var periodId = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var a1 = Item(employeeA, Guid.NewGuid(), 40000);
        var b1 = Item(employeeB, Guid.NewGuid(), 45000);

        var first = PayrollMonthFingerprintV1.Calculate(periodId, [a1, b1]);
        var reordered = PayrollMonthFingerprintV1.Calculate(periodId, [b1, a1]);
        var switched = PayrollMonthFingerprintV1.Calculate(periodId,
            [a1 with { PayrollEmployeeSnapshotId = Guid.NewGuid() }, b1]);

        Assert.Equal(first, reordered);
        Assert.NotEqual(Convert.ToHexString(first), Convert.ToHexString(switched));
    }

    [Fact]
    public void Current_Pointer_Only_Accepts_Same_Period_And_Employee_Lineage()
    {
        var periodId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var current = Snapshot(periodId, Guid.NewGuid(), employeeId,
            Guid.NewGuid(), Guid.NewGuid(), 40000);
        var pointer = new PayrollPeriodEmployeeCurrentSnapshot(periodId,
            employeeId, current.Id, DateTimeOffset.UtcNow);
        var next = Snapshot(periodId, Guid.NewGuid(), employeeId,
            Guid.NewGuid(), Guid.NewGuid(), 41000);

        pointer.MarkSourceChanged(DateTimeOffset.UtcNow);
        pointer.SwitchTo(next, DateTimeOffset.UtcNow);

        Assert.Equal(next.Id, pointer.PayrollEmployeeSnapshotId);
        Assert.False(pointer.IsSourceChanged);
        Assert.Throws<DomainValidationException>(() => pointer.SwitchTo(
            Snapshot(Guid.NewGuid(), Guid.NewGuid(), employeeId,
                Guid.NewGuid(), Guid.NewGuid(), 41000), DateTimeOffset.UtcNow));
    }

    private static PayrollMonthFingerprintItem Item(Guid employeeId,
        Guid snapshotId, decimal gross) => new(employeeId, snapshotId,
        SHA256.HashData([1, 2, 3]), gross, 3000, gross - 3000,
        PayrollCalculationStatus.Resolved, false);

    private static PayrollEmployeeSnapshot Snapshot(Guid periodId, Guid runId,
        Guid employeeId, Guid componentId, Guid sourceId, decimal amount)
    {
        var snapshot = new PayrollEmployeeSnapshot(Guid.NewGuid(), periodId, runId,
            employeeId, "PAY001", "測試員工", null, "薪資部",
            new DateOnly(2020, 1, 1), null, componentId, "STANDARD",
            DateTimeOffset.UtcNow, PayrollEmployeeSetupStatus.Ready);
        snapshot.Components.Add(new PayrollEmployeeSnapshotComponent(Guid.NewGuid(),
            snapshot.Id, componentId, "BASE_SALARY", "底薪",
            PayrollComponentCategory.Earning, PayrollSnapshotSourceType.PayrollPlan,
            sourceId, amount, null, amount, PayrollCalculationStatus.Resolved,
            new DateOnly(2026, 8, 1)));
        snapshot.ApplyTotals(new PayrollTotalCalculationResult(amount, 0, amount,
            PayrollCalculationStatus.Resolved, [],
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(amount.ToString()))),
            DateTimeOffset.UtcNow);
        return snapshot;
    }
}
