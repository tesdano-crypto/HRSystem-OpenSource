using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using HRSystem.Application.Approvals;
using HRSystem.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace HRSystem.Infrastructure.Approvals;

public sealed class LineApprovalPrivateNotificationSender(
    IHttpClientFactory httpClientFactory,
    LineApprovalBridgeOptions options,
    ILogger<LineApprovalPrivateNotificationSender> logger)
    : IApprovalPrivateNotificationSender
{
    public async Task SendAsync(ApprovalPrivateNotification notification,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
            throw new ApplicationValidationException("LINE 私訊通知尚未啟用。");
        if (!Uri.TryCreate(options.NotificationEndpoint, UriKind.Absolute, out var endpoint) ||
            string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ApplicationValidationException("LINE 私訊通知設定不完整。");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation(options.ApiKeyHeaderName, options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            targetType = "user",
            lineUserId = notification.LineUserId,
            approvalId = notification.ApprovalId,
            notification.Title,
            summary = notification.Summary,
            actions = new[]
            {
                new { type = "approve", token = notification.ApproveToken },
                new { type = "return", token = notification.ReturnToken }
            },
            notification.ExpiresAtUtc
        });
        var response = await httpClientFactory.CreateClient(nameof(LineApprovalPrivateNotificationSender))
            .SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("LINE approval bridge returned status {StatusCode}.",
                (int)response.StatusCode);
            throw new HttpRequestException("LINE approval bridge rejected the notification.");
        }
    }
}

public sealed class LineApprovalBridgeRequestVerifier(LineApprovalBridgeOptions options)
    : ILineApprovalBridgeRequestVerifier
{
    public bool IsAuthorized(string? presentedKey)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ApiKey) ||
            string.IsNullOrWhiteSpace(presentedKey)) return false;
        var expected = Encoding.UTF8.GetBytes(options.ApiKey);
        var actual = Encoding.UTF8.GetBytes(presentedKey);
        return expected.Length == actual.Length &&
            CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
