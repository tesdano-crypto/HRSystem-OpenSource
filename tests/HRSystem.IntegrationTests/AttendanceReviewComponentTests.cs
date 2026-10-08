using Bunit;
using HRSystem.Application.Attendance;
using HRSystem.Domain.Attendance;
using HRSystem.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class AttendanceReviewComponentTests : BunitContext
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 2, 0, 0, TimeSpan.Zero);

    public AttendanceReviewComponentTests()
    {
        Services.AddFluentUIComponents();
        Services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Initial_State_Uses_Taipei_Today_And_Does_Not_Query()
    {
        var service = RegisterService();

        var cut = Render<AttendanceReview>();

        Assert.Equal(0, service.SearchCalls);
        Assert.Equal("2026-08-20", cut.Find("#review-start").GetAttribute("value"));
        Assert.Equal("2026-08-20", cut.Find("#review-end").GetAttribute("value"));
        Assert.Equal(
            AttendanceReviewSortDirection.Descending.ToString(),
            cut.Find("#review-direction").GetAttribute("value"));
        Assert.Contains("請選擇員工後查詢", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AttendanceReviewSortDirection.Ascending)]
    [InlineData(AttendanceReviewSortDirection.Descending)]
    public async Task Sort_Direction_Selection_Is_Rendered_And_Passed_To_Query(
        AttendanceReviewSortDirection direction)
    {
        var service = RegisterService();
        var cut = Render<AttendanceReview>();
        cut.Find("#review-direction").Change(direction.ToString());
        await ClickButtonAsync(cut, "全選目前部門");

        await ClickButtonAsync(cut, "查詢");

        Assert.Equal(
            direction.ToString(),
            cut.Find("#review-direction").GetAttribute("value"));
        Assert.Equal(direction, Assert.Single(service.Queries).SortDirection);
    }

    [Fact]
    public void Department_Filter_Changes_Visible_Employee_Options()
    {
        RegisterService();
        var cut = Render<AttendanceReview>();

        cut.Find("#review-department").Change(Department2Id.ToString());

        Assert.DoesNotContain("EMP9201", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("EMP9203", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Select_Current_Department_Selects_Multiple_Employees()
    {
        RegisterService();
        var cut = Render<AttendanceReview>();

        await ClickButtonAsync(cut, "全選目前部門");

        Assert.Contains("已選 3 位", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clear_All_Removes_The_Multi_Selection()
    {
        RegisterService();
        var cut = Render<AttendanceReview>();
        await ClickButtonAsync(cut, "全選目前部門");

        await ClickButtonAsync(cut, "清除全部");

        Assert.Contains("已選 0 位", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_Passes_All_Selected_Employee_Ids()
    {
        var service = RegisterService();
        var cut = Render<AttendanceReview>();
        await ClickButtonAsync(cut, "全選目前部門");

        await ClickButtonAsync(cut, "查詢");

        var query = Assert.Single(service.Queries);
        Assert.Equal(3, query.EmployeeIds.Count);
        Assert.Equal(new DateOnly(2026, 8, 20), query.StartDate);
    }

    [Fact]
    public async Task Export_Uses_Current_Filters_And_Selected_Employees()
    {
        RegisterService();
        var cut = Render<AttendanceReview>();
        cut.Find("#review-quick-filter").Change(
            AttendanceReviewQuickFilter.MissingPunch.ToString());
        var anomalyLabel = cut.FindAll("label").Single(item =>
            item.TextContent.Contains("只顯示異常", StringComparison.Ordinal));
        anomalyLabel.QuerySelector("input")!.Change(true);
        await ClickButtonAsync(cut, "全選目前部門");

        await ClickButtonAsync(cut, "匯出 Excel");

        var invocation = Assert.Single(JSInterop.Invocations, item =>
            item.Identifier == "hrSystemDownloads.download");
        var url = Assert.IsType<string>(invocation.Arguments[0]);
        Assert.Contains("startDate=2026-08-20", url, StringComparison.Ordinal);
        Assert.Contains("endDate=2026-08-20", url, StringComparison.Ordinal);
        Assert.Contains("quickFilter=MissingPunch", url, StringComparison.Ordinal);
        Assert.Contains("onlyAnomalies=true", url, StringComparison.Ordinal);
        Assert.Equal(3, url.Split("employeeId=", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task Search_Without_Employee_Shows_Server_Validation()
    {
        RegisterService(rejectEmpty: true);
        var cut = Render<AttendanceReview>();

        await ClickButtonAsync(cut, "查詢");

        Assert.Contains("請至少選擇一位員工", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_Anomalies_Checkbox_Is_Passed_To_Server_Query()
    {
        var service = RegisterService();
        var cut = Render<AttendanceReview>();
        var label = cut.FindAll("label").Single(item =>
            item.TextContent.Contains("只顯示異常", StringComparison.Ordinal));
        label.QuerySelector("input")!.Change(true);
        await ClickButtonAsync(cut, "全選目前部門");

        await ClickButtonAsync(cut, "查詢");

        Assert.True(Assert.Single(service.Queries).OnlyAnomalies);
    }

    [Fact]
    public async Task Only_Anomalies_Refresh_Preserves_Sort_Direction()
    {
        var service = RegisterService();
        var cut = Render<AttendanceReview>();
        cut.Find("#review-direction").Change(
            AttendanceReviewSortDirection.Ascending.ToString());
        var label = cut.FindAll("label").Single(item =>
            item.TextContent.Contains("只顯示異常", StringComparison.Ordinal));
        label.QuerySelector("input")!.Change(true);
        await ClickButtonAsync(cut, "全選目前部門");

        await ClickButtonAsync(cut, "查詢");

        var query = Assert.Single(service.Queries);
        Assert.True(query.OnlyAnomalies);
        Assert.Equal(AttendanceReviewSortDirection.Ascending, query.SortDirection);
        Assert.Equal(
            AttendanceReviewSortDirection.Ascending.ToString(),
            cut.Find("#review-direction").GetAttribute("value"));
    }

    [Fact]
    public async Task Only_Pending_Checkbox_Is_Passed_To_Server_Query()
    {
        var service = RegisterService();
        var cut = Render<AttendanceReview>();
        var label = cut.FindAll("label").Single(item =>
            item.TextContent.Contains("只顯示待確認", StringComparison.Ordinal));
        label.QuerySelector("input")!.Change(true);
        await ClickButtonAsync(cut, "全選目前部門");

        await ClickButtonAsync(cut, "查詢");

        Assert.True(Assert.Single(service.Queries).OnlyPending);
    }

    [Theory]
    [InlineData(AttendanceReviewQuickFilter.ExtendedStay)]
    [InlineData(AttendanceReviewQuickFilter.PotentialUnreportedOvertime)]
    public async Task Overstay_Quick_Filter_Is_Passed_To_Server_Query(
        AttendanceReviewQuickFilter filter)
    {
        var service = RegisterService();
        var cut = Render<AttendanceReview>();
        cut.Find("#review-quick-filter").Change(filter.ToString());
        await ClickButtonAsync(cut, "全選目前部門");

        await ClickButtonAsync(cut, "查詢");

        Assert.Equal(filter, Assert.Single(service.Queries).QuickFilter);
    }

    [Fact]
    public async Task Result_Renders_Red_Recognized_Times_And_Missing_Punch_Text()
    {
        RegisterService(Result());
        var cut = await RenderAndSearchAsync();

        var redTimes = cut.FindAll(".anomaly-time");
        Assert.Contains(redTimes, item => item.TextContent.Contains("08:30", StringComparison.Ordinal));
        Assert.Contains(redTimes, item => item.TextContent.Contains("17:00", StringComparison.Ordinal));
        Assert.Contains("下班缺卡", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(">00:00<", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Result_Renders_Extended_And_Critical_Overstay_Emphasis()
    {
        RegisterService(Result());
        var cut = await RenderAndSearchAsync();

        Assert.Contains(cut.FindAll(".overstay-warning"), item =>
            item.TextContent.Contains("18:20", StringComparison.Ordinal));
        Assert.Contains(cut.FindAll(".overstay-warning"), item =>
            item.TextContent.Contains("超時 50 分", StringComparison.Ordinal));
        Assert.Contains(cut.FindAll(".anomaly-time"), item =>
            item.TextContent.Contains("19:48", StringComparison.Ordinal));
        Assert.Contains(cut.FindAll(".anomaly-time"), item =>
            item.TextContent.Contains("超時 2 小時 18 分", StringComparison.Ordinal));
        Assert.Contains("延後下班", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("疑似未申報加班", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_ClockOut_Does_Not_Render_Overstay()
    {
        RegisterService(Result());
        var cut = await RenderAndSearchAsync();

        var row = cut.Find(".review-grid").QuerySelectorAll("tbody tr")
            .Single(item => item.TextContent.Contains("EMP9202", StringComparison.Ordinal));
        Assert.Contains("下班缺卡", row.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("超時", row.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Result_Renders_Multiple_Engine_Status_Badges()
    {
        RegisterService(Result());
        var cut = await RenderAndSearchAsync();

        Assert.Contains("遲到", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("早退", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("部分請假", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("請假期間出勤", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Result_Renders_Leave_Exemption_Suspension_And_Nonworking_Statuses()
    {
        RegisterService(Result());
        var cut = await RenderAndSearchAsync();

        Assert.Contains("全日請假", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("出勤豁免", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("留職停薪", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("非工作日", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Result_Renders_Summary_And_Employee_Summary()
    {
        RegisterService(Result());
        var cut = await RenderAndSearchAsync();

        Assert.Contains("員工摘要", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("結果", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("異常", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("EMP9201 — 測試甲", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_Link_Reuses_Daily_Attendance_Route()
    {
        RegisterService(Result());
        var cut = await RenderAndSearchAsync();

        var link = cut.FindAll("a").Single(anchor =>
            anchor.TextContent == "查看" &&
            anchor.GetAttribute("href")!.Contains(Result1Id.ToString(),
                StringComparison.Ordinal));
        Assert.Equal(
            $"/attendance/daily?dateFrom=2026-08-20&dateTo=2026-08-20&employeeId={Employee1Id}&resultId={Result1Id}",
            link.GetAttribute("href"));
    }

    [Fact]
    public async Task Pending_Anomaly_Opens_Typed_Resolution_Dialog()
    {
        RegisterService(Result());
        var cut = await RenderAndSearchAsync();

        await ClickButtonAsync(cut, "處理");

        Assert.Contains("出勤異常處理", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("結案原因", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("確認出勤狀況", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_Button_Sends_Typed_Reason_And_Current_Fingerprint()
    {
        var service = RegisterService(Result());
        var cut = await RenderAndSearchAsync();
        await ClickButtonAsync(cut, "處理");
        cut.Find("#review-resolution-reason").Change(
            AttendanceReviewResolutionReason.IncorrectPunch.ToString());

        await ClickButtonAsync(cut, "確認結案");

        var request = Assert.Single(service.ResolveRequests);
        Assert.Equal(AttendanceReviewResolutionReason.IncorrectPunch, request.Reason);
        Assert.Equal(Convert.ToBase64String(new byte[32]), request.SourceFingerprint);
    }

    [Fact]
    public async Task Resolve_Note_Input_Is_Sent_Without_Blur()
    {
        const string note = "Phase F.2 production UAT";
        var service = RegisterService(Result());
        var cut = await RenderAndSearchAsync();
        await ClickButtonAsync(cut, "處理");

        cut.Find("#review-resolution-note").Input(note);
        await ClickButtonAsync(cut, "確認結案");

        Assert.Equal(note, Assert.Single(service.ResolveRequests).Note);
    }

    [Fact]
    public async Task Other_Reason_Note_Input_Is_Sent_Without_Blur()
    {
        const string note = "其他原因說明";
        var service = RegisterService(Result());
        var cut = await RenderAndSearchAsync();
        await ClickButtonAsync(cut, "處理");
        cut.Find("#review-resolution-reason").Change(
            AttendanceReviewResolutionReason.Other.ToString());

        cut.Find("#review-resolution-note").Input(note);
        await ClickButtonAsync(cut, "確認結案");

        var request = Assert.Single(service.ResolveRequests);
        Assert.Equal(AttendanceReviewResolutionReason.Other, request.Reason);
        Assert.Equal(note, request.Note);
    }

    [Fact]
    public async Task Resolution_Note_Field_Preserves_Max_Length()
    {
        RegisterService(Result());
        var cut = await RenderAndSearchAsync();
        await ClickButtonAsync(cut, "處理");

        Assert.Equal("1000",
            cut.Find("#review-resolution-note").GetAttribute("maxlength"));
    }

    [Fact]
    public async Task Resolved_Overtime_Keeps_Red_ClockOut_And_Caveat()
    {
        var result = Result();
        var overtime = result.Items.Last() with
        {
            ReviewItems =
            [
                new AttendanceReviewAnomalyDto(
                    AttendanceReviewAnomalyType.PotentialUnreportedOvertime,
                    138,
                    Convert.ToBase64String(new byte[32]),
                    AttendanceReviewState.Resolved,
                    Guid.NewGuid(),
                    AttendanceReviewResolutionReason.ConfirmedOvertimeWork,
                    null,
                    Convert.ToBase64String([1]))
            ]
        };
        result = result with { Items = [.. result.Items.Take(result.Items.Count - 1), overtime] };
        RegisterService(result);

        var cut = await RenderAndSearchAsync();

        Assert.Contains(cut.FindAll(".anomaly-time"), item =>
            item.TextContent.Contains("19:48", StringComparison.Ordinal));
        Assert.Contains("已結案", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolved_Anomaly_Can_Reopen_And_Show_History()
    {
        var result = Result();
        var resolutionId = Guid.NewGuid();
        var resolved = result.Items[0] with
        {
            ReviewItems =
            [
                new AttendanceReviewAnomalyDto(
                    AttendanceReviewAnomalyType.Late,
                    30,
                    Convert.ToBase64String(new byte[32]),
                    AttendanceReviewState.Resolved,
                    resolutionId,
                    AttendanceReviewResolutionReason.ConfirmedAttendance,
                    null,
                    Convert.ToBase64String([1]))
            ]
        };
        result = result with { Items = [resolved, .. result.Items.Skip(1)] };
        var service = RegisterService(result);
        var cut = await RenderAndSearchAsync();

        await ClickButtonAsync(cut, "歷程");
        Assert.Contains("建立結案紀錄", cut.Markup, StringComparison.Ordinal);
        await ClickButtonAsync(cut, "重新開啟");
        Assert.Contains("重新開啟出勤異常", cut.Markup, StringComparison.Ordinal);
        await ClickButtonAsync(cut, "確認重新開啟");

        Assert.Equal(resolutionId, Assert.Single(service.ReopenRequests).ResolutionId);
    }

    [Fact]
    public async Task Reopen_Note_Input_Is_Sent_Without_Blur()
    {
        const string note = "Phase F.2 production UAT reopen";
        var result = Result();
        var resolutionId = Guid.NewGuid();
        var resolved = result.Items[0] with
        {
            ReviewItems =
            [
                new AttendanceReviewAnomalyDto(
                    AttendanceReviewAnomalyType.Late,
                    30,
                    Convert.ToBase64String(new byte[32]),
                    AttendanceReviewState.Resolved,
                    resolutionId,
                    AttendanceReviewResolutionReason.ConfirmedAttendance,
                    null,
                    Convert.ToBase64String([1]))
            ]
        };
        result = result with { Items = [resolved, .. result.Items.Skip(1)] };
        var service = RegisterService(result);
        var cut = await RenderAndSearchAsync();
        await ClickButtonAsync(cut, "重新開啟");

        cut.Find("#review-resolution-note").Input(note);
        await ClickButtonAsync(cut, "確認重新開啟");

        var request = Assert.Single(service.ReopenRequests);
        Assert.Equal(resolutionId, request.ResolutionId);
        Assert.Equal(note, request.Note);
    }

    private async Task<IRenderedComponent<AttendanceReview>> RenderAndSearchAsync()
    {
        var cut = Render<AttendanceReview>();
        await ClickButtonAsync(cut, "全選目前部門");
        await ClickButtonAsync(cut, "查詢");
        return cut;
    }

    private static Task ClickButtonAsync(
        IRenderedComponent<AttendanceReview> cut,
        string text)
    {
        var button = cut.FindComponents<FluentButton>().Single(component =>
            component.Markup.Contains(text, StringComparison.Ordinal));
        return cut.InvokeAsync(() => button.Instance.OnClick.InvokeAsync());
    }

    private RecordingReviewService RegisterService(
        AttendanceReviewResult? result = null,
        bool rejectEmpty = false)
    {
        var service = new RecordingReviewService(result, rejectEmpty);
        Services.AddSingleton<IAttendanceReviewService>(service);
        return service;
    }

    private static AttendanceReviewResult Result()
    {
        var row = new AttendanceReviewRowDto(
            Result1Id,
            Employee1Id,
            "EMP9201",
            "測試甲",
            "行政部",
            new DateOnly(2026, 8, 20),
            true,
            "WorkingDay",
            "NORMAL",
            "正常班",
            new TimeOnly(17, 30),
            false,
            new DateTime(2026, 8, 20, 8, 30, 0),
            new DateTime(2026, 8, 20, 17, 0, 0),
            "LateAndEarlyLeave",
            true,
            true,
            false,
            false,
            30,
            30,
            60,
            420,
            300,
            120,
            15,
            "Partial",
            false,
            false,
            0,
            [new AttendanceReviewLeaveDto(Guid.NewGuid(), "ANNUAL", "特休", 60)])
        {
            ReviewItems =
            [
                new AttendanceReviewAnomalyDto(
                    AttendanceReviewAnomalyType.Late,
                    30,
                    Convert.ToBase64String(new byte[32]),
                    AttendanceReviewState.Pending,
                    null,
                    null,
                    null,
                    null)
            ]
        };
        var missing = row with
        {
            DailyAttendanceResultId = Guid.Parse(
                "30000000-0000-0000-0000-000000000002"),
            EmployeeId = Guid.Parse("20000000-0000-0000-0000-000000000002"),
            EmployeeNumber = "EMP9202",
            EmployeeName = "測試乙",
            EffectiveClockInLocalTime = new DateTime(2026, 8, 20, 8, 0, 0),
            EffectiveClockOutLocalTime = null,
            Status = "MissingClockOut",
            IsLate = false,
            IsEarlyLeave = false,
            MissingClockOut = true,
            LateMinutes = 0,
            EarlyLeaveMinutes = 0,
            ApprovedLeaveMinutes = 0,
            MissingMinutes = 480,
            WorkedDuringApprovedLeaveMinutes = 0,
            LeaveCoverageStatus = "None",
            LeaveItems = [],
            ReviewItems = []
        };
        var fullLeave = row with
        {
            DailyAttendanceResultId = Guid.NewGuid(),
            EffectiveClockInLocalTime = null,
            EffectiveClockOutLocalTime = null,
            Status = "Normal",
            IsLate = false,
            IsEarlyLeave = false,
            LateMinutes = 0,
            EarlyLeaveMinutes = 0,
            ApprovedLeaveMinutes = 480,
            RequiredAttendanceMinutes = 0,
            RecognizedWorkMinutes = 0,
            MissingMinutes = 0,
            WorkedDuringApprovedLeaveMinutes = 0,
            LeaveCoverageStatus = "Full",
            ReviewItems = []
        };
        var exempted = fullLeave with
        {
            DailyAttendanceResultId = Guid.NewGuid(),
            ApprovedLeaveMinutes = 0,
            LeaveCoverageStatus = "None",
            IsAttendanceExempted = true,
            AttendanceExceptionMinutes = 480,
            LeaveItems = []
        };
        var suspended = exempted with
        {
            DailyAttendanceResultId = Guid.NewGuid(),
            IsAttendanceExempted = false,
            AttendanceExceptionMinutes = 0,
            IsEmploymentSuspended = true
        };
        var nonworking = suspended with
        {
            DailyAttendanceResultId = Guid.NewGuid(),
            IsEmploymentSuspended = false,
            IsRequiredWorkday = false,
            Status = "RestDay"
        };
        var extendedStay = row with
        {
            DailyAttendanceResultId = Guid.NewGuid(),
            EffectiveClockInLocalTime = new DateTime(2026, 8, 20, 8, 0, 0),
            EffectiveClockOutLocalTime = new DateTime(2026, 8, 20, 18, 20, 0),
            Status = "Normal",
            IsLate = false,
            IsEarlyLeave = false,
            LateMinutes = 0,
            EarlyLeaveMinutes = 0,
            ApprovedLeaveMinutes = 0,
            MissingMinutes = 0,
            WorkedDuringApprovedLeaveMinutes = 0,
            LeaveCoverageStatus = "None",
            LeaveItems = [],
            ReviewItems = []
        };
        var potentialOvertime = extendedStay with
        {
            DailyAttendanceResultId = Guid.NewGuid(),
            EffectiveClockOutLocalTime = new DateTime(2026, 8, 20, 19, 48, 0)
        };
        return new AttendanceReviewResult(
            [row, missing, fullLeave, exempted, suspended, nonworking,
                extendedStay, potentialOvertime],
            new AttendanceReviewSummary(
                8, 2, 4, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1),
            [new AttendanceReviewEmployeeSummary(
                Employee1Id, "EMP9201", "測試甲", 7, 4, 3, 1, 1, 0, 1, 1),
             new AttendanceReviewEmployeeSummary(
                missing.EmployeeId, "EMP9202", "測試乙", 1, 0, 1, 0, 0, 1, 0, 0)]);
    }

    private static readonly Guid Department1Id = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Department2Id = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Employee1Id = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid Result1Id = Guid.Parse("30000000-0000-0000-0000-000000000001");

    private sealed class RecordingReviewService(
        AttendanceReviewResult? result,
        bool rejectEmpty) : IAttendanceReviewService
    {
        public int SearchCalls { get; private set; }
        public List<AttendanceReviewQuery> Queries { get; } = [];
        public List<ResolveAttendanceReviewRequest> ResolveRequests { get; } = [];
        public List<ReopenAttendanceReviewRequest> ReopenRequests { get; } = [];

        public Task<AttendanceReviewFilterOptions> GetFilterOptionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AttendanceReviewFilterOptions(
                [
                    new(Department1Id, "ADM", "行政部"),
                    new(Department2Id, "SAL", "業務部")
                ],
                [
                    new(Employee1Id, "EMP9201", "測試甲", Department1Id, "行政部"),
                    new(Guid.Parse("20000000-0000-0000-0000-000000000002"),
                        "EMP9202", "測試乙", Department1Id, "行政部"),
                    new(Guid.Parse("20000000-0000-0000-0000-000000000003"),
                        "EMP9203", "測試丙", Department2Id, "業務部")
                ]));

        public Task<AttendanceReviewResult> SearchAsync(
            AttendanceReviewQuery query,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            Queries.Add(query);
            if (rejectEmpty && query.EmployeeIds.Count == 0)
            {
                throw new HRSystem.Application.Common.Exceptions.ApplicationValidationException(
                    "請至少選擇一位員工。");
            }

            return Task.FromResult(result ?? new AttendanceReviewResult(
                [],
                new AttendanceReviewSummary(
                    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                []));
        }

        public Task<AttendanceReviewResolutionResult> ResolveAsync(
            ResolveAttendanceReviewRequest request,
            CancellationToken cancellationToken = default)
        {
            ResolveRequests.Add(request);
            return Task.FromResult(new AttendanceReviewResolutionResult(
                Guid.NewGuid(), AttendanceReviewResolutionStatus.Resolved,
                Convert.ToBase64String([])));
        }

        public Task<AttendanceReviewResolutionResult> ReopenAsync(
            ReopenAttendanceReviewRequest request,
            CancellationToken cancellationToken = default)
        {
            ReopenRequests.Add(request);
            return Task.FromResult(new AttendanceReviewResolutionResult(
                request.ResolutionId, AttendanceReviewResolutionStatus.Reopened,
                Convert.ToBase64String([])));
        }

        public Task<IReadOnlyList<AttendanceReviewResolutionHistoryDto>>
            GetHistoryAsync(Guid resolutionId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AttendanceReviewResolutionHistoryDto>>(
            [
                new(AttendanceReviewResolutionHistoryAction.Created, null,
                    AttendanceReviewResolutionStatus.Resolved, null, null,
                    "unit-test-user", Now)
            ]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
