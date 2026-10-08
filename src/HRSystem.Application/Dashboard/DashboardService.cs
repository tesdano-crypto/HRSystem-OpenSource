using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.LeaveRequests;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Dashboard;

public sealed class DashboardService(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IDashboardService
{
    private const int RecentLimit = 5;
    private const int TaskLimit = 10;
    private const int TodayLimit = 20;
    private static readonly TimeZoneInfo TaipeiZone = ResolveTaipeiZone();

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var context = await GetContextAsync(cancellationToken);
        var myLeave = await GetMyLeaveSummaryAsync(context, cancellationToken);
        var approvals = await GetPendingApprovalSummaryAsync(context, cancellationToken);
        var management = await GetManagementSummaryAsync(context, cancellationToken);
        var tasks = await GetTasksAsync(context, cancellationToken);
        var recent = await GetMyRecentLeaveRequestsAsync(context, cancellationToken);
        var today = await GetTodayLeaveSummaryAsync(context, cancellationToken);
        return new DashboardDto(
            MapUser(context), myLeave, approvals, management, tasks, recent, today,
            BuildQuickActions(context));
    }

    public async Task<MyLeaveSummaryDto?> GetMyLeaveSummaryAsync(CancellationToken cancellationToken = default) =>
        await GetMyLeaveSummaryAsync(await GetContextAsync(cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<RecentLeaveRequestDto>> GetMyRecentLeaveRequestsAsync(
        CancellationToken cancellationToken = default) =>
        await GetMyRecentLeaveRequestsAsync(await GetContextAsync(cancellationToken), cancellationToken);

    public async Task<ApprovalSummaryDto?> GetPendingApprovalSummaryAsync(
        CancellationToken cancellationToken = default) =>
        await GetPendingApprovalSummaryAsync(await GetContextAsync(cancellationToken), cancellationToken);

    public async Task<TodayLeaveSummaryDto?> GetTodayLeaveSummaryAsync(
        CancellationToken cancellationToken = default) =>
        await GetTodayLeaveSummaryAsync(await GetContextAsync(cancellationToken), cancellationToken);

    private async Task<MyLeaveSummaryDto?> GetMyLeaveSummaryAsync(
        DashboardContext context,
        CancellationToken cancellationToken)
    {
        if (context.Employee is null) return null;
        var counts = await dbContext.LeaveRequests.AsNoTracking()
            .Where(x => x.EmployeeId == context.Employee.Id)
            .GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);
        return new MyLeaveSummaryDto(
            Count(LeaveRequestStatus.Draft),
            Count(LeaveRequestStatus.Submitted),
            Count(LeaveRequestStatus.Approved),
            Count(LeaveRequestStatus.Rejected),
            Count(LeaveRequestStatus.Withdrawn));

        int Count(LeaveRequestStatus status) => counts.GetValueOrDefault(status);
    }

    private async Task<IReadOnlyList<RecentLeaveRequestDto>> GetMyRecentLeaveRequestsAsync(
        DashboardContext context,
        CancellationToken cancellationToken)
    {
        if (context.Employee is null) return [];
        return await dbContext.LeaveRequests.AsNoTracking()
            .Where(x => x.EmployeeId == context.Employee.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.StartAt)
            .Take(RecentLimit)
            .Select(x => new RecentLeaveRequestDto(
                x.Id, x.RequestNumber, x.LeaveType.Name, x.StartAt, x.EndAt,
                x.DurationHours, x.Status))
            .ToListAsync(cancellationToken);
    }

    private async Task<ApprovalSummaryDto?> GetPendingApprovalSummaryAsync(
        DashboardContext context,
        CancellationToken cancellationToken)
    {
        if (!context.IsApprover) return null;
        var query = ApplyApprovalScope(dbContext.LeaveRequests.AsNoTracking()
            .Where(x => x.Status == LeaveRequestStatus.Submitted ||
                x.Status == LeaveRequestStatus.CancellationRequested), context);
        if (query is null)
        {
            return new ApprovalSummaryDto(0, false, "Manager 帳號必須綁定有效員工資料才能取得部門待簽核摘要。");
        }
        if (context.Employee is not null)
        {
            query = query.Where(x => x.EmployeeId != context.Employee.Id);
        }
        return new ApprovalSummaryDto(await query.CountAsync(cancellationToken), true, null);
    }

    private async Task<ManagementSummaryDto?> GetManagementSummaryAsync(
        DashboardContext context,
        CancellationToken cancellationToken)
    {
        if (!context.IsAdmin) return null;
        var activeEmployees = await dbContext.Employees.AsNoTracking()
            .CountAsync(x => x.IsActive, cancellationToken);
        var activeDepartments = await dbContext.Departments.AsNoTracking()
            .CountAsync(x => x.IsActive, cancellationToken);
        return new ManagementSummaryDto(activeEmployees, activeDepartments);
    }

    private async Task<IReadOnlyList<DashboardTaskDto>> GetTasksAsync(
        DashboardContext context,
        CancellationToken cancellationToken)
    {
        var tasks = new List<DashboardTaskDto>();
        if (context.Employee is not null)
        {
            var personalTasks = await dbContext.LeaveRequests.AsNoTracking()
                .Where(x => x.EmployeeId == context.Employee.Id &&
                    (x.Status == LeaveRequestStatus.Draft ||
                     x.Status == LeaveRequestStatus.Rejected ||
                     x.Status == LeaveRequestStatus.Submitted ||
                     x.Status == LeaveRequestStatus.CancellationRequested))
                .OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc)
                .Take(TaskLimit)
                .Select(x => new
                {
                    x.Id,
                    x.Status,
                    x.RequestNumber,
                    LeaveTypeName = x.LeaveType.Name,
                    DateUtc = x.UpdatedAtUtc ?? x.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);
            tasks.AddRange(personalTasks.Select(x => new DashboardTaskDto(
                x.Status == LeaveRequestStatus.Draft ? "草稿待完成" :
                x.Status == LeaveRequestStatus.Rejected ? "已退回可重新申請" : "已送出待審核",
                $"{x.RequestNumber}－{x.LeaveTypeName}",
                x.Status,
                x.DateUtc,
                $"/leave-requests/{x.Id}")));
        }

        if (context.IsApprover)
        {
            var pending = ApplyApprovalScope(dbContext.LeaveRequests.AsNoTracking()
                .Where(x => x.Status == LeaveRequestStatus.Submitted ||
                    x.Status == LeaveRequestStatus.CancellationRequested), context);
            if (pending is not null)
            {
                if (context.Employee is not null)
                {
                    pending = pending.Where(x => x.EmployeeId != context.Employee.Id);
                }
                var approvalTasks = await pending
                    .OrderBy(x => x.SubmittedAtUtc ?? x.CreatedAtUtc)
                    .Take(TaskLimit)
                    .Select(x => new
                    {
                        x.Id,
                        EmployeeName = x.Employee.ChineseName,
                        x.RequestNumber,
                        x.Status,
                        DateUtc = x.SubmittedAtUtc ?? x.CreatedAtUtc
                    })
                    .ToListAsync(cancellationToken);
                tasks.AddRange(approvalTasks.Select(x => new DashboardTaskDto(
                    "待簽核申請",
                    $"{x.EmployeeName}－{x.RequestNumber}",
                    x.Status,
                    x.DateUtc,
                    $"/leave-requests/{x.Id}")));
            }
        }

        return tasks.OrderByDescending(x => x.DateUtc).Take(TaskLimit).ToList();
    }

    private async Task<TodayLeaveSummaryDto?> GetTodayLeaveSummaryAsync(
        DashboardContext context,
        CancellationToken cancellationToken)
    {
        if (!context.IsApprover) return null;
        var (startUtc, endUtc) = TodayUtcRange();
        var query = ApplyTodayScope(dbContext.LeaveRequests.AsNoTracking()
            .Where(x => (x.Status == LeaveRequestStatus.Approved ||
                    x.Status == LeaveRequestStatus.CancellationRequested) &&
                x.StartAt < endUtc && x.EndAt > startUtc), context);
        if (query is null)
        {
            return new TodayLeaveSummaryDto(0, 0, false, false,
                "Manager 帳號必須綁定有效員工資料才能取得部門今日請假摘要。", []);
        }
        var requestCount = await query.CountAsync(cancellationToken);
        var employeeCount = await query.Select(x => x.EmployeeId).Distinct().CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.StartAt).ThenBy(x => x.Employee.EmployeeNumber)
            .Take(TodayLimit)
            .Select(x => new TodayLeaveItemDto(
                x.Id, x.RequestNumber, x.Employee.EmployeeNumber, x.Employee.ChineseName,
                x.Employee.Department.Name, x.LeaveType.Name, x.StartAt, x.EndAt))
            .ToListAsync(cancellationToken);
        return new TodayLeaveSummaryDto(
            employeeCount, requestCount, requestCount > TodayLimit, true, null, items);
    }

    private IQueryable<LeaveRequest>? ApplyApprovalScope(
        IQueryable<LeaveRequest> query,
        DashboardContext context)
    {
        if (context.IsAdmin) return query;
        return context.ManagerDepartmentId.HasValue
            ? query.Where(x => x.Employee.DepartmentId == context.ManagerDepartmentId.Value)
            : null;
    }

    private IQueryable<LeaveRequest>? ApplyTodayScope(
        IQueryable<LeaveRequest> query,
        DashboardContext context) => ApplyApprovalScope(query, context);

    private async Task<DashboardContext> GetContextAsync(CancellationToken cancellationToken)
    {
        EnsureAccess();
        var role = RoleNames.Ordered.FirstOrDefault(currentUser.IsInRole) ?? RoleNames.Employee;
        EmployeeProjection? employee = null;
        if (currentUser.EmployeeId.HasValue)
        {
            employee = await dbContext.Employees.AsNoTracking()
                .Where(x => x.Id == currentUser.EmployeeId.Value)
                .Select(x => new EmployeeProjection(
                    x.Id, x.EmployeeNumber, x.ChineseName, x.DepartmentId,
                    x.Department.Name, x.IsActive))
                .SingleOrDefaultAsync(cancellationToken);
        }
        Guid? managerDepartmentId = role == RoleNames.Manager && employee?.IsActive == true
            ? employee.DepartmentId
            : null;
        return new DashboardContext(
            role, currentUser.DisplayName ?? "使用者", employee, managerDepartmentId);
    }

    private DashboardUserSummaryDto MapUser(DashboardContext context)
    {
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TaipeiZone);
        var notice = context.Employee is not null ? null : context.IsAdmin
            ? "此管理帳號未綁定員工資料，因此不顯示「我的請假」。"
            : "帳號尚未綁定員工資料，請洽系統管理員。";
        return new DashboardUserSummaryDto(
            context.DisplayName, context.Role, DateOnly.FromDateTime(localNow.DateTime),
            context.Employee is not null, context.Employee?.EmployeeNumber,
            context.Employee?.EmployeeName, context.Employee?.DepartmentName, notice);
    }

    private static IReadOnlyList<QuickActionDto> BuildQuickActions(DashboardContext context)
    {
        if (context.IsAdmin)
        {
            return
            [
                new("待簽核", "檢視待處理請假", "/approvals/leave"),
                new("全部請假", "查詢公司請假紀錄", "/admin/leave-requests"),
                new("員工管理", "維護員工基本資料", "/employees"),
                new("部門管理", "維護組織部門", "/departments"),
                new("假別管理", "維護可用假別", "/leave-types"),
                new("使用者管理", "管理登入帳號與角色", "/admin/users")
            ];
        }
        if (context.Role == RoleNames.Manager)
        {
            if (context.Employee is null)
            {
                return [new("個人資料", "查看登入帳號資料", "/account/profile")];
            }
            return
            [
                new("新增請假", "建立新的請假草稿", "/leave-requests/new"),
                new("我的請假", "追蹤自己的申請", "/leave-requests"),
                new("待簽核", "處理同部門待簽申請", "/approvals/leave"),
                new("個人資料", "查看登入帳號資料", "/account/profile")
            ];
        }
        if (context.Employee is null)
        {
            return
            [
                new("個人資料", "查看登入帳號資料", "/account/profile"),
                new("變更密碼", "更新登入密碼", "/account/change-password")
            ];
        }
        return
        [
            new("新增請假", "建立新的請假草稿", "/leave-requests/new"),
            new("我的請假", "追蹤自己的申請", "/leave-requests"),
            new("個人資料", "查看登入帳號資料", "/account/profile"),
            new("變更密碼", "更新登入密碼", "/account/change-password")
        ];
    }

    private (DateTimeOffset StartUtc, DateTimeOffset EndUtc) TodayUtcRange()
    {
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TaipeiZone);
        var startLocal = DateTime.SpecifyKind(localNow.Date, DateTimeKind.Unspecified);
        var endLocal = startLocal.AddDays(1);
        return (
            new DateTimeOffset(startLocal, TaipeiZone.GetUtcOffset(startLocal)).ToUniversalTime(),
            new DateTimeOffset(endLocal, TaipeiZone.GetUtcOffset(endLocal)).ToUniversalTime());
    }

    private void EnsureAccess()
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(PolicyNames.DashboardAccess))
        {
            throw new ForbiddenAccessException("請先登入後再使用 Dashboard。");
        }
    }

    private static TimeZoneInfo ResolveTaipeiZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time"); }
    }

    private sealed record EmployeeProjection(
        Guid Id,
        string EmployeeNumber,
        string EmployeeName,
        Guid DepartmentId,
        string DepartmentName,
        bool IsActive);

    private sealed record DashboardContext(
        string Role,
        string DisplayName,
        EmployeeProjection? Employee,
        Guid? ManagerDepartmentId)
    {
        public bool IsAdmin => Role == RoleNames.Admin;
        public bool IsApprover => IsAdmin || Role == RoleNames.Manager;
    }
}
