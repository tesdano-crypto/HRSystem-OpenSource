namespace HRSystem.Application.AuditLogs;

public sealed record AuditLogDto(
    long Id,
    string? UserId,
    string Action,
    string EntityType,
    string? EntityId,
    string? IpAddress,
    DateTimeOffset CreatedAtUtc);

public sealed class AuditLogQuery
{
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Action { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
