using HRSystem.Domain.Common;

namespace HRSystem.Domain.Overtime;

public sealed class OvertimeRequestHistory
{
    private OvertimeRequestHistory() { }

    public OvertimeRequestHistory(
        Guid id,
        Guid overtimeRequestId,
        OvertimeRequestHistoryAction action,
        OvertimeRequestStatus? fromStatus,
        OvertimeRequestStatus toStatus,
        string actorUserId,
        Guid? actorEmployeeId,
        string? note,
        DateTimeOffset occurredAtUtc)
    {
        if (overtimeRequestId == Guid.Empty || !Enum.IsDefined(action) ||
            !Enum.IsDefined(toStatus))
            throw new DomainValidationException("加班申請歷程資料不合法。");
        ValidateTransition(action, fromStatus, toStatus);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        OvertimeRequestId = overtimeRequestId;
        Action = action;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ActorUserId = Required(actorUserId);
        ActorEmployeeId = actorEmployeeId;
        Note = Normalize(note);
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid OvertimeRequestId { get; private set; }
    public OvertimeRequestHistoryAction Action { get; private set; }
    public OvertimeRequestStatus? FromStatus { get; private set; }
    public OvertimeRequestStatus ToStatus { get; private set; }
    public string ActorUserId { get; private set; } = string.Empty;
    public Guid? ActorEmployeeId { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public OvertimeRequest OvertimeRequest { get; private set; } = null!;

    private static void ValidateTransition(
        OvertimeRequestHistoryAction action,
        OvertimeRequestStatus? from,
        OvertimeRequestStatus to)
    {
        var valid = action switch
        {
            OvertimeRequestHistoryAction.Created => from is null && to == OvertimeRequestStatus.Draft,
            OvertimeRequestHistoryAction.Updated => from == OvertimeRequestStatus.Draft && to == OvertimeRequestStatus.Draft,
            OvertimeRequestHistoryAction.Submitted => from == OvertimeRequestStatus.Draft && to == OvertimeRequestStatus.Submitted,
            OvertimeRequestHistoryAction.Approved => from == OvertimeRequestStatus.Submitted && to == OvertimeRequestStatus.Approved,
            OvertimeRequestHistoryAction.Rejected => from == OvertimeRequestStatus.Submitted && to == OvertimeRequestStatus.Rejected,
            OvertimeRequestHistoryAction.Withdrawn => from == OvertimeRequestStatus.Submitted && to == OvertimeRequestStatus.Withdrawn,
            _ => false
        };
        if (!valid) throw new DomainValidationException("加班申請歷程狀態轉移不合法。");
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
