using HRSystem.Domain.Common;

namespace HRSystem.Domain.Overtime;

public sealed class OvertimeRecognitionHistory
{
    private OvertimeRecognitionHistory() { }

    public OvertimeRecognitionHistory(
        Guid id,
        Guid recognitionId,
        OvertimeRecognitionHistoryAction action,
        OvertimeRecognitionStatus? fromStatus,
        OvertimeRecognitionStatus toStatus,
        string actorUserId,
        OvertimeRecognitionReason? reason,
        string? note,
        int? previousRecognizedMinutes,
        int? newRecognizedMinutes,
        DateTimeOffset occurredAtUtc)
    {
        if (recognitionId == Guid.Empty || !Enum.IsDefined(action) ||
            !Enum.IsDefined(toStatus))
            throw new DomainValidationException("加班認列歷程資料不合法。");
        if (reason.HasValue && !Enum.IsDefined(reason.Value))
            throw new DomainValidationException("加班認列歷程原因不合法。");
        var validTransition = action switch
        {
            OvertimeRecognitionHistoryAction.Created => fromStatus is null &&
                toStatus is OvertimeRecognitionStatus.Pending or
                    OvertimeRecognitionStatus.NeedsReview,
            OvertimeRecognitionHistoryAction.Confirmed => fromStatus is
                OvertimeRecognitionStatus.Pending or
                OvertimeRecognitionStatus.NeedsReview &&
                toStatus == OvertimeRecognitionStatus.Confirmed,
            OvertimeRecognitionHistoryAction.Adjusted =>
                fromStatus == OvertimeRecognitionStatus.Reopened &&
                toStatus == OvertimeRecognitionStatus.Confirmed,
            OvertimeRecognitionHistoryAction.Reopened =>
                fromStatus == OvertimeRecognitionStatus.Confirmed &&
                toStatus == OvertimeRecognitionStatus.Reopened,
            _ => false
        };
        if (!validTransition)
            throw new DomainValidationException("加班認列歷程狀態轉移不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        RecognitionId = recognitionId;
        Action = action;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ActorUserId = Required(actorUserId);
        Reason = reason;
        Note = Normalize(note);
        PreviousRecognizedMinutes = previousRecognizedMinutes;
        NewRecognizedMinutes = newRecognizedMinutes;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid RecognitionId { get; private set; }
    public OvertimeRecognitionHistoryAction Action { get; private set; }
    public OvertimeRecognitionStatus? FromStatus { get; private set; }
    public OvertimeRecognitionStatus ToStatus { get; private set; }
    public string ActorUserId { get; private set; } = string.Empty;
    public OvertimeRecognitionReason? Reason { get; private set; }
    public string? Note { get; private set; }
    public int? PreviousRecognizedMinutes { get; private set; }
    public int? NewRecognizedMinutes { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public OvertimeRecognition Recognition { get; private set; } = null!;

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
