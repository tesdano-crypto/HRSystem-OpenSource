using Bunit;
using HRSystem.Application.Overtime;
using HRSystem.Domain.Overtime;
using HRSystem.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class OvertimeRequestComponentTests : BunitContext
{
    public OvertimeRequestComponentTests()
    {
        Services.AddFluentUIComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void My_Overtime_Renders_Self_Service_Create_Without_Employee_Picker()
    {
        Register(new FakeService(), new FakeRecognitionService());
        var cut = Render<MyOvertime>();
        Assert.Contains("新增加班草稿", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("EmployeeId", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("員工選擇", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_Draft_Uses_Current_Input_Without_Blur()
    {
        var service = new FakeService();
        Register(service, new FakeRecognitionService());
        var cut = Render<MyOvertime>();
        var text = cut.Find("textarea");
        text.Input("立即輸入的加班原因");
        cut.Find("form").Submit();

        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.NotNull(service.Created);
        Assert.Equal("立即輸入的加班原因", service.Created!.Reason);
    }

    [Fact]
    public void Admin_Review_Has_No_Proxy_Create_Action()
    {
        var service = new FakeService { Items = [Submitted()] };
        Register(service, new FakeRecognitionService());
        var cut = Render<AdminOvertime>();
        Assert.Contains("加班審核", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("新增員工加班申請", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("代申請", cut.Markup.Replace("本頁不提供代申請", string.Empty),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_Reject_Passes_Structured_Reason_And_Immediate_Note()
    {
        var service = new FakeService { Items = [Submitted()] };
        Register(service, new FakeRecognitionService());
        var cut = Render<AdminOvertime>();
        await ClickAsync(cut, "駁回");
        cut.Find("textarea").Input("不失焦直接送出的審核說明");
        await ClickAsync(cut, "確認駁回");

        Assert.NotNull(service.Rejected);
        Assert.Equal("不失焦直接送出的審核說明", service.Rejected!.Note);
        Assert.Equal(OvertimeReviewReason.BusinessNeedNotConfirmed,
            service.Rejected.Reason);
    }

    [Fact]
    public void Employee_View_Shows_Rejected_Note_And_History()
    {
        var item = Submitted() with
        {
            Status = OvertimeRequestStatus.Rejected,
            ReviewReason = OvertimeReviewReason.TimeRangeIncorrect,
            ReviewNote = "時段不符",
            Histories =
            [
                new(OvertimeRequestHistoryAction.Created, null,
                    OvertimeRequestStatus.Draft, "加班原因", Now),
                new(OvertimeRequestHistoryAction.Rejected,
                    OvertimeRequestStatus.Submitted,
                    OvertimeRequestStatus.Rejected, "時段不符", Now)
            ]
        };
        var service = new FakeService { Items = [item] };
        Register(service, new FakeRecognitionService());
        var cut = Render<MyOvertime>();
        Assert.Contains("時段不符", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("已駁回", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_Can_Confirm_Minute_Precision_Recognition()
    {
        var recognition = new FakeRecognitionService { Items = [Recognition()] };
        Register(new FakeService(), recognition);
        var cut = Render<AdminOvertime>();
        await ClickAsync(cut, "認列實際加班時間");
        var inputs = cut.FindAll("input[type='datetime-local']");
        inputs[0].Change("2026-08-21T17:30");
        inputs[1].Change("2026-08-21T19:47");
        await ClickAsync(cut, "確認實際加班");
        Assert.NotNull(recognition.Confirmed);
        Assert.Equal(new DateTime(2026, 8, 21, 19, 47, 0),
            recognition.Confirmed!.RecognizedEndAt);
    }

    [Fact]
    public void Employee_View_Shows_Final_Recognized_Minutes_Read_Only()
    {
        var item = Submitted() with
        {
            Status = OvertimeRequestStatus.Approved,
            Recognition = Recognition() with
            {
                Status = OvertimeRecognitionStatus.Confirmed,
                RecognizedMinutes = 137
            }
        };
        Register(new FakeService { Items = [item] }, new FakeRecognitionService());
        var cut = Render<MyOvertime>();
        Assert.Contains("實際認列 137 分鐘", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("確認實際加班", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Employee_View_Shows_Stale_Recognition_As_Needing_Review()
    {
        var item = Submitted() with
        {
            Status = OvertimeRequestStatus.Approved,
            Recognition = Recognition() with
            {
                Status = OvertimeRecognitionStatus.NeedsReview,
                RecognizedMinutes = 30,
                IsSourceStale = true
            }
        };
        Register(new FakeService { Items = [item] }, new FakeRecognitionService());

        var cut = Render<MyOvertime>();

        Assert.Contains("出勤資料已變更，需重新確認", cut.Markup,
            StringComparison.Ordinal);
        Assert.DoesNotContain("實際認列 30 分鐘", cut.Markup,
            StringComparison.Ordinal);
    }

    private void Register(FakeService service, FakeRecognitionService recognition)
    {
        Services.AddSingleton<IOvertimeRequestService>(service);
        Services.AddSingleton<IOvertimeRecognitionService>(recognition);
    }

    private static async Task ClickAsync(
        IRenderedComponent<AdminOvertime> cut, string text)
    {
        var button = cut.FindComponents<FluentButton>().Single(component =>
            component.Markup.Contains($">{text}<", StringComparison.Ordinal));
        await cut.InvokeAsync(() => button.Instance.OnClick.InvokeAsync());
    }

    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 2, 0, 0, TimeSpan.Zero);

    private static OvertimeRequestDto Submitted() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "EMP-G001", "測試員工",
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        "測試部", new DateOnly(2026, 8, 21),
        new DateTime(2026, 8, 21, 18, 0, 0),
        new DateTime(2026, 8, 21, 20, 0, 0), 120, "工作需求",
        OvertimeRequestStatus.Submitted, null, null, Now, null, null, null,
        Now, string.Empty,
        [new(OvertimeRequestHistoryAction.Created, null,
            OvertimeRequestStatus.Draft, "工作需求", Now)],
        new(new TimeOnly(17, 30), new DateTime(2026, 8, 21, 20, 0, 0),
            150, "PotentialUnreportedOvertime", false, 0, true));

    private static OvertimeRecognitionDto Recognition() => new(
        null, Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "EMP-G001", "測試員工", "測試部", new DateOnly(2026, 8, 21),
        new DateTime(2026, 8, 21, 17, 30, 0),
        new DateTime(2026, 8, 21, 20, 30, 0), 180,
        new DateTime(2026, 8, 21, 17, 30, 0),
        new DateTime(2026, 8, 21, 19, 47, 0), 137,
        new DateTime(2026, 8, 21, 17, 30, 0),
        new DateTime(2026, 8, 21, 19, 47, 0), 137, 0,
        null, null, null, OvertimeRecognitionStatus.Pending,
        null, null, false, false, Convert.ToBase64String(new byte[32]),
        null, []);

    private sealed class FakeService : IOvertimeRequestService
    {
        public IReadOnlyList<OvertimeRequestDto> Items { get; set; } = [];
        public CreateOvertimeDraftRequest? Created { get; private set; }
        public ReviewOvertimeRequest? Rejected { get; private set; }
        public Task<IReadOnlyList<OvertimeRequestDto>> GetMyRequestsAsync(
            OvertimeRequestQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items);
        public Task<OvertimeRequestDto> GetMyRequestAsync(Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Single(item => item.Id == id));
        public Task<OvertimeRequestDto> CreateDraftAsync(CreateOvertimeDraftRequest request,
            CancellationToken cancellationToken = default)
        { Created = request; return Task.FromResult(Submitted()); }
        public Task<OvertimeRequestDto> UpdateDraftAsync(UpdateOvertimeDraftRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(Submitted());
        public Task<OvertimeRequestDto> SubmitAsync(OvertimeActionRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(Submitted());
        public Task<OvertimeRequestDto> WithdrawAsync(OvertimeActionRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(Submitted());
        public Task<IReadOnlyList<OvertimeRequestDto>> SearchForReviewAsync(
            OvertimeRequestQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items);
        public Task<OvertimeRequestDto> ApproveAsync(ReviewOvertimeRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(Submitted());
        public Task<OvertimeRequestDto> RejectAsync(ReviewOvertimeRequest request,
            CancellationToken cancellationToken = default)
        { Rejected = request; return Task.FromResult(Submitted()); }
    }

    private sealed class FakeRecognitionService : IOvertimeRecognitionService
    {
        public IReadOnlyList<OvertimeRecognitionDto> Items { get; set; } = [];
        public ConfirmOvertimeRecognitionRequest? Confirmed { get; private set; }
        public Task<IReadOnlyList<OvertimeRecognitionDto>> SearchAsync(
            OvertimeRecognitionQuery query,
            CancellationToken cancellationToken = default) => Task.FromResult(Items);
        public Task<OvertimeRecognitionDto> ConfirmAsync(
            ConfirmOvertimeRecognitionRequest request,
            CancellationToken cancellationToken = default)
        {
            Confirmed = request;
            return Task.FromResult(Items.Single());
        }
        public Task<OvertimeRecognitionDto> ReopenAsync(
            ReopenOvertimeRecognitionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Single());
    }
}
