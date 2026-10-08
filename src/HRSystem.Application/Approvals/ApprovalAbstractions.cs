using HRSystem.Domain.Approvals;

namespace HRSystem.Application.Approvals;

public interface IApprovalSourceProvider
{
    ApprovalType ApprovalType { get; }
    Task<ApprovalSourceSnapshot> GetSnapshotAsync(
        Guid sourceId, CancellationToken cancellationToken = default);
}

public interface IApprovalActorDirectory
{
    Task<ApprovalActorDto?> FindActiveAsync(
        string userId, CancellationToken cancellationToken = default);
    Task<bool> HasPermissionAsync(
        string userId, string permission, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApprovalActorDto>> GetEligibleApproversAsync(
        ApprovalType approvalType, CancellationToken cancellationToken = default);
}

public interface IApprovalPrivateNotificationSender
{
    Task SendAsync(
        ApprovalPrivateNotification notification,
        CancellationToken cancellationToken = default);
}

public interface IApprovalService
{
    Task<IReadOnlyList<ApprovalActorDto>> GetApproversAsync(
        ApprovalType type, CancellationToken cancellationToken = default);
    Task<ApprovalDto> SubmitAsync(
        SubmitApprovalRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApprovalDto>> GetListAsync(
        CancellationToken cancellationToken = default);
    Task<ApprovalDto> GetAsync(
        Guid approvalId, CancellationToken cancellationToken = default);
    Task<ApprovalDto> MarkViewedAsync(
        Guid approvalId, CancellationToken cancellationToken = default);
    Task<ApprovalDto> ApproveAsync(
        ApprovalDecisionRequest request, CancellationToken cancellationToken = default);
    Task<ApprovalDto> ReturnAsync(
        ApprovalDecisionRequest request, CancellationToken cancellationToken = default);
    Task<ApprovalDto> CancelAsync(
        ApprovalDecisionRequest request, CancellationToken cancellationToken = default);
    Task<ApprovalDto> RetryNotificationAsync(
        Guid approvalId, CancellationToken cancellationToken = default);
    Task<ApprovalLinePostbackResult> HandleLinePostbackAsync(
        ApprovalLinePostbackRequest request, CancellationToken cancellationToken = default);
}

public interface ILineUserBindingService
{
    Task<IReadOnlyList<LinePairingStatusDto>> GetStatusesAsync(
        CancellationToken cancellationToken = default);
    Task<CreateLinePairingResult> CreatePairingRequestAsync(
        CreateLinePairingRequest request,
        CancellationToken cancellationToken = default);
    Task<CompleteLinePairingResult> CompletePrivatePairingAsync(
        CompleteLinePairingRequest request,
        CancellationToken cancellationToken = default);
    Task RevokeAsync(RevokeLineBindingRequest request,
        CancellationToken cancellationToken = default);
    Task SendTestNotificationAsync(string hrSystemUserId,
        CancellationToken cancellationToken = default);
}

public interface ILinePairingUserDirectory
{
    Task<IReadOnlyList<LinePairingUserDto>> GetOwnerUsersAsync(
        CancellationToken cancellationToken = default);
    Task<LinePairingUserDto?> FindActiveOwnerAsync(string userId,
        CancellationToken cancellationToken = default);
}

public interface ILinePrivateTargetResolver
{
    Task<LinePrivateTargetResolution> ResolveAsync(string hrSystemUserId,
        CancellationToken cancellationToken = default);
}

public interface ILinePrivateTestNotificationSender
{
    bool IsConfigured { get; }
    Task SendAsync(LinePrivateTestNotification notification,
        CancellationToken cancellationToken = default);
}

public interface ILineApprovalBridgeRequestVerifier
{
    bool IsAuthorized(string? presentedKey);
}
