using HRSystem.Domain.Common;

namespace HRSystem.Domain.AnnualLeave;

public sealed class AnnualLeaveCarryForward
{
    private AnnualLeaveCarryForward() { }

    public AnnualLeaveCarryForward(
        Guid id,
        Guid sourceEntitlementId,
        Guid targetEntitlementId,
        int minutes,
        DateOnly expiresOn,
        string authorizedByUserId,
        DateTimeOffset nowUtc)
    {
        if (sourceEntitlementId == Guid.Empty || targetEntitlementId == Guid.Empty ||
            sourceEntitlementId == targetEntitlementId)
            throw new DomainValidationException("特休遞延來源與目的額度不正確。");
        if (minutes <= 0) throw new DomainValidationException("特休遞延分鐘數必須大於零。");
        if (string.IsNullOrWhiteSpace(authorizedByUserId)) throw new DomainValidationException("特休遞延必須記錄操作人。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        SourceEntitlementId = sourceEntitlementId;
        TargetEntitlementId = targetEntitlementId;
        Minutes = minutes;
        ExpiresOn = expiresOn;
        AuthorizedByUserId = authorizedByUserId.Trim();
        Status = AnnualLeaveCarryForwardStatus.Available;
        CreatedAtUtc = nowUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid SourceEntitlementId { get; private set; }
    public Guid TargetEntitlementId { get; private set; }
    public int Minutes { get; private set; }
    public DateOnly ExpiresOn { get; private set; }
    public AnnualLeaveCarryForwardStatus Status { get; private set; }
    public string AuthorizedByUserId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public AnnualLeaveEntitlement SourceEntitlement { get; private set; } = null!;
    public AnnualLeaveEntitlement TargetEntitlement { get; private set; } = null!;
}
