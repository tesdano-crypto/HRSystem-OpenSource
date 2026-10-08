using System.Net;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HRSystem.IntegrationTests;

public sealed class AttendanceManagementWebIntegrationTests
{
    [Theory]
    [InlineData("/admin/attendance/shifts")]
    [InlineData("/admin/attendance/shift-assignments")]
    [InlineData("/attendance-review")]
    [InlineData("/attendance/daily")]
    public async Task Admin_Can_Open_Attendance_Foundation_Routes(string route)
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleNames.Manager, "/admin/attendance/shifts")]
    [InlineData(RoleNames.Manager, "/admin/attendance/shift-assignments")]
    [InlineData(RoleNames.Manager, "/attendance-review")]
    [InlineData(RoleNames.Employee, "/admin/attendance/shifts")]
    [InlineData(RoleNames.Employee, "/admin/attendance/shift-assignments")]
    [InlineData(RoleNames.Employee, "/attendance-review")]
    public async Task Non_Admin_Cannot_Open_Attendance_Management(
        string role,
        string route)
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Authenticated_Roles_Can_Open_Daily_Attendance(string role)
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);

        var response = await client.GetAsync("/attendance/daily");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Attendance_Management_Service_Resolves()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();

        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<IAttendanceManagementService>());
        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<IAttendanceReviewService>());
    }

    [Fact]
    public async Task Attendance_Review_Is_Read_Only_And_Explains_Empty_State()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = CreateClient(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();
        var dailyCount = await db.DailyAttendanceResults.CountAsync();
        var rawCount = await db.AttendanceRawEvents.CountAsync();
        var adjustmentCount = await db.AttendanceAdjustments.CountAsync();

        var html = await client.GetStringAsync("/attendance-review");

        Assert.Contains("出勤檢核", html, StringComparison.Ordinal);
        Assert.Contains("只查詢已產生的每日出勤結果", html, StringComparison.Ordinal);
        Assert.Contains("只顯示異常", html, StringComparison.Ordinal);
        Assert.DoesNotContain("重新產生出勤結果", html, StringComparison.Ordinal);
        Assert.Equal(dailyCount, await db.DailyAttendanceResults.CountAsync());
        Assert.Equal(rawCount, await db.AttendanceRawEvents.CountAsync());
        Assert.Equal(adjustmentCount, await db.AttendanceAdjustments.CountAsync());
    }

    [Fact]
    public async Task Attendance_Review_Navigation_Is_Admin_Only()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var admin = CreateClient(factory);
        using var employee = CreateClient(factory);
        employee.DefaultRequestHeaders.Add(
            TestAuthenticationHandler.RoleHeader,
            RoleNames.Employee);

        var adminHtml = await admin.GetStringAsync("/");
        var employeeHtml = await employee.GetStringAsync("/");

        Assert.Contains("href=\"/attendance-review\"", adminHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/attendance-review\"", employeeHtml,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Application_Startup_Does_Not_Create_Attendance_Results()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = CreateClient(factory);
        _ = await client.GetAsync("/");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();

        Assert.Equal(0, await db.DailyAttendanceResults.CountAsync());
        Assert.Equal(0, await db.EmployeeShiftAssignments.CountAsync());
        Assert.Equal(0, await db.AttendanceShifts.CountAsync());
    }

    [Fact]
    public async Task Daily_Page_Load_Is_Read_Only_And_Separates_Admin_Maintenance()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = CreateClient(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();
        var resultCount = await db.DailyAttendanceResults.CountAsync();
        var adjustmentCount = await db.AttendanceAdjustments.CountAsync();
        var rawCount = await db.AttendanceRawEvents.CountAsync();

        var html = await client.GetStringAsync("/attendance/daily");

        Assert.Contains("查詢僅顯示已產生的每日出勤結果", html, StringComparison.Ordinal);
        Assert.Contains("管理維護", html, StringComparison.Ordinal);
        Assert.Contains("重新產生出勤結果", html, StringComparison.Ordinal);
        Assert.Contains("不會修改原始打卡紀錄", html, StringComparison.Ordinal);
        Assert.Contains("包含停用／離職員工", html, StringComparison.Ordinal);
        Assert.Equal(resultCount, await db.DailyAttendanceResults.CountAsync());
        Assert.Equal(adjustmentCount, await db.AttendanceAdjustments.CountAsync());
        Assert.Equal(rawCount, await db.AttendanceRawEvents.CountAsync());
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Non_Admin_Daily_Page_Does_Not_Render_Maintenance_Action(
        string role)
    {
        await using var factory = new MasterDataWebApplicationFactory(services =>
        {
            services.RemoveAll<ICurrentUser>();
            services.AddSingleton<ICurrentUser>(new RoleCurrentUser(role));
        });
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);

        var html = await client.GetStringAsync("/attendance/daily");

        Assert.DoesNotContain("管理維護", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shift_Page_Offers_Explicit_Normal_Shift_Creation()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = CreateClient(factory);

        var html = await client.GetStringAsync("/admin/attendance/shifts");

        Assert.Contains("NORMAL", html, StringComparison.Ordinal);
        Assert.Contains("08:01", html, StringComparison.Ordinal);
        Assert.Contains("17:30", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Daily_Page_Labels_Raw_And_Recognized_Times_Separately()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();
            var now = new DateTimeOffset(
                2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
            var department = new Department(
                Guid.NewGuid(), "WEB-ATT", "Attendance", now);
            var employee = new Employee(
                Guid.NewGuid(),
                "EMP9801",
                "Attendance Employee",
                department.Id,
                new DateOnly(2026, 1, 1),
                now);
            var shift = new AttendanceShift(
                Guid.NewGuid(),
                "NORMAL",
                "Normal",
                new TimeOnly(8, 0),
                new TimeOnly(8, 1),
                new TimeOnly(12, 0),
                new TimeOnly(13, 30),
                new TimeOnly(17, 30),
                480,
                false,
                false,
                now);
            var workDate = new DateOnly(2026, 7, 28);
            var daily = new DailyAttendanceResult(
                Guid.NewGuid(), employee.Id, workDate, now);
            var leaveRequestId = Guid.NewGuid();
            var leaveTypeId = Guid.NewGuid();
            var calculation = AttendanceDailyCalculator.Calculate(
                workDate,
                true,
                new AttendanceShiftSnapshot(
                    shift.Id,
                    shift.Name,
                    shift.ScheduledStartTime,
                    shift.LateThresholdTime,
                    shift.LunchBreakStartTime,
                    shift.LunchBreakEndTime,
                    shift.ScheduledEndTime,
                    shift.ExpectedWorkMinutes,
                    shift.IsLunchPunchRequired,
                    shift.IsOvernightShift),
                [
                    new AttendancePunchCandidate(
                        Guid.NewGuid(),
                        workDate.ToDateTime(new TimeOnly(7, 55))),
                    new AttendancePunchCandidate(
                        Guid.NewGuid(),
                        workDate.ToDateTime(new TimeOnly(17, 35)))
                ],
                [new AttendanceApprovedLeaveInterval(
                    leaveRequestId,
                    leaveTypeId,
                    "AL",
                    "Annual Leave",
                    workDate.ToDateTime(new TimeOnly(13, 30)),
                    workDate.ToDateTime(new TimeOnly(17, 30)))]);
            daily.Recalculate(
                true,
                AttendanceCalendarClassification.FallbackWorkingDay,
                shift,
                calculation,
                "integration",
                now);
            daily.ReplaceLeaveSegments(calculation.LeaveSegments.Select(segment =>
                new DailyAttendanceLeaveSegment(
                    Guid.NewGuid(),
                    daily.Id,
                    segment.LeaveRequestId,
                    segment.LeaveTypeId,
                    segment.LeaveTypeCode,
                    segment.LeaveTypeName,
                    new DateTimeOffset(segment.StartLocal, TimeSpan.FromHours(8)),
                    new DateTimeOffset(segment.EndLocal, TimeSpan.FromHours(8)),
                    segment.CoveredMinutes,
                    now)));
            db.AddRange(department, employee, shift, daily);
            await db.SaveChangesAsync();
        }
        using var client = CreateClient(factory);

        var html = await client.GetStringAsync(
            "/attendance/daily?dateFrom=2026-07-28&dateTo=2026-07-28");

        Assert.Contains("核准請假：240 分鐘", html, StringComparison.Ordinal);
        Assert.Contains("核准請假期間有出勤紀錄", html, StringComparison.Ordinal);
        Assert.Contains("/leave-requests/", html, StringComparison.Ordinal);
        Assert.Contains("未覆蓋缺勤：0", html, StringComparison.Ordinal);
        Assert.Contains("原始打卡", html, StringComparison.Ordinal);
        Assert.Contains("認列", html, StringComparison.Ordinal);
    }

    private static HttpClient CreateClient(
        MasterDataWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private sealed class RoleCurrentUser(string role) : ICurrentUser
    {
        public string? UserId => $"integration-{role.ToLowerInvariant()}";
        public Guid? EmployeeId => null;
        public string? DisplayName => "Integration User";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string candidate) =>
            string.Equals(candidate, role, StringComparison.Ordinal);
        public bool HasPermission(string policy) =>
            RolePermissions.HasPermission([role], policy);
    }
}
