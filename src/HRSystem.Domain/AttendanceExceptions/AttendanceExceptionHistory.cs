using HRSystem.Domain.Common;

namespace HRSystem.Domain.AttendanceExceptions;

public sealed class AttendanceExceptionHistory
{
    private AttendanceExceptionHistory() { }

    public AttendanceExceptionHistory(Guid id, Guid attendanceExceptionId,
        AttendanceExceptionHistoryAction action, string actionByUserId, string actionByDisplayName,
        string? comment, DateTimeOffset actionAtUtc, AttendanceExceptionStatus fromStatus,
        AttendanceExceptionStatus toStatus)
    {
        if (attendanceExceptionId == Guid.Empty) throw new DomainValidationException("出勤豁免申請為必填欄位。");
        var valid = action switch
        {
            AttendanceExceptionHistoryAction.Submitted => fromStatus == AttendanceExceptionStatus.Draft && toStatus == AttendanceExceptionStatus.Submitted,
            AttendanceExceptionHistoryAction.Approved => fromStatus == AttendanceExceptionStatus.Submitted && toStatus == AttendanceExceptionStatus.Approved,
            AttendanceExceptionHistoryAction.Rejected => fromStatus == AttendanceExceptionStatus.Submitted && toStatus == AttendanceExceptionStatus.Rejected,
            AttendanceExceptionHistoryAction.Withdrawn => fromStatus == AttendanceExceptionStatus.Submitted && toStatus == AttendanceExceptionStatus.Withdrawn,
            AttendanceExceptionHistoryAction.CancellationRequested => fromStatus == AttendanceExceptionStatus.Approved && toStatus == AttendanceExceptionStatus.CancellationRequested,
            AttendanceExceptionHistoryAction.CancellationApproved => fromStatus == AttendanceExceptionStatus.CancellationRequested && toStatus == AttendanceExceptionStatus.Cancelled,
            AttendanceExceptionHistoryAction.CancellationRejected => fromStatus == AttendanceExceptionStatus.CancellationRequested && toStatus == AttendanceExceptionStatus.Approved,
            _ => false
        };
        if (!valid) throw new DomainValidationException("出勤豁免歷程狀態轉移不合法。");
        if ((action is AttendanceExceptionHistoryAction.Rejected or
                AttendanceExceptionHistoryAction.CancellationRequested or
                AttendanceExceptionHistoryAction.CancellationRejected) &&
            string.IsNullOrWhiteSpace(comment))
            throw new DomainValidationException("此歷程必須填寫原因。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; AttendanceExceptionId = attendanceExceptionId; Action = action;
        ActionByUserId = Required(actionByUserId, 450); ActionByDisplayName = Required(actionByDisplayName, 100);
        Comment = Optional(comment, 1000); ActionAtUtc = actionAtUtc.ToUniversalTime(); FromStatus = fromStatus; ToStatus = toStatus;
    }

    public Guid Id { get; private set; }
    public Guid AttendanceExceptionId { get; private set; }
    public AttendanceExceptionHistoryAction Action { get; private set; }
    public string ActionByUserId { get; private set; } = string.Empty;
    public string ActionByDisplayName { get; private set; } = string.Empty;
    public string? Comment { get; private set; }
    public DateTimeOffset ActionAtUtc { get; private set; }
    public AttendanceExceptionStatus FromStatus { get; private set; }
    public AttendanceExceptionStatus ToStatus { get; private set; }
    public AttendanceException AttendanceException { get; private set; } = null!;
    private static string Required(string? value, int max) { var text = value?.Trim(); if (string.IsNullOrWhiteSpace(text) || text.Length > max) throw new DomainValidationException("歷程欄位內容不合法。"); return text; }
    private static string? Optional(string? value, int max) { var text = value?.Trim(); if (string.IsNullOrWhiteSpace(text)) return null; if (text.Length > max) throw new DomainValidationException("歷程意見過長。"); return text; }
}
