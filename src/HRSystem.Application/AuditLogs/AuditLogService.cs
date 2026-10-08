using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Models;
using HRSystem.Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.AuditLogs;

public sealed class AuditLogService(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser) : IAuditLogService
{
    public async Task<PagedResult<AuditLogDto>> GetListAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(HRSystem.Application.Security.PolicyNames.AuditLogRead))
        {
            throw new HRSystem.Application.Common.Exceptions.ForbiddenAccessException("您沒有檢視稽核紀錄的權限。");
        }
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var logs = dbContext.AuditLogs.AsNoTracking().AsQueryable();

        if (query.StartDate.HasValue)
        {
            var start = new DateTimeOffset(query.StartDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            logs = logs.Where(x => x.CreatedAtUtc >= start);
        }

        if (query.EndDate.HasValue)
        {
            var endExclusive = new DateTimeOffset(query.EndDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            logs = logs.Where(x => x.CreatedAtUtc < endExclusive);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action.Trim();
            logs = logs.Where(x => x.Action == action);
        }

        var totalCount = await logs.CountAsync(cancellationToken);
        var items = await logs.OrderByDescending(x => x.CreatedAtUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AuditLogDto(
                x.Id, x.UserId, x.Action, x.EntityType, x.EntityId, x.IpAddress, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return new PagedResult<AuditLogDto>(items, totalCount, pageNumber, pageSize);
    }
}
