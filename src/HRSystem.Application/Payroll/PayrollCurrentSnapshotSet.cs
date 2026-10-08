using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Payroll;

public static class PayrollCurrentSnapshotSet
{
    public static IQueryable<PayrollPeriodEmployeeCurrentSnapshot> Query(
        IApplicationDbContext db, bool tracking = false)
    {
        var query = db.PayrollPeriodEmployeeCurrentSnapshots
            .Include(x => x.PayrollEmployeeSnapshot).ThenInclude(x => x.PayrollRun)
            .Include(x => x.PayrollEmployeeSnapshot).ThenInclude(x => x.Components)
                .ThenInclude(x => x.AttendanceAllowanceSnapshot).ThenInclude(x => x!.Evidence)
            .Include(x => x.PayrollEmployeeSnapshot).ThenInclude(x => x.Components)
                .ThenInclude(x => x.LeaveDeductionSnapshot).ThenInclude(x => x!.Evidence)
            .Include(x => x.PayrollEmployeeSnapshot).ThenInclude(x => x.Components)
                .ThenInclude(x => x.LaborInsuranceSnapshot).ThenInclude(x => x!.Contributions)
            .Include(x => x.PayrollEmployeeSnapshot).ThenInclude(x => x.Components)
                .ThenInclude(x => x.HealthInsuranceSnapshot).ThenInclude(x => x!.Evidence)
            .Include(x => x.PayrollEmployeeSnapshot).ThenInclude(x => x.Components)
                .ThenInclude(x => x.PeriodicAccrualSnapshot).ThenInclude(x => x!.Months)
            .Include(x => x.PayrollEmployeeSnapshot).ThenInclude(x => x.OvertimePaySnapshot)
                .ThenInclude(x => x!.IncludedComponents)
            .Include(x => x.PayrollEmployeeSnapshot).ThenInclude(x => x.OvertimePaySnapshot)
                .ThenInclude(x => x!.Buckets)
            .Include(x => x.PayrollEmployeeSnapshot).ThenInclude(x => x.OvertimePaySnapshot)
                .ThenInclude(x => x!.Days)
            .Include(x => x.PayrollEmployeeSnapshot).ThenInclude(x => x.TotalBlockingEvidence);
        return tracking ? query : query.AsNoTracking();
    }

    public static byte[] Fingerprint(Guid periodId,
        IEnumerable<PayrollPeriodEmployeeCurrentSnapshot> pointers) =>
        PayrollMonthFingerprintV1.Calculate(periodId, pointers.Select(pointer =>
        {
            var snapshot = pointer.PayrollEmployeeSnapshot;
            return new PayrollMonthFingerprintItem(snapshot.EmployeeId, snapshot.Id,
                snapshot.TotalSourceFingerprint, snapshot.GrossPay,
                snapshot.TotalDeductions, snapshot.NetPay,
                pointer.IsSourceChanged
                    ? PayrollCalculationStatus.SourceChanged
                    : snapshot.TotalCalculationStatus,
                pointer.IsSourceChanged);
        }));
}
