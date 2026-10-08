using HRSystem.Domain.Common;
using HRSystem.Domain.LeaveRequests;

namespace HRSystem.Domain.AnnualLeave;

public sealed class AnnualLeaveAllocation
{
    private AnnualLeaveAllocation() { }

    public AnnualLeaveAllocation(
        Guid id,
        Guid leaveRequestId,
        Guid entitlementId,
        int allocatedMinutes,
        DateTimeOffset nowUtc)
    {
        if (leaveRequestId == Guid.Empty || entitlementId == Guid.Empty)
            throw new DomainValidationException("特休配置關聯不可為空。");
        if (allocatedMinutes <= 0) throw new DomainValidationException("配置分鐘數必須大於零。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        LeaveRequestId = leaveRequestId;
        AnnualLeaveEntitlementId = entitlementId;
        AllocatedMinutes = allocatedMinutes;
        Status = AnnualLeaveAllocationStatus.Reserved;
        CreatedAtUtc = nowUtc.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid LeaveRequestId { get; private set; }
    public Guid AnnualLeaveEntitlementId { get; private set; }
    public int AllocatedMinutes { get; private set; }
    public AnnualLeaveAllocationStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public LeaveRequest LeaveRequest { get; private set; } = null!;
    public AnnualLeaveEntitlement Entitlement { get; private set; } = null!;

    public void Consume(DateTimeOffset nowUtc)
    {
        EnsureStatus(AnnualLeaveAllocationStatus.Reserved);
        Status = AnnualLeaveAllocationStatus.Consumed;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    public void Release(DateTimeOffset nowUtc)
    {
        EnsureStatus(AnnualLeaveAllocationStatus.Reserved);
        Status = AnnualLeaveAllocationStatus.Released;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    public void Restore(DateTimeOffset nowUtc)
    {
        EnsureStatus(AnnualLeaveAllocationStatus.Consumed);
        Status = AnnualLeaveAllocationStatus.Restored;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    private void EnsureStatus(AnnualLeaveAllocationStatus expected)
    {
        if (Status != expected) throw new DomainValidationException("特休配置狀態不允許此操作。");
    }
}
