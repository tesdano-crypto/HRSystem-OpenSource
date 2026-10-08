using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Common.Models;
using HRSystem.Application.Common.Validation;
using HRSystem.Application.Security;
using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.LeaveTypes;

public sealed class LeaveTypeService(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ILeaveTypeService
{
    public async Task<PagedResult<LeaveTypeDto>> GetListAsync(
        LeaveTypeQuery query,
        CancellationToken cancellationToken = default)
    {
        EnsureManage();
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var leaveTypes = dbContext.LeaveTypes.AsNoTracking().AsQueryable();
        if (query.IsActive.HasValue)
        {
            leaveTypes = leaveTypes.Where(x => x.IsActive == query.IsActive.Value);
        }

        var totalCount = await leaveTypes.CountAsync(cancellationToken);
        var entities = await leaveTypes.OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<LeaveTypeDto>(entities.Select(Map).ToList(), totalCount, pageNumber, pageSize);
    }

    public async Task<LeaveTypeDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureManage();
        var entity = await dbContext.LeaveTypes.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("找不到指定的假別。");
        return Map(entity);
    }

    public async Task<LeaveTypeDto> CreateAsync(
        CreateLeaveTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureManage();
        RequestValidator.Validate(request);
        var code = request.Code.Trim().ToUpperInvariant();
        await EnsureCodeUniqueAsync(code, null, cancellationToken);
        var entity = new LeaveType(
            Guid.NewGuid(), code, request.Name, request.Unit,
            request.MinimumUnit, request.RequiresReason, request.IsPaid, request.SortOrder,
            request.Category, request.CalculationMode, request.AllowHourlyRequest,
            request.MinimumRequestMinutes, request.RequiresAttachment,
            request.IsEmployeeRequestEnabled, request.Description,
            timeProvider.GetUtcNow());
        dbContext.LeaveTypes.Add(entity);
        dbContext.AuditLogs.Add(AuditLogFactory.Create(currentUser, timeProvider, AuditActions.Created,
            nameof(LeaveType), entity.Id.ToString(), null, Snapshot(entity)));
        await SaveAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<LeaveTypeDto> UpdateAsync(
        UpdateLeaveTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureManage();
        RequestValidator.Validate(request);
        var entity = await dbContext.LeaveTypes.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("找不到指定的假別。");
        EnsureRowVersion(entity.RowVersion, request.RowVersion);
        var code = request.Code.Trim().ToUpperInvariant();
        if (!string.Equals(code, entity.Code, StringComparison.Ordinal))
        {
            throw new ApplicationValidationException("假別代碼建立後不可修改。");
        }
        var oldValues = Snapshot(entity);
        entity.Update(
            request.Name, request.Unit, request.MinimumUnit,
            request.RequiresReason, request.IsPaid, request.SortOrder,
            request.Category, request.CalculationMode, request.AllowHourlyRequest,
            request.MinimumRequestMinutes, request.RequiresAttachment,
            request.IsEmployeeRequestEnabled, request.Description,
            timeProvider.GetUtcNow());
        dbContext.AuditLogs.Add(AuditLogFactory.Create(currentUser, timeProvider, AuditActions.Updated,
            nameof(LeaveType), entity.Id.ToString(), oldValues, Snapshot(entity)));
        await SaveAsync(cancellationToken);
        return Map(entity);
    }

    public async Task SetActiveAsync(
        Guid id,
        bool isActive,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        EnsureManage();
        var entity = await dbContext.LeaveTypes.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("找不到指定的假別。");
        EnsureRowVersion(entity.RowVersion, rowVersion);
        var oldValues = Snapshot(entity);
        if (isActive)
        {
            entity.Activate(timeProvider.GetUtcNow());
        }
        else
        {
            entity.Deactivate(timeProvider.GetUtcNow());
        }

        dbContext.AuditLogs.Add(AuditLogFactory.Create(currentUser, timeProvider,
            isActive ? AuditActions.Activated : AuditActions.Deactivated,
            nameof(LeaveType), entity.Id.ToString(), oldValues, Snapshot(entity)));
        await SaveAsync(cancellationToken);
    }

    private async Task EnsureCodeUniqueAsync(string code, Guid? excludedId, CancellationToken cancellationToken)
    {
        if (await dbContext.LeaveTypes.AnyAsync(
                x => x.Code == code && (!excludedId.HasValue || x.Id != excludedId.Value), cancellationToken))
        {
            throw new ApplicationValidationException("假別代碼已存在。");
        }
    }

    private void EnsureManage()
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.LeaveManage))
        {
            throw new ForbiddenAccessException("您沒有假別管理權限。");
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
        catch (DbUpdateException)
        {
            throw new ApplicationValidationException("假別代碼已存在，請使用其他代碼。");
        }
    }

    private static void EnsureRowVersion(byte[] current, string supplied)
    {
        var expected = string.IsNullOrWhiteSpace(supplied) ? [] : Convert.FromBase64String(supplied);
        if (!current.SequenceEqual(expected))
        {
            throw new ConcurrencyConflictException();
        }
    }

    private static LeaveTypeDto Map(LeaveType entity) => new(
        entity.Id, entity.Code, entity.Name, entity.Unit, entity.MinimumUnit,
        entity.RequiresReason, entity.IsPaid, entity.IsActive, entity.SortOrder,
        entity.Category, entity.CalculationMode, entity.AllowHourlyRequest,
        entity.MinimumRequestMinutes, entity.RequiresAttachment,
        entity.IsEmployeeRequestEnabled, entity.Description,
        Convert.ToBase64String(entity.RowVersion));

    private static object Snapshot(LeaveType entity) => new
    {
        entity.Code,
        entity.Name,
        entity.Unit,
        entity.MinimumUnit,
        entity.RequiresReason,
        entity.IsPaid,
        entity.IsActive,
        entity.SortOrder,
        entity.Category,
        entity.CalculationMode,
        entity.AllowHourlyRequest,
        entity.MinimumRequestMinutes,
        entity.RequiresAttachment,
        entity.IsEmployeeRequestEnabled,
        entity.Description
    };
}
