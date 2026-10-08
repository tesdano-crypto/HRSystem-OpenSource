using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Overtime;

public sealed class OvertimeRecognition
{
    public static readonly TimeSpan MaximumRecognitionDuration = TimeSpan.FromHours(24);

    private OvertimeRecognition() { }

    public OvertimeRecognition(
        Guid id,
        Guid overtimeRequestId,
        Guid employeeId,
        DateOnly workDate,
        DateTime approvedStartAt,
        DateTime approvedEndAt,
        DateTime? observedClockOutAt,
        DateTime? suggestedStartAt,
        DateTime? suggestedEndAt,
        int? suggestedMinutes,
        OvertimeRecognitionStatus initialStatus,
        byte[] sourceFingerprint,
        DateTimeOffset nowUtc)
    {
        if (overtimeRequestId == Guid.Empty || employeeId == Guid.Empty)
            throw new DomainValidationException("加班認列來源不合法。");
        if (initialStatus is not (OvertimeRecognitionStatus.Pending or
            OvertimeRecognitionStatus.NeedsReview))
            throw new DomainValidationException("加班認列初始狀態不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        OvertimeRequestId = overtimeRequestId;
        EmployeeId = employeeId;
        WorkDate = workDate;
        ApprovedStartAt = Local(approvedStartAt, "核准開始時間");
        ApprovedEndAt = Local(approvedEndAt, "核准結束時間");
        if (ApprovedEndAt <= ApprovedStartAt)
            throw new DomainValidationException("核准加班時段不合法。");
        ApplySource(observedClockOutAt, suggestedStartAt, suggestedEndAt,
            suggestedMinutes, sourceFingerprint);
        Status = initialStatus;
        CreatedAtUtc = nowUtc.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid OvertimeRequestId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public DateTime ApprovedStartAt { get; private set; }
    public DateTime ApprovedEndAt { get; private set; }
    public DateTime? ObservedClockOutAt { get; private set; }
    public DateTime? SuggestedStartAt { get; private set; }
    public DateTime? SuggestedEndAt { get; private set; }
    public int? SuggestedMinutes { get; private set; }
    public DateTime? RecognizedStartAt { get; private set; }
    public DateTime? RecognizedEndAt { get; private set; }
    public int? RecognizedMinutes { get; private set; }
    public OvertimeRecognitionStatus Status { get; private set; }
    public OvertimeRecognitionReason? Reason { get; private set; }
    public string? Note { get; private set; }
    public byte[] SourceFingerprint { get; private set; } = [];
    public string? RecognizedByUserId { get; private set; }
    public DateTimeOffset? RecognizedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public OvertimeRequest OvertimeRequest { get; private set; } = null!;
    public Employee Employee { get; private set; } = null!;
    public ICollection<OvertimeRecognitionHistory> Histories { get; } =
        new List<OvertimeRecognitionHistory>();

    public OvertimeRecognitionHistoryAction Confirm(
        DateTime? recognizedStartAt,
        DateTime? recognizedEndAt,
        OvertimeRecognitionReason reason,
        string? note,
        byte[] sourceFingerprint,
        string actorUserId,
        DateTimeOffset nowUtc)
    {
        if (Status is not (OvertimeRecognitionStatus.Pending or
            OvertimeRecognitionStatus.NeedsReview or
            OvertimeRecognitionStatus.Reopened))
            throw new DomainValidationException("目前加班認列狀態不可確認。");
        if (!Enum.IsDefined(reason))
            throw new DomainValidationException("請選擇有效的加班認列原因。");
        var normalizedNote = NormalizeOptional(note, 1000, "認列說明");
        if (reason == OvertimeRecognitionReason.Other && normalizedNote is null)
            throw new DomainValidationException("選擇其他時必須填寫認列說明。");

        if (reason == OvertimeRecognitionReason.NoActualOvertime)
        {
            if (recognizedStartAt.HasValue || recognizedEndAt.HasValue)
                throw new DomainValidationException("未實際加班時不可填寫認列時段。");
            RecognizedStartAt = null;
            RecognizedEndAt = null;
            RecognizedMinutes = 0;
        }
        else if (reason == OvertimeRecognitionReason.NonWorkActivityExcluded &&
            !recognizedStartAt.HasValue && !recognizedEndAt.HasValue)
        {
            RecognizedStartAt = null;
            RecognizedEndAt = null;
            RecognizedMinutes = 0;
        }
        else
        {
            if (!recognizedStartAt.HasValue || !recognizedEndAt.HasValue)
                throw new DomainValidationException("請填寫完整的實際加班認列時段。");
            var start = Local(recognizedStartAt.Value, "認列開始時間");
            var end = Local(recognizedEndAt.Value, "認列結束時間");
            if (end <= start)
                throw new DomainValidationException("認列結束時間必須晚於開始時間。");
            if (end - start > MaximumRecognitionDuration)
                throw new DomainValidationException("單次實際加班認列不得超過 24 小時。");
            RecognizedStartAt = start;
            RecognizedEndAt = end;
            RecognizedMinutes = checked((int)(end - start).TotalMinutes);
        }

        var action = Status == OvertimeRecognitionStatus.Reopened
            ? OvertimeRecognitionHistoryAction.Adjusted
            : OvertimeRecognitionHistoryAction.Confirmed;
        Status = OvertimeRecognitionStatus.Confirmed;
        Reason = reason;
        Note = normalizedNote;
        SourceFingerprint = Fingerprint(sourceFingerprint);
        RecognizedByUserId = RequiredActor(actorUserId);
        RecognizedAtUtc = nowUtc.ToUniversalTime();
        UpdatedAtUtc = RecognizedAtUtc.Value;
        return action;
    }

    public void Reopen(
        DateTime? observedClockOutAt,
        DateTime? suggestedStartAt,
        DateTime? suggestedEndAt,
        int? suggestedMinutes,
        byte[] sourceFingerprint,
        string actorUserId,
        DateTimeOffset nowUtc)
    {
        if (Status != OvertimeRecognitionStatus.Confirmed)
            throw new DomainValidationException("只有已確認的加班認列可以重新開啟。");
        ApplySource(observedClockOutAt, suggestedStartAt, suggestedEndAt,
            suggestedMinutes, sourceFingerprint);
        Status = OvertimeRecognitionStatus.Reopened;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
        _ = RequiredActor(actorUserId);
    }

    private void ApplySource(
        DateTime? observedClockOutAt,
        DateTime? suggestedStartAt,
        DateTime? suggestedEndAt,
        int? suggestedMinutes,
        byte[] sourceFingerprint)
    {
        ObservedClockOutAt = observedClockOutAt.HasValue
            ? Local(observedClockOutAt.Value, "實際下班時間") : null;
        SuggestedStartAt = suggestedStartAt.HasValue
            ? Local(suggestedStartAt.Value, "建議開始時間") : null;
        SuggestedEndAt = suggestedEndAt.HasValue
            ? Local(suggestedEndAt.Value, "建議結束時間") : null;
        if (suggestedMinutes is < 0)
            throw new DomainValidationException("建議認列分鐘不可為負數。");
        if ((SuggestedStartAt.HasValue || SuggestedEndAt.HasValue) &&
            (!SuggestedStartAt.HasValue || !SuggestedEndAt.HasValue))
            throw new DomainValidationException("建議認列時段不完整。");
        if (SuggestedStartAt.HasValue && SuggestedEndAt < SuggestedStartAt)
            throw new DomainValidationException("建議認列時段不合法。");
        SuggestedMinutes = suggestedMinutes;
        SourceFingerprint = Fingerprint(sourceFingerprint);
    }

    private static DateTime Local(DateTime value, string field)
    {
        if (value.Kind != DateTimeKind.Unspecified)
            throw new DomainValidationException($"{field}必須使用台北本地時間。");
        if (value.Second != 0 || value.Millisecond != 0)
            throw new DomainValidationException($"{field}必須精確到整分鐘。");
        return value;
    }

    private static byte[] Fingerprint(byte[]? value)
    {
        if (value is null || value.Length != 32)
            throw new DomainValidationException("加班認列來源指紋不合法。");
        return value.ToArray();
    }

    private static string RequiredActor(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 450)
            throw new DomainValidationException("操作人資料不合法。");
        return text;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string field)
    {
        var text = value?.Trim();
        if (text?.Length > maxLength)
            throw new DomainValidationException($"{field}不可超過 {maxLength} 個字元。");
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
