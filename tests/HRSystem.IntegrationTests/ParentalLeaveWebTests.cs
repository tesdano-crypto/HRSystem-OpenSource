using Bunit;
using HRSystem.Application.Common.Models;
using HRSystem.Application.ParentalLeave;
using HRSystem.Domain.ParentalLeave;
using HRSystem.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class ParentalLeaveWebTests : BunitContext
{
    private readonly StubService _service = new();

    public ParentalLeaveWebTests()
    {
        var authorization = AddAuthorization();
        authorization.SetAuthorized("phase-c-test-user");
        Services.AddFluentUIComponents();
        Services.AddSingleton<IParentalLeaveService>(_service);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Employee_New_Page_Shows_Dedicated_Fields_And_Quota()
    {
        var cut = Render<ParentalLeaveNew>();
        Assert.NotNull(cut.Find("#parental-child-birth"));
        Assert.NotNull(cut.Find("#parental-start"));
        Assert.NotNull(cut.Find("#parental-end"));
        Assert.NotNull(cut.Find("#parental-address"));
        Assert.NotNull(cut.Find("#parental-phone"));
        Assert.Contains("按日額度", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("短期次數", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedHours", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emergency_Notice_Shows_Reason_Field()
    {
        var cut = Render<ParentalLeaveNew>();
        cut.Find("#parental-notice").Change(
            ParentalLeaveNoticeType.EmergencyCare.ToString());
        Assert.NotNull(cut.Find("#parental-emergency"));
    }

    [Fact]
    public void Save_Draft_Uses_Dedicated_Parental_Service()
    {
        var cut = Render<ParentalLeaveNew>();
        cut.Find("#parental-address").Change("測試地址");
        cut.Find("#parental-phone").Change("0900000000");
        var saveButton = Assert.Single(
            cut.FindAll("fluent-button"),
            button => button.TextContent.Trim() == "儲存草稿");
        saveButton.Click();
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Equal(1, _service.CreateCount));
    }

    [Fact]
    public void Approval_Page_Shows_Employee_Child_Period_And_Notice()
    {
        var cut = Render<ParentalLeaveApprovals>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("EMP9001", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("子女出生日期", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("緊急照顧", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("30 日", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Detail_Page_Shows_Status_And_Append_Only_History()
    {
        var cut = Render<ParentalLeaveDetail>(parameters => parameters
            .Add(component => component.Id, _service.RequestId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("待核准", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("已送出", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Phase C 測試送出", cut.Markup, StringComparison.Ordinal);
        });
    }

    private sealed class StubService : IParentalLeaveService
    {
        private readonly Guid _requestId = Guid.NewGuid();
        public Guid RequestId => _requestId;
        public int CreateCount { get; private set; }

        public Task<IReadOnlyList<ParentalLeaveChildOptionDto>> GetChildOptionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ParentalLeaveChildOptionDto>>([]);

        public Task<ParentalLeaveEstimateDto> EstimateAsync(
            CreateParentalLeaveDraftRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(
                new ParentalLeaveEstimateDto(
                    180,
                    ParentalLeaveApplicationType.Standard,
                    10,
                    10,
                    true,
                    5,
                    25,
                    1,
                    1,
                    35,
                    695,
                    []));

        public Task<PagedResult<ParentalLeaveRequestDto>> GetMyRequestsAsync(
            ParentalLeaveQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<ParentalLeaveRequestDto>([], 0, 1, 20));

        public Task<PagedResult<ParentalLeaveRequestDto>> GetPendingApprovalsAsync(
            ParentalLeaveQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<ParentalLeaveRequestDto>([Dto()], 1, 1, 20));

        public Task<ParentalLeaveRequestDto> GetDetailAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Dto());
        public Task<ParentalLeaveRequestDto> CreateDraftAsync(CreateParentalLeaveDraftRequest request, CancellationToken cancellationToken = default) { CreateCount++; return Task.FromResult(Dto()); }
        public Task<ParentalLeaveRequestDto> UpdateDraftAsync(UpdateParentalLeaveDraftRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Dto());
        public Task<ParentalLeaveRequestDto> SubmitAsync(ParentalLeaveActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Dto());
        public Task<ParentalLeaveRequestDto> WithdrawAsync(ParentalLeaveActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Dto());
        public Task<ParentalLeaveRequestDto> ApproveAsync(ParentalLeaveActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Dto());
        public Task<ParentalLeaveRequestDto> RejectAsync(ParentalLeaveReasonActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Dto());
        public Task<ParentalLeaveRequestDto> RequestCancellationAsync(ParentalLeaveReasonActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Dto());
        public Task<ParentalLeaveRequestDto> ApproveCancellationAsync(ParentalLeaveActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Dto());
        public Task<ParentalLeaveRequestDto> RejectCancellationAsync(ParentalLeaveReasonActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Dto());
        public Task<ParentalLeaveRequestDto> RequestEarlyReturnAsync(RequestParentalLeaveEarlyReturnRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Dto());
        public Task<ParentalLeaveRequestDto> ApproveEarlyReturnAsync(ParentalLeaveActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Dto());

        private ParentalLeaveRequestDto Dto() => new(
            _requestId, "PL-TEST", Guid.NewGuid(), "EMP9001", "測試員工",
            Guid.NewGuid(), "測試部", Guid.NewGuid(), new DateOnly(2025, 1, 1),
            "子女 A", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30),
            new DateOnly(2026, 9, 30), 30,
            ParentalLeaveApplicationType.ShortTerm30DaysOrMore,
            ParentalLeaveNoticeType.EmergencyCare, null, "測試地址", "0900",
            true, "停托", ParentalLeaveStatus.Submitted,
            ParentalLeaveStatus.Submitted, DateTimeOffset.UtcNow, null, null,
            null, null, false, DateTimeOffset.UtcNow, string.Empty,
            [
                new ParentalLeaveApprovalHistoryDto(
                    Guid.NewGuid(),
                    ParentalLeaveApprovalAction.Submitted,
                    "測試員工",
                    "Phase C 測試送出",
                    DateTimeOffset.UtcNow,
                    ParentalLeaveStatus.Draft,
                    ParentalLeaveStatus.Submitted)
            ]);
    }
}
