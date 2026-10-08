using System.Net;
using System.Net.Http.Json;
using HRSystem.Application.Approvals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HRSystem.IntegrationTests;

public sealed class LinePairingEndpointTests
{
    [Fact]
    public async Task Callback_Fails_Closed_Without_Trusted_Bridge_Key()
    {
        var service = new FakeService();
        await using var factory = Factory(service);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/integrations/line/private-pairing",
            new { pairingToken = "one-time", lineUserId = "U-private", sourceType = "user" });

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(0, service.CompleteCalls);
    }

    [Fact]
    public async Task Trusted_Callback_Passes_Private_Source_To_Application_Service()
    {
        var service = new FakeService();
        await using var factory = Factory(service);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-HRSystem-Bridge-Key", "trusted");

        var response = await client.PostAsJsonAsync(
            "/api/integrations/line/private-pairing",
            new { pairingToken = "one-time", lineUserId = "U-private", sourceType = "user" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, service.CompleteCalls);
        Assert.Equal("user", service.LastRequest?.SourceType);
    }

    private static MasterDataWebApplicationFactory Factory(FakeService service) =>
        new(services =>
        {
            services.RemoveAll<ILineApprovalBridgeRequestVerifier>();
            services.RemoveAll<ILineUserBindingService>();
            services.AddSingleton<ILineApprovalBridgeRequestVerifier>(
                new FixedVerifier("trusted"));
            services.AddSingleton<ILineUserBindingService>(service);
        });

    private sealed class FixedVerifier(string expected)
        : ILineApprovalBridgeRequestVerifier
    {
        public bool IsAuthorized(string? presentedKey) => presentedKey == expected;
    }

    private sealed class FakeService : ILineUserBindingService
    {
        public int CompleteCalls { get; private set; }
        public CompleteLinePairingRequest? LastRequest { get; private set; }
        public Task<IReadOnlyList<LinePairingStatusDto>> GetStatusesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LinePairingStatusDto>>([]);
        public Task<CreateLinePairingResult> CreatePairingRequestAsync(
            CreateLinePairingRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CompleteLinePairingResult> CompletePrivatePairingAsync(
            CompleteLinePairingRequest request,
            CancellationToken cancellationToken = default)
        {
            CompleteCalls++;
            LastRequest = request;
            return Task.FromResult(new CompleteLinePairingResult(true, "完成"));
        }
        public Task RevokeAsync(RevokeLineBindingRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SendTestNotificationAsync(string hrSystemUserId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
