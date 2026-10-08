using System.Text.Json;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Domain.Auditing;

namespace HRSystem.Application.Common.Auditing;

public static class AuditLogFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static AuditLog Create(
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        string action,
        string entityType,
        string entityId,
        object? oldValues,
        object? newValues) =>
        new(
            currentUser.UserId,
            action,
            entityType,
            entityId,
            oldValues is null ? null : JsonSerializer.Serialize(oldValues, JsonOptions),
            newValues is null ? null : JsonSerializer.Serialize(newValues, JsonOptions),
            currentUser.IpAddress,
            timeProvider.GetUtcNow());
}
