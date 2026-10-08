using HRSystem.Application.Dashboard;
using HRSystem.Application.Security;
using HRSystem.Domain.Auditing;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class DashboardServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TodayStartUtc = new(2026, 7, 18, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Employee_Dashboard_Only_Returns_Own_Leave_Statistics()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Draft);
        await setup.AddRequestAsync(setup.OtherEmployee, LeaveRequestStatus.Draft);
        var dashboard = await setup.Service(RoleNames.Employee, setup.Employee.Id).GetDashboardAsync();
        Assert.Equal(1, dashboard.MyLeave?.DraftCount);
        Assert.All(dashboard.RecentRequests, x => Assert.Contains(setup.Employee.EmployeeNumber, x.RequestNumber));
    }

    [Fact]
    public async Task Employee_Does_Not_Receive_Approval_Summary()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        Assert.Null((await setup.Service(RoleNames.Employee, setup.Employee.Id).GetDashboardAsync()).Approvals);
    }

    [Fact]
    public async Task Manager_Receives_Only_Same_Department_Pending_Count()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Submitted);
        await setup.AddRequestAsync(setup.OtherEmployee, LeaveRequestStatus.Submitted);
        await setup.AddRequestAsync(setup.Manager, LeaveRequestStatus.Submitted);
        var summary = await setup.Service(RoleNames.Manager, setup.Manager.Id).GetPendingApprovalSummaryAsync();
        Assert.Equal(1, summary?.PendingCount);
    }

    [Fact]
    public async Task Admin_Receives_Global_Management_And_Approval_Summary()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Submitted);
        await setup.AddRequestAsync(setup.OtherEmployee, LeaveRequestStatus.Submitted);
        var dashboard = await setup.Service(RoleNames.Admin).GetDashboardAsync();
        Assert.Equal(2, dashboard.Approvals?.PendingCount);
        Assert.Equal(3, dashboard.Management?.ActiveEmployeeCount);
        Assert.Equal(2, dashboard.Management?.ActiveDepartmentCount);
    }

    [Fact]
    public async Task Unbound_Employee_Returns_Friendly_Empty_State()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        var dashboard = await setup.Service(RoleNames.Employee).GetDashboardAsync();
        Assert.False(dashboard.User.HasEmployee);
        Assert.NotNull(dashboard.User.Notice);
        Assert.Null(dashboard.MyLeave);
        Assert.Empty(dashboard.RecentRequests);
    }

    [Fact]
    public async Task Unbound_Admin_Can_Load_Dashboard()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        var dashboard = await setup.Service(RoleNames.Admin).GetDashboardAsync();
        Assert.False(dashboard.User.HasEmployee);
        Assert.NotNull(dashboard.Management);
        Assert.NotNull(dashboard.Approvals);
    }

    [Fact]
    public async Task Draft_Count_Is_Correct()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Draft);
        await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Draft, 2);
        Assert.Equal(2, (await setup.ServiceForEmployee().GetMyLeaveSummaryAsync())?.DraftCount);
    }

    [Fact]
    public async Task Submitted_Count_Is_Correct()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Submitted);
        Assert.Equal(1, (await setup.ServiceForEmployee().GetMyLeaveSummaryAsync())?.SubmittedCount);
    }

    [Fact]
    public async Task Approved_Count_Is_Correct()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Approved);
        Assert.Equal(1, (await setup.ServiceForEmployee().GetMyLeaveSummaryAsync())?.ApprovedCount);
    }

    [Fact]
    public async Task Rejected_Count_Is_Correct()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Rejected);
        Assert.Equal(1, (await setup.ServiceForEmployee().GetMyLeaveSummaryAsync())?.RejectedCount);
    }

    [Fact]
    public async Task Withdrawn_Does_Not_Count_As_Submitted()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Withdrawn);
        var summary = await setup.ServiceForEmployee().GetMyLeaveSummaryAsync();
        Assert.Equal(0, summary?.SubmittedCount);
        Assert.Equal(1, summary?.WithdrawnCount);
    }

    [Fact]
    public async Task Recent_Requests_Are_Limited_To_Five()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        for (var index = 0; index < 7; index++) await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Draft, index);
        Assert.Equal(5, (await setup.ServiceForEmployee().GetMyRecentLeaveRequestsAsync()).Count);
    }

    [Fact]
    public async Task Recent_Requests_Are_Ordered_By_Creation_Time()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        var older = await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Draft, 1);
        var newer = await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Draft, 3);
        var items = await setup.ServiceForEmployee().GetMyRecentLeaveRequestsAsync();
        Assert.Equal(newer.Id, items[0].Id);
        Assert.Equal(older.Id, items[1].Id);
    }

    [Fact]
    public async Task Today_Leave_Includes_Approved()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Approved, startAt: TodayStartUtc.AddHours(2));
        Assert.Single((await setup.Service(RoleNames.Admin).GetTodayLeaveSummaryAsync())!.Items);
    }

    [Fact]
    public async Task Today_Leave_Excludes_Submitted() =>
        await AssertTodayExcludesAsync(LeaveRequestStatus.Submitted);

    [Fact]
    public async Task Today_Leave_Excludes_Rejected() =>
        await AssertTodayExcludesAsync(LeaveRequestStatus.Rejected);

    [Fact]
    public async Task Today_Leave_Excludes_Withdrawn() =>
        await AssertTodayExcludesAsync(LeaveRequestStatus.Withdrawn);

    [Fact]
    public async Task Today_Leave_Includes_Approved_Request_Crossing_Midnight()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Approved,
            startAt: TodayStartUtc.AddHours(-2), durationHours: 4);
        Assert.Single((await setup.Service(RoleNames.Admin).GetTodayLeaveSummaryAsync())!.Items);
    }

    [Fact]
    public async Task Taipei_Today_Boundaries_Are_Converted_To_Utc_Correctly()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Approved,
            startAt: TodayStartUtc.AddHours(-2), durationHours: 2);
        await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Approved,
            startAt: TodayStartUtc.AddDays(1), durationHours: 2, offset: 2);
        await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Approved,
            startAt: TodayStartUtc.AddMinutes(1), durationHours: 1, offset: 3);
        var today = await setup.Service(RoleNames.Admin).GetTodayLeaveSummaryAsync();
        Assert.Single(today!.Items);
    }

    [Fact]
    public void Dashboard_Read_Models_Do_Not_Expose_Reason()
    {
        Assert.Null(typeof(RecentLeaveRequestDto).GetProperty("Reason"));
        Assert.Null(typeof(TodayLeaveItemDto).GetProperty("Reason"));
        Assert.Null(typeof(DashboardTaskDto).GetProperty("Reason"));
    }

    [Fact]
    public void Dashboard_Read_Models_Do_Not_Expose_Approval_Comment()
    {
        Assert.Null(typeof(DashboardDto).GetProperty("Comment"));
        Assert.Null(typeof(TodayLeaveItemDto).GetProperty("Comment"));
        Assert.Null(typeof(DashboardTaskDto).GetProperty("Comment"));
    }

    [Fact]
    public async Task Dashboard_Does_Not_Create_Or_Modify_Audit_Log()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        setup.Db.AuditLogs.Add(new AuditLog("test", "Existing", "Test", "1", null, null, null, Now));
        await setup.Db.SaveChangesAsync();
        var before = await setup.Db.AuditLogs.CountAsync();
        await setup.Service(RoleNames.Admin).GetDashboardAsync();
        Assert.Equal(before, await setup.Db.AuditLogs.CountAsync());
        Assert.False(setup.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Quick_Actions_Are_Role_Based()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        var employee = await setup.Service(RoleNames.Employee, setup.Employee.Id).GetDashboardAsync();
        var manager = await setup.Service(RoleNames.Manager, setup.Manager.Id).GetDashboardAsync();
        var admin = await setup.Service(RoleNames.Admin).GetDashboardAsync();
        Assert.Contains(employee.QuickActions, x => x.Href == "/leave-requests/new");
        Assert.DoesNotContain(employee.QuickActions, x => x.Href == "/approvals/leave");
        Assert.Contains(manager.QuickActions, x => x.Href == "/approvals/leave");
        Assert.Contains(admin.QuickActions, x => x.Href == "/admin/users");
    }

    [Fact]
    public async Task Empty_Dashboard_Returns_Zeroes_And_Empty_Collections()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        var dashboard = await setup.ServiceForEmployee().GetDashboardAsync();
        Assert.Equal(0, dashboard.MyLeave?.DraftCount);
        Assert.Empty(dashboard.Tasks);
        Assert.Empty(dashboard.RecentRequests);
    }

    [Fact]
    public async Task CancellationToken_Is_Propagated()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            setup.ServiceForEmployee().GetDashboardAsync(cancellation.Token));
    }

    [Fact]
    public async Task Manager_Today_Leave_Is_Limited_To_Own_Department()
    {
        await using var setup = await DashboardSetup.CreateAsync();
        await setup.AddRequestAsync(setup.Employee, LeaveRequestStatus.Approved, startAt: TodayStartUtc.AddHours(1));
        await setup.AddRequestAsync(setup.OtherEmployee, LeaveRequestStatus.Approved, startAt: TodayStartUtc.AddHours(2), offset: 2);
        var today = await setup.Service(RoleNames.Manager, setup.Manager.Id).GetTodayLeaveSummaryAsync();
        Assert.Single(today!.Items);
        Assert.Equal(setup.Employee.EmployeeNumber, today.Items[0].EmployeeNumber);
    }

    private static async Task AssertTodayExcludesAsync(LeaveRequestStatus status)
    {
        await using var setup = await DashboardSetup.CreateAsync();
        await setup.AddRequestAsync(setup.Employee, status, startAt: TodayStartUtc.AddHours(2));
        Assert.Empty((await setup.Service(RoleNames.Admin).GetTodayLeaveSummaryAsync())!.Items);
    }

    private sealed class DashboardSetup : IAsyncDisposable
    {
        public required HRSystem.Infrastructure.Persistence.HRSystemDbContext Db { get; init; }
        public required Department Department { get; init; }
        public required Department OtherDepartment { get; init; }
        public required Employee Employee { get; init; }
        public required Employee Manager { get; init; }
        public required Employee OtherEmployee { get; init; }
        public required LeaveType LeaveType { get; init; }

        public static async Task<DashboardSetup> CreateAsync()
        {
            var db = TestDb.Create();
            var department = new Department(Guid.NewGuid(), "DASH", "Dashboard 部門", Now);
            var otherDepartment = new Department(Guid.NewGuid(), "OTHER", "其他部門", Now);
            var employee = new Employee(Guid.NewGuid(), "DASH001", "Dashboard 員工", department.Id, new DateOnly(2026, 1, 1), Now);
            var manager = new Employee(Guid.NewGuid(), "DASH002", "Dashboard 主管", department.Id, new DateOnly(2026, 1, 1), Now);
            var other = new Employee(Guid.NewGuid(), "OTHER001", "其他員工", otherDepartment.Id, new DateOnly(2026, 1, 1), Now);
            var leaveType = new LeaveType(Guid.NewGuid(), "DASH-LEAVE", "測試假", LeaveUnit.Hour, 0.5m, true, false, 1, Now);
            db.AddRange(department, otherDepartment, employee, manager, other, leaveType);
            await db.SaveChangesAsync();
            return new DashboardSetup
            {
                Db = db,
                Department = department,
                OtherDepartment = otherDepartment,
                Employee = employee,
                Manager = manager,
                OtherEmployee = other,
                LeaveType = leaveType
            };
        }

        public DashboardService Service(string role, Guid? employeeId = null) =>
            new(Db, new TestCurrentUser(role, employeeId), new FixedTimeProvider(Now));

        public DashboardService ServiceForEmployee() => Service(RoleNames.Employee, Employee.Id);

        public async Task<LeaveRequest> AddRequestAsync(
            Employee employee,
            LeaveRequestStatus status,
            int offset = 1,
            DateTimeOffset? startAt = null,
            int durationHours = 8)
        {
            var createdAt = Now.AddMinutes(offset);
            var start = startAt ?? Now.AddDays(offset + 1);
            var request = new LeaveRequest(
                Guid.NewGuid(), ($"{employee.EmployeeNumber}-{offset}-{Guid.NewGuid():N}")[..30],
                employee.Id, LeaveType.Id, start, start.AddHours(durationHours),
                durationHours, "不應出現在 Dashboard 的完整原因", "unit-user", createdAt);
            if (status != LeaveRequestStatus.Draft) request.Submit("unit-user", createdAt.AddMinutes(1));
            if (status == LeaveRequestStatus.Approved) request.Approve("approver", createdAt.AddMinutes(2));
            if (status == LeaveRequestStatus.Rejected) request.Reject("不應回傳的簽核意見", "approver", createdAt.AddMinutes(2));
            if (status == LeaveRequestStatus.Withdrawn) request.Withdraw("unit-user", createdAt.AddMinutes(2));
            Db.LeaveRequests.Add(request);
            await Db.SaveChangesAsync();
            return request;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
