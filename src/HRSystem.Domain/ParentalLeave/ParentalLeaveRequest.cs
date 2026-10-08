using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.ParentalLeave;

public sealed class ParentalLeaveRequest
{
    private ParentalLeaveRequest()
    {
    }

    public ParentalLeaveRequest(
        Guid id,
        string requestNumber,
        Guid employeeId,
        Guid childReferenceId,
        DateOnly childBirthDate,
        DateOnly startDate,
        DateOnly endDate,
        string contactAddress,
        string contactPhone,
        bool continueSocialInsurance,
        ParentalLeaveNoticeType noticeType,
        string? reason,
        string? emergencyCareReason,
        string? childDisplayName,
        string createdByUserId,
        DateTimeOffset nowUtc)
    {
        if (employeeId == Guid.Empty)
        {
            throw new DomainValidationException("員工為必填欄位。");
        }

        if (childReferenceId == Guid.Empty)
        {
            throw new DomainValidationException("子女識別為必填欄位。");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        RequestNumber = RequiredText(requestNumber, "申請單號", 40);
        EmployeeId = employeeId;
        ChildReferenceId = childReferenceId;
        CreatedByUserId = RequiredText(createdByUserId, "建立者", 450);
        CreatedAtUtc = nowUtc.ToUniversalTime();
        Status = ParentalLeaveStatus.Draft;
        SetDraftDetails(
            childBirthDate,
            startDate,
            endDate,
            contactAddress,
            contactPhone,
            continueSocialInsurance,
            noticeType,
            reason,
            emergencyCareReason,
            childDisplayName,
            createdByUserId,
            nowUtc);
    }

    public Guid Id { get; private set; }
    public string RequestNumber { get; private set; } = string.Empty;
    public Guid EmployeeId { get; private set; }
    public Guid ChildReferenceId { get; private set; }
    public DateOnly ChildBirthDate { get; private set; }
    public string? ChildDisplayName { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public ParentalLeaveApplicationType ApplicationType { get; private set; }
    public ParentalLeaveNoticeType NoticeType { get; private set; }
    public DateTimeOffset? RequestedAtUtc { get; private set; }
    public string? Reason { get; private set; }
    public string ContactAddress { get; private set; } = string.Empty;
    public string ContactPhone { get; private set; } = string.Empty;
    public bool ContinueSocialInsurance { get; private set; }
    public string? EmergencyCareReason { get; private set; }
    public ParentalLeaveStatus Status { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public DateTimeOffset? RejectedAtUtc { get; private set; }
    public DateTimeOffset? WithdrawnAtUtc { get; private set; }
    public DateTimeOffset? CancellationRequestedAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? CancellationRequestedByUserId { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public DateOnly? EarlyReturnDate { get; private set; }
    public DateTimeOffset? EarlyReturnRequestedAtUtc { get; private set; }
    public string? EarlyReturnReason { get; private set; }
    public string? EarlyReturnRequestedByUserId { get; private set; }
    public DateTimeOffset? EarlyReturnApprovedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public string? UpdatedByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Employee Employee { get; private set; } = null!;
    public ICollection<ParentalLeaveApprovalHistory> ApprovalHistories { get; } =
        new List<ParentalLeaveApprovalHistory>();

    public int CalendarDayCount =>
        ParentalLeavePolicy.CalendarDays(StartDate, EndDate);

    public DateOnly EffectiveEndDate =>
        EarlyReturnApprovedAtUtc.HasValue && EarlyReturnDate.HasValue
            ? EarlyReturnDate.Value.AddDays(-1)
            : EndDate;

    public void UpdateDraft(
        DateOnly childBirthDate,
        DateOnly startDate,
        DateOnly endDate,
        string contactAddress,
        string contactPhone,
        bool continueSocialInsurance,
        ParentalLeaveNoticeType noticeType,
        string? reason,
        string? emergencyCareReason,
        string? childDisplayName,
        string updatedByUserId,
        DateTimeOffset nowUtc)
    {
        EnsureStatus(ParentalLeaveStatus.Draft, "只有草稿可以修改。");
        SetDraftDetails(
            childBirthDate,
            startDate,
            endDate,
            contactAddress,
            contactPhone,
            continueSocialInsurance,
            noticeType,
            reason,
            emergencyCareReason,
            childDisplayName,
            updatedByUserId,
            nowUtc);
    }

    public void Submit(DateOnly requestedDate, string userId, DateTimeOffset nowUtc)
    {
        EnsureStatus(ParentalLeaveStatus.Draft, "只有草稿可以送出。");
        ParentalLeavePolicy.EnsureNotice(
            requestedDate,
            StartDate,
            ApplicationType,
            NoticeType,
            EmergencyCareReason);
        RequestedAtUtc = nowUtc.ToUniversalTime();
        SubmittedAtUtc = RequestedAtUtc;
        Status = ParentalLeaveStatus.Submitted;
        Touch(userId, nowUtc);
    }

    public void Approve(string userId, DateTimeOffset nowUtc)
    {
        EnsureStatus(ParentalLeaveStatus.Submitted, "只有已送出的申請可以核准。");
        Status = ParentalLeaveStatus.Approved;
        ApprovedAtUtc = nowUtc.ToUniversalTime();
        Touch(userId, nowUtc);
    }

    public void Reject(string reason, string userId, DateTimeOffset nowUtc)
    {
        EnsureStatus(ParentalLeaveStatus.Submitted, "只有已送出的申請可以駁回。");
        _ = RequiredText(reason, "駁回原因", 1000);
        Status = ParentalLeaveStatus.Rejected;
        RejectedAtUtc = nowUtc.ToUniversalTime();
        Touch(userId, nowUtc);
    }

    public void Withdraw(string userId, DateTimeOffset nowUtc)
    {
        EnsureStatus(ParentalLeaveStatus.Submitted, "只有已送出的申請可以撤回。");
        Status = ParentalLeaveStatus.Withdrawn;
        WithdrawnAtUtc = nowUtc.ToUniversalTime();
        Touch(userId, nowUtc);
    }

    public void RequestCancellation(string reason, string userId, DateTimeOffset nowUtc)
    {
        EnsureStatus(ParentalLeaveStatus.Approved, "只有已核准的申請可以提出取消。");
        CancellationReason = RequiredText(reason, "取消原因", 1000);
        CancellationRequestedByUserId = RequiredText(userId, "取消申請人", 450);
        CancellationRequestedAtUtc = nowUtc.ToUniversalTime();
        Status = ParentalLeaveStatus.CancellationRequested;
        Touch(userId, nowUtc);
    }

    public void ApproveCancellation(string userId, DateTimeOffset nowUtc)
    {
        EnsureStatus(ParentalLeaveStatus.CancellationRequested, "此申請不是待處理取消狀態。");
        Status = ParentalLeaveStatus.Cancelled;
        CancelledAtUtc = nowUtc.ToUniversalTime();
        Touch(userId, nowUtc);
    }

    public void RejectCancellation(string reason, string userId, DateTimeOffset nowUtc)
    {
        EnsureStatus(ParentalLeaveStatus.CancellationRequested, "此申請不是待處理取消狀態。");
        _ = RequiredText(reason, "取消駁回原因", 1000);
        Status = ParentalLeaveStatus.Approved;
        Touch(userId, nowUtc);
    }

    public void RequestEarlyReturn(
        DateOnly returnDate,
        string reason,
        string userId,
        DateTimeOffset nowUtc)
    {
        EnsureStatus(ParentalLeaveStatus.Approved, "只有已核准的申請可以提出提前復職。");
        if (EarlyReturnRequestedAtUtc.HasValue)
        {
            throw new DomainValidationException("提前復職申請已提出，請勿重複送出。");
        }

        if (returnDate <= StartDate || returnDate > EndDate)
        {
            throw new DomainValidationException("提前復職日必須晚於留停開始日且不晚於原結束日。");
        }

        EarlyReturnDate = returnDate;
        EarlyReturnReason = RequiredText(reason, "提前復職原因", 1000);
        EarlyReturnRequestedByUserId = RequiredText(userId, "提前復職申請人", 450);
        EarlyReturnRequestedAtUtc = nowUtc.ToUniversalTime();
        Touch(userId, nowUtc);
    }

    public void ApproveEarlyReturn(string userId, DateTimeOffset nowUtc)
    {
        EnsureStatus(ParentalLeaveStatus.Approved, "此申請目前不可核准提前復職。");
        if (!EarlyReturnRequestedAtUtc.HasValue || !EarlyReturnDate.HasValue)
        {
            throw new DomainValidationException("找不到待處理的提前復職申請。");
        }

        if (EarlyReturnApprovedAtUtc.HasValue)
        {
            throw new DomainValidationException("提前復職已核准。");
        }

        EarlyReturnApprovedAtUtc = nowUtc.ToUniversalTime();
        Touch(userId, nowUtc);
    }

    public ParentalLeaveStatus GetEffectiveStatus(DateOnly today) =>
        Status == ParentalLeaveStatus.Approved && EffectiveEndDate < today
            ? ParentalLeaveStatus.Completed
            : Status;

    private void SetDraftDetails(
        DateOnly childBirthDate,
        DateOnly startDate,
        DateOnly endDate,
        string contactAddress,
        string contactPhone,
        bool continueSocialInsurance,
        ParentalLeaveNoticeType noticeType,
        string? reason,
        string? emergencyCareReason,
        string? childDisplayName,
        string userId,
        DateTimeOffset nowUtc)
    {
        if (childBirthDate == default || childBirthDate > startDate)
        {
            throw new DomainValidationException("子女出生日期不得晚於申請開始日期。");
        }

        _ = ParentalLeavePolicy.CalendarDays(startDate, endDate);
        ChildBirthDate = childBirthDate;
        ChildDisplayName = OptionalText(childDisplayName, 100);
        StartDate = startDate;
        EndDate = endDate;
        ApplicationType = ParentalLeavePolicy.Classify(startDate, endDate);
        ContactAddress = RequiredText(contactAddress, "聯絡地址", 500);
        ContactPhone = RequiredText(contactPhone, "聯絡電話", 30);
        ContinueSocialInsurance = continueSocialInsurance;
        NoticeType = noticeType;
        Reason = OptionalText(reason, 1000);
        EmergencyCareReason = noticeType == ParentalLeaveNoticeType.EmergencyCare
            ? RequiredText(emergencyCareReason, "緊急照顧原因", 1000)
            : null;
        Touch(userId, nowUtc);
    }

    private void Touch(string userId, DateTimeOffset nowUtc)
    {
        UpdatedByUserId = RequiredText(userId, "異動者", 450);
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    private void EnsureStatus(ParentalLeaveStatus expected, string message)
    {
        if (Status != expected)
        {
            throw new DomainValidationException(message);
        }
    }

    private static string RequiredText(string? value, string field, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new DomainValidationException($"{field}為必填欄位。");
        }

        if (normalized.Length > maxLength)
        {
            throw new DomainValidationException($"{field}不可超過 {maxLength} 個字元。");
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
            throw new DomainValidationException($"欄位不可超過 {maxLength} 個字元。");
        }

        return normalized;
    }
}
