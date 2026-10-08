using Bunit;
using HRSystem.Application.Attendance;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.CompanyCalendars;
using HRSystem.Domain.Overtime;
using HRSystem.Web.Components.Shared;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Security;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AngleSharp.Html.Parser;

namespace HRSystem.IntegrationTests;

public sealed class NonWorkingPunchComponentTests : BunitContext
{
    [Theory]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.Employee)]
    [InlineData(RoleNames.Manager)]
    public async Task Daily_Page_Renders_Exact_Evidence_Without_Recognized_Time_Or_Internal_Reason(string role)
    {
        var employeeId = Guid.NewGuid();
        await using var factory = new MasterDataWebApplicationFactory(services =>
        {
            services.RemoveAll<ICurrentUser>();
            services.AddSingleton<ICurrentUser>(new DailyUser(role, employeeId));
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();
        var date = new DateOnly(2026, 8, 29);
        var now = DateTimeOffset.UtcNow;
        var department = new Department(Guid.NewGuid(), "DAILY", "測試部門", now);
        var employee = new Employee(employeeId, "DAILY001", "測試員工", department.Id, new(2026, 7, 13), now);
        var daily = new DailyAttendanceResult(Guid.NewGuid(), employee.Id, date, now);
        daily.Recalculate(false, AttendanceCalendarClassification.Weekend, null,
            AttendanceDailyCalculator.Calculate(date, false, null, []), "test", now);
        db.AddRange(department, employee, daily,
            new AttendanceRawEvent(Guid.NewGuid(), AttendanceSourceSystems.BioWebTa, 1, employee.Id, "TEST", null,
                date.ToDateTime(new(10, 9, 14)), null, null, null, now),
            new AttendanceRawEvent(Guid.NewGuid(), AttendanceSourceSystems.BioWebTa, 2, employee.Id, "TEST", null,
                date.ToDateTime(new(12, 36, 12)), null, null, null, now));
        await db.SaveChangesAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);

        var html = await client.GetStringAsync("/attendance/daily?dateFrom=2026-08-29&dateTo=2026-08-29");
        var document = new HtmlParser().ParseDocument(html);
        var row = Assert.Single(document.QuerySelectorAll("table tbody tr"));
        var cells = row.QuerySelectorAll("td");
        Assert.Contains("非工作日", cells[3].TextContent);
        Assert.Contains("10:09:14", cells[4].TextContent);
        Assert.Contains("12:36:12", cells[5].TextContent);
        Assert.Equal("—", cells[6].TextContent.Trim());
        Assert.Equal("—", cells[7].TextContent.Trim());
        Assert.Contains("非工作日有打卡", cells[8].TextContent);
        Assert.Contains("2 筆打卡", cells[8].TextContent);
        Assert.Contains("02:26:58", cells[8].TextContent);
        Assert.Contains("尚未申請", cells[8].TextContent);
        Assert.Contains("待確認", cells[8].TextContent);
        Assert.Contains("認列出勤：0", cells[10].TextContent);
        Assert.DoesNotContain("18:09:14", html);
        Assert.DoesNotContain("SourceFingerprint", html);
        Assert.Null((await db.DailyAttendanceResults.AsNoTracking().SingleAsync()).EffectiveClockInLocalTime);
        Assert.Empty(db.PayrollRuns);
        Assert.Empty(db.AttendanceReviewResolutions);
        Assert.Empty(db.OvertimeRequests);
    }

    private sealed class DailyUser(string role, Guid employeeId) : ICurrentUser
    {
        public string? UserId => "daily-component";
        public Guid? EmployeeId => role == RoleNames.Admin ? null : employeeId;
        public string? DisplayName => "測試";
        public string? IpAddress => null;
        public bool IsAuthenticated => true;
        public bool IsInRole(string candidate) => candidate == role;
        public bool HasPermission(string policy) => RolePermissions.HasPermission([role], policy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Evidence_Renders_All_Punches_Without_Payable_Claim(bool employee)
    {
        var date = new DateOnly(2026, 8, 29);
        var evidence = new NonWorkingDayPunchEvidence(false, AttendanceCalendarClassification.Weekend,
            CompanyCalendarDayType.Saturday,
            [new(Guid.NewGuid(), date.ToDateTime(new(8, 3)), new byte[32]),
             new(Guid.NewGuid(), date.ToDateTime(new(12, 6)), new byte[32])], "internal-fingerprint");
        var cut = Render<NonWorkingPunchEvidenceView>(p => p.Add(x => x.Evidence, evidence)
            .Add(x => x.EmployeeView, employee).Add(x => x.Date, date));
        Assert.Contains("非工作日有打卡", cut.Markup);
        Assert.Contains("4 小時 3 分鐘", cut.Markup);
        Assert.Contains("2 筆打卡", cut.Markup);
        Assert.Contains("需人工確認", cut.Markup);
        Assert.DoesNotContain("internal-fingerprint", cut.Markup);
        Assert.DoesNotContain("00:00", cut.Markup);
        Assert.Equal(employee, cut.Markup.Contains("僅草稿", StringComparison.Ordinal));
    }

    [Fact]
    public void Existing_Draft_Is_Linked_Instead_Of_Offering_Duplicate()
    {
        var date = new DateOnly(2026, 8, 29);
        var evidence = new NonWorkingDayPunchEvidence(false, AttendanceCalendarClassification.Weekend, null,
            [new(Guid.NewGuid(), date.ToDateTime(new(8, 3)), new byte[32])], "hidden");
        var id = Guid.NewGuid();
        var cut = Render<NonWorkingPunchEvidenceView>(p => p.Add(x => x.Evidence, evidence)
            .Add(x => x.EmployeeView, true).Add(x => x.Date, date)
            .Add(x => x.Links, new AttendanceOvertimeLink[] { new(id, OvertimeRequestStatus.Draft, 240, null) }));
        Assert.Contains(id.ToString(), cut.Markup);
        Assert.Contains("已建立加班申請", cut.Markup);
        Assert.DoesNotContain("僅草稿", cut.Markup);
    }
}
