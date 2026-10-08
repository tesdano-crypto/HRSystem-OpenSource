using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.LeaveRequests;

public sealed class LeaveRequest
{
    private LeaveRequest()
    {
    }

    public LeaveRequest(
        Guid id,
        string requestNumber,
        Guid employeeId,
        Guid leaveTypeId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        decimal durationHours,
        string reason,
        string createdByUserId,
        DateTimeOffset nowUtc)
        : this(
            id,
            requestNumber,
            employeeId,
            leaveTypeId,
            startAt,
            endAt,
            durationHours,
            reason,
            createdByUserId,
            nowUtc,
            LeaveCalculationMode.WorkingSchedule,
            string.Empty,
            null)
    {
    }

    public LeaveRequest(
        Guid id,
        string requestNumber,
        Guid employeeId,
        Guid leaveTypeId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        decimal durationHours,
        string reason,
        string createdByUserId,
        DateTimeOffset nowUtc,
        LeaveCalculationMode calculationMode,
        string leaveTypeCode,
        PregnancyDurationCategory? pregnancyDurationCategory)
    {
        if (employeeId == Guid.Empty) throw new DomainValidationException("必須指定申請員工。");
        if (leaveTypeId == Guid.Empty) throw new DomainValidationException("必須指定假別。");

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        RequestNumber = LeaveRequestRules.RequiredText(requestNumber, "申請單號", 40);
        EmployeeId = employeeId;
        CreatedByUserId = LeaveRequestRules.RequiredText(createdByUserId, "建立者", 450);
        CreatedAtUtc = nowUtc.ToUniversalTime();
        Status = LeaveRequestStatus.Draft;
        SetDraftDetails(
            leaveTypeId,
            startAt,
            endAt,
            durationHours,
            reason,
            createdByUserId,
            nowUtc,
            calculationMode,
            leaveTypeCode,
            pregnancyDurationCategory);
    }

