using HRSystem.Application.Common.Models;

namespace HRSystem.Application.AuditLogs;

public interface IAuditLogService
{
    Task<PagedResult<AuditLogDto>> GetListAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
}
