using HRSystem.Domain.LeaveRequests;

namespace HRSystem.Application.Dashboard;

public sealed record DashboardDto(
    DashboardUserSummaryDto User,
    MyLeaveSummaryDto? MyLeave,
    ApprovalSummaryDto? Approvals,
    ManagementSummaryDto? Management,
    IReadOnlyList<DashboardTaskDto> Tasks,
    IReadOnlyList<RecentLeaveRequestDto> RecentRequests,
    TodayLeaveSummaryDto? TodayLeave,
    IReadOnlyList<QuickActionDto> QuickActions);

public sealed record DashboardUserSummaryDto(
    string DisplayName,
    string Role,
    DateOnly LocalDate,
    bool HasEmployee,
    string? EmployeeNumber,
    string? EmployeeName,
    string? DepartmentName,
    string? Notice);

public sealed record MyLeaveSummaryDto(
    int DraftCount,
    int SubmittedCount,
    int ApprovedCount,
    int RejectedCount,
    int WithdrawnCount);

public sealed record ApprovalSummaryDto(int PendingCount, bool ScopeAvailable, string? Notice);

public sealed record ManagementSummaryDto(int ActiveEmployeeCount, int ActiveDepartmentCount);

public sealed record DashboardTaskDto(
    string Type,
    string Title,
    LeaveRequestStatus Status,
    DateTimeOffset DateUtc,
    string Href);

public sealed record RecentLeaveRequestDto(
    Guid Id,
    string RequestNumber,
    string LeaveTypeName,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    decimal DurationHours,
    LeaveRequestStatus Status);

public sealed record TodayLeaveItemDto(
    Guid RequestId,
    string RequestNumber,
    string EmployeeNumber,
    string EmployeeName,
    string DepartmentName,
    string LeaveTypeName,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt);

public sealed record TodayLeaveSummaryDto(
    int EmployeeCount,
    int RequestCount,
    bool HasMore,
    bool ScopeAvailable,
    string? Notice,
    IReadOnlyList<TodayLeaveItemDto> Items);

public sealed record QuickActionDto(string Title, string Description, string Href);
