using HRSystem.Application.Common.Models;

namespace HRSystem.Application.LeaveRequests;

public interface ILeaveRequestService
{
    Task<IReadOnlyList<LeaveTypeOptionDto>> GetAvailableLeaveTypesAsync(CancellationToken cancellationToken = default);
    Task<LeaveDurationEstimateDto> EstimateDurationAsync(
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        CancellationToken cancellationToken = default);
    Task<LeaveDurationEstimateDto> EstimateDurationAsync(
        LeaveDurationEstimateRequest request,
        CancellationToken cancellationToken = default) =>
        EstimateDurationAsync(request.StartAt, request.EndAt, cancellationToken);
    Task<PagedResult<LeaveRequestDto>> GetMyRequestsAsync(LeaveRequestQuery query, CancellationToken cancellationToken = default);
    Task<LeaveRequestDto> GetRequestDetailAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<LeaveRequestDto>> GetPendingApprovalsAsync(LeaveRequestQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<LeaveRequestDto>> GetProcessedApprovalsAsync(LeaveRequestQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<LeaveRequestDto>> SearchAllRequestsAsync(LeaveRequestQuery query, CancellationToken cancellationToken = default);
    Task<LeaveRequestDto> CreateDraftAsync(CreateLeaveDraftRequest request, CancellationToken cancellationToken = default);
    Task<LeaveRequestDto> UpdateDraftAsync(UpdateLeaveDraftRequest request, CancellationToken cancellationToken = default);
    Task DeleteDraftAsync(LeaveRequestActionRequest request, CancellationToken cancellationToken = default);
    Task<LeaveRequestDto> SubmitAsync(LeaveRequestActionRequest request, CancellationToken cancellationToken = default);
    Task<LeaveRequestDto> WithdrawAsync(LeaveRequestActionRequest request, CancellationToken cancellationToken = default);
    Task<LeaveRequestDto> RequestCancellationAsync(RequestLeaveCancellationRequest request, CancellationToken cancellationToken = default);
    Task<LeaveRequestDto> ApproveAsync(LeaveRequestActionRequest request, CancellationToken cancellationToken = default);
    Task<LeaveRequestDto> RejectAsync(RejectLeaveRequestRequest request, CancellationToken cancellationToken = default);
    Task<LeaveRequestDto> ApproveCancellationAsync(LeaveRequestActionRequest request, CancellationToken cancellationToken = default);
    Task<LeaveRequestDto> RejectCancellationAsync(RejectLeaveCancellationRequest request, CancellationToken cancellationToken = default);
    Task<LeaveRequestDto> CopyToDraftAsync(Guid sourceId, CancellationToken cancellationToken = default);
}
