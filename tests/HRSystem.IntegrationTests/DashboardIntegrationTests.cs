using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Dashboard;
using HRSystem.Application.Departments;
using HRSystem.Application.Employees;
using HRSystem.Application.LeaveRequests;
using HRSystem.Application.LeaveTypes;
using HRSystem.Application.Security;
using HRSystem.Application.UserAccounts;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRSystem.IntegrationTests;

public sealed class DashboardIntegrationTests : IClassFixture<IdentityWebApplicationFactory>
{
    private const string InitialPassword = "T3st!InitialPassword";
    private static readonly DateTimeOffset DashboardWorkdayNow =
        new(2026, 8, 3, 10, 0, 0, TimeSpan.FromHours(8));
    private readonly IdentityWebApplicationFactory _factory;

    public DashboardIntegrationTests(IdentityWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_Dashboard_Redirects_To_Login()
    {
        using var client = _factory.CreateCookieClient();
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location?.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Employee_Can_Open_Dashboard()
    {
        var actors = await CreateActorsAsync();
        using var client = await LoginClientAsync(actors.EmployeeUser.UserName, InitialPassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Employee_Dashboard_Only_Contains_Own_Request()
    {
        var actors = await CreateActorsAsync();
        var own = await CreateRequestAsync(actors.EmployeeUser.Id, actors.LeaveType.Id, LeaveRequestStatus.Draft);
        var other = await CreateRequestAsync(actors.OtherUser.Id, actors.LeaveType.Id, LeaveRequestStatus.Draft, 2);
        using var client = await LoginClientAsync(actors.EmployeeUser.UserName, InitialPassword);
        var html = await client.GetStringAsync("/");
        Assert.Contains(own.RequestNumber, html, StringComparison.Ordinal);
        Assert.DoesNotContain(other.RequestNumber, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Employee_Dashboard_Does_Not_Show_Approval_Card()
    {
        var actors = await CreateActorsAsync();
        using var client = await LoginClientAsync(actors.EmployeeUser.UserName, InitialPassword);
        Assert.DoesNotContain("data-summary-key=\"pending-approval\"", await client.GetStringAsync("/"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Employee_Dashboard_Does_Not_Show_Admin_Quick_Actions()
    {
        var actors = await CreateActorsAsync();
        using var client = await LoginClientAsync(actors.EmployeeUser.UserName, InitialPassword);
        var html = await client.GetStringAsync("/");
        Assert.DoesNotContain("href=\"/admin/users\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/admin/leave-requests\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manager_Dashboard_Shows_Same_Department_Pending_Count()
    {
        var actors = await CreateActorsAsync();
        await CreateRequestAsync(actors.EmployeeUser.Id, actors.LeaveType.Id, LeaveRequestStatus.Submitted);
        await CreateRequestAsync(actors.OtherUser.Id, actors.LeaveType.Id, LeaveRequestStatus.Submitted, 2);
        using var client = await LoginClientAsync(actors.ManagerUser.UserName, InitialPassword);
        var html = await client.GetStringAsync("/");
        Assert.Equal("1", SummaryValue(html, "pending-approval"));
    }

    [Fact]
    public async Task Manager_Can_Open_Existing_Approval_Page()
    {
        var actors = await CreateActorsAsync();
        using var client = await LoginClientAsync(actors.ManagerUser.UserName, InitialPassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/approvals/leave")).StatusCode);
    }

    [Fact]
    public async Task Admin_Dashboard_Shows_Management_Summary()
    {
        using var client = await LoginClientAsync(IdentityWebApplicationFactory.AdminUserName, _factory.AdminPassword);
        var html = await client.GetStringAsync("/");
        Assert.Contains("data-summary-key=\"active-employees\"", html, StringComparison.Ordinal);
        Assert.Contains("data-summary-key=\"active-departments\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_Dashboard_Shows_Management_Quick_Actions()
    {
        using var client = await LoginClientAsync(IdentityWebApplicationFactory.AdminUserName, _factory.AdminPassword);
        var html = await client.GetStringAsync("/");
        foreach (var href in new[] { "/employees", "/departments", "/leave-types", "/admin/users" })
            Assert.Contains($"href=\"{href}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unbound_Admin_Can_Load_Dashboard()
    {
        using var client = await LoginClientAsync(IdentityWebApplicationFactory.AdminUserName, _factory.AdminPassword);
        var html = await client.GetStringAsync("/");
        Assert.Contains("管理帳號", html, StringComparison.Ordinal);
        Assert.DoesNotContain("最近請假紀錄", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unbound_Employee_Does_Not_Leak_Leave_Data()
    {
        var actors = await CreateActorsAsync();
        var request = await CreateRequestAsync(actors.EmployeeUser.Id, actors.LeaveType.Id, LeaveRequestStatus.Draft);
        var unbound = await CreateUnboundEmployeeUserAsync();
        using var client = await LoginClientAsync(unbound.UserName, InitialPassword);
        var html = await client.GetStringAsync("/");
        Assert.Contains("帳號尚未綁定員工資料", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.DoesNotContain(request.RequestNumber, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dashboard_Shows_My_Recent_Request()
    {
        var actors = await CreateActorsAsync();
        var request = await CreateRequestAsync(actors.EmployeeUser.Id, actors.LeaveType.Id, LeaveRequestStatus.Draft);
        using var client = await LoginClientAsync(actors.EmployeeUser.UserName, InitialPassword);
        Assert.Contains(request.RequestNumber, await client.GetStringAsync("/"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Today_Leave_Shows_Approved_Request()
    {
        var actors = await CreateActorsAsync();
        var request = await CreateRequestAsync(actors.EmployeeUser.Id, actors.LeaveType.Id,
            LeaveRequestStatus.Approved, startAt: DashboardWorkdayNow.AddHours(-1));
        var dashboard = await GetAdminDashboardAsync(DashboardWorkdayNow);
        Assert.Contains(dashboard.TodayLeave!.Items, x => x.RequestNumber == request.RequestNumber);
    }

    [Fact]
    public async Task Today_Leave_Does_Not_Show_Rejected_Request()
    {
        var actors = await CreateActorsAsync();
        var request = await CreateRequestAsync(actors.EmployeeUser.Id, actors.LeaveType.Id,
            LeaveRequestStatus.Rejected, startAt: DashboardWorkdayNow.AddHours(-1));
        var dashboard = await GetAdminDashboardAsync(DashboardWorkdayNow);
        Assert.DoesNotContain(dashboard.TodayLeave!.Items, x => x.RequestNumber == request.RequestNumber);
    }

    [Fact]
    public async Task Today_Leave_Does_Not_Show_Submitted_Request()
    {
        var actors = await CreateActorsAsync();
        var request = await CreateRequestAsync(actors.EmployeeUser.Id, actors.LeaveType.Id,
            LeaveRequestStatus.Submitted, startAt: DashboardWorkdayNow.AddHours(-1));
        using var client = await LoginClientAsync(IdentityWebApplicationFactory.AdminUserName, _factory.AdminPassword);
        var dashboard = await GetAdminDashboardAsync(DashboardWorkdayNow);
        Assert.DoesNotContain(dashboard.TodayLeave!.Items, x => x.RequestNumber == request.RequestNumber);
    }

    [Fact]
    public async Task Quick_Action_Links_Target_Existing_Routes()
    {
        var actors = await CreateActorsAsync();
        using var client = await LoginClientAsync(actors.EmployeeUser.UserName, InitialPassword);
        var html = await client.GetStringAsync("/");
        foreach (var href in new[] { "/leave-requests/new", "/leave-requests", "/account/profile", "/account/change-password" })
            Assert.Contains($"href=\"{href}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dashboard_Service_Rejects_Unauthenticated_User()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = new DashboardService(
            scope.ServiceProvider.GetRequiredService<HRSystem.Application.Abstractions.Persistence.IApplicationDbContext>(),
            new AnonymousCurrentUser(), TimeProvider.System);
        await Assert.ThrowsAsync<HRSystem.Application.Common.Exceptions.ForbiddenAccessException>(() => service.GetDashboardAsync());
    }

    [Fact]
    public async Task Logout_Prevents_Further_Dashboard_Access()
    {
        var actors = await CreateActorsAsync();
        using var client = await LoginClientAsync(actors.EmployeeUser.UserName, InitialPassword);
        var dashboardHtml = await client.GetStringAsync("/");
        var token = AntiforgeryToken(dashboardHtml);
        await client.PostAsync("/logout", Form(("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Browsing_Dashboard_Does_Not_Create_AuditLog()
    {
        var actors = await CreateActorsAsync();
        using var client = await LoginClientAsync(actors.EmployeeUser.UserName, InitialPassword);
        var before = await CountAuditLogsAsync();
        await client.GetStringAsync("/");
        Assert.Equal(before, await CountAuditLogsAsync());
    }

    [Fact]
    public void Dashboard_Integration_Uses_InMemory_Not_Development_Database()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();
        Assert.True(db.Database.IsInMemory());
        Assert.Equal("Microsoft.EntityFrameworkCore.InMemory", db.Database.ProviderName);
    }

    [Fact]
    public void Dashboard_Integration_Does_Not_Load_Development_User_Secrets()
    {
        using var scope = _factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var environment = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        Assert.Equal("Testing", environment.EnvironmentName);
        Assert.Null(configuration.GetConnectionString("HRSystemDb"));
    }

    [Fact]
    public async Task Dashboard_Load_Does_Not_Modify_LeaveRequest()
    {
        var actors = await CreateActorsAsync();
        var request = await CreateRequestAsync(actors.EmployeeUser.Id, actors.LeaveType.Id, LeaveRequestStatus.Submitted);
        var before = await LeaveSnapshotAsync(request.Id);
        await _factory.RunAsAsync(actors.EmployeeUser.Id, services => services.GetRequiredService<IDashboardService>().GetDashboardAsync());
        Assert.Equal(before, await LeaveSnapshotAsync(request.Id));
    }

    [Fact]
    public async Task Dashboard_Load_Does_Not_Add_Any_Data()
    {
        var actors = await CreateActorsAsync();
        var before = await DatabaseCountsAsync();
        await _factory.RunAsAsync(actors.EmployeeUser.Id, services => services.GetRequiredService<IDashboardService>().GetDashboardAsync());
        Assert.Equal(before, await DatabaseCountsAsync());
    }

    [Fact]
    public async Task Empty_Dashboard_Renders_Friendly_State()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = factory.CreateCookieClient();
        await LoginAsync(client, IdentityWebApplicationFactory.AdminUserName, factory.AdminPassword);
        var html = await client.GetStringAsync("/");
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("目前沒有待辦", decoded, StringComparison.Ordinal);
        Assert.Contains("今日無已核准請假", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dashboard_Contains_Responsive_Mobile_Structure()
    {
        using var client = await LoginClientAsync(IdentityWebApplicationFactory.AdminUserName, _factory.AdminPassword);
        var html = await client.GetStringAsync("/");
        Assert.Contains("dashboard-summary-grid", html, StringComparison.Ordinal);
        Assert.Contains("dashboard-quick-grid", html, StringComparison.Ordinal);
        Assert.Contains("name=\"viewport\"", html, StringComparison.Ordinal);
    }

    private async Task<DashboardActors> CreateActorsAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return await _factory.RunAsAdminAsync(async services =>
        {
            var departmentService = services.GetRequiredService<IDepartmentService>();
            var department = await departmentService.CreateAsync(new CreateDepartmentRequest { Code = $"DB{suffix}", Name = $"Dashboard 部門 {suffix}" });
            var otherDepartment = await departmentService.CreateAsync(new CreateDepartmentRequest { Code = $"DO{suffix}", Name = $"Dashboard 其他部門 {suffix}" });
            var employeeService = services.GetRequiredService<IEmployeeService>();
            var employee = await employeeService.CreateAsync(NewEmployee("Dashboard 員工", department.Id));
            var manager = await employeeService.CreateAsync(NewEmployee("Dashboard 主管", department.Id));
            var other = await employeeService.CreateAsync(NewEmployee("Dashboard 其他員工", otherDepartment.Id));
            var leaveType = await services.GetRequiredService<ILeaveTypeService>().CreateAsync(new CreateLeaveTypeRequest
            { Code = $"DL{suffix}", Name = "Dashboard 測試假", Unit = LeaveUnit.Hour, MinimumUnit = 0.5m, RequiresReason = true });
            var db = services.GetRequiredService<HRSystemDbContext>();
            var shift = new AttendanceShift(
                Guid.NewGuid(),
                $"DB-{suffix}",
                $"Dashboard 正常班 {suffix}",
                new TimeOnly(8, 0),
                new TimeOnly(8, 1),
                new TimeOnly(12, 0),
                new TimeOnly(13, 30),
                new TimeOnly(17, 30),
                480,
                false,
                false,
                DashboardWorkdayNow.ToUniversalTime());
            db.Add(shift);
            foreach (var employeeId in new[]
                     {
                         employee.Id,
                         manager.Id,
                         other.Id
                     })
            {
                db.EmployeeShiftAssignments.Add(
                    new EmployeeShiftAssignment(
                        Guid.NewGuid(),
                        employeeId,
                        shift.Id,
                        new DateOnly(2026, 1, 1),
                        null,
                        DashboardWorkdayNow.ToUniversalTime()));
            }

            await db.SaveChangesAsync();
            var accounts = services.GetRequiredService<IUserAccountService>();
            var employeeUser = await accounts.CreateAsync(NewUser($"dash-e-{suffix}", employee.Id, RoleNames.Employee));
            var managerUser = await accounts.CreateAsync(NewUser($"dash-m-{suffix}", manager.Id, RoleNames.Manager));
            var otherUser = await accounts.CreateAsync(NewUser($"dash-o-{suffix}", other.Id, RoleNames.Employee));
            return new DashboardActors(employee, manager, other, leaveType, employeeUser, managerUser, otherUser);
        });
    }

    private async Task<UserAccountDto> CreateUnboundEmployeeUserAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return await _factory.RunAsAdminAsync(services => services.GetRequiredService<IUserAccountService>().CreateAsync(new CreateUserAccountRequest
        {
            UserName = $"unbound-{suffix}",
            Email = $"unbound-{suffix}@example.test",
            DisplayName = "未綁定員工",
            TemporaryPassword = InitialPassword,
            Roles = [RoleNames.Employee]
        }));
    }

    private async Task<LeaveRequestDto> CreateRequestAsync(
        string userId,
        Guid leaveTypeId,
        LeaveRequestStatus status,
        int dayOffset = 1,
        DateTimeOffset? startAt = null)
    {
        var start = startAt ?? WorkdayStart(dayOffset);
        var draft = await _factory.RunAsAsync(userId, services => services.GetRequiredService<ILeaveRequestService>().CreateDraftAsync(new CreateLeaveDraftRequest
        { LeaveTypeId = leaveTypeId, StartAt = start, EndAt = start.AddHours(2), Reason = "Dashboard 整合測試原因" }));
        if (status == LeaveRequestStatus.Draft) return draft;
        var submitted = await _factory.RunAsAsync(userId, services => services.GetRequiredService<ILeaveRequestService>().SubmitAsync(new LeaveRequestActionRequest
        { Id = draft.Id, RowVersion = draft.RowVersion }));
        if (status == LeaveRequestStatus.Submitted) return submitted;

        var actors = await FindApproverForAsync(userId);
        if (status == LeaveRequestStatus.Approved)
            return await _factory.RunAsAsync(actors, services => services.GetRequiredService<ILeaveRequestService>().ApproveAsync(new LeaveRequestActionRequest
            { Id = submitted.Id, RowVersion = submitted.RowVersion }));
        if (status == LeaveRequestStatus.Rejected)
            return await _factory.RunAsAsync(actors, services => services.GetRequiredService<ILeaveRequestService>().RejectAsync(new RejectLeaveRequestRequest
            { Id = submitted.Id, RowVersion = submitted.RowVersion, Comment = "Dashboard 測試退回" }));
        return await _factory.RunAsAsync(userId, services => services.GetRequiredService<ILeaveRequestService>().WithdrawAsync(new LeaveRequestActionRequest
        { Id = submitted.Id, RowVersion = submitted.RowVersion }));
    }

    private static DateTimeOffset WorkdayStart(int dayOffset)
    {
        var date = new DateOnly(2026, 8, 3).AddDays(dayOffset);
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }

        return new DateTimeOffset(
            date.ToDateTime(new TimeOnly(8, 0)),
            TimeSpan.FromHours(8));
    }

    private async Task<string> FindApproverForAsync(string employeeUserId) => await _factory.RunAsAdminAsync(async services =>
    {
        var db = services.GetRequiredService<HRSystemDbContext>();
        var employeeId = await db.Users.Where(x => x.Id == employeeUserId).Select(x => x.EmployeeId).SingleAsync();
        var departmentId = await db.Employees.Where(x => x.Id == employeeId).Select(x => x.DepartmentId).SingleAsync();
        return await db.Users.Where(x => x.EmployeeId != employeeId && x.Employee!.DepartmentId == departmentId)
            .Join(db.UserRoles, user => user.Id, role => role.UserId, (user, role) => new { user.Id, role.RoleId })
            .Join(db.Roles.Where(x => x.Name == RoleNames.Manager), x => x.RoleId, role => role.Id, (x, _) => x.Id)
            .SingleAsync();
    });

    private Task<DashboardDto> GetAdminDashboardAsync(DateTimeOffset now) =>
        _factory.RunAsAdminAsync(services => new DashboardService(
            services.GetRequiredService<HRSystem.Application.Abstractions.Persistence.IApplicationDbContext>(),
            services.GetRequiredService<ICurrentUser>(),
            new FixedTimeProvider(now)).GetDashboardAsync());

    private async Task<HttpClient> LoginClientAsync(string account, string password)
    {
        var client = _factory.CreateCookieClient();
        await LoginAsync(client, account, password);
        return client;
    }

    private static async Task LoginAsync(HttpClient client, string account, string password)
    {
        var page = await client.GetStringAsync("/login");
        var response = await client.PostAsync("/account/login", Form(
            ("__RequestVerificationToken", AntiforgeryToken(page)), ("account", account),
            ("password", password), ("rememberMe", "false"), ("returnUrl", "/")));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<int> CountAuditLogsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<HRSystemDbContext>().AuditLogs.CountAsync();
    }

    private async Task<(LeaveRequestStatus Status, DateTimeOffset? UpdatedAt)> LeaveSnapshotAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<HRSystemDbContext>().LeaveRequests
            .Where(x => x.Id == id).Select(x => new ValueTuple<LeaveRequestStatus, DateTimeOffset?>(x.Status, x.UpdatedAtUtc)).SingleAsync();
    }

    private async Task<(int Departments, int Employees, int Requests, int Histories, int Audits)> DatabaseCountsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();
        return (await db.Departments.CountAsync(), await db.Employees.CountAsync(), await db.LeaveRequests.CountAsync(),
            await db.LeaveApprovalHistories.CountAsync(), await db.AuditLogs.CountAsync());
    }

    private static CreateEmployeeRequest NewEmployee(string name, Guid departmentId) => new()
    { ChineseName = name, DepartmentId = departmentId, HireDate = new DateOnly(2026, 1, 1) };

    private static CreateUserAccountRequest NewUser(string name, Guid employeeId, string role) => new()
    {
        UserName = name,
        Email = $"{name}@example.test",
        DisplayName = name,
        TemporaryPassword = InitialPassword,
        EmployeeId = employeeId,
        Roles = [role]
    };

    private static string SummaryValue(string html, string key)
    {
        var match = Regex.Match(html, $"data-summary-key=\"{Regex.Escape(key)}\"[\\s\\S]*?dashboard-card-value[^>]*>([0-9]+)<", RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Summary card {key} was not rendered.");
        return match.Groups[1].Value;
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) =>
        new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));

    private static string AntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Antiforgery token was not rendered.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private sealed record DashboardActors(
        EmployeeDto Employee,
        EmployeeDto Manager,
        EmployeeDto OtherEmployee,
        LeaveTypeDto LeaveType,
        UserAccountDto EmployeeUser,
        UserAccountDto ManagerUser,
        UserAccountDto OtherUser);

    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        public string? UserId => null;
        public Guid? EmployeeId => null;
        public string? DisplayName => null;
        public string? IpAddress => null;
        public bool IsAuthenticated => false;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string policy) => false;
    }
}
