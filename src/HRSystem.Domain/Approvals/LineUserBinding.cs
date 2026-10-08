namespace HRSystem.Domain.Approvals;

public enum LineBindingTargetType : byte
{
    User = 1
}

public sealed class LineUserBinding
{
    private LineUserBinding() { }

    public LineUserBinding(Guid id, string hrSystemUserId, string lineUserId,
        DateTimeOffset verifiedAtUtc, DateTimeOffset nowUtc,
        LineBindingTargetType targetType = LineBindingTargetType.User)
    {
        if (!Enum.IsDefined(targetType) || targetType != LineBindingTargetType.User)
            throw new HRSystem.Domain.Common.DomainValidationException(
                "LINE 綁定只允許私人使用者。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        HrSystemUserId = Approval.Required(hrSystemUserId, 450, "HRSystem 使用者");
        LineUserId = Approval.Required(lineUserId, 100, "LINE 使用者識別");
        TargetType = targetType;
        VerifiedAtUtc = Approval.Utc(verifiedAtUtc);
        IsActive = true;
        CreatedAtUtc = Approval.Utc(nowUtc);
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public string HrSystemUserId { get; private set; } = string.Empty;
    public string LineUserId { get; private set; } = string.Empty;
    public LineBindingTargetType TargetType { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset VerifiedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
            throw new HRSystem.Domain.Common.DomainValidationException(
                "LINE 綁定已解除。");
        IsActive = false;
        RevokedAtUtc = Approval.Utc(nowUtc);
        UpdatedAtUtc = RevokedAtUtc.Value;
    }
}
