using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Payroll;

public sealed class InsuranceManagementService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IInsuranceManagementService
{
    private static readonly TimeZoneInfo Taipei = ResolveTaipei();

    public async Task<IReadOnlyList<InsuranceEmployeeListItemDto>> SearchEmployeesAsync(
        string? keyword = null,
        bool includeInactive = false,
        bool needsSetupOnly = false,
        CancellationToken ct = default)
    {
        EnsureView();
        var query = db.Employees.AsNoTracking().Include(x => x.Department).AsQueryable();
        if (!includeInactive)
            query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var value = keyword.Trim();
            query = query.Where(x => x.EmployeeNumber.Contains(value) ||
                x.ChineseName.Contains(value));
        }

        var employees = await query.OrderBy(x => x.EmployeeNumber).ToListAsync(ct);
        if (employees.Count == 0)
            return [];

        var ids = employees.Select(x => x.Id).ToArray();
        var labor = await db.EmployeeLaborInsuranceEnrollments.AsNoTracking()
            .Where(x => ids.Contains(x.EmployeeId) && x.IsActive)
            .OrderBy(x => x.EffectiveFrom).ToListAsync(ct);
        var occupational = await db.EmployeeOccupationalInsuranceEnrollments
            .AsNoTracking().Where(x => ids.Contains(x.EmployeeId) && x.IsActive)
            .OrderBy(x => x.EffectiveFrom).ToListAsync(ct);
        var health = await db.EmployeeHealthInsuranceEnrollments.AsNoTracking()
            .Where(x => ids.Contains(x.EmployeeId) && x.IsActive)
            .OrderBy(x => x.EffectiveFrom).ToListAsync(ct);
        var today = Today();