    public Guid Id { get; private set; }
    public string RequestNumber { get; private set; } = string.Empty;
    public Guid EmployeeId { get; private set; }
    public Guid LeaveTypeId { get; private set; }
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }
    public decimal DurationHours { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public PregnancyDurationCategory? PregnancyDurationCategory { get; private set; }
    public LeaveRequestStatus Status { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public DateTimeOffset? RejectedAtUtc { get; private set; }
    public DateTimeOffset? WithdrawnAtUtc { get; private set; }
    public DateTimeOffset? CancellationRequestedAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? CancellationRequestedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public string? UpdatedByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Employee Employee { get; private set; } = null!;
    public LeaveType LeaveType { get; private set; } = null!;
    public ICollection<LeaveApprovalHistory> ApprovalHistories { get; } = new List<LeaveApprovalHistory>();

    public bool CanDeleteDraft => Status == LeaveRequestStatus.Draft && SubmittedAtUtc is null;

    public void UpdateDraft(
        Guid leaveTypeId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        decimal durationHours,
        string reason,
        string updatedByUserId,
        DateTimeOffset nowUtc)
        => UpdateDraft(
            leaveTypeId,
            startAt,
            endAt,
            durationHours,
            reason,
            updatedByUserId,
            nowUtc,
            LeaveCalculationMode.WorkingSchedule,
            string.Empty,
            null);

    public void UpdateDraft(
        Guid leaveTypeId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        decimal durationHours,
        string reason,
        string updatedByUserId,
        DateTimeOffset nowUtc,
        LeaveCalculationMode calculationMode,
        string leaveTypeCode,
        PregnancyDurationCategory? pregnancyDurationCategory)
    {
        EnsureStatus(LeaveRequestStatus.Draft, "只有草稿可以編輯。");
        SetDraftDetails(
            leaveTypeId,
            startAt,
            endAt,
            durationHours,
            reason,
            updatedByUserId,
            nowUtc,
            calculationMode,
            leaveTypeCode,
            pregnancyDurationCategory);
    }

    public void RefreshDraftDuration(decimal durationHours)
    {
        EnsureStatus(LeaveRequestStatus.Draft, "只有草稿可以重新計算請假時數。");
        DurationHours = RequiredDuration(durationHours);
    }

    public void RefreshDraftDuration(
        decimal durationHours,
        LeaveCalculationMode calculationMode)
    {
        EnsureStatus(LeaveRequestStatus.Draft, "只有草稿可以重新計算請假時數。");
        DurationHours = RequiredDuration(durationHours, calculationMode);
    }

    public void Submit(string updatedByUserId, DateTimeOffset nowUtc)
    {
        EnsureStatus(LeaveRequestStatus.Draft, "只有草稿可以送出。");
        Status = LeaveRequestStatus.Submitted;
        SubmittedAtUtc = nowUtc.ToUniversalTime();
        Touch(updatedByUserId, nowUtc);
    }

    public void Approve(string updatedByUserId, DateTimeOffset nowUtc)
    {
        EnsureStatus(LeaveRequestStatus.Submitted, "只有待簽核申請可以核准。");
        Status = LeaveRequestStatus.Approved;
        ApprovedAtUtc = nowUtc.ToUniversalTime();
        Touch(updatedByUserId, nowUtc);
    }

    public void Reject(string rejectionReason, string updatedByUserId, DateTimeOffset nowUtc)
    {
        EnsureStatus(LeaveRequestStatus.Submitted, "只有待簽核申請可以退回。");
        _ = LeaveRequestRules.RequiredText(rejectionReason, "退回原因", 1000);
        Status = LeaveRequestStatus.Rejected;
        RejectedAtUtc = nowUtc.ToUniversalTime();
        Touch(updatedByUserId, nowUtc);
    }

    public void Withdraw(string updatedByUserId, DateTimeOffset nowUtc)
    {
        EnsureStatus(LeaveRequestStatus.Submitted, "只有待簽核申請可以撤回。");
        Status = LeaveRequestStatus.Withdrawn;
        WithdrawnAtUtc = nowUtc.ToUniversalTime();
        Touch(updatedByUserId, nowUtc);
    }

    public void RequestCancellation(
        string cancellationReason,
        string requestedByUserId,
        DateTimeOffset nowUtc)
    {
        EnsureStatus(
            LeaveRequestStatus.Approved,
            "只有已核准的請假可以申請撤簽。");
        CancellationReason = LeaveRequestRules.RequiredText(
            cancellationReason,
            "撤簽原因",
            1000);
        CancellationRequestedByUserId = LeaveRequestRules.RequiredText(
            requestedByUserId,
            "撤簽申請人",
            450);
        CancellationRequestedAtUtc = nowUtc.ToUniversalTime();
        Status = LeaveRequestStatus.CancellationRequested;
        Touch(requestedByUserId, nowUtc);
    }

    public void ApproveCancellation(
        string updatedByUserId,
        DateTimeOffset nowUtc)
    {
        EnsureStatus(
            LeaveRequestStatus.CancellationRequested,
            "只有撤簽申請中的請假可以核准撤簽。");
        Status = LeaveRequestStatus.Cancelled;
        Touch(updatedByUserId, nowUtc);
    }

    public void RejectCancellation(
        string rejectionReason,
        string updatedByUserId,
        DateTimeOffset nowUtc)
    {
        EnsureStatus(
            LeaveRequestStatus.CancellationRequested,
            "只有撤簽申請中的請假可以駁回撤簽。");
        _ = LeaveRequestRules.RequiredText(
            rejectionReason,
            "撤簽駁回原因",
            1000);
        Status = LeaveRequestStatus.Approved;
        Touch(updatedByUserId, nowUtc);
    }

    private void SetDraftDetails(
        Guid leaveTypeId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        decimal durationHours,
        string reason,
        string updatedByUserId,
        DateTimeOffset nowUtc,
        LeaveCalculationMode calculationMode,
        string leaveTypeCode,
        PregnancyDurationCategory? pregnancyDurationCategory)
    {
        if (leaveTypeId == Guid.Empty) throw new DomainValidationException("必須指定假別。");
        var startUtc = startAt.ToUniversalTime();
        var endUtc = endAt.ToUniversalTime();
        if (startUtc >= endUtc) throw new DomainValidationException("開始時間必須早於結束時間。");
        if (calculationMode == LeaveCalculationMode.WorkingSchedule &&
            endUtc - startUtc > TimeSpan.FromDays(31))
        {
            throw new DomainValidationException("單張請假申請不得超過 31 天。");
        }

        LeaveTypeId = leaveTypeId;
        StartAt = startUtc;
        EndAt = endUtc;
        if (calculationMode == LeaveCalculationMode.CalendarDays)
        {
            if (!CalendarDayLeavePolicy.IsSupported(leaveTypeCode))
            {
                throw new DomainValidationException("此連續曆日假別尚未開放申請。");
            }
        }
        else if (pregnancyDurationCategory.HasValue)
        {
            throw new DomainValidationException("非連續曆日假別不得設定流產假妊娠期間類別。");
        }

        DurationHours = RequiredDuration(durationHours, calculationMode);
        PregnancyDurationCategory = pregnancyDurationCategory;
        Reason = LeaveRequestRules.RequiredText(reason, "請假原因", 500);
        Touch(updatedByUserId, nowUtc);
    }

    private static decimal RequiredDuration(decimal durationHours)
        => RequiredDuration(durationHours, LeaveCalculationMode.WorkingSchedule);

    private static decimal RequiredDuration(
        decimal durationHours,
        LeaveCalculationMode calculationMode)
    {
        var normalized = Math.Round(durationHours, 2, MidpointRounding.AwayFromZero);
        if (normalized < 0 ||
            (calculationMode == LeaveCalculationMode.WorkingSchedule &&
             normalized == 0))
        {
            throw new DomainValidationException("請假時數必須大於 0。");
        }

        return normalized;
    }

    private void Touch(string userId, DateTimeOffset nowUtc)
    {
        UpdatedByUserId = LeaveRequestRules.RequiredText(userId, "更新者", 450);
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    private void EnsureStatus(LeaveRequestStatus expected, string message)
    {
        if (Status != expected) throw new DomainValidationException(message);
    }
}
