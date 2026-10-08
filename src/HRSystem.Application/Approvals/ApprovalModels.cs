using HRSystem.Domain.Approvals;

namespace HRSystem.Application.Approvals;

public sealed record ApprovalSummaryItemDto(string Label, string Value);

public sealed record ApprovalSourceSnapshot(
    ApprovalType ApprovalType,
    string SourceEntityType,
    string SourceEntityId,
    short FingerprintVersion,
    byte[] Fingerprint,
    string Title,
    IReadOnlyList<ApprovalSummaryItemDto> Summary);

public sealed record ApprovalActorDto(string UserId, string DisplayName);

public sealed record ApprovalHistoryDto(
    Guid Id,
    ApprovalHistoryAction Action,
    string? ActorUserId,
    DateTimeOffset ActionAtUtc,
    ApprovalChannel Channel,
    string? Comment);

public sealed record ApprovalDto(
    Guid Id,
    ApprovalType ApprovalType,
    string SourceEntityType,
    string SourceEntityId,
    short SourceFingerprintVersion,
    string Title,
    IReadOnlyList<ApprovalSummaryItemDto> Summary,
    string RequestedByUserId,
    DateTimeOffset RequestedAtUtc,
    string ApproverUserId,
    ApprovalStatus Status,
    DateTimeOffset? DecisionAtUtc,
    string? DecisionByUserId,
    ApprovalChannel? DecisionChannel,
    string? DecisionReason,
    ApprovalNotificationStatus NotificationStatus,
    string? NotificationErrorSummary,
    IReadOnlyList<ApprovalHistoryDto> History,
    string? SourceFingerprint = null);

public sealed class SubmitApprovalRequest
{
    public ApprovalType ApprovalType { get; set; }
    public Guid SourceId { get; set; }
    public string ApproverUserId { get; set; } = string.Empty;
}

public sealed class ApprovalDecisionRequest
{
    public Guid ApprovalId { get; set; }
    public string? Reason { get; set; }
}

public sealed record ApprovalLinePostbackRequest(
    string LineUserId,
    string Token,
    string? Reason);

public sealed record ApprovalLinePostbackResult(
    bool Succeeded,
    string SafeMessage,
    Guid? ApprovalId = null,
    ApprovalStatus? Status = null);

public sealed record ApprovalPrivateNotification(
    string LineUserId,
    Guid ApprovalId,
    string Title,
    IReadOnlyList<ApprovalSummaryItemDto> Summary,
    string ApproveToken,
    string ReturnToken,
    DateTimeOffset ExpiresAtUtc);

public enum LinePairingStatus
{
    Unbound,
    Pending,
    Verified,
    Revoked
}

public sealed record LinePairingUserDto(
    string UserId,
    string UserName,
    string DisplayName,
    string? EmployeeNumber,
    string? EmployeeName,
    IReadOnlyList<string> Roles);

public sealed record LinePairingStatusDto(
    string UserId,
    string UserName,
    string DisplayName,
    string? EmployeeNumber,
    string? EmployeeName,
    IReadOnlyList<string> Roles,
    LinePairingStatus Status,
    DateTimeOffset? VerifiedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    DateTimeOffset? PendingExpiresAtUtc,
    string? MaskedLineIdentifier,
    bool CanManage,
    bool TestNotificationAvailable);

public sealed record CreateLinePairingResult(
    Guid PairingRequestId,
    string PairingCommand,
    DateTimeOffset ExpiresAtUtc);

public sealed class CreateLinePairingRequest
{
    public string HrSystemUserId { get; set; } = string.Empty;
    public bool ReplaceExistingBinding { get; set; }
    public string? ReplacementReason { get; set; }
}

public sealed record CompleteLinePairingRequest(
    string PairingToken,
    string LineUserId,
    string SourceType);

public sealed record CompleteLinePairingResult(bool Succeeded, string SafeMessage);

public sealed record RevokeLineBindingRequest(
    string HrSystemUserId,
    string Reason);

public enum LinePrivateTargetStatus
{
    Found,
    MissingBinding
}

public sealed record LinePrivateTargetResolution(
    LinePrivateTargetStatus Status,
    string? LineUserId = null);

public sealed record LinePrivateTestNotification(string LineUserId, string Message);

public static class ApprovalDisplay
{
    public static string Status(ApprovalStatus value) => value switch
    {
        ApprovalStatus.Pending => "待簽核",
        ApprovalStatus.Approved => "已核准",
        ApprovalStatus.Returned => "已退回",
        ApprovalStatus.Cancelled => "已取消",
        ApprovalStatus.Superseded => "來源已變更",
        _ => "未知狀態"
    };

    public static string NotificationStatus(ApprovalNotificationStatus value) => value switch
    {
        ApprovalNotificationStatus.Pending => "待通知",
        ApprovalNotificationStatus.Sent => "已通知",
        ApprovalNotificationStatus.Failed => "通知失敗",
        _ => "未知狀態"
    };

    public static string HistoryAction(ApprovalHistoryAction value) => value switch
    {
        ApprovalHistoryAction.Submitted => "已送簽",
        ApprovalHistoryAction.NotificationSent => "LINE 通知已送出",
        ApprovalHistoryAction.NotificationFailed => "LINE 私訊通知失敗",
        ApprovalHistoryAction.Viewed => "已檢視",
        ApprovalHistoryAction.Approved => "已核准",
        ApprovalHistoryAction.Returned => "已退回",
        ApprovalHistoryAction.Cancelled => "已取消",
        ApprovalHistoryAction.Superseded => "來源已變更",
        _ => "未知動作"
    };
}
