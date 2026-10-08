using HRSystem.Domain.Common;

namespace HRSystem.Domain.Attendance;

public sealed class AttendanceReviewResolutionHistory
{
    private AttendanceReviewResolutionHistory() { }

    public AttendanceReviewResolutionHistory(
        Guid id,
        Guid resolutionId,
        AttendanceReviewResolutionHistoryAction action,
        AttendanceReviewResolutionStatus? fromStatus,
        AttendanceReviewResolutionStatus toStatus,
        AttendanceReviewResolutionReason? reason,
        string? note,
        string actorUserId,
        DateTimeOffset actionAtUtc,
        byte[] sourceFingerprint)
    {
        if (resolutionId == Guid.Empty || !Enum.IsDefined(action) ||
            !Enum.IsDefined(toStatus) || sourceFingerprint.Length != 32)
            throw new DomainValidationException("出勤檢核歷程資料不合法。");
        var valid = action switch
        {
            AttendanceReviewResolutionHistoryAction.Created => fromStatus is null &&
                toStatus == AttendanceReviewResolutionStatus.Resolved,
            AttendanceReviewResolutionHistoryAction.Resolved =>
                (fromStatus is null or AttendanceReviewResolutionStatus.Reopened) &&
                toStatus == AttendanceReviewResolutionStatus.Resolved,
            AttendanceReviewResolutionHistoryAction.Reopened =>
                fromStatus == AttendanceReviewResolutionStatus.Resolved &&
                toStatus == AttendanceReviewResolutionStatus.Reopened,
            _ => false
        };
        if (!valid) throw new DomainValidationException("出勤檢核歷程狀態轉移不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        AttendanceReviewResolutionId = resolutionId;
        Action = action;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Reason = reason;
        Note = Normalize(note);
        ActorUserId = Required(actorUserId);
        ActionAtUtc = actionAtUtc.ToUniversalTime();
        SourceFingerprint = sourceFingerprint.ToArray();
    }

    public Guid Id { get; private set; }
    public Guid AttendanceReviewResolutionId { get; private set; }
    public AttendanceReviewResolutionHistoryAction Action { get; private set; }
    public AttendanceReviewResolutionStatus? FromStatus { get; private set; }
    public AttendanceReviewResolutionStatus ToStatus { get; private set; }
    public AttendanceReviewResolutionReason? Reason { get; private set; }
    public string? Note { get; private set; }
    public string ActorUserId { get; private set; } = string.Empty;
    public DateTimeOffset ActionAtUtc { get; private set; }
    public byte[] SourceFingerprint { get; private set; } = [];
    public AttendanceReviewResolution AttendanceReviewResolution { get; private set; } = null!;

    private static string Required(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 450)
            throw new DomainValidationException("歷程操作人資料不合法。");
        return text;
    }

    private static string? Normalize(string? value)
    {
        var text = value?.Trim();
        if (text?.Length > 1000)
            throw new DomainValidationException("歷程說明不可超過 1000 個字元。");
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
