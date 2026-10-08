using System.Net;
using HRSystem.Application.Approvals;
using HRSystem.Infrastructure.Approvals;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRSystem.UnitTests;

public sealed class LineApprovalBridgeTests
{
    [Fact]
    public async Task Notification_Is_Strictly_Private_User_Target_Without_Group_Fallback()
    {
        var handler = new CaptureHandler();
        var options = new LineApprovalBridgeOptions
        {
            Enabled = true,
            NotificationEndpoint = "https://assistant.invalid/internal/approval",
            ApiKey = "bridge-secret"
        };
        var sender = new LineApprovalPrivateNotificationSender(
            new Factory(new HttpClient(handler)), options,
            NullLogger<LineApprovalPrivateNotificationSender>.Instance);

        await sender.SendAsync(new("U-private", Guid.NewGuid(), "薪資待核准",
            [new("員工人數", "2")], "approve-token", "return-token",
            DateTimeOffset.UtcNow.AddHours(1)));

        Assert.NotNull(handler.RequestBody);
        Assert.Contains("\"targetType\":\"user\"", handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains("\"lineUserId\":\"U-private\"", handler.RequestBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain("groupId", handler.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("roomId", handler.RequestBody, StringComparison.Ordinal);
        Assert.Equal("bridge-secret", handler.ApiKey);
    }

    [Fact]
    public void Bridge_Verifier_Is_Deny_By_Default_And_Uses_Exact_Key()
    {
        Assert.False(new LineApprovalBridgeRequestVerifier(new()).IsAuthorized("any"));
        var verifier = new LineApprovalBridgeRequestVerifier(new()
        { Enabled = true, ApiKey = "expected" });
        Assert.False(verifier.IsAuthorized("Expected"));
        Assert.False(verifier.IsAuthorized("expected "));
        Assert.True(verifier.IsAuthorized("expected"));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public string? ApiKey { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            ApiKey = request.Headers.GetValues("X-HRSystem-Bridge-Key").Single();
            return new(HttpStatusCode.OK);
        }
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
