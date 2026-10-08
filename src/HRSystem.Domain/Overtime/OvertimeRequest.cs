using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Overtime;

public sealed class OvertimeRequest
{
    public static readonly TimeSpan MaximumRequestDuration = TimeSpan.FromHours(12);

    private OvertimeRequest() { }

    public OvertimeRequest(
        Guid id,
        Guid employeeId,
        DateTime plannedStartAt,
        DateTime plannedEndAt,
        string reason,
        string actorUserId,
        DateTimeOffset nowUtc)
    {
        if (employeeId == Guid.Empty)
            throw new DomainValidationException("無法識別申請員工。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        EmployeeId = employeeId;
        Status = OvertimeRequestStatus.Draft;
        CreatedAtUtc = nowUtc.ToUniversalTime();
        CreatedByUserId = RequiredActor(actorUserId);
        SetDetails(plannedStartAt, plannedEndAt, reason, nowUtc);
    }

    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly OvertimeDate { get; private set; }
    public DateTime PlannedStartAt { get; private set; }
    public DateTime PlannedEndAt { get; private set; }
    public int RequestedMinutes { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public OvertimeRequestStatus Status { get; private set; }
    public OvertimeReviewReason? ReviewReason { get; private set; }
    public string? ReviewNote { get; private set; }
    public string? ReviewedByUserId { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public DateTimeOffset? RejectedAtUtc { get; private set; }
    public DateTimeOffset? WithdrawnAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public string? UpdatedByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Employee Employee { get; private set; } = null!;
    public ICollection<OvertimeRequestHistory> Histories { get; } =
        new List<OvertimeRequestHistory>();
    public OvertimeRecognition? Recognition { get; private set; }

    public void UpdateDraft(
        DateTime plannedStartAt,
        DateTime plannedEndAt,
        string reason,
        string actorUserId,
        DateTimeOffset nowUtc)
    {
        EnsureStatus(OvertimeRequestStatus.Draft, "只有草稿可以修改。");
        SetDetails(plannedStartAt, plannedEndAt, reason, nowUtc);
        UpdatedByUserId = RequiredActor(actorUserId);
    }

    public void Submit(string actorUserId, DateTimeOffset nowUtc)
    {
        EnsureStatus(OvertimeRequestStatus.Draft, "只有草稿可以送出。");
        ValidateDetails(PlannedStartAt, PlannedEndAt, Reason);
        Status = OvertimeRequestStatus.Submitted;
        SubmittedAtUtc = nowUtc.ToUniversalTime();
        Touch(actorUserId, nowUtc);
    }

    public void Approve(
        OvertimeReviewReason reason,
        string? note,
        string actorUserId,
        DateTimeOffset nowUtc)
    {
        EnsureStatus(OvertimeRequestStatus.Submitted, "只有待審核申請可以核准。");
        if (reason != OvertimeReviewReason.ApprovedAsRequested)
            throw new DomainValidationException("核准必須使用依申請內容核准原因。");
        SetReview(reason, note, actorUserId);
        Status = OvertimeRequestStatus.Approved;
        ApprovedAtUtc = nowUtc.ToUniversalTime();
        Touch(actorUserId, nowUtc);
    }

    public void Reject(
        OvertimeReviewReason reason,
        string? note,
        string actorUserId,
        DateTimeOffset nowUtc)
    {
        EnsureStatus(OvertimeRequestStatus.Submitted, "只有待審核申請可以駁回。");
        if (reason == OvertimeReviewReason.ApprovedAsRequested || !Enum.IsDefined(reason))
            throw new DomainValidationException("請選擇有效的駁回原因。");
        SetReview(reason, note, actorUserId);
        if (reason == OvertimeReviewReason.Other && ReviewNote is null)
            throw new DomainValidationException("選擇其他時必須填寫審核說明。");
        Status = OvertimeRequestStatus.Rejected;
        RejectedAtUtc = nowUtc.ToUniversalTime();
        Touch(actorUserId, nowUtc);
    }

    public void Withdraw(string? note, string actorUserId, DateTimeOffset nowUtc)
    {
        EnsureStatus(OvertimeRequestStatus.Submitted, "只有待審核申請可以撤回。");
        ReviewNote = NormalizeOptional(note, 1000, "撤回說明");
        Status = OvertimeRequestStatus.Withdrawn;
        WithdrawnAtUtc = nowUtc.ToUniversalTime();
        Touch(actorUserId, nowUtc);
    }

    private void SetDetails(
        DateTime plannedStartAt,
        DateTime plannedEndAt,
        string reason,
        DateTimeOffset nowUtc)
    {
        ValidateDetails(plannedStartAt, plannedEndAt, reason);
        PlannedStartAt = DateTime.SpecifyKind(plannedStartAt, DateTimeKind.Unspecified);
        PlannedEndAt = DateTime.SpecifyKind(plannedEndAt, DateTimeKind.Unspecified);
        OvertimeDate = DateOnly.FromDateTime(PlannedStartAt);
        RequestedMinutes = checked((int)(PlannedEndAt - PlannedStartAt).TotalMinutes);
        Reason = reason.Trim();
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    private static void ValidateDetails(DateTime start, DateTime end, string? reason)
    {
        if (start.Kind != DateTimeKind.Unspecified || end.Kind != DateTimeKind.Unspecified)
            throw new DomainValidationException("加班時間必須使用台北本地時間。");
        if (end <= start)
            throw new DomainValidationException("預計結束時間必須晚於開始時間。");
        if (end - start > MaximumRequestDuration)
            throw new DomainValidationException("單張加班申請不得超過 12 小時。");
        if ((end - start).TotalMinutes % 30 != 0)
            throw new DomainValidationException("加班申請必須以 30 分鐘為單位。");
        if (start.Second != 0 || end.Second != 0 || start.Millisecond != 0 || end.Millisecond != 0)
            throw new DomainValidationException("加班時間請以整分鐘填寫。");
        var text = reason?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainValidationException("加班原因為必填欄位。");
        if (text.Length > 500)
            throw new DomainValidationException("加班原因不可超過 500 個字元。");
    }

    private void SetReview(OvertimeReviewReason reason, string? note, string actorUserId)
    {
        if (!Enum.IsDefined(reason))
            throw new DomainValidationException("審核原因不合法。");
        ReviewReason = reason;
        ReviewNote = NormalizeOptional(note, 1000, "審核說明");
        ReviewedByUserId = RequiredActor(actorUserId);
    }

    private void Touch(string actorUserId, DateTimeOffset nowUtc)
    {
        UpdatedByUserId = RequiredActor(actorUserId);
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    private void EnsureStatus(OvertimeRequestStatus status, string message)
    {
        if (Status != status) throw new DomainValidationException(message);
    }

    private static string RequiredActor(string? value)
    {
        var actor = value?.Trim();
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 450)
            throw new DomainValidationException("操作人資料不合法。");
        return actor;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string field)
    {
        var text = value?.Trim();
        if (text?.Length > maxLength)
            throw new DomainValidationException($"{field}不可超過 {maxLength} 個字元。");
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
