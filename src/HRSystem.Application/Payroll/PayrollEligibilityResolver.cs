using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Payroll;

internal sealed record PayrollEligibleEmployee(
    Employee Employee,
    PayrollParticipationDecision Participation,
    EmployeePayrollPayCycle? ExplicitPayCycle);

internal static class PayrollEligibilityResolver
{
    public static async Task<IReadOnlyList<PayrollEligibleEmployee>> ResolveAsync(
        IApplicationDbContext db, PayrollPeriod period, Guid[]? employeeIds,
        CancellationToken ct)
    {
        var query = db.Employees.Include(x => x.Department)
            .Where(x => x.HireDate <= period.PeriodEnd &&
                (!x.TerminationDate.HasValue ||
                    x.TerminationDate >= period.PeriodStart) &&
                (x.IsActive || x.TerminationDate.HasValue));
        if (employeeIds is not null)
            query = query.Where(x => employeeIds.Contains(x.Id));
        var employees = await query.OrderBy(x => x.EmployeeNumber).ToListAsync(ct);
        var ids = employees.Select(x => x.Id).ToArray();
        var cycles = await db.EmployeePayrollPayCycles.AsNoTracking()
            .Where(x => ids.Contains(x.EmployeeId) && x.IsActive &&
                x.EffectiveFrom <= period.PeriodStart &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= period.PeriodStart))
            .ToListAsync(ct);

        var results = new List<PayrollEligibleEmployee>();
        foreach (var employee in employees)
        {
            var effective = cycles.Where(x => x.EmployeeId == employee.Id)
                .ToArray();
            var decision = PayrollParticipationPolicy.Evaluate(employee.IsActive,
                employee.HireDate, employee.TerminationDate,
                period.PeriodStart, period.PeriodEnd, effective);
            if (decision.Status == PayrollParticipationStatus.AmbiguousConfiguration)
                throw new ApplicationValidationException(
                    $"員工 {employee.EmployeeNumber} 的發薪方式設定期間重疊。");
            if (decision.IsEligible)
                results.Add(new(employee, decision, effective.SingleOrDefault()));
        }
        return results;
    }

    public static async Task<(Employee Employee,
        PayrollParticipationDecision Participation,
        EmployeePayrollPayCycle? ExplicitPayCycle)> ResolveEmployeeAsync(
        IApplicationDbContext db, Guid employeeId, PayrollPeriod period,
        CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking().Include(x => x.Department)
            .SingleOrDefaultAsync(x => x.Id == employeeId, ct)
            ?? throw new ApplicationValidationException("找不到員工資料。");
        var cycles = await db.EmployeePayrollPayCycles.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.IsActive &&
                x.EffectiveFrom <= period.PeriodStart &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= period.PeriodStart))
            .ToListAsync(ct);
        var decision = PayrollParticipationPolicy.Evaluate(employee.IsActive,
            employee.HireDate, employee.TerminationDate,
            period.PeriodStart, period.PeriodEnd, cycles);
        return (employee, decision, cycles.Count == 1 ? cycles[0] : null);
    }
}
