namespace HRSystem.Infrastructure.Approvals;

public sealed class LineApprovalBridgeOptions
{
    public const string SectionName = "ApprovalLineBridge";
    public bool Enabled { get; set; }
    public string? NotificationEndpoint { get; set; }
    public string? ApiKey { get; set; }
    public string ApiKeyHeaderName { get; set; } = "X-HRSystem-Bridge-Key";
}
