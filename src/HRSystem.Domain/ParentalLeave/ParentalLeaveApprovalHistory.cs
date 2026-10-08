using HRSystem.Domain.Common;

namespace HRSystem.Domain.ParentalLeave;

public sealed class ParentalLeaveApprovalHistory
{
    private ParentalLeaveApprovalHistory()
    {
    }

    public ParentalLeaveApprovalHistory(
        Guid id,
        Guid parentalLeaveRequestId,
        ParentalLeaveApprovalAction action,
        string actionByUserId,
        string actionByDisplayName,
        string? comment,
        DateTimeOffset actionAtUtc,
        ParentalLeaveStatus fromStatus,
        ParentalLeaveStatus toStatus)
    {
        if (parentalLeaveRequestId == Guid.Empty)
        {
            throw new DomainValidationException("育嬰留停申請為必填欄位。");
        }

        EnsureTransition(action, fromStatus, toStatus);
        if ((action is ParentalLeaveApprovalAction.Rejected or
                ParentalLeaveApprovalAction.CancellationRequested or
                ParentalLeaveApprovalAction.CancellationRejected or
                ParentalLeaveApprovalAction.EarlyReturnRequested) &&
            string.IsNullOrWhiteSpace(comment))
        {
            throw new DomainValidationException("此歷程必須填寫原因。");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        ParentalLeaveRequestId = parentalLeaveRequestId;
        Action = action;
        ActionByUserId = RequiredText(actionByUserId, 450);
        ActionByDisplayName = RequiredText(actionByDisplayName, 100);
        Comment = OptionalText(comment, 1000);
        ActionAtUtc = actionAtUtc.ToUniversalTime();
        FromStatus = fromStatus;
        ToStatus = toStatus;
    }

    public Guid Id { get; private set; }
    public Guid ParentalLeaveRequestId { get; private set; }
    public ParentalLeaveApprovalAction Action { get; private set; }
    public string ActionByUserId { get; private set; } = string.Empty;
    public string ActionByDisplayName { get; private set; } = string.Empty;
    public string? Comment { get; private set; }
    public DateTimeOffset ActionAtUtc { get; private set; }
    public ParentalLeaveStatus FromStatus { get; private set; }
    public ParentalLeaveStatus ToStatus { get; private set; }
    public ParentalLeaveRequest ParentalLeaveRequest { get; private set; } = null!;

    private static void EnsureTransition(
        ParentalLeaveApprovalAction action,
        ParentalLeaveStatus from,
        ParentalLeaveStatus to)
    {
        var valid = action switch
        {
            ParentalLeaveApprovalAction.Submitted =>
                from == ParentalLeaveStatus.Draft && to == ParentalLeaveStatus.Submitted,
            ParentalLeaveApprovalAction.Approved =>
                from == ParentalLeaveStatus.Submitted && to == ParentalLeaveStatus.Approved,
            ParentalLeaveApprovalAction.Rejected =>
                from == ParentalLeaveStatus.Submitted && to == ParentalLeaveStatus.Rejected,
            ParentalLeaveApprovalAction.Withdrawn =>
                from == ParentalLeaveStatus.Submitted && to == ParentalLeaveStatus.Withdrawn,
            ParentalLeaveApprovalAction.CancellationRequested =>
                from == ParentalLeaveStatus.Approved && to == ParentalLeaveStatus.CancellationRequested,
            ParentalLeaveApprovalAction.CancellationApproved =>
                from == ParentalLeaveStatus.CancellationRequested && to == ParentalLeaveStatus.Cancelled,
            ParentalLeaveApprovalAction.CancellationRejected =>
                from == ParentalLeaveStatus.CancellationRequested && to == ParentalLeaveStatus.Approved,
            ParentalLeaveApprovalAction.EarlyReturnRequested or
                ParentalLeaveApprovalAction.EarlyReturnApproved =>
                from == ParentalLeaveStatus.Approved && to == ParentalLeaveStatus.Approved,
            _ => false
        };
        if (!valid)
        {
            throw new DomainValidationException("育嬰留停歷程狀態轉移不合法。");
        }
    }

    private static string RequiredText(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
        {
            throw new DomainValidationException("歷程欄位內容不合法。");
        }

        return normalized;
    }

    private static string? OptionalText(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length > maxLength)
        {
            throw new DomainValidationException("歷程意見過長。");
        }

        return normalized;
    }
}
