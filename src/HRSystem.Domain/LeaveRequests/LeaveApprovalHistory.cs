using HRSystem.Domain.Common;

namespace HRSystem.Domain.LeaveRequests;

public sealed class LeaveApprovalHistory
{
    private LeaveApprovalHistory()
    {
    }

    public LeaveApprovalHistory(
        Guid id,
        Guid leaveRequestId,
        ApprovalAction action,
        string actionByUserId,
        string actionByDisplayName,
        string? comment,
        DateTimeOffset actionAtUtc,
        LeaveRequestStatus fromStatus,
        LeaveRequestStatus toStatus)
    {
        if (leaveRequestId == Guid.Empty) throw new DomainValidationException("必須指定請假申請。");
        EnsureTransition(action, fromStatus, toStatus);
        if ((action is ApprovalAction.Rejected or
                ApprovalAction.CancellationRequested or
                ApprovalAction.CancellationRejected) &&
            string.IsNullOrWhiteSpace(comment))
        {
            throw new DomainValidationException("退回原因為必填欄位。");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        LeaveRequestId = leaveRequestId;
        Action = action;
        ActionByUserId = LeaveRequestRules.RequiredText(actionByUserId, "操作者", 450);
        ActionByDisplayName = LeaveRequestRules.RequiredText(actionByDisplayName, "操作者顯示名稱", 100);
        Comment = LeaveRequestRules.OptionalText(comment, "簽核意見", 1000);
        ActionAtUtc = actionAtUtc.ToUniversalTime();
        FromStatus = fromStatus;
        ToStatus = toStatus;
    }

    public Guid Id { get; private set; }
    public Guid LeaveRequestId { get; private set; }
    public ApprovalAction Action { get; private set; }
    public string ActionByUserId { get; private set; } = string.Empty;
    public string ActionByDisplayName { get; private set; } = string.Empty;
    public string? Comment { get; private set; }
    public DateTimeOffset ActionAtUtc { get; private set; }
    public LeaveRequestStatus FromStatus { get; private set; }
    public LeaveRequestStatus ToStatus { get; private set; }
    public LeaveRequest LeaveRequest { get; private set; } = null!;

    private static void EnsureTransition(
        ApprovalAction action,
        LeaveRequestStatus fromStatus,
        LeaveRequestStatus toStatus)
    {
        var valid = action switch
        {
            ApprovalAction.Submitted => fromStatus == LeaveRequestStatus.Draft && toStatus == LeaveRequestStatus.Submitted,
            ApprovalAction.Approved => fromStatus == LeaveRequestStatus.Submitted && toStatus == LeaveRequestStatus.Approved,
            ApprovalAction.Rejected => fromStatus == LeaveRequestStatus.Submitted && toStatus == LeaveRequestStatus.Rejected,
            ApprovalAction.Withdrawn => fromStatus == LeaveRequestStatus.Submitted && toStatus == LeaveRequestStatus.Withdrawn,
            ApprovalAction.CancellationRequested => fromStatus == LeaveRequestStatus.Approved && toStatus == LeaveRequestStatus.CancellationRequested,
            ApprovalAction.CancellationApproved => fromStatus == LeaveRequestStatus.CancellationRequested && toStatus == LeaveRequestStatus.Cancelled,
            ApprovalAction.CancellationRejected => fromStatus == LeaveRequestStatus.CancellationRequested && toStatus == LeaveRequestStatus.Approved,
            _ => false
        };
        if (!valid) throw new DomainValidationException("簽核歷程的狀態轉換不合法。");
    }
}
