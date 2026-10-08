using System.Net.Http.Json;
using HRSystem.Application.Approvals;
using HRSystem.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace HRSystem.Infrastructure.Approvals;

public sealed class LinePrivateTestNotificationSender(
    IHttpClientFactory httpClientFactory,
    LineApprovalBridgeOptions options,
    ILogger<LinePrivateTestNotificationSender> logger)
    : ILinePrivateTestNotificationSender
{
    public bool IsConfigured => options.Enabled &&
        Uri.TryCreate(options.NotificationEndpoint, UriKind.Absolute, out _) &&
        !string.IsNullOrWhiteSpace(options.ApiKey);

    public async Task SendAsync(LinePrivateTestNotification notification,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured ||
            !Uri.TryCreate(options.NotificationEndpoint, UriKind.Absolute, out var endpoint))
            throw new ApplicationValidationException("LINE 私訊 bridge 尚未設定。");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation(options.ApiKeyHeaderName, options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            targetType = "user",
            lineUserId = notification.LineUserId,
            message = notification.Message
        });
        var response = await httpClientFactory
            .CreateClient(nameof(LinePrivateTestNotificationSender))
            .SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return;
        logger.LogWarning("LINE private test bridge returned status {StatusCode}.",
            (int)response.StatusCode);
        throw new HttpRequestException("LINE private test bridge rejected the notification.");
    }
}
