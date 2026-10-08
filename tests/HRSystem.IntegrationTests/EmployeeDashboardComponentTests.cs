using Bunit;
using HRSystem.Application.Attendance;
using HRSystem.Application.Dashboard;
using HRSystem.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class EmployeeDashboardComponentTests : BunitContext
{
    public EmployeeDashboardComponentTests()
    {
        Services.AddFluentUIComponents();
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Unbound_User_Sees_Safe_Message_Without_Admin_Data()
    {
        Register(Empty(false));
        var cut = Render<EmployeeHome>();
        Assert.Contains("目前帳號尚未綁定員工資料", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("出勤匯入", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/employees\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_Renders_Required_Self_Service_Sections_And_Links()
    {
        Register(Empty(true));
        var cut = Render<EmployeeHome>();
        foreach (var text in new[] { "我的首頁", "今日出勤", "需要我處理", "等待審核／確認", "本月摘要", "最近 7 個工作日" })
            Assert.Contains(text, cut.Markup, StringComparison.Ordinal);
        foreach (var href in new[] { "/my-attendance", "/my-overtime", "/my-attendance-requests", "/leave-requests" })
            Assert.Contains($"href=\"{href}\"", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/admin", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Today_In_Progress_Is_Not_Rendered_As_Historical_Missing()
    {
        var dto = Empty(true) with
        {
            TodayAttendance = new EmployeeDashboardTodayDto(Guid.NewGuid(), "08:00–17:30",
                new DateTime(2026, 8, 24, 8, 0, 0), null, "已上班，尚未下班",
                null, null, true, false, "/my-attendance/x")
        };
        Register(dto);
        var cut = Render<EmployeeHome>();
        Assert.Contains("已上班，尚未下班", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("尚未下班", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("下班缺卡", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Tasks_And_Recent_Attendance_Render_Employee_Public_Data_Only()
    {
        var row = EmployeeDashboardServiceTestsAccessor.Row(new DateOnly(2026, 8, 22));
        var dto = Empty(true) with
        {
            NeedsAction = [new("出勤", "缺卡待處理", "下班缺卡", row.WorkDate, "/my-attendance-requests")],
            RecentAttendance = [row]
        };
        Register(dto);
        var cut = Render<EmployeeHome>();
        Assert.Contains("缺卡待處理", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("fingerprint", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RowVersion", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Stale_Recognition_Renders_Employee_Safe_Needs_Review_Status()
    {
        var dto = Empty(true) with
        {
            WaitingReview =
            [
                new("加班", "加班申請", "出勤資料已變更，待重新確認",
                    new DateOnly(2026, 8, 21), "/my-overtime")
            ],
            WorkflowSummary = new(0, 0, 1, 0, 1, 0,
                0, 0, 0, 0, 0, 0)
        };
        Register(dto);

        var cut = Render<EmployeeHome>();

        Assert.Contains("出勤資料已變更，待重新確認", cut.Markup,
            StringComparison.Ordinal);
        Assert.Contains("需重新確認 1", cut.Markup,
            StringComparison.Ordinal);
        Assert.Contains("已認列 0 分鐘", cut.Markup,
            StringComparison.Ordinal);
        Assert.DoesNotContain("fingerprint", cut.Markup,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Service_Failure_Shows_Safe_Error_State()
    {
        Services.AddSingleton<IEmployeeDashboardService>(new FailingService());
        var cut = Render<EmployeeHome>();
        Assert.Contains("個人首頁暫時無法載入", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", cut.Markup, StringComparison.Ordinal);
    }

    private void Register(EmployeeDashboardDto dto) =>
        Services.AddSingleton<IEmployeeDashboardService>(new FakeService(dto));

    private static EmployeeDashboardDto Empty(bool bound) => new(
        bound, bound ? "測試員工" : null, new DateOnly(2026, 8, 24), null,
        [], [], [], [], new(0, 0, 0, 0, 0, 0), new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), [],
        [new("查看我的出勤", "/my-attendance"), new("申請加班", "/my-overtime"),
         new("出勤更正申請", "/my-attendance-requests"), new("新增請假", "/leave-requests")]);

    private sealed class FakeService(EmployeeDashboardDto dto) : IEmployeeDashboardService
    { public Task<EmployeeDashboardDto> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(dto); }
    private sealed class FailingService : IEmployeeDashboardService
    { public Task<EmployeeDashboardDto> GetAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("private diagnostic"); }

    private static class EmployeeDashboardServiceTestsAccessor
    {
        public static MyAttendanceRowDto Row(DateOnly date) => new(
            Guid.NewGuid(), date, true, "Workday", "正常班", new TimeOnly(8, 0), new TimeOnly(17, 30),
            date.ToDateTime(new TimeOnly(8, 0)), date.ToDateTime(new TimeOnly(17, 30)), "Normal",
            false, false, false, false, 0, 0, 0, 480, 480, 0, 0, "None", false, false, 0, [],
            new AttendanceReviewOvertimeRequestDto(AttendanceReviewOvertimeRequestState.NoRequest, 0, 0, 0, null),
            [], 0, AttendanceReviewOverstayLevel.None, false);
    }
}
