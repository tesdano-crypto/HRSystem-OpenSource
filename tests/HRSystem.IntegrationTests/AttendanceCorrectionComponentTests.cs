using Bunit;
using HRSystem.Application.Attendance;
using HRSystem.Domain.Attendance;
using HRSystem.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class AttendanceCorrectionComponentTests : BunitContext
{
    public AttendanceCorrectionComponentTests()
    {
        Services.AddFluentUIComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Employee_Page_Has_No_Employee_Selector()
    {
        Register(new FakeService());
        var cut = Render<MyAttendanceRequests>();
        Assert.Contains("我的出勤更正申請", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("員工選擇", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("EmployeeId", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_Both_Context_Shows_Combined_Correction_Form()
    {
        Register(new FakeService { Context = Context(true, true) });
        NavigateToRequest(AttendanceCorrectionRequestType.MissingBoth);
        var cut = Render<MyAttendanceRequests>();
        Assert.Contains("補上下班卡", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(2, cut.FindAll("input[type='datetime-local']").Count);
    }

    [Fact]
    public async Task Employee_Reason_Is_Synchronized_Without_Blur()
    {
        var service = new FakeService { Context = Context(true, false) };
        Register(service);
        NavigateToRequest(AttendanceCorrectionRequestType.MissingClockIn);
        var cut = Render<MyAttendanceRequests>();
        cut.Find("textarea").Input("不失焦直接儲存的補卡原因");
        cut.Find("form").Submit();
        await cut.InvokeAsync(() => Task.CompletedTask);
        Assert.Equal("不失焦直接儲存的補卡原因",
            service.Created?.EmployeeReason);
    }

    [Fact]
    public async Task Draft_Can_Be_Submitted_And_Submitted_Can_Be_Withdrawn()
    {
        var item = Request(AttendanceCorrectionRequestStatus.Draft);
        var service = new FakeService { Items = [item] };
        Register(service);
        var cut = Render<MyAttendanceRequests>();
        await ClickAsync(cut, "送出");
        Assert.Equal(item.Id, service.Submitted?.Id);

        service.Items = [Request(AttendanceCorrectionRequestStatus.Submitted)];
        cut = Render<MyAttendanceRequests>();
        await ClickAsync(cut, "撤回");
        Assert.Equal(item.Id, service.Withdrawn?.Id);
    }

    [Fact]
    public void Employee_View_Does_Not_Render_Internal_Reviewer_Note()
    {
        Register(new FakeService
        {
            Items = [Request(AttendanceCorrectionRequestStatus.Rejected) with
                { ReviewerNote = "管理員內部機密備註" }]
        });
        var cut = Render<MyAttendanceRequests>();
        Assert.DoesNotContain("管理員內部機密備註", cut.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_Pending_List_Shows_Attendance_Context_And_Proposal()
    {
        Register(new FakeService
        {
            Items = [Request(AttendanceCorrectionRequestStatus.Submitted)]
        });
        var cut = Render<AdminAttendanceRequests>();
        Assert.Contains("目前出勤", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("建議更正", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("查看／處理", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_Approve_Passes_Current_Fingerprint()
    {
        var service = new FakeService
        {
            Items = [Request(AttendanceCorrectionRequestStatus.Submitted)]
        };
        Register(service);
        var cut = Render<AdminAttendanceRequests>();
        await ClickAsync(cut, "查看／處理");
        await ClickAsync(cut, "核准");
        Assert.Equal(Convert.ToBase64String(new byte[32]),
            service.Approved?.SourceFingerprint);
    }

    [Fact]
    public async Task Admin_Reject_Note_Is_Synchronized_Without_Blur()
    {
        var service = new FakeService
        {
            Items = [Request(AttendanceCorrectionRequestStatus.Submitted)]
        };
        Register(service);
        var cut = Render<AdminAttendanceRequests>();
        await ClickAsync(cut, "查看／處理");
        cut.Find("textarea").Input("不失焦直接駁回的審核說明");
        await ClickAsync(cut, "駁回");
        Assert.Equal("不失焦直接駁回的審核說明",
            service.Rejected?.Note);
    }

    [Fact]
    public async Task Stale_Request_Disables_Approve()
    {
        Register(new FakeService
        {
            Items = [Request(AttendanceCorrectionRequestStatus.Submitted) with
                { IsSourceStale = true }]
        });
        var cut = Render<AdminAttendanceRequests>();
        await ClickAsync(cut, "查看／處理");
        var approve = cut.FindAll("fluent-button").Single(button =>
            button.TextContent.Contains("核准", StringComparison.Ordinal));
        Assert.True(approve.HasAttribute("disabled"));
        Assert.Contains("出勤資料已變更", cut.Markup, StringComparison.Ordinal);
    }

    private void Register(FakeService service) =>
        Services.AddSingleton<IAttendanceCorrectionService>(service);

    private void NavigateToRequest(AttendanceCorrectionRequestType type)
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(
            $"http://localhost/my-attendance-requests?attendanceResultId={ResultId:D}&requestType={type}");
    }

    private static async Task ClickAsync<T>(IRenderedComponent<T> cut,
        string text) where T : Microsoft.AspNetCore.Components.IComponent
    {
        var button = cut.FindAll("fluent-button").Single(item =>
            item.TextContent.Contains(text, StringComparison.Ordinal));
        await cut.InvokeAsync(() => button.Click());
    }

    private static readonly Guid ResultId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RequestId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EmployeeId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DepartmentId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateOnly WorkDate = new(2026, 8, 24);
    private static readonly DateTimeOffset Now =
        new(2026, 8, 23, 2, 0, 0, TimeSpan.Zero);

    private static AttendanceCorrectionContextDto Context(
        bool missingIn = false, bool missingOut = false) => new(
            ResultId, WorkDate, new TimeOnly(8, 0), new TimeOnly(17, 30),
            missingIn ? null : WorkDate.ToDateTime(new TimeOnly(8, 10)),
            missingOut ? null : WorkDate.ToDateTime(new TimeOnly(17, 30)),
            missingIn, missingOut, missingIn ? 0 : 10, 0, "Late", false,
            0, 0, "無", "2 筆原始打卡");

    private static AttendanceCorrectionRequestDto Request(
        AttendanceCorrectionRequestStatus status) => new(
            RequestId, EmployeeId, "EMP-G301", "測試員工", DepartmentId,
            "更正測試部", WorkDate, ResultId,
            AttendanceCorrectionRequestType.MissingClockOut, status,
            WorkDate.ToDateTime(new TimeOnly(8, 0)), null, null,
            WorkDate.ToDateTime(new TimeOnly(17, 30)),
            AttendanceCorrectionReason.ForgotPunch, "忘記下班打卡", null,
            false, Convert.ToBase64String(new byte[32]), null,
            Now, null, null, null, string.Empty, Context(false, true), []);

    private sealed class FakeService : IAttendanceCorrectionService
    {
        public IReadOnlyList<AttendanceCorrectionRequestDto> Items { get; set; } = [];
        public AttendanceCorrectionContextDto? Context { get; set; }
        public CreateAttendanceCorrectionDraftRequest? Created { get; private set; }
        public AttendanceCorrectionActionRequest? Submitted { get; private set; }
        public AttendanceCorrectionActionRequest? Withdrawn { get; private set; }
        public ReviewAttendanceCorrectionRequest? Approved { get; private set; }
        public ReviewAttendanceCorrectionRequest? Rejected { get; private set; }
        public Task<IReadOnlyList<AttendanceCorrectionRequestDto>> GetMyRequestsAsync(AttendanceCorrectionQuery query, CancellationToken cancellationToken = default) => Task.FromResult(Items);
        public Task<AttendanceCorrectionRequestDto> GetMyRequestAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.Single(item => item.Id == id));
        public Task<AttendanceCorrectionContextDto?> GetMyAttendanceContextAsync(Guid attendanceResultId, CancellationToken cancellationToken = default) => Task.FromResult(Context);
        public Task<AttendanceCorrectionRequestDto> CreateDraftAsync(CreateAttendanceCorrectionDraftRequest request, CancellationToken cancellationToken = default) { Created = request; return Task.FromResult(Request(AttendanceCorrectionRequestStatus.Draft)); }
        public Task<AttendanceCorrectionRequestDto> UpdateDraftAsync(UpdateAttendanceCorrectionDraftRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Request(AttendanceCorrectionRequestStatus.Draft));
        public Task<AttendanceCorrectionRequestDto> SubmitAsync(AttendanceCorrectionActionRequest request, CancellationToken cancellationToken = default) { Submitted = request; return Task.FromResult(Request(AttendanceCorrectionRequestStatus.Submitted)); }
        public Task<AttendanceCorrectionRequestDto> WithdrawAsync(AttendanceCorrectionActionRequest request, CancellationToken cancellationToken = default) { Withdrawn = request; return Task.FromResult(Request(AttendanceCorrectionRequestStatus.Withdrawn)); }
        public Task<AttendanceCorrectionFilterOptions> GetReviewFilterOptionsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AttendanceCorrectionFilterOptions([new(DepartmentId, "G3", "更正測試部")], [new(EmployeeId, "EMP-G301", "測試員工", DepartmentId, "更正測試部")]));
        public Task<IReadOnlyList<AttendanceCorrectionRequestDto>> SearchForReviewAsync(AttendanceCorrectionQuery query, CancellationToken cancellationToken = default) => Task.FromResult(Items);
        public Task<AttendanceCorrectionRequestDto> ApproveAsync(ReviewAttendanceCorrectionRequest request, CancellationToken cancellationToken = default) { Approved = request; return Task.FromResult(Request(AttendanceCorrectionRequestStatus.Approved)); }
        public Task<AttendanceCorrectionRequestDto> RejectAsync(ReviewAttendanceCorrectionRequest request, CancellationToken cancellationToken = default) { Rejected = request; return Task.FromResult(Request(AttendanceCorrectionRequestStatus.Rejected)); }
    }
}
