using HRSystem.Domain.Common;

namespace HRSystem.Domain.Approvals;

public enum LinePairingPurpose : byte
{
    PrivateUserBinding = 1
}

public sealed class LinePairingRequest
{
    private LinePairingRequest() { }

    public LinePairingRequest(Guid id, string hrSystemUserId, byte[] tokenHash,
        DateTimeOffset expiresAtUtc, string createdByUserId, DateTimeOffset createdAtUtc,
        bool replacesExistingBinding = false, string? replacementReason = null,
        LinePairingPurpose purpose = LinePairingPurpose.PrivateUserBinding)
    {
        if (purpose != LinePairingPurpose.PrivateUserBinding)
            throw new DomainValidationException("LINE 配對用途不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        HrSystemUserId = Approval.Required(hrSystemUserId, 450, "HRSystem 使用者");
        TokenHash = Approval.Fingerprint(tokenHash);
        Purpose = purpose;
        CreatedAtUtc = Approval.Utc(createdAtUtc);
        ExpiresAtUtc = Approval.Utc(expiresAtUtc);
        CreatedByUserId = Approval.Required(createdByUserId, 450, "建立者");
        if (ExpiresAtUtc <= CreatedAtUtc)
            throw new DomainValidationException("LINE 配對碼到期時間不合法。");
        ReplacesExistingBinding = replacesExistingBinding;
        ReplacementReason = replacesExistingBinding
            ? Approval.Required(replacementReason, 1000, "更換原因")
            : null;
    }

    public Guid Id { get; private set; }
    public string HrSystemUserId { get; private set; } = string.Empty;
    public byte[] TokenHash { get; private set; } = [];
    public LinePairingPurpose Purpose { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public bool ReplacesExistingBinding { get; private set; }
    public string? ReplacementReason { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsValid(DateTimeOffset nowUtc) =>
        Purpose == LinePairingPurpose.PrivateUserBinding &&
        ConsumedAtUtc is null && RevokedAtUtc is null &&
        ExpiresAtUtc > Approval.Utc(nowUtc);

    public void Consume(DateTimeOffset nowUtc)
    {
        if (!IsValid(nowUtc))
            throw new DomainValidationException("LINE 配對碼已失效或已使用。");
        ConsumedAtUtc = Approval.Utc(nowUtc);
    }

    public void Revoke(DateTimeOffset nowUtc)
    {
        if (ConsumedAtUtc is null && RevokedAtUtc is null)
            RevokedAtUtc = Approval.Utc(nowUtc);
    }
}
