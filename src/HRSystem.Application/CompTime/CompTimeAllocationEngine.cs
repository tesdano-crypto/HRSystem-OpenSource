using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Domain.CompTime;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.CompTime;

internal sealed record CompTimeCredit(Guid Id, bool IsPool, decimal Remaining,
    DateOnly? Expiration, DateOnly Date, DateTimeOffset RecordedAt);

internal static class CompTimeAllocationEngine
{
    // Call within the same Serializable transaction as Consume/Restore. No date filtering:
    // Phase 1 expiry is metadata and ordering only, including already-past expiry dates.
    internal static async Task<IReadOnlyList<CompTimeCredit>> ReadAsync(
        IApplicationDbContext db, Guid employeeId, CancellationToken ct)
    {
        var ledger = await db.CompTimeTransactions.AsNoTracking().Where(x => x.EmployeeId == employeeId).ToListAsync(ct);
        var byId = ledger.ToDictionary(x => x.Id);
        var pools = await db.CompTimeLegacyPools.AsNoTracking().Where(x => x.EmployeeId == employeeId).ToListAsync(ct);
        var poolIds = pools.Select(x => x.Id).ToArray();
        var members = await db.CompTimeLegacyMembers.AsNoTracking().Where(x => poolIds.Contains(x.PoolId)).ToListAsync(ct);
        var memberIds = members.Select(x => x.TransactionId).ToHashSet();
        var ids = ledger.Select(x => x.Id).ToArray();
        var allocations = await db.CompTimeAllocations.AsNoTracking().Where(x => ids.Contains(x.ConsumeTransactionId)).ToListAsync(ct);
        var allocationIds = allocations.Select(x => x.Id).ToArray();
        var returns = await db.CompTimeAllocationReturns.AsNoTracking().Where(x => allocationIds.Contains(x.AllocationId)).ToListAsync(ct);
        var returned = returns.Select(x => x.AllocationId).ToHashSet();
        var legacyReturns = await db.CompTimeLegacyReturns.AsNoTracking().Where(x => poolIds.Contains(x.PoolId)).ToListAsync(ct);
        var training = await db.TrainingCompTimeGrants.AsNoTracking().Where(x => x.EmployeeId == employeeId).ToDictionaryAsync(x => x.Id, ct);
        foreach (var pool in pools)
        {
            var source = members.Where(x => x.PoolId == pool.Id).Select(x => byId.GetValueOrDefault(x.TransactionId)).ToArray();
            if (source.Any(x => x is null) || source.Length != pool.LedgerCount ||
                source.Sum(x => x!.SignedHours) != pool.BaselineHours) Fail();
        }
        foreach (var entry in ledger.Where(x => !memberIds.Contains(x.Id)))
        {
            if (entry.TransactionType == CompTimeTransactionType.Consume &&
                allocations.Where(x => x.ConsumeTransactionId == entry.Id).Sum(x => x.AllocatedHours) != entry.Hours) Fail();
            if (entry.TransactionType == CompTimeTransactionType.Restore)
            {
                var consumed = ledger.SingleOrDefault(x => x.SourceType == CompTimeSourceType.LeaveRequest &&
                    x.SourceId == entry.SourceId && x.TransactionType == CompTimeTransactionType.Consume);
                if (consumed is null || consumed.Hours != entry.Hours) Fail();
                var old = legacyReturns.SingleOrDefault(x => x.RestoreTransactionId == entry.Id);
                var restoredAllocations = returns.Where(x => x.RestoreTransactionId == entry.Id)
                    .Select(x => allocations.Single(a => a.Id == x.AllocationId)).ToArray();
                if (old is not null)
                {
                    if (old.ConsumeTransactionId != consumed!.Id || !members.Any(x => x.TransactionId == consumed.Id && x.PoolId == old.PoolId) || restoredAllocations.Length != 0) Fail();
                }
                else if (restoredAllocations.Sum(x => x.AllocatedHours) != entry.Hours ||
                    restoredAllocations.Any(x => x.ConsumeTransactionId != consumed!.Id)) Fail();
            }
        }
        var credits = new List<CompTimeCredit>();
        foreach (var pool in pools)
        {
            var restored = legacyReturns.Where(x => x.PoolId == pool.Id).Sum(x => byId[x.RestoreTransactionId].Hours);
            var used = allocations.Where(x => x.LegacyPoolId == pool.Id && !returned.Contains(x.Id)).Sum(x => x.AllocatedHours);
            var localDate = DateOnly.FromDateTime(pool.CutoverAtUtc.ToOffset(TimeSpan.FromHours(8)).DateTime);
            credits.Add(new(pool.Id, true, pool.BaselineHours + restored - used, null, localDate, pool.CutoverAtUtc));
        }
        foreach (var grant in ledger.Where(x => x.TransactionType == CompTimeTransactionType.Grant && !memberIds.Contains(x.Id)))
        {
            DateOnly? expiry = null;
            if (grant.SourceType == CompTimeSourceType.TrainingCompTimeGrant)
            {
                if (!training.TryGetValue(grant.SourceId!.Value, out var origin) || origin.Status != TrainingCompTimeStatus.Approved || origin.ApprovedHours != grant.Hours) Fail();
                expiry = training[grant.SourceId.Value].ExpirationDate;
            }
            credits.Add(new(grant.Id, false, grant.Hours - allocations.Where(x => x.GrantTransactionId == grant.Id && !returned.Contains(x.Id)).Sum(x => x.AllocatedHours), expiry, grant.EffectiveDate, grant.CreatedAtUtc));
        }
        if (allocations.Any(x => !credits.Any(c => c.IsPool ? x.LegacyPoolId == c.Id : x.GrantTransactionId == c.Id)) ||
            credits.Any(x => x.Remaining < 0) || credits.Sum(x => x.Remaining) != ledger.Sum(x => x.SignedHours)) Fail();
        return credits.OrderBy(x => x.Expiration.HasValue ? 0 : 1).ThenBy(x => x.Expiration)
            .ThenBy(x => x.Expiration.HasValue ? x.Date : DateOnly.MinValue)
            .ThenBy(x => x.RecordedAt).ThenBy(x => x.Id).ToArray();
    }
    internal static async Task AllocateAsync(IApplicationDbContext db, CompTimeTransaction consume, CancellationToken ct)
    {
        var credits = await ReadAsync(db, consume.EmployeeId, ct);
        var remaining = consume.Hours;
        var planned = new List<CompTimeAllocation>();
        foreach (var credit in credits)
        {
            var hours = Math.Min(remaining, credit.Remaining);
            if (hours <= 0) continue;
            planned.Add(new(consume.Id, credit.IsPool ? null : credit.Id, credit.IsPool ? credit.Id : null, hours));
            remaining -= hours;
        }
        if (remaining != 0) throw new ApplicationValidationException("補休可分配餘額不足，未建立扣抵。");
        db.CompTimeAllocations.AddRange(planned);
    }
    internal static async Task RestoreAsync(IApplicationDbContext db, CompTimeTransaction consumed,
        CompTimeTransaction restore, CancellationToken ct)
    {
        await ReadAsync(db, consumed.EmployeeId, ct);
        var allocations = await db.CompTimeAllocations.AsNoTracking().Where(x => x.ConsumeTransactionId == consumed.Id).ToListAsync(ct);
        if (allocations.Count == 0)
        {
            var member = await db.CompTimeLegacyMembers.SingleOrDefaultAsync(x => x.TransactionId == consumed.Id, ct);
            if (member is null) Fail();
            db.CompTimeLegacyReturns.Add(new(restore.Id, consumed.Id, member!.PoolId));
        }
        else
        {
            if (allocations.Sum(x => x.AllocatedHours) != restore.Hours) Fail();
            db.CompTimeAllocationReturns.AddRange(allocations.Select(x => new CompTimeAllocationReturn(x.Id, restore.Id)));
        }
    }
    private static void Fail() => throw new ApplicationValidationException("補休帳本與可分配餘額不一致，已停止作業；請由管理者檢查切換基準，系統不會自動補差額。");
}
