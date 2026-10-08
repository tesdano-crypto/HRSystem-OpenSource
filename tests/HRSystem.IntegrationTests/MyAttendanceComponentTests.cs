using Bunit;
using HRSystem.Application.Attendance;
using HRSystem.Domain.Attendance;
using HRSystem.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class MyAttendanceComponentTests : BunitContext
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly WorkDate = new(2026, 8, 20);

    public MyAttendanceComponentTests()
    {
        Services.AddFluentUIComponents();
        Services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Initial_Query_Uses_Current_Month_And_Has_No_EmployeeId_Input()
    {
        var service = Register(Result(BaseRow()));

        var cut = Render<MyAttendance>();

        Assert.Equal("2026-08-01", cut.Find("#my-attendance-start").GetAttribute("value"));
        Assert.Equal("2026-08-31", cut.Find("#my-attendance-end").GetAttribute("value"));
        Assert.Equal(AttendanceReviewSortDirection.Descending,
            Assert.Single(service.Queries).SortDirection);
        Assert.Null(typeof(MyAttendanceQuery).GetProperty("EmployeeId"));
        Assert.DoesNotContain("employeeId", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unbound_User_Sees_Safe_Message_And_No_Data()
    {
        Register(new MyAttendanceResult(false, [], Summary([])));

        var cut = Render<MyAttendance>();

        Assert.Contains("目前帳號尚未綁定員工資料，無法查看個人出勤", cut.Markup,
            StringComparison.Ordinal);
        Assert.DoesNotContain("查看加班申請", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Anomaly_And_Missing_Punch_Render_Without_Zero_Time()
    {
        var late = BaseRow() with
        {
            IsLate = true,
            LateMinutes = 6,
            EffectiveClockInLocalTime = new DateTime(2026, 8, 20, 8, 7, 0),
            IsAnomaly = true
        };
        var missing = BaseRow() with
        {
            DailyAttendanceResultId = Guid.NewGuid(),
            MissingClockIn = true,
            MissingClockOut = true,
            EffectiveClockInLocalTime = null,
            EffectiveClockOutLocalTime = null,
            IsAnomaly = true
        };
        Register(Result(late, missing));

        var cut = Render<MyAttendance>();

        Assert.Contains("遲到 6 分鐘", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("上下班缺卡", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("00:00", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("anomaly-time", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("確認結案", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("重新開啟", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AttendanceReviewOverstayLevel.ExtendedStay, "延後下班 31 分鐘")]
    [InlineData(AttendanceReviewOverstayLevel.PotentialUnreportedOvertime,
        "疑似未申報加班 120 分鐘")]
    public void Overstay_Uses_Employee_Friendly_Wording(
        AttendanceReviewOverstayLevel level, string expected)
    {
        var minutes = level == AttendanceReviewOverstayLevel.ExtendedStay ? 31 : 120;
        Register(Result(BaseRow() with
        {
            OverstayLevel = level,
            OverstayMinutes = minutes,
            IsAnomaly = true
        }));

        var cut = Render<MyAttendance>();

        Assert.Contains(expected, cut.Markup, StringComparison.Ordinal);
        Assert.Contains("不代表已認定加班", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Recognition_Zero_And_Public_Review_State_Are_Not_Hidden()
    {
        var row = BaseRow() with
        {
            OvertimeRequest = new AttendanceReviewOvertimeRequestDto(
                AttendanceReviewOvertimeRequestState.RecognitionConfirmed,
                0, 120, 180, Guid.NewGuid())
            {
                RecognizedMinutes = 0
            },
            ReviewItems =
            [
                new(AttendanceReviewAnomalyType.PotentialUnreportedOvertime,
                    120, AttendanceReviewState.NeedsReview,
                    "出勤狀態待管理人員重新確認")
            ]
        };
        Register(Result(row));

        var cut = Render<MyAttendance>();

        Assert.Contains("核准 180 分鐘／實際認列 0 分鐘", cut.Markup,
            StringComparison.Ordinal);
        Assert.Contains("出勤狀態待管理人員重新確認", cut.Markup,
            StringComparison.Ordinal);
        Assert.DoesNotContain("fingerprint", cut.Markup,
            StringComparison.OrdinalIgnoreCase);
        Assert.Null(typeof(MyAttendanceReviewItemDto).GetProperty("Note"));
    }

    [Fact]
    public void Missing_Punch_And_Late_Early_Show_Scoped_Correction_Actions()
    {
        var row = BaseRow() with
        {
            MissingClockIn = true,
            MissingClockOut = true,
            EffectiveClockInLocalTime = null,
            EffectiveClockOutLocalTime = null,
            IsLate = true,
            IsEarlyLeave = true,
            IsAnomaly = true
        };
        Register(Result(row));
        var cut = Render<MyAttendance>();
        Assert.Contains("申請補卡", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("提交遲到說明", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("提交早退說明", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Normal_Day_Has_No_Correction_Action()
    {
        Register(Result(BaseRow()));
        var cut = Render<MyAttendance>();
        Assert.DoesNotContain("申請補卡", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("提交遲到說明", cut.Markup,
            StringComparison.Ordinal);
        Assert.DoesNotContain("更正上班時間", cut.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Potential_Overtime_Routes_To_Overtime_With_Prefill()
    {
        Register(Result(BaseRow() with
        {
            OverstayLevel = AttendanceReviewOverstayLevel
                .PotentialUnreportedOvertime,
            OverstayMinutes = 120,
            IsAnomaly = true
        }));
        var cut = Render<MyAttendance>();
        Assert.Contains("補加班申請", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("/my-overtime?workDate=2026-08-20", cut.Markup,
            StringComparison.Ordinal);
        Assert.Contains("start=2026-08-20T17:30", cut.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Correction_Public_Status_Is_Shown_Without_Internal_Note()
    {
        Register(Result(BaseRow() with
        {
            CorrectionRequests =
            [
                new(Guid.NewGuid(),
                    AttendanceCorrectionRequestType.LateExplanation,
                    AttendanceCorrectionRequestStatus.Approved,
                    "說明已核准")
            ]
        }));
        var cut = Render<MyAttendance>();
        Assert.Contains("說明已核准", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ReviewerNote", cut.Markup,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Filter_And_Sort_Are_Typed_And_Preserved_On_Search()
    {
        var service = Register(Result(BaseRow()));
        var cut = Render<MyAttendance>();
        cut.Find("#my-attendance-filter").Change(
            MyAttendanceQuickFilter.MissingPunch.ToString());
        cut.Find("#my-attendance-direction").Change(
            AttendanceReviewSortDirection.Ascending.ToString());

        await ClickButtonAsync(cut, "查詢");

        var query = service.Queries.Last();
        Assert.Equal(MyAttendanceQuickFilter.MissingPunch, query.QuickFilter);
        Assert.Equal(AttendanceReviewSortDirection.Ascending, query.SortDirection);
        Assert.Equal(AttendanceReviewSortDirection.Ascending.ToString(),
            cut.Find("#my-attendance-direction").GetAttribute("value"));
    }

    [Fact]
    public void Detail_Tampering_Returns_Safe_Not_Found_State()
    {
        var service = Register(Result(BaseRow()));
        service.DetailResult = new MyAttendanceDetailResult(true, null);
        var id = Guid.NewGuid();

        var cut = Render<MyAttendanceDetail>(parameters =>
            parameters.Add(component => component.ResultId, id));

        Assert.Equal(id, service.DetailIds.Single());
        Assert.Contains("不屬於目前登入員工", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("人工調整", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Own_Detail_Renders_Final_Data_Without_Admin_Controls()
    {
        var service = Register(Result(BaseRow()));
        var row = BaseRow() with
        {
            IsLate = true,
            LateMinutes = 6,
            IsAnomaly = true,
            OvertimeRequest = new AttendanceReviewOvertimeRequestDto(
                AttendanceReviewOvertimeRequestState.RecognitionConfirmed,
                120, 137, 180, Guid.NewGuid())
            {
                RecognizedMinutes = 137
            },
            ReviewItems =
            [
                new(AttendanceReviewAnomalyType.Late, 6,
                    AttendanceReviewState.Resolved, "已處理")
            ]
        };
        service.DetailResult = new MyAttendanceDetailResult(true, row);

        var cut = Render<MyAttendanceDetail>(parameters =>
            parameters.Add(component => component.ResultId,
                row.DailyAttendanceResultId));

        Assert.Contains("08:00–17:30", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("遲到 6 分鐘", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("核准 180 分鐘／實際認列 137 分鐘", cut.Markup,
            StringComparison.Ordinal);
        Assert.Contains("已處理", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("確認結案", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("重新開啟", cut.Markup, StringComparison.Ordinal);
    }

    private RecordingService Register(MyAttendanceResult result)
    {
        var service = new RecordingService(result);
        Services.AddSingleton<IAttendanceReviewService>(service);
        return service;
    }

    private static Task ClickButtonAsync(IRenderedComponent<MyAttendance> cut,
        string text)
    {
        var button = cut.FindComponents<FluentButton>().Single(component =>
            component.Markup.Contains(text, StringComparison.Ordinal));
        return cut.InvokeAsync(() => button.Instance.OnClick.InvokeAsync());
    }

    private static MyAttendanceResult Result(params MyAttendanceRowDto[] rows) =>
        new(true, rows, Summary(rows));

    private static MyAttendanceSummary Summary(
        IReadOnlyCollection<MyAttendanceRowDto> rows) => new(
            rows.Count,
            rows.Count(item => item.IsRequiredWorkday),
            rows.Count(item => !item.IsAnomaly),
            rows.Count(item => item.IsAnomaly),
            rows.Count(item => item.IsLate),
            rows.Count(item => item.IsEarlyLeave),
            rows.Count(item => item.MissingClockIn || item.MissingClockOut),
            rows.Count(item => item.OverstayLevel ==
                AttendanceReviewOverstayLevel.ExtendedStay),
            rows.Count(item => item.OverstayLevel ==
                AttendanceReviewOverstayLevel.PotentialUnreportedOvertime),
            rows.Count(item => item.OvertimeRequest.State ==
                AttendanceReviewOvertimeRequestState.RecognitionConfirmed));

    private static MyAttendanceRowDto BaseRow() => new(
        Guid.NewGuid(), WorkDate, true, "WorkingDay", "正常班",
        new TimeOnly(8, 0), new TimeOnly(17, 30),
        new DateTime(2026, 8, 20, 7, 55, 0),
        new DateTime(2026, 8, 20, 17, 35, 0), "Normal",
        false, false, false, false, 0, 0, 0, 480, 480, 0, 0,
        "None", false, false, 0, [],
        new AttendanceReviewOvertimeRequestDto(
            AttendanceReviewOvertimeRequestState.NoRequest, 0, 0, 0, null),
        [], 0, AttendanceReviewOverstayLevel.None, false);

    private sealed class RecordingService(MyAttendanceResult result) :
        IAttendanceReviewService
    {
        public List<MyAttendanceQuery> Queries { get; } = [];
        public List<Guid> DetailIds { get; } = [];
        public MyAttendanceDetailResult DetailResult { get; set; } =
            new(true, BaseRow());

        public Task<AttendanceReviewFilterOptions> GetFilterOptionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AttendanceReviewFilterOptions([], []));
        public Task<AttendanceReviewResult> SearchAsync(AttendanceReviewQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AttendanceReviewResult([], new AttendanceReviewSummary(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), []));
        public Task<MyAttendanceResult> SearchMineAsync(MyAttendanceQuery query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(new MyAttendanceQuery
            {
                StartDate = query.StartDate,
                EndDate = query.EndDate,
                QuickFilter = query.QuickFilter,
                SortDirection = query.SortDirection
            });
            return Task.FromResult(result);
        }
        public Task<MyAttendanceDetailResult> GetMineDetailAsync(Guid id,
            CancellationToken cancellationToken = default)
        {
            DetailIds.Add(id);
            return Task.FromResult(DetailResult);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
