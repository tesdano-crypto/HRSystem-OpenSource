using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Auditing;

public sealed class AuditLog
{
    private AuditLog()
    {
    }

    public AuditLog(
        string? userId,
        string action,
        string entityType,
        string? entityId,
        string? oldValuesJson,
        string? newValuesJson,
        string? ipAddress,
        DateTimeOffset createdAtUtc)
    {
        UserId = MasterDataRules.OptionalText(userId, "使用者識別碼", 450);
        Action = MasterDataRules.RequiredText(action, "稽核動作", 100);
        EntityType = MasterDataRules.RequiredText(entityType, "Entity 類型", 100);
        EntityId = MasterDataRules.OptionalText(entityId, "Entity Id", 100);
        OldValuesJson = oldValuesJson;
        NewValuesJson = newValuesJson;
        IpAddress = MasterDataRules.OptionalText(ipAddress, "IP 位址", 45);
        CreatedAtUtc = createdAtUtc;
    }

    public long Id { get; private set; }
    public string? UserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string? EntityId { get; private set; }
    public string? OldValuesJson { get; private set; }
    public string? NewValuesJson { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
