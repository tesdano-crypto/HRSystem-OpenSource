using HRSystem.Domain.Common;

namespace HRSystem.Domain.Approvals;

public enum ApprovalType : byte
{
    Payroll = 1
}

public enum ApprovalStatus : byte
{
    Pending = 1,
    Approved = 2,
    Returned = 3,
    Cancelled = 4,
    Superseded = 5
}

public enum ApprovalNotificationStatus : byte
{
    Pending = 1,
    Sent = 2,
    Failed = 3
}

public enum ApprovalChannel : byte
{
    System = 1,
    Web = 2,
    Line = 3
}

public sealed class Approval
{
    private Approval() { }

    public Approval(
        Guid id,
        ApprovalType approvalType,
        string sourceEntityType,
        string sourceEntityId,
        short sourceFingerprintVersion,
        byte[] sourceFingerprint,
        string title,
        string summaryJson,
        string requestedByUserId,
        string approverUserId,
        DateTimeOffset nowUtc)
    {
        if (!Enum.IsDefined(approvalType))
            throw new DomainValidationException("簽核類型不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        ApprovalType = approvalType;
        SourceEntityType = Required(sourceEntityType, 100, "來源類型");
        SourceEntityId = Required(sourceEntityId, 128, "來源識別");
        if (sourceFingerprintVersion <= 0)
            throw new DomainValidationException("來源指紋版本不合法。");
        SourceFingerprintVersion = sourceFingerprintVersion;
        SourceFingerprint = Fingerprint(sourceFingerprint);
        Title = Required(title, 200, "簽核標題");
        SummaryJson = Required(summaryJson, 4000, "簽核摘要");
        RequestedByUserId = Required(requestedByUserId, 450, "送簽人");
        ApproverUserId = Required(approverUserId, 450, "簽核人");
        RequestedAtUtc = Utc(nowUtc);
        Status = ApprovalStatus.Pending;
        NotificationStatus = ApprovalNotificationStatus.Pending;
        CreatedAtUtc = RequestedAtUtc;
        UpdatedAtUtc = RequestedAtUtc;
    }

    public Guid Id { get; private set; }
    public ApprovalType ApprovalType { get; private set; }
    public string SourceEntityType { get; private set; } = string.Empty;
    public string SourceEntityId { get; private set; } = string.Empty;
    public short SourceFingerprintVersion { get; private set; }
    public byte[] SourceFingerprint { get; private set; } = [];
    public string Title { get; private set; } = string.Empty;
    public string SummaryJson { get; private set; } = "[]";
    public string RequestedByUserId { get; private set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public string ApproverUserId { get; private set; } = string.Empty;
    public ApprovalStatus Status { get; private set; }
    public DateTimeOffset? DecisionAtUtc { get; private set; }
    public string? DecisionByUserId { get; private set; }
    public ApprovalChannel? DecisionChannel { get; private set; }
    public string? DecisionReason { get; private set; }
    public ApprovalNotificationStatus NotificationStatus { get; private set; }
    public DateTimeOffset? NotificationAttemptedAtUtc { get; private set; }
    public string? NotificationErrorSummary { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public ICollection<ApprovalHistory> Histories { get; } = new List<ApprovalHistory>();
    public ICollection<ApprovalLineActionToken> LineActionTokens { get; } =
        new List<ApprovalLineActionToken>();

    public bool MatchesSource(short version, byte[] fingerprint) =>
        SourceFingerprintVersion == version &&
        SourceFingerprint.AsSpan().SequenceEqual(Fingerprint(fingerprint));

    public void MarkNotificationSent(DateTimeOffset nowUtc)
    {
        EnsurePending();
        NotificationStatus = ApprovalNotificationStatus.Sent;
        NotificationAttemptedAtUtc = Utc(nowUtc);
        NotificationErrorSummary = null;
        UpdatedAtUtc = NotificationAttemptedAtUtc.Value;
    }

    public void MarkNotificationFailed(string safeError, DateTimeOffset nowUtc)
    {
        EnsurePending();
        NotificationStatus = ApprovalNotificationStatus.Failed;
        NotificationAttemptedAtUtc = Utc(nowUtc);
        NotificationErrorSummary = Required(safeError, 500, "通知錯誤摘要");
        UpdatedAtUtc = NotificationAttemptedAtUtc.Value;
    }

    public void Approve(string actorUserId, ApprovalChannel channel, DateTimeOffset nowUtc) =>
        Decide(ApprovalStatus.Approved, actorUserId, channel, null, nowUtc);

    public void Return(string actorUserId, ApprovalChannel channel, string reason,
        DateTimeOffset nowUtc) =>
        Decide(ApprovalStatus.Returned, actorUserId, channel,
            Required(reason, 1000, "退回原因"), nowUtc);

    public void Cancel(string actorUserId, ApprovalChannel channel, string? reason,
        DateTimeOffset nowUtc) =>
        Decide(ApprovalStatus.Cancelled, actorUserId, channel,
            Optional(reason, 1000, "取消原因"), nowUtc);

    public void Supersede(string actorUserId, ApprovalChannel channel,
        DateTimeOffset nowUtc) =>
        Decide(ApprovalStatus.Superseded, actorUserId, channel,
            "來源資料已變更，請重新送出簽核。", nowUtc);

    private void Decide(ApprovalStatus status, string actorUserId,
        ApprovalChannel channel, string? reason, DateTimeOffset nowUtc)
    {
        EnsurePending();
        if (!Enum.IsDefined(channel))
            throw new DomainValidationException("簽核管道不合法。");
        Status = status;
        DecisionByUserId = Required(actorUserId, 450, "簽核執行者");
        DecisionChannel = channel;
        DecisionReason = reason;
        DecisionAtUtc = Utc(nowUtc);
        UpdatedAtUtc = DecisionAtUtc.Value;
    }

    private void EnsurePending()
    {
        if (Status != ApprovalStatus.Pending)
            throw new DomainValidationException("此簽核已完成，無法重複處理。");
    }

    internal static string Required(string? value, int maxLength, string label)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new DomainValidationException($"{label}不可空白。");
        if (normalized.Length > maxLength)
            throw new DomainValidationException($"{label}不可超過 {maxLength} 個字元。");
        return normalized;
    }

    internal static string? Optional(string? value, int maxLength, string label)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (normalized.Length > maxLength)
            throw new DomainValidationException($"{label}不可超過 {maxLength} 個字元。");
        return normalized;
    }

    internal static byte[] Fingerprint(byte[]? value)
    {
        if (value is null || value.Length != 32)
            throw new DomainValidationException("來源指紋必須為 32 bytes。");
        return value.ToArray();
    }

    internal static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();
}
