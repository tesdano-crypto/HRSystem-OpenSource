namespace HRSystem.Domain.Approvals;

public enum ApprovalLineAction : byte
{
    Approve = 1,
    Return = 2
}

public sealed class ApprovalLineActionToken
{
    private ApprovalLineActionToken() { }

    public ApprovalLineActionToken(Guid id, Guid approvalId, byte[] tokenHash,
        ApprovalLineAction action, string intendedApproverUserId,
        string lineUserId, DateTimeOffset expiresAtUtc, DateTimeOffset createdAtUtc)
    {
        if (approvalId == Guid.Empty || !Enum.IsDefined(action))
            throw new HRSystem.Domain.Common.DomainValidationException("LINE 簽核 token 資料不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        ApprovalId = approvalId;
        TokenHash = Approval.Fingerprint(tokenHash);
        Action = action;
        IntendedApproverUserId = Approval.Required(
            intendedApproverUserId, 450, "預定簽核人");
        LineUserId = Approval.Required(lineUserId, 100, "LINE 使用者識別");
        ExpiresAtUtc = Approval.Utc(expiresAtUtc);
        CreatedAtUtc = Approval.Utc(createdAtUtc);
        if (ExpiresAtUtc <= CreatedAtUtc)
            throw new HRSystem.Domain.Common.DomainValidationException("LINE 簽核 token 到期時間不合法。");
    }

    public Guid Id { get; private set; }
    public Guid ApprovalId { get; private set; }
    public byte[] TokenHash { get; private set; } = [];
    public ApprovalLineAction Action { get; private set; }
    public string IntendedApproverUserId { get; private set; } = string.Empty;
    public string LineUserId { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Approval Approval { get; private set; } = null!;

    public bool IsValid(DateTimeOffset nowUtc) =>
        ConsumedAtUtc is null && RevokedAtUtc is null && ExpiresAtUtc > Approval.Utc(nowUtc);

    public void Consume(DateTimeOffset nowUtc)
    {
        if (!IsValid(nowUtc))
            throw new HRSystem.Domain.Common.DomainValidationException("LINE 簽核連結已失效或已使用。");
        ConsumedAtUtc = Approval.Utc(nowUtc);
    }

    public void Revoke(DateTimeOffset nowUtc)
    {
        if (ConsumedAtUtc is null && RevokedAtUtc is null)
            RevokedAtUtc = Approval.Utc(nowUtc);
    }
}
