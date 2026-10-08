namespace HRSystem.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<MyLeaveSummaryDto?> GetMyLeaveSummaryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecentLeaveRequestDto>> GetMyRecentLeaveRequestsAsync(CancellationToken cancellationToken = default);
    Task<ApprovalSummaryDto?> GetPendingApprovalSummaryAsync(CancellationToken cancellationToken = default);
    Task<TodayLeaveSummaryDto?> GetTodayLeaveSummaryAsync(CancellationToken cancellationToken = default);
}
