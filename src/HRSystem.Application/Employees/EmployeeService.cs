using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Common.Models;
using HRSystem.Application.Common.Security;
using HRSystem.Application.Common.Validation;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.AnnualLeave;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRSystem.Application.Employees;

public sealed class EmployeeService(
    IApplicationDbContext dbContext,
    IEmployeeNumberSequence employeeNumberSequence,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<EmployeeService> logger) : IEmployeeService
{
    private const string EmployeeNumberUniqueIndex = "UX_Employees_EmployeeNumber";

    public async Task<PagedResult<EmployeeDto>> GetListAsync(
        EmployeeQuery query,
        CancellationToken cancellationToken = default)
    {
        MasterDataAuthorization.EnsureOrganizationReader(currentUser);
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var employees = dbContext.Employees.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToUpperInvariant();
            employees = employees.Where(x =>
                x.EmployeeNumber.Contains(keyword) ||
                x.ChineseName.ToUpper().Contains(keyword) ||
                (x.EnglishName != null && x.EnglishName.ToUpper().Contains(keyword)));
        }

        if (query.DepartmentId.HasValue)
        {
            employees = employees.Where(x => x.DepartmentId == query.DepartmentId.Value);
        }

        if (query.IsActive.HasValue)
        {
            employees = employees.Where(x => x.IsActive == query.IsActive.Value);
        }

        var totalCount = await employees.CountAsync(cancellationToken);
        var entities = await employees
            .Include(x => x.Department)
            .OrderBy(x => x.EmployeeNumber)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<EmployeeDto>(entities.Select(Map).ToList(), totalCount, pageNumber, pageSize);
    }

    public async Task<EmployeeDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        MasterDataAuthorization.EnsureOrganizationReader(currentUser);
        var entity = await dbContext.Employees.AsNoTracking().Include(x => x.Department)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("找不到指定的員工。");
        return Map(entity);
    }

    public async Task<EmployeeDto> CreateAsync(
        CreateEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        MasterDataAuthorization.EnsureAdmin(currentUser);
        RequestValidator.Validate(request);
        return await dbContext.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            await ValidateDepartmentAsync(request.DepartmentId, transactionCancellationToken);
            var sequenceValue = await employeeNumberSequence.GetNextValueAsync(transactionCancellationToken);
            var number = EmployeeNumberFormatter.Format(sequenceValue);
            var now = timeProvider.GetUtcNow();
            var entity = new Employee(Guid.NewGuid(), number, request.ChineseName, request.DepartmentId,
                request.HireDate, now, request.EnglishName, request.JobTitle, request.TerminationDate,
                request.Email, request.MobilePhone);
            dbContext.Employees.Add(entity);
            dbContext.AuditLogs.Add(AuditLogFactory.Create(currentUser, timeProvider, AuditActions.Created,
                nameof(Employee), entity.Id.ToString(), null, Snapshot(entity)));
            await SaveCreateAsync(transactionCancellationToken);
            return await GetForAdminAsync(entity.Id, transactionCancellationToken);
        }, cancellationToken);
    }

    public async Task<EmployeeDto> UpdateAsync(
        UpdateEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        MasterDataAuthorization.EnsureAdmin(currentUser);
        RequestValidator.Validate(request);
        return await dbContext.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            await ValidateDepartmentAsync(request.DepartmentId, transactionCancellationToken);
            var entity = await dbContext.Employees.SingleOrDefaultAsync(
                    x => x.Id == request.Id, transactionCancellationToken)
                ?? throw new EntityNotFoundException("找不到指定的員工。");
            EnsureRowVersion(entity.RowVersion, request.RowVersion);
            if (entity.DepartmentId != request.DepartmentId)
            {
                await EnsureNotDepartmentManagerAsync(entity.Id, transactionCancellationToken);
            }

            if (entity.HireDate != request.HireDate &&
                (await dbContext.AnnualLeaveEntitlements.AsNoTracking()
                    .AnyAsync(x => x.EmployeeId == entity.Id, transactionCancellationToken) ||
                 await dbContext.LeaveRequests.AsNoTracking()
                    .Where(x => x.EmployeeId == entity.Id)
                    .Join(dbContext.LeaveTypes.AsNoTracking(), x => x.LeaveTypeId, x => x.Id,
                        (_, leaveType) => leaveType.Code)
                    .AnyAsync(code => code == AnnualLeavePolicy.LeaveTypeCode, transactionCancellationToken)))
            {
                throw new ApplicationValidationException(
                    "此員工已有特休額度或特休申請，聘用日不可直接修改；請使用受控更正流程。");
            }

            var oldValues = Snapshot(entity);
            entity.Update(request.ChineseName, request.DepartmentId, request.HireDate,
                request.EnglishName, request.JobTitle, request.TerminationDate, request.Email,
                request.MobilePhone, timeProvider.GetUtcNow());
            dbContext.AuditLogs.Add(AuditLogFactory.Create(currentUser, timeProvider, AuditActions.Updated,
                nameof(Employee), entity.Id.ToString(), oldValues, Snapshot(entity)));
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
            var entity = await dbContext.Employees.SingleOrDefaultAsync(
                    x => x.Id == id, transactionCancellationToken)
                ?? throw new EntityNotFoundException("找不到指定的員工。");
            EnsureRowVersion(entity.RowVersion, rowVersion);
            if (isActive)
            {
                await ValidateDepartmentAsync(entity.DepartmentId, transactionCancellationToken);
            }
            else
            {
                await EnsureNotDepartmentManagerAsync(entity.Id, transactionCancellationToken);
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
                nameof(Employee), entity.Id.ToString(), oldValues, Snapshot(entity)));
            await SaveAsync(transactionCancellationToken);
        }, cancellationToken);
    }

    private async Task ValidateDepartmentAsync(Guid departmentId, CancellationToken cancellationToken)
    {
        var department = await dbContext.Departments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == departmentId, cancellationToken);
        if (department is null || !department.IsActive)
        {
            throw new ApplicationValidationException("員工必須指定存在且啟用的部門。");
        }
    }

    private async Task EnsureNotDepartmentManagerAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (await dbContext.Departments.AsNoTracking()
                .AnyAsync(x => x.ManagerEmployeeId == employeeId, cancellationToken))
        {
            throw new ApplicationValidationException(
                "此員工目前擔任部門主管。請先改派或移除部門主管，再轉調或停用員工。");
        }
    }

    private async Task<EmployeeDto> GetForAdminAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Employees.AsNoTracking().Include(x => x.Department)
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
    }

    private async Task SaveCreateAsync(CancellationToken cancellationToken)
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
            dbContext.IsUniqueConstraintViolation(exception, EmployeeNumberUniqueIndex))
        {
            logger.LogCritical(
                "The generated Employee Number was rejected by unique index {IndexName}.",
                EmployeeNumberUniqueIndex);
            throw new EmployeeNumberGenerationException(
                "無法產生唯一員工編號，請聯絡系統管理員。",
                exception);
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

    private static EmployeeDto Map(Employee entity) => new(
        entity.Id, entity.EmployeeNumber, entity.ChineseName, entity.EnglishName,
        entity.DepartmentId, entity.Department.Name, entity.JobTitle, entity.HireDate,
        entity.TerminationDate, entity.Email, entity.MobilePhone,
        entity.IsActive, Convert.ToBase64String(entity.RowVersion));

    private static object Snapshot(Employee entity) => new
    {
        entity.EmployeeNumber,
        entity.ChineseName,
        entity.EnglishName,
        entity.DepartmentId,
        entity.JobTitle,
        entity.HireDate,
        entity.TerminationDate,
        entity.IsActive
    };
}
