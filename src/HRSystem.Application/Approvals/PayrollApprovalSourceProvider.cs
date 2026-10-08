using System.Globalization;
using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Payroll;
using HRSystem.Domain.Approvals;
using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Approvals;

public sealed class PayrollApprovalSourceProvider(IApplicationDbContext db)
    : IApprovalSourceProvider
{
    public const short FingerprintVersion = PayrollMonthFingerprintV1.Version;
    public ApprovalType ApprovalType => ApprovalType.Payroll;

    public async Task<ApprovalSourceSnapshot> GetSnapshotAsync(
        Guid sourceId, CancellationToken cancellationToken = default)
    {
        var period = await db.PayrollPeriods.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == sourceId, cancellationToken)
            ?? throw new ApplicationValidationException("找不到薪資月份。");
        if (period.Status == PayrollPeriodStatus.Finalized)
            throw new ApplicationValidationException("已結算薪資月份不可送簽。");

        var pointers = await PayrollCurrentSnapshotSet.Query(db)
            .Where(x => x.PayrollPeriodId == period.Id)
            .ToListAsync(cancellationToken);
        var eligibleCount = (await PayrollEligibilityResolver.ResolveAsync(
            db, period, null, cancellationToken)).Count;
        if (pointers.Count == 0 || pointers.Count != eligibleCount)
            throw new ApplicationValidationException(
                "尚有符合資格員工未建立薪資試算，無法送簽。");
        if (pointers.Any(pointer => pointer.IsSourceChanged ||
                pointer.PayrollEmployeeSnapshot.TotalCalculationStatus !=
                    PayrollCalculationStatus.Resolved ||
                !pointer.PayrollEmployeeSnapshot.NetPay.HasValue ||
                pointer.PayrollEmployeeSnapshot.TotalSourceFingerprintVersion is null ||
                pointer.PayrollEmployeeSnapshot.TotalSourceFingerprint is not { Length: 32 }))
        {
            throw new ApplicationValidationException(
                "薪資月份仍有未完成、資料已變更或待確認項目，無法送簽。");
        }

        var fingerprint = PayrollCurrentSnapshotSet.Fingerprint(period.Id, pointers);
        var culture = CultureInfo.GetCultureInfo("zh-TW");
        var gross = pointers.Sum(x => x.PayrollEmployeeSnapshot.GrossPay);
        var deductions = pointers.Sum(x => x.PayrollEmployeeSnapshot.TotalDeductions);
        var net = pointers.Sum(x => x.PayrollEmployeeSnapshot.NetPay!.Value);
        var title = $"{period.Year} 年 {period.Month:00} 月薪資待核准";
        var summary = new[]
        {
            new ApprovalSummaryItemDto("薪資月份", $"{period.Year}/{period.Month:00}"),
            new ApprovalSummaryItemDto("員工人數", pointers.Count.ToString(culture)),
            new ApprovalSummaryItemDto("應發總額", gross.ToString("N0", culture)),
            new ApprovalSummaryItemDto("扣款總額", deductions.ToString("N0", culture)),
            new ApprovalSummaryItemDto("實領總額", net.ToString("N0", culture))
        };
        return new(ApprovalType.Payroll, nameof(PayrollPeriod), period.Id.ToString(),
            FingerprintVersion, fingerprint, title, summary);
    }
}
