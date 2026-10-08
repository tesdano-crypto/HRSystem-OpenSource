using HRSystem.Domain.Common;

namespace HRSystem.Domain.CompTime;

// A migration snapshot, never an additional ledger credit. Membership is the exact watermark.
public sealed class CompTimeLegacyPool
{
    private CompTimeLegacyPool() { }
    public CompTimeLegacyPool(Guid employeeId, decimal granted, decimal consumed, decimal restored,
        int count, DateTimeOffset cutover, string watermark)
    {
        if (granted + restored - consumed < 0 || count < 1) throw new DomainValidationException("歷史補休基準不合法。");
        Id = employeeId; EmployeeId = employeeId; GrantedHours = granted; ConsumedHours = consumed;
        RestoredHours = restored; LedgerCount = count; CutoverAtUtc = cutover; Watermark = watermark;
    }
    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public decimal GrantedHours { get; private set; }
    public decimal ConsumedHours { get; private set; }
    public decimal RestoredHours { get; private set; }
    public int LedgerCount { get; private set; }
    public DateTimeOffset CutoverAtUtc { get; private set; }
    public string Watermark { get; private set; } = "";
    public decimal BaselineHours => GrantedHours + RestoredHours - ConsumedHours;
}

public sealed class CompTimeLegacyMember
{
    private CompTimeLegacyMember() { }
    public CompTimeLegacyMember(Guid transactionId, Guid poolId) { TransactionId = transactionId; PoolId = poolId; }
    public Guid TransactionId { get; private set; }
    public Guid PoolId { get; private set; }
}

// Only for restoring pre-cutover unallocated consumption. One row per original consume/restore.
public sealed class CompTimeLegacyReturn
{
    private CompTimeLegacyReturn() { }
    public CompTimeLegacyReturn(Guid restoreId, Guid consumeId, Guid poolId)
    { RestoreTransactionId = restoreId; ConsumeTransactionId = consumeId; PoolId = poolId; }
    public Guid RestoreTransactionId { get; private set; }
    public Guid ConsumeTransactionId { get; private set; }
    public Guid PoolId { get; private set; }
}

public sealed class CompTimeAllocation
{
    private CompTimeAllocation() { }
    public CompTimeAllocation(Guid consumeId, Guid? grantId, Guid? poolId, decimal hours)
    {
        if (consumeId == Guid.Empty || grantId.HasValue == poolId.HasValue || grantId == Guid.Empty || poolId == Guid.Empty)
            throw new DomainValidationException("補休配置必須且只能指定一種來源。");
        Id = Guid.NewGuid(); ConsumeTransactionId = consumeId; GrantTransactionId = grantId; LegacyPoolId = poolId;
        AllocatedHours = CompTimePolicy.ValidateHours(hours);
    }
    public Guid Id { get; private set; }
    public Guid ConsumeTransactionId { get; private set; }
    public Guid? GrantTransactionId { get; private set; }
    public Guid? LegacyPoolId { get; private set; }
    public decimal AllocatedHours { get; private set; }
}

public sealed class CompTimeAllocationReturn
{
    private CompTimeAllocationReturn() { }
    public CompTimeAllocationReturn(Guid allocationId, Guid restoreId)
    { AllocationId = allocationId; RestoreTransactionId = restoreId; }
    public Guid AllocationId { get; private set; }
    public Guid RestoreTransactionId { get; private set; }
}
