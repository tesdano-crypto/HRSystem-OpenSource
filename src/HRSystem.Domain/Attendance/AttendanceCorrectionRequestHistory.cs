using HRSystem.Domain.Common;

namespace HRSystem.Domain.Attendance;

public sealed class AttendanceCorrectionRequestHistory
{
    private AttendanceCorrectionRequestHistory() { }

    public AttendanceCorrectionRequestHistory(
        Guid id,
        Guid requestId,
        AttendanceCorrectionRequestHistoryAction action,
        AttendanceCorrectionRequestStatus? fromStatus,
        AttendanceCorrectionRequestStatus toStatus,
        string actorUserId,
        Guid? actorEmployeeId,
        string? note,
        DateTimeOffset occurredAtUtc)
    {
        if (requestId == Guid.Empty || !Enum.IsDefined(action) ||
            !Enum.IsDefined(toStatus))
            throw new DomainValidationException("出勤更正申請歷程資料不合法。");
        ValidateTransition(action, fromStatus, toStatus);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        RequestId = requestId;
        Action = action;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ActorUserId = Required(actorUserId);
        ActorEmployeeId = actorEmployeeId;
        Note = Normalize(note);
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid RequestId { get; private set; }
    public AttendanceCorrectionRequestHistoryAction Action { get; private set; }
    public AttendanceCorrectionRequestStatus? FromStatus { get; private set; }
    public AttendanceCorrectionRequestStatus ToStatus { get; private set; }
    public string ActorUserId { get; private set; } = string.Empty;
    public Guid? ActorEmployeeId { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public AttendanceCorrectionRequest Request { get; private set; } = null!;

    private static void ValidateTransition(
        AttendanceCorrectionRequestHistoryAction action,
        AttendanceCorrectionRequestStatus? from,
        AttendanceCorrectionRequestStatus to)
    {
        var valid = action switch
        {
            AttendanceCorrectionRequestHistoryAction.Created =>
                from is null && to == AttendanceCorrectionRequestStatus.Draft,
            AttendanceCorrectionRequestHistoryAction.Updated =>
                from == AttendanceCorrectionRequestStatus.Draft &&
                to == AttendanceCorrectionRequestStatus.Draft,
            AttendanceCorrectionRequestHistoryAction.Submitted =>
                from == AttendanceCorrectionRequestStatus.Draft &&
                to == AttendanceCorrectionRequestStatus.Submitted,
            AttendanceCorrectionRequestHistoryAction.Approved =>
                from == AttendanceCorrectionRequestStatus.Submitted &&
                to == AttendanceCorrectionRequestStatus.Approved,
            AttendanceCorrectionRequestHistoryAction.Rejected =>
                from == AttendanceCorrectionRequestStatus.Submitted &&
                to == AttendanceCorrectionRequestStatus.Rejected,
            AttendanceCorrectionRequestHistoryAction.Withdrawn =>
                from == AttendanceCorrectionRequestStatus.Submitted &&
                to == AttendanceCorrectionRequestStatus.Withdrawn,
            AttendanceCorrectionRequestHistoryAction.AdjustmentApplied =>
                from == AttendanceCorrectionRequestStatus.Approved &&
                to == AttendanceCorrectionRequestStatus.Approved,
            _ => false
        };
        if (!valid)
            throw new DomainValidationException("出勤更正申請歷程狀態轉移不合法。");
    }

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
