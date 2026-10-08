namespace HRSystem.Application.Attendance;

public interface IAttendanceReviewService
{
    Task<AttendanceReviewFilterOptions> GetFilterOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<AttendanceReviewResult> SearchAsync(
        AttendanceReviewQuery query,
        CancellationToken cancellationToken = default);

    Task<MyAttendanceResult> SearchMineAsync(
        MyAttendanceQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromException<MyAttendanceResult>(
            new NotSupportedException("此測試服務未提供個人出勤查詢。"));

    Task<MyAttendanceDetailResult> GetMineDetailAsync(
        Guid dailyAttendanceResultId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<MyAttendanceDetailResult>(
            new NotSupportedException("此測試服務未提供個人出勤明細。"));

    Task<AttendanceReviewResolutionResult> ResolveAsync(
        ResolveAttendanceReviewRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromException<AttendanceReviewResolutionResult>(
            new NotSupportedException("此測試服務未提供結案操作。"));

    Task<AttendanceReviewResolutionResult> ReopenAsync(
        ReopenAttendanceReviewRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromException<AttendanceReviewResolutionResult>(
            new NotSupportedException("此測試服務未提供重新開啟操作。"));

    Task<IReadOnlyList<AttendanceReviewResolutionHistoryDto>> GetHistoryAsync(
        Guid resolutionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AttendanceReviewResolutionHistoryDto>>([]);
}
