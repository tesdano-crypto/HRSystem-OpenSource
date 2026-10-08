using System.Security.Cryptography;
using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Approvals;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.Approvals;
using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Payroll;

public sealed class PayrollFinalizationService(IApplicationDbContext db,
    ICurrentUser currentUser, IApprovalActorDirectory actorDirectory,
    TimeProvider timeProvider, OrganizationBranding? organization = null) : IPayrollFinalizationService
{
    private string CompanyName => organization?.Name ?? "Example Company";

    public async Task<PayrollFinalizationDto?> GetForPeriodAsync(Guid periodId,
        CancellationToken ct = default)
    {
        EnsurePayrollView();
        var entity = await Finalizations(includeEmployees: false).SingleOrDefaultAsync(
            x => x.PayrollPeriodId == periodId, ct);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<IReadOnlyList<PayrollFinalizationDto>> GetHistoryAsync(
        CancellationToken ct = default)
    {
        EnsurePayrollView();
        var values = await Finalizations(includeEmployees: false)
            .OrderByDescending(x => x.PayrollPeriod.Year)
            .ThenByDescending(x => x.PayrollPeriod.Month).ToListAsync(ct);
        return values.Select(ToDto).ToArray();
    }

    public async Task<PayrollFinalizationDto> GetAsync(Guid finalizationId,
        CancellationToken ct = default)
    {
        EnsurePayrollView();
        var entity = await Finalizations(includeEmployees: true)
            .SingleOrDefaultAsync(x => x.Id == finalizationId, ct)
            ?? throw new ApplicationValidationException("找不到正式結算紀錄。");
        return ToDto(entity);
    }

    public async Task<PayrollFinalizationDto> FinalizeAsync(Guid periodId,
        CancellationToken ct = default)
    {
        EnsureFinalize();
        try
        {
            return await db.ExecuteSerializableAsync(async token =>
            {
                var period = await db.PayrollPeriods.SingleOrDefaultAsync(
                    x => x.Id == periodId, token)
                    ?? throw new ApplicationValidationException("找不到薪資月份。");
                if (period.Status == PayrollPeriodStatus.Finalized ||
                    await db.PayrollFinalizations.AnyAsync(x =>
                        x.PayrollPeriodId == periodId, token))
                    throw new ApplicationValidationException("此薪資月份已完成正式結算。");

                var pointers = await PayrollCurrentSnapshotSet.Query(db, tracking: true)
                    .Where(x => x.PayrollPeriodId == periodId).ToListAsync(token);
                var eligibleCount = (await PayrollEligibilityResolver.ResolveAsync(
                    db, period, null, token)).Count;
                if (eligibleCount == 0 || pointers.Count != eligibleCount)
                    throw new ApplicationValidationException(
                        "全員薪資快照尚未完成，無法正式結算。");
                foreach (var pointer in pointers)
                    ValidatePointer(pointer);

                var monthFingerprint = PayrollCurrentSnapshotSet.Fingerprint(periodId, pointers);
                var approval = await db.Approvals.AsNoTracking()
                    .Where(x => x.ApprovalType == ApprovalType.Payroll &&
                        x.SourceEntityType == nameof(PayrollPeriod) &&
                        x.SourceEntityId == periodId.ToString() &&
                        x.Status == ApprovalStatus.Approved &&
                        x.SourceFingerprintVersion == PayrollMonthFingerprintV1.Version)
                    .OrderByDescending(x => x.DecisionAtUtc)
                    .FirstOrDefaultAsync(token);
                if (approval is null || approval.DecisionAtUtc is null ||
                    approval.DecisionByUserId is null || approval.DecisionChannel is null)
                    throw new ApplicationValidationException(
                        "目前薪資版本尚未取得相符的核准，無法正式結算。");
                if (!CryptographicOperations.FixedTimeEquals(
                        approval.SourceFingerprint, monthFingerprint))
                    throw new ApplicationValidationException(
                        "薪資資料已變更，需重新送簽並取得核准後才能正式結算。");

                var approver = await actorDirectory.FindActiveAsync(
                    approval.DecisionByUserId, token);
                var userId = currentUser.UserId
                    ?? throw new ForbiddenAccessException("無法確認目前使用者。");
                var entity = new PayrollFinalization(Guid.NewGuid(), period.Id,
                    approval.Id, approval.DecisionByUserId,
                    approver?.DisplayName ?? approval.DecisionByUserId,
                    approval.DecisionAtUtc.Value, approval.DecisionChannel.Value,
                    userId, currentUser.DisplayName ?? userId,
                    timeProvider.GetUtcNow(), PayrollMonthFingerprintV1.Version,
                    monthFingerprint);
                foreach (var pointer in pointers.OrderBy(x => x.EmployeeId))
                    entity.AddEmployee(new PayrollFinalEmployeeSnapshot(
                        Guid.NewGuid(), entity.Id, pointer.PayrollEmployeeSnapshot));

                period.FinalizePeriod();
                db.PayrollFinalizations.Add(entity);
                db.AuditLogs.Add(AuditLogFactory.Create(currentUser, timeProvider,
                    AuditActions.PayrollFinalized, nameof(PayrollFinalization),
                    entity.Id.ToString(), null, new
                    {
                        FinalizationId = entity.Id,
                        PeriodId = period.Id,
                        entity.ApprovalId,
                        entity.EmployeeCount,
                        entity.GrossPay,
                        entity.TotalDeductions,
                        entity.NetPay,
                        FingerprintVersion = entity.MonthFingerprintVersion,
                        Fingerprint = Convert.ToHexString(entity.MonthFingerprint),
                        entity.FinalizedByUserId,
                        entity.FinalizedAtUtc
                    }));
                await db.SaveChangesAsync(token);
                return ToDto(entity, period.Year, period.Month);
            }, ct);
        }
        catch (DbUpdateException ex) when (db.IsUniqueConstraintViolation(
                   ex, "UX_PayrollFinalizations_Period"))
        {
            throw new ApplicationValidationException("此薪資月份已完成正式結算。");
        }
    }

    public async Task<PayrollPayslipDto> GetPayslipAsync(Guid id,
        CancellationToken ct = default)
    {
        EnsurePayrollView();
        return ToPayslip(await Payslips().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ApplicationValidationException("找不到薪資單。"));
    }

    public async Task<IReadOnlyList<PayrollPayslipDto>> GetMyPayslipsAsync(
        CancellationToken ct = default)
    {
        var employeeId = RequireSelf();
        var values = await Payslips().Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.PayrollFinalization.PayrollPeriod.Year)
            .ThenByDescending(x => x.PayrollFinalization.PayrollPeriod.Month)
            .ToListAsync(ct);
        return values.Select(ToPayslip).ToArray();
    }

    public async Task<PayrollPayslipDto> GetMyPayslipAsync(Guid id,
        CancellationToken ct = default)
    {
        var employeeId = RequireSelf();
        var value = await Payslips().SingleOrDefaultAsync(
            x => x.Id == id && x.EmployeeId == employeeId, ct)
            ?? throw new ForbiddenAccessException("您只能查看自己的薪資單。");
        return ToPayslip(value);
    }

    private static void ValidatePointer(PayrollPeriodEmployeeCurrentSnapshot pointer)
    {
        var snapshot = pointer.PayrollEmployeeSnapshot;
        if (pointer.IsSourceChanged || snapshot.SetupStatus != PayrollEmployeeSetupStatus.Ready ||
            snapshot.TotalCalculationStatus != PayrollCalculationStatus.Resolved ||
            snapshot.BlockingComponentCount != 0 || snapshot.NetPay is null ||
            snapshot.NetPay < 0 || snapshot.TotalSourceFingerprintVersion !=
            PayrollTotalFingerprintV1.Version || snapshot.TotalSourceFingerprint is not { Length: 32 })
            throw new ApplicationValidationException(
                $"員工 {snapshot.EmployeeCode} 的薪資仍有待處理項目。");
        var result = PayrollTotalCalculator.Calculate(snapshot.Components.Select(x =>
            new PayrollTotalComponentInput(x.PayrollComponentDefinitionId,
                x.ComponentCode, x.Category, x.SourceType, x.SourceId,
                x.CalculationStatus, x.ResolvedAmount)));
        if (result.CalculationStatus != PayrollCalculationStatus.Resolved ||
            result.NetPay != snapshot.NetPay || result.KnownGrossPay != snapshot.GrossPay ||
            result.KnownDeductions != snapshot.TotalDeductions ||
            !CryptographicOperations.FixedTimeEquals(result.SourceFingerprint,
                snapshot.TotalSourceFingerprint))
            throw new ApplicationValidationException(
                $"員工 {snapshot.EmployeeCode} 的薪資快照一致性驗證失敗。");
    }

    private IQueryable<PayrollFinalization> Finalizations(bool includeEmployees)
    {
        var query = db.PayrollFinalizations.AsNoTracking()
            .Include(x => x.PayrollPeriod).AsQueryable();
        return includeEmployees ? query.Include(x => x.EmployeeSnapshots) : query;
    }

    private IQueryable<PayrollFinalEmployeeSnapshot> Payslips() =>
        db.PayrollFinalEmployeeSnapshots.AsNoTracking()
            .Include(x => x.PayrollFinalization).ThenInclude(x => x.PayrollPeriod)
            .Include(x => x.PayrollEmployeeSnapshot).ThenInclude(x => x.Components);

    private static PayrollFinalizationDto ToDto(PayrollFinalization x) =>
        ToDto(x, x.PayrollPeriod.Year, x.PayrollPeriod.Month);

    private static PayrollFinalizationDto ToDto(PayrollFinalization x, int year, int month) =>
        new(x.Id, x.PayrollPeriodId, year, month, x.FinalVersionNumber, x.Status,
            x.FinalizedAtUtc, x.FinalizedByDisplayName, x.ApprovalId, x.ApprovedAtUtc,
            x.ApprovedByDisplayName, x.ApprovalChannel, x.MonthFingerprintVersion,
            Convert.ToHexString(x.MonthFingerprint), x.EmployeeCount, x.GrossPay,
            x.TotalDeductions, x.NetPay, x.EmployeeSnapshots.OrderBy(e => e.EmployeeNumber)
                .Select(e => new PayrollFinalEmployeeDto(e.Id, e.EmployeeId,
                    e.EmployeeNumber, e.EmployeeName, e.DepartmentName, e.GrossPay,
                    e.TotalDeductions, e.NetPay)).ToArray());

    private PayrollPayslipDto ToPayslip(PayrollFinalEmployeeSnapshot x)
    {
        var period = x.PayrollFinalization.PayrollPeriod;
        var lines = x.PayrollEmployeeSnapshot.Components
            .Where(c => c.CalculationStatus == PayrollCalculationStatus.Resolved &&
                c.ResolvedAmount.HasValue && c.ResolvedAmount.Value != 0 &&
                c.Category is PayrollComponentCategory.Earning or PayrollComponentCategory.Deduction)
            .OrderBy(c => c.ComponentCode)
            .Select(c => new PayrollPayslipLineDto(c.ComponentCode, c.ComponentName,
                c.Category, c.ResolvedAmount!.Value)).ToArray();
        return new(x.Id, x.PayrollFinalizationId, CompanyName, period.Year, period.Month,
            period.PeriodStart, period.PeriodEnd, x.EmployeeNumber, x.EmployeeName,
            x.DepartmentName, x.PayrollFinalization.FinalizedAtUtc,
            x.PayrollFinalization.FinalizedByDisplayName, x.GrossPay,
            x.TotalDeductions, x.NetPay,
            lines.Where(l => l.Category == PayrollComponentCategory.Earning).ToArray(),
            lines.Where(l => l.Category == PayrollComponentCategory.Deduction).ToArray(),
            x.SnapshotFingerprintVersion, Convert.ToHexString(x.SnapshotFingerprint),
            x.PayrollEmployeeSnapshot.PayrollPlanCode);
    }

    private void EnsurePayrollView()
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(PolicyNames.PayrollView))
            throw new ForbiddenAccessException("您沒有薪資查看權限。");
    }

    private void EnsureFinalize()
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(PolicyNames.PayrollFinalize))
            throw new ForbiddenAccessException("您沒有薪資正式結算權限。");
    }

    private Guid RequireSelf()
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(PolicyNames.PayslipViewSelf))
            throw new ForbiddenAccessException("您沒有薪資單查看權限。");
        return currentUser.EmployeeId
            ?? throw new ForbiddenAccessException("目前帳號未綁定員工資料。");
    }
}
