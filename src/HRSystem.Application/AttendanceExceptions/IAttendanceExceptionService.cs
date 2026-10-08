using HRSystem.Application.Common.Models;

namespace HRSystem.Application.AttendanceExceptions;

public interface IAttendanceExceptionService
{
    Task<IReadOnlyList<AttendanceExceptionEmployeeOptionDto>> GetEmployeeOptionsAsync(CancellationToken cancellationToken = default);
    Task<AttendanceExceptionReviewContextDto> GetReviewContextAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<AttendanceExceptionDto>> GetMyRequestsAsync(AttendanceExceptionQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<AttendanceExceptionDto>> GetPendingApprovalsAsync(AttendanceExceptionQuery query, CancellationToken cancellationToken = default);
    Task<AttendanceExceptionDto> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AttendanceExceptionDto> CreateDraftAsync(CreateAttendanceExceptionDraftRequest request, CancellationToken cancellationToken = default);
    Task<AttendanceExceptionDto> UpdateDraftAsync(UpdateAttendanceExceptionDraftRequest request, CancellationToken cancellationToken = default);
    Task<AttendanceExceptionDto> SubmitAsync(AttendanceExceptionActionRequest request, CancellationToken cancellationToken = default);
    Task<AttendanceExceptionDto> WithdrawAsync(AttendanceExceptionActionRequest request, CancellationToken cancellationToken = default);
    Task<AttendanceExceptionDto> ApproveAsync(AttendanceExceptionActionRequest request, CancellationToken cancellationToken = default);
    Task<AttendanceExceptionDto> RejectAsync(AttendanceExceptionReasonActionRequest request, CancellationToken cancellationToken = default);
    Task<AttendanceExceptionDto> RequestCancellationAsync(AttendanceExceptionReasonActionRequest request, CancellationToken cancellationToken = default);
    Task<AttendanceExceptionDto> ApproveCancellationAsync(AttendanceExceptionActionRequest request, CancellationToken cancellationToken = default);
    Task<AttendanceExceptionDto> RejectCancellationAsync(AttendanceExceptionReasonActionRequest request, CancellationToken cancellationToken = default);
}