        var output = employees.Select(employee => ToListItem(employee.Id,
                employee.EmployeeNumber, employee.ChineseName,
                employee.Department.Name, employee.IsActive, employee.HireDate,
                employee.TerminationDate,
                labor.Where(x => x.EmployeeId == employee.Id).ToArray(),
                occupational.Where(x => x.EmployeeId == employee.Id).ToArray(),
                health.Where(x => x.EmployeeId == employee.Id).ToArray(), today))
            .Where(x => !needsSetupOnly || x.NeedsSetup)
            .ToArray();
        return output;
    }

    public async Task<InsuranceEmployeeDetailDto> GetEmployeeAsync(
        Guid employeeId, CancellationToken ct = default)
    {
        EnsureView();
        var employee = await db.Employees.AsNoTracking().Include(x => x.Department)
            .SingleOrDefaultAsync(x => x.Id == employeeId, ct)
            ?? throw new ApplicationValidationException("找不到員工資料。");
        var labor = await db.EmployeeLaborInsuranceEnrollments.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.EffectiveFrom)
            .Select(x => new EmployeeLaborInsuranceEnrollmentDto(x.Id, x.Status,
                x.MonthlyLaborInsuredSalary, x.EffectiveFrom, x.EffectiveTo,
                x.IsActive))
            .ToListAsync(ct);
        var occupational = await db.EmployeeOccupationalInsuranceEnrollments
            .AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.EffectiveFrom)
            .Select(x => new EmployeeOccupationalInsuranceEnrollmentDto(
                x.Id, x.Status, x.MonthlyInsuredSalary, x.EffectiveFrom,
                x.EffectiveTo, x.IsActive)).ToListAsync(ct);
        var health = await db.EmployeeHealthInsuranceEnrollments.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.EffectiveFrom)
            .Select(x => new EmployeeHealthInsuranceEnrollmentDto(x.Id, x.Status,
                x.MonthlyInsuredAmount, x.DependentCount, x.EffectiveFrom,
                x.EffectiveTo, x.IsActive))
            .ToListAsync(ct);
        var today = Today();
        var summary = ToListItem(employee.Id, employee.EmployeeNumber,
            employee.ChineseName, employee.Department.Name, employee.IsActive,
            employee.HireDate, employee.TerminationDate,
            await db.EmployeeLaborInsuranceEnrollments.AsNoTracking()
                .Where(x => x.EmployeeId == employeeId && x.IsActive).ToListAsync(ct),
            await db.EmployeeOccupationalInsuranceEnrollments.AsNoTracking()
                .Where(x => x.EmployeeId == employeeId && x.IsActive).ToListAsync(ct),
            await db.EmployeeHealthInsuranceEnrollments.AsNoTracking()
                .Where(x => x.EmployeeId == employeeId && x.IsActive).ToListAsync(ct),
            today);
        var hasLaborPolicy = await db.LaborInsuranceRatePolicies.AsNoTracking()
            .AnyAsync(x => x.IsActive && x.EffectiveFrom <= today &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        var hasHealthPolicy = await db.HealthInsuranceRatePolicies.AsNoTracking()
            .AnyAsync(x => x.IsActive && x.EffectiveFrom <= today &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        return new(summary, labor, occupational, health, hasLaborPolicy,
            false, hasHealthPolicy);
    }

    public Task<InsuranceChangePreviewDto> PreviewLaborAsync(
        PreviewLaborInsuranceChangeRequest request, CancellationToken ct = default)
    {
        EnsureManage();
        return BuildLaborPreviewAsync(request, ct);
    }

    public Task<InsuranceChangePreviewDto> PreviewHealthAsync(
        PreviewHealthInsuranceChangeRequest request, CancellationToken ct = default)
    {
        EnsureManage();
        return BuildHealthPreviewAsync(request, ct);
    }

    public Task<InsuranceChangePreviewDto> PreviewOccupationalAsync(
        PreviewOccupationalInsuranceChangeRequest request,
        CancellationToken ct = default)
    {
        EnsureManage();
        return BuildOccupationalPreviewAsync(request, ct);
    }

    public Task<InsuranceApplyResultDto> ApplyLaborAsync(
        PreviewLaborInsuranceChangeRequest request, string previewToken,
        CancellationToken ct = default)
    {
        EnsureManage();
        return db.ExecuteSerializableAsync(async token =>
        {
            var preview = await BuildLaborPreviewAsync(request, token);
            EnsureConfirmed(preview, previewToken);
            EmployeeLaborInsuranceEnrollment? superseded = null;
            DateOnly? previousEffectiveTo = null;
            if (preview.SupersededEnrollmentId is { } supersededId)
            {
                superseded = await db.EmployeeLaborInsuranceEnrollments
                    .SingleAsync(x => x.Id == supersededId, token);
                previousEffectiveTo = superseded.EffectiveTo;
                superseded.CloseBefore(request.CoverageFrom);
            }

            var status = request.IsEnrolled
                ? LaborInsuranceEnrollmentStatus.Enrolled
                : LaborInsuranceEnrollmentStatus.NotEnrolled;
            var entity = new EmployeeLaborInsuranceEnrollment(Guid.NewGuid(),
                request.EmployeeId, status,
                request.IsEnrolled ? request.MonthlyLaborInsuredSalary : null,
                request.CoverageFrom, preview.StoredEffectiveTo);
            db.EmployeeLaborInsuranceEnrollments.Add(entity);
            var staleCount = await MarkCurrentSnapshotsChangedAsync(preview, token);
            var reason = request.Reason.Trim();
            if (superseded is not null)
                AddAudit(AuditActions.EmployeeLaborInsuranceEnrollmentSuperseded,
                    nameof(EmployeeLaborInsuranceEnrollment), superseded.Id,
                    new { superseded.EmployeeId, superseded.Status,
                        PreviousEffectiveTo = previousEffectiveTo },
                    new { superseded.EmployeeId, superseded.Status,
                        superseded.EffectiveTo, SupersededByEnrollmentId = entity.Id,
                        Reason = reason });
            AddAudit(AuditActions.EmployeeLaborInsuranceEnrollmentCreated,
                nameof(EmployeeLaborInsuranceEnrollment), entity.Id,
                null, new { entity.EmployeeId, entity.Status,
                    entity.EffectiveFrom, entity.EffectiveTo,
                    HasLaborInsuredSalary = entity.MonthlyLaborInsuredSalary.HasValue,
                    SupersedesEnrollmentId = superseded?.Id,
                    Reason = reason, SourceChangedSnapshotCount = staleCount });
            if (request.WithdrawalDate.HasValue)
                AddAudit(AuditActions.EmployeeLaborInsuranceEnrollmentEnded,
                    nameof(EmployeeLaborInsuranceEnrollment), entity.Id,
                    null, new { entity.EmployeeId, request.WithdrawalDate,
                        Reason = reason });
            await db.SaveChangesAsync(token);
            return new InsuranceApplyResultDto(entity.Id, superseded?.Id, staleCount);
        }, ct);
    }

    public Task<InsuranceApplyResultDto> ApplyHealthAsync(
        PreviewHealthInsuranceChangeRequest request, string previewToken,
        CancellationToken ct = default)
    {
        EnsureManage();
        return db.ExecuteSerializableAsync(async token =>
        {
            var preview = await BuildHealthPreviewAsync(request, token);
            EnsureConfirmed(preview, previewToken);
            EmployeeHealthInsuranceEnrollment? superseded = null;
            DateOnly? previousEffectiveTo = null;
            if (preview.SupersededEnrollmentId is { } supersededId)
            {
                superseded = await db.EmployeeHealthInsuranceEnrollments
                    .SingleAsync(x => x.Id == supersededId, token);
                previousEffectiveTo = superseded.EffectiveTo;
                superseded.CloseBefore(request.CoverageFrom);
            }

            var status = request.IsEnrolled
                ? HealthInsuranceEnrollmentStatus.Enrolled
                : HealthInsuranceEnrollmentStatus.NotEnrolled;
            var entity = new EmployeeHealthInsuranceEnrollment(Guid.NewGuid(),
                request.EmployeeId, status,
                request.IsEnrolled ? request.MonthlyHealthInsuredAmount : null,
                request.IsEnrolled ? request.DependentCount : null,
                request.CoverageFrom, preview.StoredEffectiveTo);
            db.EmployeeHealthInsuranceEnrollments.Add(entity);
            var staleCount = await MarkCurrentSnapshotsChangedAsync(preview, token);
            var reason = request.Reason.Trim();
            if (superseded is not null)
            {
                AddAudit(AuditActions.EmployeeHealthInsuranceEnrollmentSuperseded,
                    nameof(EmployeeHealthInsuranceEnrollment), superseded.Id,
                    new { superseded.EmployeeId, superseded.Status,
                        PreviousEffectiveTo = previousEffectiveTo,
                        HadDependentCount = superseded.DependentCount.HasValue },
                    new { superseded.EmployeeId, superseded.Status,
                        superseded.EffectiveTo, SupersededByEnrollmentId = entity.Id,
                        Reason = reason });
                if (superseded.DependentCount != entity.DependentCount)
                    AddAudit(AuditActions.EmployeeHealthInsuranceDependentsChanged,
                        nameof(EmployeeHealthInsuranceEnrollment), entity.Id,
                        new { superseded.EmployeeId,
                            HadDependentCount = superseded.DependentCount.HasValue },
                        new { entity.EmployeeId,
                            HasDependentCount = entity.DependentCount.HasValue,
                            Reason = reason });
            }
            AddAudit(AuditActions.EmployeeHealthInsuranceEnrollmentCreated,
                nameof(EmployeeHealthInsuranceEnrollment), entity.Id,
                null, new { entity.EmployeeId, entity.Status,
                    entity.EffectiveFrom, entity.EffectiveTo,
                    HasInsuredAmount = entity.MonthlyInsuredAmount.HasValue,
                    HasDependentCount = entity.DependentCount.HasValue,
                    SupersedesEnrollmentId = superseded?.Id,
                    Reason = reason, SourceChangedSnapshotCount = staleCount });
            if (request.WithdrawalDate.HasValue)
                AddAudit(AuditActions.EmployeeHealthInsuranceEnrollmentEnded,
                    nameof(EmployeeHealthInsuranceEnrollment), entity.Id,
                    null, new { entity.EmployeeId, request.WithdrawalDate,
                        Reason = reason });
            await db.SaveChangesAsync(token);
            return new InsuranceApplyResultDto(entity.Id, superseded?.Id, staleCount);
        }, ct);
    }

    public Task<InsuranceApplyResultDto> ApplyOccupationalAsync(
        PreviewOccupationalInsuranceChangeRequest request, string previewToken,
        CancellationToken ct = default)
    {
        EnsureManage();
        return db.ExecuteSerializableAsync(async token =>
        {
            var preview = await BuildOccupationalPreviewAsync(request, token);
            EnsureConfirmed(preview, previewToken);
            EmployeeOccupationalInsuranceEnrollment? superseded = null;
            DateOnly? previousEffectiveTo = null;
            if (preview.SupersededEnrollmentId is { } supersededId)
            {
                superseded = await db.EmployeeOccupationalInsuranceEnrollments
                    .SingleAsync(x => x.Id == supersededId, token);
                previousEffectiveTo = superseded.EffectiveTo;
                superseded.CloseBefore(request.CoverageFrom);
            }

            var status = request.IsEnrolled
                ? OccupationalInsuranceEnrollmentStatus.Enrolled
                : OccupationalInsuranceEnrollmentStatus.NotEnrolled;
            var entity = new EmployeeOccupationalInsuranceEnrollment(
                Guid.NewGuid(), request.EmployeeId, status,
                request.IsEnrolled ? request.MonthlyInsuredSalary : null,
                request.CoverageFrom, preview.StoredEffectiveTo);
            db.EmployeeOccupationalInsuranceEnrollments.Add(entity);
            var staleCount = await MarkCurrentSnapshotsChangedAsync(preview, token);
            var reason = request.Reason.Trim();
            if (superseded is not null)
                AddAudit(AuditActions.OccupationalInsuranceEnrollmentSuperseded,
                    nameof(EmployeeOccupationalInsuranceEnrollment), superseded.Id,
                    new { superseded.EmployeeId, superseded.Status,
                        PreviousEffectiveTo = previousEffectiveTo },
                    new { superseded.EmployeeId, superseded.Status,
                        superseded.EffectiveTo,
                        SupersededByEnrollmentId = entity.Id, Reason = reason });
            AddAudit(AuditActions.OccupationalInsuranceEnrollmentCreated,
                nameof(EmployeeOccupationalInsuranceEnrollment), entity.Id,
                null, new { entity.EmployeeId, entity.Status,
                    entity.EffectiveFrom, entity.EffectiveTo,
                    HasInsuredSalary = entity.MonthlyInsuredSalary.HasValue,
                    SupersedesEnrollmentId = superseded?.Id, Reason = reason,
                    SourceChangedSnapshotCount = staleCount });
            if (request.WithdrawalDate.HasValue)
                AddAudit(AuditActions.OccupationalInsuranceEnrollmentEnded,
                    nameof(EmployeeOccupationalInsuranceEnrollment), entity.Id,
                    null, new { entity.EmployeeId, request.WithdrawalDate,
                        Reason = reason });
            await db.SaveChangesAsync(token);
            return new InsuranceApplyResultDto(entity.Id, superseded?.Id,
                staleCount);
        }, ct);
    }

    public Task DeactivateLaborEnrollmentAsync(Guid enrollmentId, string reason,
        CancellationToken ct = default)
    {
        EnsureManage();
        return db.ExecuteSerializableAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
                throw new ApplicationValidationException(
                    "停用原因為必填且不得超過 500 字。");
            var entity = await db.EmployeeLaborInsuranceEnrollments
                .SingleOrDefaultAsync(x => x.Id == enrollmentId, token)
                ?? throw new ApplicationValidationException("找不到勞保投保設定。");
            if (!entity.IsActive)
                throw new ApplicationValidationException("勞保投保設定已停用。");
            if (await db.PayrollLaborInsuranceSnapshots.AsNoTracking()
                .AnyAsync(x => x.EnrollmentId == enrollmentId, token))
                throw new ApplicationValidationException(
                    "勞保投保設定已被薪資快照引用，不可停用。");
            // A NeedsSetup snapshot may have no EnrollmentId yet. Conservatively
            // reject any calculation dependency in this employee's covered months,
            // including historical revisions and subsequently finalized snapshots.
            var effectiveTo = entity.EffectiveTo ?? DateOnly.MaxValue;
            if (await (from snapshot in db.PayrollEmployeeSnapshots.AsNoTracking()
                       join period in db.PayrollPeriods.AsNoTracking()
                           on snapshot.PayrollPeriodId equals period.Id
                       where snapshot.EmployeeId == entity.EmployeeId &&
                           period.PeriodEnd >= entity.EffectiveFrom &&
                           period.PeriodStart <= effectiveTo
                       select snapshot.Id).AnyAsync(token))
                throw new ApplicationValidationException(
                    "勞保有效期間已有薪資計算紀錄，需人工檢視，不可停用。");
            entity.Deactivate();
            AddAudit(AuditActions.EmployeeLaborInsuranceEnrollmentDeactivated,
                nameof(EmployeeLaborInsuranceEnrollment), entity.Id,
                new { entity.EmployeeId, WasActive = true },
                new { entity.EmployeeId, entity.IsActive,
                    Reason = reason.Trim() });
            await db.SaveChangesAsync(token);
        }, ct);
    }

    private async Task<InsuranceChangePreviewDto> BuildLaborPreviewAsync(
        PreviewLaborInsuranceChangeRequest request, CancellationToken ct)
    {
        await EnsureEmployeeAsync(request.EmployeeId, ct);
        var rows = await db.EmployeeLaborInsuranceEnrollments.AsNoTracking()
            .Where(x => x.EmployeeId == request.EmployeeId && x.IsActive)
            .OrderBy(x => x.EffectiveFrom).ToListAsync(ct);
        var errors = ValidateBase(request).ToList();
        if (request.IsEnrolled)
        {
            if (request.MonthlyLaborInsuredSalary is null or <= 0)
                errors.Add("勞保投保薪資必須大於 0。");
        }
        else if (request.MonthlyLaborInsuredSalary.HasValue)
            errors.Add("未投保期間不得填寫勞保投保薪資。");

        var plan = await BuildCommonPlanAsync(request.EmployeeId,
            InsuranceEnrollmentKind.Labor,
            request.CoverageFrom, request.WithdrawalDate, request.Reason,
            $"{request.IsEnrolled}|{request.MonthlyLaborInsuredSalary?.ToString(CultureInfo.InvariantCulture)}",
            rows.Select(x => new ExistingRange(x.Id, x.EffectiveFrom,
                x.EffectiveTo, x.Status.ToString(),
                x.MonthlyLaborInsuredSalary, null, null)).ToArray(), errors, ct);
        var changes = plan.Changes.ToList();
        changes.Add(request.IsEnrolled
            ? $"新增勞保投保薪資 {request.MonthlyLaborInsuredSalary:N0} 元。"
            : "新增未投保期間。");
        return plan with { Changes = changes };
    }

    private async Task<InsuranceChangePreviewDto> BuildOccupationalPreviewAsync(
        PreviewOccupationalInsuranceChangeRequest request, CancellationToken ct)
    {
        await EnsureEmployeeAsync(request.EmployeeId, ct);
        var rows = await db.EmployeeOccupationalInsuranceEnrollments.AsNoTracking()
            .Where(x => x.EmployeeId == request.EmployeeId && x.IsActive)
            .OrderBy(x => x.EffectiveFrom).ToListAsync(ct);
        var errors = ValidateBase(request).ToList();
        if (request.IsEnrolled && request.MonthlyInsuredSalary is null or <= 0)
            errors.Add("災保投保薪資必須大於 0。");
        else if (!request.IsEnrolled && request.MonthlyInsuredSalary.HasValue)
            errors.Add("未投保期間不得填寫災保投保薪資。");

        var plan = await BuildCommonPlanAsync(request.EmployeeId,
            InsuranceEnrollmentKind.Occupational, request.CoverageFrom,
            request.WithdrawalDate, request.Reason,
            $"{request.IsEnrolled}|{request.MonthlyInsuredSalary?.ToString(CultureInfo.InvariantCulture)}",
            rows.Select(x => new ExistingRange(x.Id, x.EffectiveFrom,
                x.EffectiveTo, x.Status.ToString(), x.MonthlyInsuredSalary,
                null, null)).ToArray(), errors, ct);
        var changes = plan.Changes.ToList();
        changes.Add(request.IsEnrolled
            ? $"新增災保投保薪資 {request.MonthlyInsuredSalary:N0} 元。"
            : "新增災保未投保期間。");
        return plan with { Changes = changes };
    }

    private async Task<InsuranceChangePreviewDto> BuildHealthPreviewAsync(
        PreviewHealthInsuranceChangeRequest request, CancellationToken ct)
    {
        await EnsureEmployeeAsync(request.EmployeeId, ct);
        var rows = await db.EmployeeHealthInsuranceEnrollments.AsNoTracking()
            .Where(x => x.EmployeeId == request.EmployeeId && x.IsActive)
            .OrderBy(x => x.EffectiveFrom).ToListAsync(ct);
        var errors = ValidateBase(request).ToList();
        if (request.IsEnrolled)
        {
            if (request.MonthlyHealthInsuredAmount is null or <= 0)
                errors.Add("健保投保金額必須大於 0。");
            if (request.DependentCount is null or < 0)
                errors.Add("健保眷屬人數必須為 0 或正整數。");
        }
        else if (request.MonthlyHealthInsuredAmount.HasValue ||
                 request.DependentCount.HasValue)
            errors.Add("未投保期間不得填寫健保投保金額或眷屬人數。");

        var plan = await BuildCommonPlanAsync(request.EmployeeId,
            InsuranceEnrollmentKind.Health, request.CoverageFrom,
            request.WithdrawalDate, request.Reason,
            $"{request.IsEnrolled}|{request.MonthlyHealthInsuredAmount?.ToString(CultureInfo.InvariantCulture)}|{request.DependentCount?.ToString(CultureInfo.InvariantCulture)}",
            rows.Select(x => new ExistingRange(x.Id, x.EffectiveFrom,
                x.EffectiveTo, x.Status.ToString(), x.MonthlyInsuredAmount,
                null, x.DependentCount)).ToArray(), errors, ct);
        var changes = plan.Changes.ToList();
        changes.Add(request.IsEnrolled
            ? $"新增健保投保金額 {request.MonthlyHealthInsuredAmount:N0} 元、眷屬 {request.DependentCount} 人。"
            : "新增健保未投保期間。");
        return plan with { Changes = changes };
    }

    private async Task<InsuranceChangePreviewDto> BuildCommonPlanAsync(
        Guid employeeId,
        InsuranceEnrollmentKind kind,
        DateOnly coverageFrom,
        DateOnly? withdrawalDate,
        string reason,
        string inputSignature,
        IReadOnlyList<ExistingRange> rows,
        List<string> errors,
        CancellationToken ct)
    {
        DateOnly? storedTo = withdrawalDate.HasValue && withdrawalDate > coverageFrom
            ? withdrawalDate.Value.AddDays(-1)
            : null;
        var newTo = storedTo ?? DateOnly.MaxValue;
        var overlaps = rows.Where(x => x.From <= newTo &&
            (!x.To.HasValue || x.To.Value >= coverageFrom)).ToArray();
        ExistingRange? superseded = null;
        if (overlaps.Length > 1)
            errors.Add("生效期間與多筆既有保險設定重疊。");
        else if (overlaps.Length == 1)
        {
            var existing = overlaps[0];
            if (existing.From >= coverageFrom)
                errors.Add("生效期間與既有保險設定重疊。");
            else if (storedTo.HasValue &&
                     (!existing.To.HasValue || storedTo.Value < existing.To.Value))
                errors.Add("有限期間的新設定會切斷後續既有歷史，請拆成可保留完整歷史的設定。");
            else
                superseded = existing;
        }

        var previousEnd = rows.Where(x => x.To.HasValue && x.To < coverageFrom)
            .Select(x => x.To!.Value).DefaultIfEmpty().Max();
        var hasGap = previousEnd != default && previousEnd.AddDays(1) < coverageFrom;
        if (storedTo.HasValue)
        {
            var nextStart = rows.Where(x => x.From > storedTo.Value)
                .Select(x => x.From).DefaultIfEmpty().Min();
            hasGap |= nextStart != default && storedTo.Value.AddDays(1) < nextStart;
        }

        var periods = await db.PayrollPeriods.AsNoTracking()
            .Where(x => x.PeriodEnd >= coverageFrom &&
                (!storedTo.HasValue || x.PeriodStart <= storedTo.Value))
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ToListAsync(ct);
        var periodIds = periods.Select(x => x.Id).ToArray();
        var pointers = await db.PayrollPeriodEmployeeCurrentSnapshots.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && periodIds.Contains(x.PayrollPeriodId))
            .Select(x => x.PayrollPeriodId).ToListAsync(ct);
        var pointerIds = pointers.ToHashSet();
        var affected = periods.Select(x => new InsuranceAffectedPeriodDto(
            x.Id, x.Year, x.Month, x.Status, pointerIds.Contains(x.Id),
            pointerIds.Contains(x.Id) && x.Status != PayrollPeriodStatus.Finalized,
            x.Status == PayrollPeriodStatus.Finalized)).ToArray();

        var warnings = new List<string>();
        if (hasGap)
            warnings.Add("此設定與既有紀錄之間存在未投保期間；系統允許保留此 gap。");
        if (affected.Any(x => x.IsFinalizedProtected))
            warnings.Add("已正式結算月份維持不可變，不會回寫既有正式薪資單。");
        var hasPolicy = kind switch
        {
            InsuranceEnrollmentKind.Labor =>
                await db.LaborInsuranceRatePolicies.AsNoTracking().AnyAsync(x =>
                    x.IsActive && x.EffectiveFrom <= newTo &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= coverageFrom), ct),
            InsuranceEnrollmentKind.Health =>
                await db.HealthInsuranceRatePolicies.AsNoTracking().AnyAsync(x =>
                    x.IsActive && x.EffectiveFrom <= newTo &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= coverageFrom), ct),
            InsuranceEnrollmentKind.Occupational => false,
            _ => false
        };
        if (!hasPolicy)
            warnings.Add("投保資料可建立，但正式費率政策尚待核定；薪資會顯示政策待確認。");

        var changes = new List<string>();
        if (superseded is not null)
            changes.Add($"既有設定將結束於 {coverageFrom.AddDays(-1):yyyy-MM-dd}，原紀錄仍保留。 ");
        var token = CreateToken(kind, employeeId, coverageFrom, withdrawalDate,
            reason, inputSignature, rows, affected);
        return new InsuranceChangePreviewDto(kind, employeeId, coverageFrom,
            withdrawalDate, storedTo, superseded?.Id, hasGap, changes,
            warnings, errors.Distinct(StringComparer.Ordinal).ToArray(), affected,
            affected.Count(x => x.WillMarkSourceChanged), token);
    }

    private async Task<int> MarkCurrentSnapshotsChangedAsync(
        InsuranceChangePreviewDto preview, CancellationToken ct)
    {
        var ids = preview.AffectedPeriods.Where(x => x.WillMarkSourceChanged)
            .Select(x => x.PayrollPeriodId).ToArray();
        if (ids.Length == 0)
            return 0;
        var pointers = await db.PayrollPeriodEmployeeCurrentSnapshots
            .Where(x => x.EmployeeId == preview.EmployeeId &&
                ids.Contains(x.PayrollPeriodId)).ToListAsync(ct);
        var now = timeProvider.GetUtcNow();
        foreach (var pointer in pointers)
            pointer.MarkSourceChanged(now);
        return pointers.Count;
    }

    private static IEnumerable<string> ValidateBase(InsuranceChangeRequest request)
    {
        if (request.EmployeeId == Guid.Empty)
            yield return "請選擇員工。";
        if (request.CoverageFrom == default)
            yield return "請填寫加保日。";
        if (request.WithdrawalDate.HasValue &&
            request.WithdrawalDate.Value <= request.CoverageFrom)
            yield return "不再投保生效日必須晚於加保日；該日起即不再投保。";
        if (string.IsNullOrWhiteSpace(request.Reason))
            yield return "維護原因為必填。";
        else if (request.Reason.Trim().Length > 500)
            yield return "維護原因不得超過 500 字。";
    }

    private async Task EnsureEmployeeAsync(Guid employeeId, CancellationToken ct)
    {
        if (!await db.Employees.AsNoTracking().AnyAsync(x => x.Id == employeeId, ct))
            throw new ApplicationValidationException("員工不存在。");
    }

    private static void EnsureConfirmed(InsuranceChangePreviewDto preview,
        string suppliedToken)
    {
        if (!preview.CanApply)
            throw new ApplicationValidationException(preview.Errors[0]);
        if (string.IsNullOrWhiteSpace(suppliedToken) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(preview.PreviewToken),
                Encoding.UTF8.GetBytes(suppliedToken)))
            throw new ApplicationValidationException("投保資料已變更，請重新預覽後再套用。");
    }

    private void AddAudit(string action, string entityType, Guid entityId,
        object? oldValues, object? newValues) => db.AuditLogs.Add(
            AuditLogFactory.Create(currentUser, timeProvider, action, entityType,
                entityId.ToString(), oldValues, newValues));

    private void EnsureView()
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.InsuranceView))
            throw new ForbiddenAccessException("您沒有勞健保查看權限。");
    }

    private void EnsureManage()
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.InsuranceManage))
            throw new ForbiddenAccessException("您沒有勞健保管理權限。");
    }

    private DateOnly Today() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), Taipei).DateTime);

    private static InsuranceEmployeeListItemDto ToListItem(
        Guid employeeId, string employeeNumber, string employeeName,
        string departmentName, bool isActive, DateOnly hireDate,
        DateOnly? terminationDate,
        IReadOnlyList<EmployeeLaborInsuranceEnrollment> labor,
        IReadOnlyList<EmployeeOccupationalInsuranceEnrollment> occupational,
        IReadOnlyList<EmployeeHealthInsuranceEnrollment> health,
        DateOnly today)
    {
        var currentLabor = labor.SingleOrDefault(x => x.Overlaps(today, today));
        var currentOccupational = occupational.SingleOrDefault(x =>
            x.Overlaps(today, today));
        var currentHealth = health.SingleOrDefault(x => x.Overlaps(today, today));
        var laborDisplay = currentLabor ?? labor.Where(x => x.EffectiveFrom > today)
            .OrderBy(x => x.EffectiveFrom).FirstOrDefault();
        var occupationalDisplay = currentOccupational ?? occupational
            .Where(x => x.EffectiveFrom > today).OrderBy(x => x.EffectiveFrom)
            .FirstOrDefault();
        var healthDisplay = currentHealth ?? health.Where(x => x.EffectiveFrom > today)
            .OrderBy(x => x.EffectiveFrom).FirstOrDefault();
        return new InsuranceEmployeeListItemDto(employeeId, employeeNumber,
            employeeName, departmentName, isActive, hireDate, terminationDate,
            Status(labor, currentLabor, today),
            laborDisplay?.MonthlyLaborInsuredSalary,
            Status(occupational, currentOccupational, today),
            occupationalDisplay?.MonthlyInsuredSalary,
            Status(health, currentHealth, today),
            healthDisplay?.MonthlyInsuredAmount,
            healthDisplay?.DependentCount,
            currentOccupational is null || currentHealth is null);
    }

    private static InsuranceEnrollmentDisplayStatus Status(
        IReadOnlyList<EmployeeLaborInsuranceEnrollment> rows,
        EmployeeLaborInsuranceEnrollment? current, DateOnly today) =>
        StatusCore(rows.Count, current?.Status == LaborInsuranceEnrollmentStatus.Enrolled,
            current is not null, rows.Any(x => x.EffectiveFrom > today));

    private static InsuranceEnrollmentDisplayStatus Status(
        IReadOnlyList<EmployeeOccupationalInsuranceEnrollment> rows,
        EmployeeOccupationalInsuranceEnrollment? current, DateOnly today) =>
        StatusCore(rows.Count,
            current?.Status == OccupationalInsuranceEnrollmentStatus.Enrolled,
            current is not null, rows.Any(x => x.EffectiveFrom > today));

    private static InsuranceEnrollmentDisplayStatus Status(
        IReadOnlyList<EmployeeHealthInsuranceEnrollment> rows,
        EmployeeHealthInsuranceEnrollment? current, DateOnly today) =>
        StatusCore(rows.Count, current?.Status == HealthInsuranceEnrollmentStatus.Enrolled,
            current is not null, rows.Any(x => x.EffectiveFrom > today));

    private static InsuranceEnrollmentDisplayStatus StatusCore(int count,
        bool enrolled, bool hasCurrent, bool hasFuture)
    {
        if (hasCurrent)
            return enrolled ? InsuranceEnrollmentDisplayStatus.Enrolled :
                InsuranceEnrollmentDisplayStatus.UninsuredPeriod;
        if (count == 0)
            return InsuranceEnrollmentDisplayStatus.NotConfigured;
        if (hasFuture)
            return InsuranceEnrollmentDisplayStatus.FutureEffective;
        return InsuranceEnrollmentDisplayStatus.Withdrawn;
    }

    private static string CreateToken(InsuranceEnrollmentKind kind,
        Guid employeeId, DateOnly coverageFrom, DateOnly? withdrawalDate,
        string reason, string inputSignature, IEnumerable<ExistingRange> rows,
        IEnumerable<InsuranceAffectedPeriodDto> periods)
    {
        var value = string.Join('\n',
            ((byte)kind).ToString(CultureInfo.InvariantCulture),
            employeeId.ToString("D"), coverageFrom.ToString("yyyy-MM-dd"),
            withdrawalDate?.ToString("yyyy-MM-dd") ?? "<null>",
            reason?.Trim() ?? string.Empty, inputSignature,
            string.Join(';', rows.OrderBy(x => x.Id).Select(x => string.Join('|',
                x.Id, x.From, x.To?.ToString() ?? "<null>", x.Status,
                x.Amount1?.ToString(CultureInfo.InvariantCulture) ?? "<null>",
                x.Amount2?.ToString(CultureInfo.InvariantCulture) ?? "<null>",
                x.Dependents?.ToString(CultureInfo.InvariantCulture) ?? "<null>"))),
            string.Join(';', periods.OrderBy(x => x.PayrollPeriodId).Select(x =>
                $"{x.PayrollPeriodId:D}|{(byte)x.Status}|{x.HasCurrentSnapshot}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static TimeZoneInfo ResolveTaipei()
    {
        foreach (var id in new[] { "Taipei Standard Time", "Asia/Taipei" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        return TimeZoneInfo.Utc;
    }

    private sealed record ExistingRange(Guid Id, DateOnly From, DateOnly? To,
        string Status, decimal? Amount1, decimal? Amount2, int? Dependents);
}
