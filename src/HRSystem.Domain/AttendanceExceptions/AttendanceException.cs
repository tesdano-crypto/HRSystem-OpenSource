using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.AttendanceExceptions;

public sealed class AttendanceException
{
    private AttendanceException() { }

    public AttendanceException(
        Guid id,
        string requestNumber,
        Guid employeeId,
        DateOnly workDate,
        NaturalDisasterReasonType reasonType,
        AttendanceExceptionImpactType impactType,
        TimeOnly? exemptFromTime,
        TimeOnly? exemptToTime,
        string? reason,
        string createdByUserId,
        DateTimeOffset nowUtc)
    {
        if (employeeId == Guid.Empty) throw new DomainValidationException("員工為必填欄位。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        RequestNumber = Required(requestNumber, "申請單號", 40);
        EmployeeId = employeeId;
        ExceptionType = AttendanceExceptionType.NaturalDisaster;
        CreatedByUserId = Required(createdByUserId, "建立者", 450);
        CreatedAtUtc = nowUtc.ToUniversalTime();
        Status = AttendanceExceptionStatus.Draft;
        SetDetails(workDate, reasonType, impactType, exemptFromTime, exemptToTime, reason, createdByUserId, nowUtc);
    }

    public Guid Id { get; private set; }
    public string RequestNumber { get; private set; } = string.Empty;
    public Guid EmployeeId { get; private set; }
    public AttendanceExceptionType ExceptionType { get; private set; }
    public NaturalDisasterReasonType ReasonType { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public AttendanceExceptionImpactType ImpactType { get; private set; }
    public TimeOnly? ExemptFromTime { get; private set; }
    public TimeOnly? ExemptToTime { get; private set; }
    public string? Reason { get; private set; }
    public AttendanceExceptionStatus Status { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public DateTimeOffset? RejectedAtUtc { get; private set; }
    public DateTimeOffset? WithdrawnAtUtc { get; private set; }
    public DateTimeOffset? CancellationRequestedAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? CancellationRequestedByUserId { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public string? UpdatedByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Employee Employee { get; private set; } = null!;
    public ICollection<AttendanceExceptionHistory> Histories { get; } = new List<AttendanceExceptionHistory>();

    public void UpdateDraft(DateOnly workDate, NaturalDisasterReasonType reasonType,
        AttendanceExceptionImpactType impactType, TimeOnly? exemptFromTime, TimeOnly? exemptToTime,
        string? reason, string userId, DateTimeOffset nowUtc)
    {
        Ensure(AttendanceExceptionStatus.Draft, "只有草稿可以修改。");
        SetDetails(workDate, reasonType, impactType, exemptFromTime, exemptToTime, reason, userId, nowUtc);
    }

    public void Submit(string userId, DateTimeOffset nowUtc) { Ensure(AttendanceExceptionStatus.Draft, "只有草稿可以送出。"); Status = AttendanceExceptionStatus.Submitted; SubmittedAtUtc = nowUtc.ToUniversalTime(); Touch(userId, nowUtc); }
    public void Approve(string userId, DateTimeOffset nowUtc) { Ensure(AttendanceExceptionStatus.Submitted, "只有已送出的申請可以核准。"); Status = AttendanceExceptionStatus.Approved; ApprovedAtUtc = nowUtc.ToUniversalTime(); Touch(userId, nowUtc); }
    public void Reject(string reason, string userId, DateTimeOffset nowUtc) { Ensure(AttendanceExceptionStatus.Submitted, "只有已送出的申請可以駁回。"); _ = Required(reason, "駁回原因", 1000); Status = AttendanceExceptionStatus.Rejected; RejectedAtUtc = nowUtc.ToUniversalTime(); Touch(userId, nowUtc); }
    public void Withdraw(string userId, DateTimeOffset nowUtc) { Ensure(AttendanceExceptionStatus.Submitted, "只有已送出的申請可以撤回。"); Status = AttendanceExceptionStatus.Withdrawn; WithdrawnAtUtc = nowUtc.ToUniversalTime(); Touch(userId, nowUtc); }
    public void RequestCancellation(string reason, string userId, DateTimeOffset nowUtc) { Ensure(AttendanceExceptionStatus.Approved, "只有已核准的申請可以提出取消。"); CancellationReason = Required(reason, "取消原因", 1000); CancellationRequestedByUserId = Required(userId, "取消申請人", 450); CancellationRequestedAtUtc = nowUtc.ToUniversalTime(); Status = AttendanceExceptionStatus.CancellationRequested; Touch(userId, nowUtc); }
    public void ApproveCancellation(string userId, DateTimeOffset nowUtc) { Ensure(AttendanceExceptionStatus.CancellationRequested, "此申請不是待處理取消狀態。"); Status = AttendanceExceptionStatus.Cancelled; CancelledAtUtc = nowUtc.ToUniversalTime(); Touch(userId, nowUtc); }
    public void RejectCancellation(string reason, string userId, DateTimeOffset nowUtc) { Ensure(AttendanceExceptionStatus.CancellationRequested, "此申請不是待處理取消狀態。"); _ = Required(reason, "取消駁回原因", 1000); Status = AttendanceExceptionStatus.Approved; Touch(userId, nowUtc); }

    private void SetDetails(DateOnly workDate, NaturalDisasterReasonType reasonType, AttendanceExceptionImpactType impactType, TimeOnly? from, TimeOnly? to, string? reason, string userId, DateTimeOffset nowUtc)
    {
        if (workDate == default) throw new DomainValidationException("出勤日期為必填欄位。");
        if (!Enum.IsDefined(reasonType)) throw new DomainValidationException("天然災害原因類型不合法。");
        if (!Enum.IsDefined(impactType)) throw new DomainValidationException("出勤影響類型不合法。");
        if (impactType == AttendanceExceptionImpactType.FullDay && (from.HasValue || to.HasValue)) throw new DomainValidationException("全日豁免不可填寫起訖時間。");
        if (impactType == AttendanceExceptionImpactType.LateArrival && (from.HasValue || !to.HasValue)) throw new DomainValidationException("延後到班必須填寫豁免截止時間。");
        if (impactType == AttendanceExceptionImpactType.EarlyDeparture && (!from.HasValue || to.HasValue)) throw new DomainValidationException("提前離開必須填寫豁免開始時間。");
        WorkDate = workDate; ReasonType = reasonType; ImpactType = impactType; ExemptFromTime = from; ExemptToTime = to;
        Reason = Optional(reason, 1000); Touch(userId, nowUtc);
    }

    private void Ensure(AttendanceExceptionStatus expected, string message) { if (Status != expected) throw new DomainValidationException(message); }
    private void Touch(string userId, DateTimeOffset nowUtc) { UpdatedByUserId = Required(userId, "異動者", 450); UpdatedAtUtc = nowUtc.ToUniversalTime(); }
    private static string Required(string? value, string field, int max) { var text = value?.Trim(); if (string.IsNullOrWhiteSpace(text)) throw new DomainValidationException($"{field}為必填欄位。"); if (text.Length > max) throw new DomainValidationException($"{field}不可超過 {max} 個字元。"); return text; }
    private static string? Optional(string? value, int max) { var text = value?.Trim(); if (string.IsNullOrWhiteSpace(text)) return null; if (text.Length > max) throw new DomainValidationException($"欄位不可超過 {max} 個字元。"); return text; }
}
