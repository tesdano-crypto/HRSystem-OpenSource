using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.CompTime;
using HRSystem.Domain.LeaveRequests;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.CompTime;

public sealed partial class CompTimeService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICompTimeService
{
    private static readonly TimeZoneInfo TaipeiZone = ResolveTaipeiZone();

    public Task<CompTimeBalanceDto> GetMyBalanceAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureSelfService();
        var employeeId = currentUser.EmployeeId ??
            throw new ForbiddenAccessException("帳號尚未綁定員工資料，請洽系統管理員。");
        return BuildBalanceAsync(employeeId, cancellationToken);
    }

    public async Task<IReadOnlyList<CompTimeEmployeeSummaryDto>> GetAdminSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureManage();
        var employees = await db.Employees.AsNoTracking()
            .OrderBy(x => x.EmployeeNumber)
            .Select(x => new
            {
                x.Id,
                x.EmployeeNumber,
                x.ChineseName,
                DepartmentName = x.Department.Name
            })
            .ToListAsync(cancellationToken);
        var totals = await db.CompTimeTransactions.AsNoTracking()
            .GroupBy(x => x.EmployeeId)
            .Select(group => new
            {
                EmployeeId = group.Key,
                Balance = group.Sum(x =>
                    x.TransactionType == CompTimeTransactionType.Consume
                        ? -x.Hours
                        : x.Hours)
            })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Balance, cancellationToken);
        return employees.Select(x => new CompTimeEmployeeSummaryDto(
            x.Id,
            x.EmployeeNumber,
            x.ChineseName,
            x.DepartmentName,
            totals.GetValueOrDefault(x.Id))).ToList();
    }

    public Task<CompTimeBalanceDto> GetAdminBalanceAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        EnsureManage();
        return BuildBalanceAsync(employeeId, cancellationToken);
    }

    public async Task<CompTimeBalanceDto> CreateLegacyOpeningBalanceAsync(
        CreateLegacyCompTimeOpeningBalanceRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureManage();
        CompTimePolicy.ValidateHours(request.Hours);
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ApplicationValidationException("補休期初餘額原因為必填欄位。");
        }

        try
        {
            return await db.ExecuteSerializableAsync(async transactionToken =>
            {
                if (!await db.Employees.AsNoTracking()
                    .AnyAsync(x => x.Id == request.EmployeeId, transactionToken))
                {
                    throw new EntityNotFoundException("找不到指定的員工。");
                }

                if (await db.CompTimeTransactions.AsNoTracking().AnyAsync(x =>
                    x.EmployeeId == request.EmployeeId &&
                    x.SourceType == CompTimeSourceType.LegacyOpeningBalance &&
                    x.EffectiveDate == request.CutoverDate,
                    transactionToken))
                {
                    throw new ApplicationValidationException(
                        "此員工在指定接管日期已存在補休歷史期初餘額，請勿重複建立。");
                }

                var now = timeProvider.GetUtcNow();
                await CompTimeAllocationEngine.ReadAsync(db, request.EmployeeId, transactionToken);
                var entity = new CompTimeTransaction(
                    Guid.NewGuid(),
                    request.EmployeeId,
                    CompTimeTransactionType.Grant,
                    request.Hours,
                    request.CutoverDate,
                    CompTimeSourceType.LegacyOpeningBalance,
                    null,
                    request.Reason,
                    RequireUserId(),
                    now);
                db.CompTimeTransactions.Add(entity);
                AddAudit(
                    AuditActions.CompTimeLegacyOpeningBalanceCreated,
                    entity,
                    new
                    {
                        entity.EmployeeId,
                        entity.Hours,
                        CutoverDate = entity.EffectiveDate,
                        entity.SourceType
                    });
                await db.SaveChangesAsync(transactionToken);
                await CompTimeAllocationEngine.ReadAsync(db, request.EmployeeId, transactionToken);
                return await BuildBalanceAsync(request.EmployeeId, transactionToken);
            }, cancellationToken);
        }
        catch (DbUpdateException exception) when (db.IsUniqueConstraintViolation(
            exception,
            "UX_CompTimeTransactions_LegacyOpeningBalance"))
        {
            db.ClearTrackedChanges();
            throw new ApplicationValidationException(
                "此員工在指定接管日期已存在補休歷史期初餘額，請勿重複建立。");
        }
        catch
        {
            db.ClearTrackedChanges();
            throw;
        }
    }

    public async Task ConsumeForApprovalAsync(
        LeaveRequest request,
        CancellationToken cancellationToken)
    {
        if (!CompTimePolicy.IsCompTime(request.LeaveType.Code))
        {
            return;
        }

        CompTimePolicy.ValidateHours(request.DurationHours);
        if (await db.CompTimeTransactions.AsNoTracking().AnyAsync(x =>
            x.SourceType == CompTimeSourceType.LeaveRequest &&
            x.SourceId == request.Id &&
            x.TransactionType == CompTimeTransactionType.Consume,
            cancellationToken))
        {
            return;
        }

        var available = await CalculateBalanceAsync(request.EmployeeId, cancellationToken);
        if (available < request.DurationHours)
        {
            throw new ApplicationValidationException(
                $"補休可用時數不足，目前可用 {available:0.##} 小時，本次申請需要 {request.DurationHours:0.##} 小時。");
        }

        var entity = new CompTimeTransaction(
            Guid.NewGuid(),
            request.EmployeeId,
            CompTimeTransactionType.Consume,
            request.DurationHours,
            LocalDate(request.StartAt),
            CompTimeSourceType.LeaveRequest,
            request.Id,
            "核准補休請假",
            RequireUserId(),
            timeProvider.GetUtcNow());
        await CompTimeAllocationEngine.AllocateAsync(db, entity, cancellationToken);
        db.CompTimeTransactions.Add(entity);
        AddAudit(
            AuditActions.CompTimeConsumed,
            entity,
            new
            {
                entity.EmployeeId,
                LeaveRequestId = request.Id,
                entity.Hours
            });
    }

    public async Task RestoreAfterCancellationAsync(
        LeaveRequest request,
        CancellationToken cancellationToken)
    {
        if (!CompTimePolicy.IsCompTime(request.LeaveType.Code))
        {
            return;
        }

        var consumed = await db.CompTimeTransactions.AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.SourceType == CompTimeSourceType.LeaveRequest &&
                x.SourceId == request.Id &&
                x.TransactionType == CompTimeTransactionType.Consume,
                cancellationToken)
            ?? throw new ApplicationValidationException("此補休申請尚未建立使用紀錄，無法返還。");
        if (await db.CompTimeTransactions.AsNoTracking().AnyAsync(x =>
            x.SourceType == CompTimeSourceType.LeaveRequest &&
            x.SourceId == request.Id &&
            x.TransactionType == CompTimeTransactionType.Restore,
            cancellationToken))
        {
            return;
        }

        var entity = new CompTimeTransaction(
            Guid.NewGuid(),
            request.EmployeeId,
            CompTimeTransactionType.Restore,
            consumed.Hours,
            TaipeiToday(),
            CompTimeSourceType.LeaveRequest,
            request.Id,
            "已核准補休撤簽返還",
            RequireUserId(),
            timeProvider.GetUtcNow());
        await CompTimeAllocationEngine.RestoreAsync(db, consumed, entity, cancellationToken);
        db.CompTimeTransactions.Add(entity);
        AddAudit(
            AuditActions.CompTimeRestored,
            entity,
            new
            {
                entity.EmployeeId,
                LeaveRequestId = request.Id,
                entity.Hours
            });
    }

    private async Task<CompTimeBalanceDto> BuildBalanceAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking()
            .Where(x => x.Id == employeeId)
            .Select(x => new { x.Id, x.EmployeeNumber, x.ChineseName })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("找不到指定的員工。");
        var transactions = await db.CompTimeTransactions.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var granted = transactions
            .Where(x => x.TransactionType == CompTimeTransactionType.Grant)
            .Sum(x => x.Hours);
        var consumed = transactions
            .Where(x => x.TransactionType == CompTimeTransactionType.Consume)
            .Sum(x => x.Hours);
        var restored = transactions
            .Where(x => x.TransactionType == CompTimeTransactionType.Restore)
            .Sum(x => x.Hours);
        return new CompTimeBalanceDto(
            employee.Id,
            employee.EmployeeNumber,
            employee.ChineseName,
            granted,
            consumed,
            restored,
            granted + restored - consumed,
            transactions.Select(Map).ToList());
    }

    private async Task<decimal> CalculateBalanceAsync(
        Guid employeeId,
        CancellationToken cancellationToken) =>
        await db.CompTimeTransactions.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .SumAsync(x => x.TransactionType == CompTimeTransactionType.Consume
                ? -x.Hours
                : x.Hours,
                cancellationToken);

    private static CompTimeTransactionDto Map(CompTimeTransaction entity) => new(
        entity.Id,
        entity.TransactionType,
        entity.Hours,
        entity.SignedHours,
        entity.EffectiveDate,
        entity.SourceType,
        entity.SourceId,
        entity.Reason,
        entity.CreatedAtUtc,
        entity.CreatedBy);

    private void AddAudit(
        string action,
        CompTimeTransaction entity,
        object values) =>
        db.AuditLogs.Add(AuditLogFactory.Create(
            currentUser,
            timeProvider,
            action,
            nameof(CompTimeTransaction),
            entity.Id.ToString(),
            null,
            values));

    private void EnsureSelfService()
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.LeaveRequestSelfService))
        {
            throw new ForbiddenAccessException("請先登入。");
        }
    }

    private void EnsureManage()
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.LeaveManage))
        {
            throw new ForbiddenAccessException("您沒有管理補休的權限。");
        }
    }

    private string RequireUserId() => currentUser.UserId ??
        throw new ForbiddenAccessException("無法識別目前登入帳號。");

    private DateOnly TaipeiToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TaipeiZone).DateTime);

    private static DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(instant, TaipeiZone).DateTime);

    private static TimeZoneInfo ResolveTaipeiZone()
    {
        foreach (var id in new[] { "Asia/Taipei", "Taipei Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        throw new InvalidOperationException("找不到 Asia/Taipei 時區。");
    }
}
