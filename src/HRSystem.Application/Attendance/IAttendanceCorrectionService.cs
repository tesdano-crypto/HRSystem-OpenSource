namespace HRSystem.Application.Attendance;

public interface IAttendanceCorrectionService
{
    Task<IReadOnlyList<AttendanceCorrectionRequestDto>> GetMyRequestsAsync(
        AttendanceCorrectionQuery query,
        CancellationToken cancellationToken = default);
    Task<AttendanceCorrectionRequestDto> GetMyRequestAsync(
        Guid id,
        CancellationToken cancellationToken = default);
    Task<AttendanceCorrectionContextDto?> GetMyAttendanceContextAsync(
        Guid attendanceResultId,
        CancellationToken cancellationToken = default);
    Task<AttendanceCorrectionRequestDto> CreateDraftAsync(
        CreateAttendanceCorrectionDraftRequest request,
        CancellationToken cancellationToken = default);
    Task<AttendanceCorrectionRequestDto> UpdateDraftAsync(
        UpdateAttendanceCorrectionDraftRequest request,
        CancellationToken cancellationToken = default);
    Task<AttendanceCorrectionRequestDto> SubmitAsync(
        AttendanceCorrectionActionRequest request,
        CancellationToken cancellationToken = default);
    Task<AttendanceCorrectionRequestDto> WithdrawAsync(
        AttendanceCorrectionActionRequest request,
        CancellationToken cancellationToken = default);
    Task<AttendanceCorrectionFilterOptions> GetReviewFilterOptionsAsync(
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AttendanceCorrectionRequestDto>> SearchForReviewAsync(
        AttendanceCorrectionQuery query,
        CancellationToken cancellationToken = default);
    Task<AttendanceCorrectionRequestDto> ApproveAsync(
        ReviewAttendanceCorrectionRequest request,
        CancellationToken cancellationToken = default);
    Task<AttendanceCorrectionRequestDto> RejectAsync(
        ReviewAttendanceCorrectionRequest request,
        CancellationToken cancellationToken = default);
}
