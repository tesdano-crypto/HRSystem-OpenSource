using Bunit;
using HRSystem.Application.Approvals;
using HRSystem.Application.Security;
using HRSystem.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class LinePairingComponentTests : BunitContext
{
    private const string FullLineUserId = "U12345678901234567890123456789012";
    private const string MaskedLineUserId = "U12***************************012";

    public LinePairingComponentTests()
    {
        Services.AddFluentUIComponents();
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Verified_Binding_Renders_Only_Masked_Private_Identifier()
    {
        Services.AddSingleton<ILineUserBindingService>(new FakeService([
            Status(canManage: true)
        ]));

        var cut = Render<LineBindings>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("已驗證", cut.Markup, StringComparison.Ordinal);
            Assert.Contains(MaskedLineUserId, cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain(FullLineUserId, cut.Markup, StringComparison.Ordinal);
            Assert.Contains("更換綁定", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("解除綁定", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Owner_Self_View_Does_Not_Expose_Administrative_Actions()
    {
        Services.AddSingleton<ILineUserBindingService>(new FakeService([
            Status(canManage: false)
        ]));

        var cut = Render<LineBindings>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("已驗證", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("更換綁定", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("解除綁定", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("傳送測試通知", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Replacement_Reason_Is_Captured_Without_Blur()
    {
        var service = new FakeService([Status(canManage: true)]);
        Services.AddSingleton<ILineUserBindingService>(service);
        var cut = Render<LineBindings>();
        cut.WaitForAssertion(() => Assert.Contains("更換綁定", cut.Markup));

        await cut.InvokeAsync(() => cut.FindAll("fluent-button")
            .Single(x => x.TextContent.Contains("更換綁定", StringComparison.Ordinal))
            .Click());
        cut.Find("textarea").Input("Owner 主動更換私人帳號");
        await cut.InvokeAsync(() => cut.FindAll("fluent-button")
            .Single(x => x.TextContent.Contains("確認", StringComparison.Ordinal))
            .Click());

        Assert.NotNull(service.LastCreateRequest);
        Assert.True(service.LastCreateRequest!.ReplaceExistingBinding);
        Assert.Equal("Owner 主動更換私人帳號",
            service.LastCreateRequest.ReplacementReason);
    }

    private static LinePairingStatusDto Status(bool canManage) => new(
        "owner-user", "owner", "老闆", "EMP-TEST", "測試 Owner",
        [RoleNames.Owner], LinePairingStatus.Verified,
        new DateTimeOffset(2026, 8, 30, 6, 0, 0, TimeSpan.Zero),
        null, null, MaskedLineUserId, canManage, false);

    private sealed class FakeService(IReadOnlyList<LinePairingStatusDto> statuses)
        : ILineUserBindingService
    {
        public CreateLinePairingRequest? LastCreateRequest { get; private set; }

        public Task<IReadOnlyList<LinePairingStatusDto>> GetStatusesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(statuses);

        public Task<CreateLinePairingResult> CreatePairingRequestAsync(
            CreateLinePairingRequest request,
            CancellationToken cancellationToken = default)
        {
            LastCreateRequest = request;
            return Task.FromResult(new CreateLinePairingResult(Guid.NewGuid(),
                "綁定 HRSystem one-time-code",
                new DateTimeOffset(2026, 8, 30, 6, 20, 0, TimeSpan.Zero)));
        }

        public Task<CompleteLinePairingResult> CompletePrivatePairingAsync(
            CompleteLinePairingRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompleteLinePairingResult(true, "完成"));

        public Task RevokeAsync(RevokeLineBindingRequest request,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendTestNotificationAsync(string hrSystemUserId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
