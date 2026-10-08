using HRSystem.Application.Common.Models;

namespace HRSystem.Application.ParentalLeave;

public interface IParentalLeaveService
{
    Task<IReadOnlyList<ParentalLeaveChildOptionDto>> GetChildOptionsAsync(
        CancellationToken cancellationToken = default);
    Task<ParentalLeaveEstimateDto> EstimateAsync(
        CreateParentalLeaveDraftRequest request,
        CancellationToken cancellationToken = default);
    Task<PagedResult<ParentalLeaveRequestDto>> GetMyRequestsAsync(
        ParentalLeaveQuery query,
        CancellationToken cancellationToken = default);
    Task<PagedResult<ParentalLeaveRequestDto>> GetPendingApprovalsAsync(
        ParentalLeaveQuery query,
        CancellationToken cancellationToken = default);
    Task<ParentalLeaveRequestDto> GetDetailAsync(
        Guid id,
        CancellationToken cancellationToken = default);
    Task<ParentalLeaveRequestDto> CreateDraftAsync(
        CreateParentalLeaveDraftRequest request,
        CancellationToken cancellationToken = default);
    Task<ParentalLeaveRequestDto> UpdateDraftAsync(
        UpdateParentalLeaveDraftRequest request,
        CancellationToken cancellationToken = default);
    Task<ParentalLeaveRequestDto> SubmitAsync(
        ParentalLeaveActionRequest request,
        CancellationToken cancellationToken = default);
    Task<ParentalLeaveRequestDto> WithdrawAsync(
        ParentalLeaveActionRequest request,
        CancellationToken cancellationToken = default);
    Task<ParentalLeaveRequestDto> ApproveAsync(
        ParentalLeaveActionRequest request,
        CancellationToken cancellationToken = default);
    Task<ParentalLeaveRequestDto> RejectAsync(
        ParentalLeaveReasonActionRequest request,
        CancellationToken cancellationToken = default);
    Task<ParentalLeaveRequestDto> RequestCancellationAsync(
        ParentalLeaveReasonActionRequest request,
        CancellationToken cancellationToken = default);
    Task<ParentalLeaveRequestDto> ApproveCancellationAsync(
        ParentalLeaveActionRequest request,
        CancellationToken cancellationToken = default);
    Task<ParentalLeaveRequestDto> RejectCancellationAsync(
        ParentalLeaveReasonActionRequest request,
        CancellationToken cancellationToken = default);
    Task<ParentalLeaveRequestDto> RequestEarlyReturnAsync(
        RequestParentalLeaveEarlyReturnRequest request,
        CancellationToken cancellationToken = default);
    Task<ParentalLeaveRequestDto> ApproveEarlyReturnAsync(
        ParentalLeaveActionRequest request,
        CancellationToken cancellationToken = default);
}
