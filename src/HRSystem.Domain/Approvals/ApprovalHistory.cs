namespace HRSystem.Domain.Approvals;

public enum ApprovalHistoryAction : byte
{
    Submitted = 1,
    NotificationSent = 2,
    NotificationFailed = 3,
    Viewed = 4,
    Approved = 5,
    Returned = 6,
    Cancelled = 7,
    Superseded = 8
}

public sealed class ApprovalHistory
{
    private ApprovalHistory() { }

    public ApprovalHistory(Guid id, Guid approvalId, ApprovalHistoryAction action,
        string? actorUserId, DateTimeOffset actionAtUtc, ApprovalChannel channel,
        string? comment = null, string? safeMetadataJson = null)
    {
        if (approvalId == Guid.Empty || !Enum.IsDefined(action) || !Enum.IsDefined(channel))
            throw new HRSystem.Domain.Common.DomainValidationException("簽核歷程資料不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        ApprovalId = approvalId;
        Action = action;
        ActorUserId = Approval.Optional(actorUserId, 450, "歷程執行者");
        ActionAtUtc = Approval.Utc(actionAtUtc);
        Channel = channel;
        Comment = Approval.Optional(comment, 1000, "歷程說明");
        SafeMetadataJson = Approval.Optional(safeMetadataJson, 2000, "安全中繼資料");
    }

    public Guid Id { get; private set; }
    public Guid ApprovalId { get; private set; }
    public ApprovalHistoryAction Action { get; private set; }
    public string? ActorUserId { get; private set; }
    public DateTimeOffset ActionAtUtc { get; private set; }
    public ApprovalChannel Channel { get; private set; }
    public string? Comment { get; private set; }
    public string? SafeMetadataJson { get; private set; }
    public Approval Approval { get; private set; } = null!;
}
