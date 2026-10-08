namespace HRSystem.Application.Overtime;

public interface IOvertimeRequestService
{
    Task<IReadOnlyList<OvertimeRequestDto>> GetMyRequestsAsync(
        OvertimeRequestQuery query, CancellationToken cancellationToken = default);
    Task<OvertimeRequestDto> GetMyRequestAsync(
        Guid id, CancellationToken cancellationToken = default);
    Task<OvertimeRequestDto> CreateDraftAsync(
        CreateOvertimeDraftRequest request, CancellationToken cancellationToken = default);
    Task<OvertimeRequestDto> UpdateDraftAsync(
        UpdateOvertimeDraftRequest request, CancellationToken cancellationToken = default);
    Task<OvertimeRequestDto> SubmitAsync(
        OvertimeActionRequest request, CancellationToken cancellationToken = default);
    Task<OvertimeRequestDto> WithdrawAsync(
        OvertimeActionRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OvertimeRequestDto>> SearchForReviewAsync(
        OvertimeRequestQuery query, CancellationToken cancellationToken = default);
    Task<OvertimeRequestDto> ApproveAsync(
        ReviewOvertimeRequest request, CancellationToken cancellationToken = default);
    Task<OvertimeRequestDto> RejectAsync(
        ReviewOvertimeRequest request, CancellationToken cancellationToken = default);
}
