using Bunit;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Approvals;
using HRSystem.Application.Security;
using HRSystem.Domain.Approvals;
using HRSystem.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using System.Net;

namespace HRSystem.IntegrationTests;

public sealed class ApprovalComponentTests : BunitContext
{
    private static readonly Guid ApprovalId = Guid.NewGuid();

    public ApprovalComponentTests()
    {
        Services.AddFluentUIComponents();
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(RoleNames.Admin, HttpStatusCode.OK)]
    [InlineData(RoleNames.Owner, HttpStatusCode.OK)]
    [InlineData(RoleNames.Accounting, HttpStatusCode.OK)]
    public async Task Authorized_Approval_Center_Route_Loads(
        string role, HttpStatusCode expected)
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);
        Assert.Equal(expected, (await client.GetAsync("/approvals")).StatusCode);
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Manager_And_Employee_Cannot_Open_Approval_Center(string role)
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);
        var status = (await client.GetAsync("/approvals")).StatusCode;
        Assert.True(status is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
    }

    [Fact]
    public void Center_Renders_Centralized_Traditional_Chinese_Statuses()
    {
        Services.AddSingleton<IApprovalService>(new FakeApprovalService());
        Services.AddSingleton<ICurrentUser>(new FakeCurrentUser(RoleNames.Owner, "owner"));

        var cut = Render<ApprovalCenter>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("2026 年 08 月薪資待核准", cut.Markup,
                StringComparison.Ordinal);
            Assert.Contains("待簽核", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("通知失敗", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Owner_Can_Approve_From_Detail_And_History_Remains_Visible()
    {
        var service = new FakeApprovalService();
        Services.AddSingleton<IApprovalService>(service);
        Services.AddSingleton<ICurrentUser>(new FakeCurrentUser(RoleNames.Owner, "owner"));
        var cut = Render<ApprovalDetail>(p => p.Add(x => x.Id, ApprovalId));
        cut.WaitForAssertion(() => Assert.Contains("簽核決策", cut.Markup));

        await cut.InvokeAsync(() => cut.FindAll("fluent-button")
            .Single(x => x.TextContent.Contains("核准", StringComparison.Ordinal)).Click());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, service.ApproveCalls);
            Assert.Contains("已核准", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("簽核歷程", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Return_Uses_Latest_Reason_Without_Requiring_Blur()
    {
        var service = new FakeApprovalService();
        Services.AddSingleton<IApprovalService>(service);
        Services.AddSingleton<ICurrentUser>(new FakeCurrentUser(RoleNames.Owner, "owner"));
        var cut = Render<ApprovalDetail>(p => p.Add(x => x.Id, ApprovalId));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("textarea")));
        cut.Find("textarea").Input("請補正薪資摘要");

        await cut.InvokeAsync(() => cut.FindAll("fluent-button")
            .Single(x => x.TextContent.Contains("退回", StringComparison.Ordinal)).Click());

        Assert.Equal("請補正薪資摘要", service.LastReturnReason);
    }

    private sealed class FakeApprovalService : IApprovalService
    {
        private ApprovalStatus _status = ApprovalStatus.Pending;
        public int ApproveCalls { get; private set; }
        public string? LastReturnReason { get; private set; }
        public Task<IReadOnlyList<ApprovalActorDto>> GetApproversAsync(ApprovalType type,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalActorDto>>([new("owner", "老闆")]);
        public Task<ApprovalDto> SubmitAsync(SubmitApprovalRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(Item());
        public Task<IReadOnlyList<ApprovalDto>> GetListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalDto>>([Item()]);
        public Task<ApprovalDto> GetAsync(Guid approvalId,
            CancellationToken cancellationToken = default) => Task.FromResult(Item());
        public Task<ApprovalDto> MarkViewedAsync(Guid approvalId,
            CancellationToken cancellationToken = default) => Task.FromResult(Item());
        public Task<ApprovalDto> ApproveAsync(ApprovalDecisionRequest request,
            CancellationToken cancellationToken = default)
        { ApproveCalls++; _status = ApprovalStatus.Approved; return Task.FromResult(Item()); }
        public Task<ApprovalDto> ReturnAsync(ApprovalDecisionRequest request,
            CancellationToken cancellationToken = default)
        { LastReturnReason = request.Reason; _status = ApprovalStatus.Returned; return Task.FromResult(Item()); }
        public Task<ApprovalDto> CancelAsync(ApprovalDecisionRequest request,
            CancellationToken cancellationToken = default)
        { _status = ApprovalStatus.Cancelled; return Task.FromResult(Item()); }
        public Task<ApprovalDto> RetryNotificationAsync(Guid approvalId,
            CancellationToken cancellationToken = default) => Task.FromResult(Item());
        public Task<ApprovalLinePostbackResult> HandleLinePostbackAsync(
            ApprovalLinePostbackRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApprovalLinePostbackResult(true, "完成"));

        private ApprovalDto Item() => new(ApprovalId, ApprovalType.Payroll,
            "PayrollRun", Guid.NewGuid().ToString(), 1,
            "2026 年 08 月薪資待核准", [new("員工人數", "10")],
            "accounting", new(2026, 8, 28, 1, 0, 0, TimeSpan.Zero),
            "owner", _status, null, null, null, null,
            ApprovalNotificationStatus.Failed, "通知失敗，簽核仍有效。",
            [new(Guid.NewGuid(), ApprovalHistoryAction.Submitted, "accounting",
                new(2026, 8, 28, 1, 0, 0, TimeSpan.Zero),
                ApprovalChannel.Web, null)]);
    }

    private sealed class FakeCurrentUser(string role, string userId) : ICurrentUser
    {
        public string? UserId => userId;
        public Guid? EmployeeId => null;
        public string? DisplayName => role;
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string expectedRole) => expectedRole == role;
        public bool HasPermission(string policy) => RolePermissions.HasPermission([role], policy);
    }
}
