using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Common.Models;
using HRSystem.Application.Common.Security;
using HRSystem.Application.Common.Validation;
using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Departments;

public sealed class DepartmentService(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IDepartmentService
{
    private const string CodeUniqueIndex = "UX_Departments_Code";

    public async Task<PagedResult<DepartmentDto>> GetListAsync(
        DepartmentQuery query,
        CancellationToken cancellationToken = default)
    {
        MasterDataAuthorization.EnsureOrganizationReader(currentUser);
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var departments = dbContext.Departments.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToUpperInvariant();
            departments = departments.Where(x => x.Code.Contains(keyword) || x.Name.ToUpper().Contains(keyword));
        }

        if (query.IsActive.HasValue)
        {
            departments = departments.Where(x => x.IsActive == query.IsActive.Value);
        }

        var totalCount = await departments.CountAsync(cancellationToken);
        var entities = await departments
            .Include(x => x.ManagerEmployee)
            .OrderBy(x => x.Code)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<DepartmentDto>(entities.Select(Map).ToList(), totalCount, pageNumber, pageSize);
    }

    public async Task<DepartmentDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        MasterDataAuthorization.EnsureOrganizationReader(currentUser);
        var entity = await dbContext.Departments.AsNoTracking()
            .Include(x => x.ManagerEmployee)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? throw new EntityNotFoundException("找不到指定的部門。") : Map(entity);
    }

    public async Task<DepartmentDto> CreateAsync(
        CreateDepartmentRequest request,
        CancellationToken cancellationToken = default)
    {
        MasterDataAuthorization.EnsureAdmin(currentUser);
        RequestValidator.Validate(request);
        return await dbContext.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            var code = request.Code.Trim().ToUpperInvariant();
            await EnsureCodeUniqueAsync(code, null, transactionCancellationToken);
            var now = timeProvider.GetUtcNow();
            var entity = new Department(Guid.NewGuid(), code, request.Name, now);
            if (request.ManagerEmployeeId.HasValue)
            {
                await ValidateManagerAsync(request.ManagerEmployeeId.Value, entity.Id, transactionCancellationToken);
                entity.Update(code, request.Name, request.ManagerEmployeeId, now);
            }

            dbContext.Departments.Add(entity);
            dbContext.AuditLogs.Add(AuditLogFactory.Create(currentUser, timeProvider, AuditActions.Created,
                nameof(Department), entity.Id.ToString(), null, Snapshot(entity)));
            await SaveAsync(transactionCancellationToken);
            return await GetForAdminAsync(entity.Id, transactionCancellationToken);
        }, cancellationToken);
    }

    public async Task<DepartmentDto> UpdateAsync(
        UpdateDepartmentRequest request,
        CancellationToken cancellationToken = default)
    {
        MasterDataAuthorization.EnsureAdmin(currentUser);
        RequestValidator.Validate(request);
        return await dbContext.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            var entity = await dbContext.Departments.SingleOrDefaultAsync(
                    x => x.Id == request.Id, transactionCancellationToken)
                ?? throw new EntityNotFoundException("找不到指定的部門。");
            EnsureRowVersion(entity.RowVersion, request.RowVersion);
            var code = request.Code.Trim().ToUpperInvariant();
            await EnsureCodeUniqueAsync(code, entity.Id, transactionCancellationToken);
            if (request.ManagerEmployeeId.HasValue)
            {
                await ValidateManagerAsync(request.ManagerEmployeeId.Value, entity.Id, transactionCancellationToken);
            }

            var oldValues = Snapshot(entity);
            entity.Update(code, request.Name, request.ManagerEmployeeId, timeProvider.GetUtcNow());
            dbContext.AuditLogs.Add(AuditLogFactory.Create(currentUser, timeProvider, AuditActions.Updated,
                nameof(Department), entity.Id.ToString(), oldValues, Snapshot(entity)));
            await SaveAsync(transactionCancellationToken);
            return await GetForAdminAsync(entity.Id, transactionCancellationToken);
        }, cancellationToken);
    }

    public async Task SetActiveAsync(
        Guid id,
        bool isActive,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        MasterDataAuthorization.EnsureAdmin(currentUser);
        await dbContext.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            var entity = await dbContext.Departments.SingleOrDefaultAsync(
                    x => x.Id == id, transactionCancellationToken)
                ?? throw new EntityNotFoundException("找不到指定的部門。");
            EnsureRowVersion(entity.RowVersion, rowVersion);
            if (!isActive && await dbContext.Employees.AsNoTracking()
                    .AnyAsync(x => x.DepartmentId == entity.Id && x.IsActive, transactionCancellationToken))
            {
                throw new ApplicationValidationException(
                    "此部門仍有啟用中的員工。請先轉調或停用這些員工，再停用部門。");
            }

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
                nameof(Department), entity.Id.ToString(), oldValues, Snapshot(entity)));
            await SaveAsync(transactionCancellationToken);
        }, cancellationToken);
    }

    private async Task EnsureCodeUniqueAsync(string code, Guid? excludedId, CancellationToken cancellationToken)
    {
        if (await dbContext.Departments.AnyAsync(
                x => x.Code == code && (!excludedId.HasValue || x.Id != excludedId.Value), cancellationToken))
        {
            throw new ApplicationValidationException("部門代碼已存在。");
        }
    }

    private async Task ValidateManagerAsync(Guid employeeId, Guid departmentId, CancellationToken cancellationToken)
    {
        var manager = await dbContext.Employees.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == employeeId, cancellationToken)
            ?? throw new ApplicationValidationException("指定的主管不存在。");
        if (!manager.IsActive || manager.DepartmentId != departmentId)
        {
            throw new ApplicationValidationException("主管必須是此部門的啟用員工。");
        }
    }

    private async Task<DepartmentDto> GetForAdminAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Departments.AsNoTracking().Include(x => x.ManagerEmployee)
            .SingleAsync(x => x.Id == id, cancellationToken);
        return Map(entity);
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
        catch (DbUpdateException exception) when (
            dbContext.IsUniqueConstraintViolation(exception, CodeUniqueIndex))
        {
            throw new ApplicationValidationException("部門代碼已存在，請使用其他代碼。");
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

    private static object Snapshot(Department entity) => new
    {
        entity.Code,
        entity.Name,
        entity.ManagerEmployeeId,
        entity.IsActive
    };

    private static DepartmentDto Map(Department entity) => new(
        entity.Id, entity.Code, entity.Name, entity.ManagerEmployeeId,
        entity.ManagerEmployee?.ChineseName, entity.IsActive,
        Convert.ToBase64String(entity.RowVersion));
}
