using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.Overtime;
using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Payroll;

public sealed class PayrollService(IApplicationDbContext db, ICurrentUser currentUser,
    TimeProvider timeProvider) : IPayrollService
{
    private static readonly TimeZoneInfo Taipei = ResolveTaipei();

    public async Task<PayrollSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        EnsureView();
        var definitions = await db.PayrollComponentDefinitions.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Code).ToListAsync(ct);
        var plans = await db.PayrollPlans.AsNoTracking()
            .Include(x => x.Components).ThenInclude(x => x.ComponentDefinition)
            .Include(x => x.Components).ThenInclude(x => x.SeniorityTiers)
            .OrderBy(x => x.Code).ToListAsync(ct);
        return new(definitions.Select(ToDto).ToArray(), plans.Select(ToDto).ToArray());
    }

    public async Task<IReadOnlyList<EmployeePayrollSummaryDto>> GetEmployeesAsync(
        Guid? departmentId = null, string? keyword = null, CancellationToken ct = default)
    {
        EnsureView();
        var date = Today();
        var query = db.Employees.AsNoTracking().Include(x => x.Department).AsQueryable();
        if (departmentId.HasValue) query = query.Where(x => x.DepartmentId == departmentId);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var value = keyword.Trim();
            query = query.Where(x => x.EmployeeNumber.Contains(value) || x.ChineseName.Contains(value));
        }
        var employees = await query.OrderBy(x => x.EmployeeNumber).ToListAsync(ct);
        var ids = employees.Select(x => x.Id).ToArray();
        var assignments = await db.EmployeePayrollAssignments.AsNoTracking()
            .Include(x => x.PayrollPlan).ThenInclude(x => x.Components).ThenInclude(x => x.ComponentDefinition)
            .Where(x => ids.Contains(x.EmployeeId) && x.IsActive && x.EffectiveFrom <= date &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= date)).ToListAsync(ct);
        var baseOverrides = await db.EmployeePayrollComponentOverrides.AsNoTracking()
            .Include(x => x.ComponentDefinition)
            .Where(x => ids.Contains(x.EmployeeId) && x.ComponentDefinition.Code == "BASE_SALARY" &&
                x.EffectiveFrom <= date && (!x.EffectiveTo.HasValue || x.EffectiveTo >= date))
            .ToListAsync(ct);
        return employees.Select(employee =>
        {
            var matches = assignments.Where(x => x.EmployeeId == employee.Id).ToArray();
            var assignment = matches.Length == 1 ? matches[0] : null;
            decimal? baseAmount = assignment?.PayrollPlan.Components.FirstOrDefault(x =>
                x.ComponentDefinition.Code == "BASE_SALARY" && x.IsEffectiveOn(date))?.DefaultAmount;
            var baseMatches = baseOverrides.Where(x => x.EmployeeId == employee.Id).ToArray();
            if (baseMatches.Length == 1)
            {
                baseAmount = baseMatches[0].OverrideMode switch
                {
                    PayrollOverrideMode.Replace => baseMatches[0].OverrideAmount,
                    PayrollOverrideMode.Add when baseAmount.HasValue => baseAmount + baseMatches[0].OverrideAmount,
                    PayrollOverrideMode.Disable => null,
                    _ => baseAmount
                };
            }
            return new EmployeePayrollSummaryDto(employee.Id, employee.EmployeeNumber,
                employee.ChineseName, employee.DepartmentId, employee.Department.Name,
                assignment?.PayrollPlanId, assignment?.PayrollPlan.Code,
                assignment?.PayrollPlan.Name, baseAmount,
                matches.Length != 1 || baseMatches.Length > 1 || !baseAmount.HasValue);
        }).ToArray();
    }

    public async Task<EmployeePayrollDetailDto> GetEmployeeAsync(Guid employeeId,
        CancellationToken ct = default)
    {
        EnsureView();
        var summary = (await GetEmployeesAsync(ct: ct))
            .SingleOrDefault(x => x.EmployeeId == employeeId)
            ?? throw new ApplicationValidationException("找不到員工資料。");
        var assignments = await db.EmployeePayrollAssignments.AsNoTracking()
            .Include(x => x.PayrollPlan).Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.EffectiveFrom)
            .Select(x => new EmployeePayrollAssignmentDto(x.Id, x.PayrollPlanId,
                x.PayrollPlan.Code, x.PayrollPlan.Name, x.EffectiveFrom, x.EffectiveTo, x.IsActive))
            .ToListAsync(ct);
        var overrides = await db.EmployeePayrollComponentOverrides.AsNoTracking()
            .Include(x => x.ComponentDefinition).Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.EffectiveFrom)
            .Select(x => new EmployeePayrollOverrideDto(x.Id, x.PayrollComponentDefinitionId,
                x.ComponentDefinition.Code, x.ComponentDefinition.Name, x.OverrideMode,
                x.OverrideAmount, x.EffectiveFrom, x.EffectiveTo, x.ReasonCode)).ToListAsync(ct);
        var canViewInsurance = currentUser.HasPermission(PolicyNames.InsuranceView);
        IReadOnlyList<EmployeeLaborInsuranceEnrollmentDto> insuranceEnrollments =
            canViewInsurance
                ? await db.EmployeeLaborInsuranceEnrollments.AsNoTracking()
                    .Where(x => x.EmployeeId == employeeId)
                    .OrderByDescending(x => x.EffectiveFrom)
                    .Select(x => new EmployeeLaborInsuranceEnrollmentDto(x.Id, x.Status,
                        x.MonthlyLaborInsuredSalary, x.EffectiveFrom,
                        x.EffectiveTo, x.IsActive))
                    .ToListAsync(ct)
                : [];
        IReadOnlyList<EmployeeOccupationalInsuranceEnrollmentDto>
            occupationalEnrollments = canViewInsurance
                ? await db.EmployeeOccupationalInsuranceEnrollments.AsNoTracking()
                    .Where(x => x.EmployeeId == employeeId)
                    .OrderByDescending(x => x.EffectiveFrom)
                    .Select(x => new EmployeeOccupationalInsuranceEnrollmentDto(
                        x.Id, x.Status, x.MonthlyInsuredSalary,
                        x.EffectiveFrom, x.EffectiveTo, x.IsActive))
                    .ToListAsync(ct)
                : [];
        var today = Today();
        var hasPolicy = canViewInsurance &&
            await db.LaborInsuranceRatePolicies.AsNoTracking()
                .AnyAsync(x => x.IsActive && x.EffectiveFrom <= today &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        IReadOnlyList<EmployeeHealthInsuranceEnrollmentDto> healthEnrollments =
            canViewInsurance
                ? await db.EmployeeHealthInsuranceEnrollments.AsNoTracking()
                    .Where(x => x.EmployeeId == employeeId)
                    .OrderByDescending(x => x.EffectiveFrom)
                    .Select(x => new EmployeeHealthInsuranceEnrollmentDto(x.Id, x.Status,
                        x.MonthlyInsuredAmount, x.DependentCount, x.EffectiveFrom,
                        x.EffectiveTo, x.IsActive)).ToListAsync(ct)
                : [];
        var hasHealthPolicy = canViewInsurance &&
            await db.HealthInsuranceRatePolicies.AsNoTracking()
                .AnyAsync(x => x.IsActive && x.EffectiveFrom <= today &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        var payCycles = await db.EmployeePayrollPayCycles.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.EffectiveFrom)
            .Select(x => new EmployeePayrollPayCycleDto(x.Id, x.Type,
                x.EffectiveFrom, x.EffectiveTo, x.AnchorPayMonth,
                x.FixedPaymentAmount, x.Reason, x.IsActive,
                x.MonthlyFixedAmount, x.CycleMonths, x.PaymentTiming))
            .ToListAsync(ct);
        return new(summary, assignments, overrides, insuranceEnrollments, hasPolicy,
            healthEnrollments, hasHealthPolicy, payCycles,
            occupationalEnrollments, false);
    }

    public async Task<IReadOnlyList<PayrollPeriodDto>> GetPeriodsAsync(CancellationToken ct = default)
    {
        EnsureView();
        var values = await db.PayrollPeriods.AsNoTracking()
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
            .Select(x => new
            {
                Period = x,
                RunId = x.Runs.OrderByDescending(run => run.RevisionNumber)
                    .Select(run => (Guid?)run.Id).FirstOrDefault(),
                CurrentCount = x.CurrentSnapshots.Count,
                SetupCount = x.CurrentSnapshots.Count(pointer =>
                    pointer.IsSourceChanged ||
                    pointer.PayrollEmployeeSnapshot.SetupStatus ==
                        PayrollEmployeeSetupStatus.NeedsSetup),
                LatestRevision = x.Runs.Select(run => (int?)run.RevisionNumber).Max() ?? 0,
                GrossPay = x.CurrentSnapshots.Sum(pointer =>
                    pointer.PayrollEmployeeSnapshot.GrossPay),
                Deductions = x.CurrentSnapshots.Sum(pointer =>
                    pointer.PayrollEmployeeSnapshot.TotalDeductions),
                NetPay = x.CurrentSnapshots.Sum(pointer =>
                    pointer.PayrollEmployeeSnapshot.NetPay ?? 0),
                CurrentBlocking = x.CurrentSnapshots.Count(pointer =>
                    pointer.IsSourceChanged ||
                    pointer.PayrollEmployeeSnapshot.TotalCalculationStatus !=
                        PayrollCalculationStatus.Resolved)
            }).ToListAsync(ct);
        var output = new List<PayrollPeriodDto>(values.Count);
        foreach (var x in values)
        {
            var eligibleCount = (await PayrollEligibilityResolver.ResolveAsync(
                db, x.Period, null, ct)).Count;
            output.Add(new PayrollPeriodDto(x.Period.Id,
                x.Period.Year, x.Period.Month, x.Period.PeriodStart,
                x.Period.PeriodEnd, x.Period.Status, x.RunId, x.CurrentCount,
                x.SetupCount + Math.Max(0, eligibleCount - x.CurrentCount),
                x.LatestRevision, x.GrossPay, x.Deductions, x.NetPay,
                x.CurrentBlocking + Math.Max(0, eligibleCount - x.CurrentCount)));
        }
        return output;
    }

    public async Task<PayrollRunDto> GetRunAsync(Guid runId, CancellationToken ct = default)
    {
        EnsureView();
        var run = await db.PayrollRuns.AsNoTracking()
            .Include(x => x.EmployeeSnapshots).ThenInclude(x => x.Components)
                .ThenInclude(x => x.AttendanceAllowanceSnapshot).ThenInclude(x => x!.Evidence)
            .Include(x => x.EmployeeSnapshots).ThenInclude(x => x.Components)
                .ThenInclude(x => x.LeaveDeductionSnapshot).ThenInclude(x => x!.Evidence)
            .Include(x => x.EmployeeSnapshots).ThenInclude(x => x.Components)
                .ThenInclude(x => x.LaborInsuranceSnapshot).ThenInclude(x => x!.Contributions)
            .Include(x => x.EmployeeSnapshots).ThenInclude(x => x.Components)
                .ThenInclude(x => x.HealthInsuranceSnapshot).ThenInclude(x => x!.Evidence)
            .Include(x => x.EmployeeSnapshots).ThenInclude(x => x.OvertimePaySnapshot)
                .ThenInclude(x => x!.IncludedComponents)
            .Include(x => x.EmployeeSnapshots).ThenInclude(x => x.OvertimePaySnapshot)
                .ThenInclude(x => x!.Buckets)
            .Include(x => x.EmployeeSnapshots).ThenInclude(x => x.OvertimePaySnapshot)
                .ThenInclude(x => x!.Days)
            .Include(x => x.EmployeeSnapshots).ThenInclude(x => x.TotalBlockingEvidence)
            .SingleOrDefaultAsync(x => x.Id == runId, ct)
            ?? throw new ApplicationValidationException("找不到薪資 Draft。");
        return new(run.Id, run.PayrollPeriodId, run.Status, run.CreatedAtUtc, run.CreatedBy,
            run.EmployeeSnapshots.OrderBy(x => x.EmployeeCode)
                .Select(x => ToEmployeeSnapshotDto(x)).ToArray(),
            run.RevisionNumber, run.Trigger);
    }

    public async Task<PayrollMonthDto> GetMonthAsync(Guid periodId,
        CancellationToken ct = default)
    {
        EnsureView();
        var period = await db.PayrollPeriods.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == periodId, ct)
            ?? throw new ApplicationValidationException("薪資月份不存在。");
        var pointers = await PayrollCurrentSnapshotSet.Query(db)
            .Where(x => x.PayrollPeriodId == periodId)
            .OrderBy(x => x.PayrollEmployeeSnapshot.EmployeeCode)
            .ToListAsync(ct);
        var eligible = (await PayrollEligibilityResolver.ResolveAsync(
            db, period, null, ct)).Count;
        var fingerprint = PayrollCurrentSnapshotSet.Fingerprint(periodId, pointers);
        var adjustments = await db.PayrollAdjustments.AsNoTracking()
            .Where(x => x.PayrollPeriodId == periodId)
            .OrderBy(x => x.Employee.EmployeeNumber)
            .ThenBy(x => x.ComponentDefinition.SortOrder)
            .Select(x => new PayrollAdjustmentDto(x.Id, x.EmployeeId,
                x.Employee.EmployeeNumber, x.Employee.ChineseName,
                x.ComponentDefinition.Code, x.ComponentDefinition.Name,
                x.Amount, x.Direction, x.Reason,
                Convert.ToBase64String(x.RowVersion)))
            .ToListAsync(ct);
        var employees = pointers.Select(pointer =>
        {
            var snapshot = pointer.PayrollEmployeeSnapshot;
            return new PayrollCurrentEmployeeSnapshotDto(snapshot.EmployeeId,
                snapshot.PayrollRunId,
                snapshot.PayrollRun.RevisionNumber, pointer.UpdatedAtUtc,
                pointer.IsSourceChanged,
                ToEmployeeSnapshotDto(snapshot, pointer.IsSourceChanged));
        }).ToArray();
        return new(period.Id, period.Year, period.Month, period.PeriodStart,
            period.PeriodEnd, period.Status, eligible, pointers.Count,
            pointers.Count(x => !x.IsSourceChanged &&
                x.PayrollEmployeeSnapshot.TotalCalculationStatus == PayrollCalculationStatus.Resolved),
            pointers.Count(x => x.IsSourceChanged ||
                x.PayrollEmployeeSnapshot.TotalCalculationStatus != PayrollCalculationStatus.Resolved) +
                Math.Max(0, eligible - pointers.Count),
            pointers.Sum(x => x.PayrollEmployeeSnapshot.GrossPay),
            pointers.Sum(x => x.PayrollEmployeeSnapshot.TotalDeductions),
            pointers.Sum(x => x.PayrollEmployeeSnapshot.NetPay ?? 0),
            PayrollMonthFingerprintV1.Version, Convert.ToHexString(fingerprint),
            pointers.Select(x => x.PayrollEmployeeSnapshot.PayrollRun.RevisionNumber)
                .DefaultIfEmpty(0).Max(), employees, adjustments);
    }

    public async Task<PayrollFixedEarningsPreviewDto> PreviewFixedEarningsAsync(
        Guid employeeId, int year, int month, CancellationToken ct = default)
    {
        EnsureView();
        var period = new PayrollPeriod(Guid.NewGuid(), year, month);
        var eligibility = await PayrollEligibilityResolver.ResolveEmployeeAsync(
            db, employeeId, period, ct);
        var employee = eligibility.Employee;
        if (eligibility.Participation.Status is
            PayrollParticipationStatus.NotEmployed or
            PayrollParticipationStatus.InactiveWithoutTermination)
            throw new ApplicationValidationException("員工不在此薪資月份的任職範圍內。");
        if (eligibility.Participation.Status ==
            PayrollParticipationStatus.AmbiguousConfiguration)
            throw new ApplicationValidationException("員工的發薪方式設定期間重疊。");
        if (eligibility.Participation.Status == PayrollParticipationStatus.NonPayMonth)
        {
            return new(employee.Id, employee.EmployeeNumber, employee.ChineseName,
                year, month, period.PeriodStart, period.PeriodEnd,
                employee.HireDate, employee.TerminationDate, "PERIODIC_FIXED",
                0, [], null, eligibility.Participation.PayCycleType,
                eligibility.Participation.Status,
                eligibility.Participation.Message, null);
        }

        var assignments = await db.EmployeePayrollAssignments.AsNoTracking()
            .Include(x => x.PayrollPlan).ThenInclude(x => x.Components)
            .ThenInclude(x => x.ComponentDefinition)
            .Include(x => x.PayrollPlan).ThenInclude(x => x.Components)
            .ThenInclude(x => x.SeniorityTiers)
            .Where(x => x.EmployeeId == employeeId && x.IsActive)
            .ToListAsync(ct);
        var overrides = await db.EmployeePayrollComponentOverrides.AsNoTracking()
            .Include(x => x.ComponentDefinition)
            .Where(x => x.EmployeeId == employeeId)
            .ToListAsync(ct);
        var date = ResolutionDate(employee, period);
        PayrollPlan plan;
        IReadOnlyList<PayrollComponentResolutionResult> resolved;
        if (eligibility.Participation.PayCycleType is
            PayrollPayCycleType.SemiannualFixed or
            PayrollPayCycleType.PeriodicAccruedFixed)
        {
            plan = await db.PayrollPlans.AsNoTracking()
                .SingleAsync(x => x.Code == "PERIODIC_FIXED", ct);
            resolved = await ResolvePeriodicComponentsAsync(
                eligibility.ExplicitPayCycle!, eligibility.Participation,
                period.PeriodStart, ct);
        }
        else
        {
            var effectiveAssignments = Effective(assignments, date);
            if (effectiveAssignments.Count != 1)
                throw new ApplicationValidationException(
                    "員工缺少唯一有效薪資方案，無法試算固定薪資。");
            plan = effectiveAssignments[0].PayrollPlan;
            if (!plan.IsEffectiveOn(date))
                throw new ApplicationValidationException("薪資方案不在有效期間。");
            resolved = ResolveEmployeeComponents(employee, plan, overrides, period);
        }
        var attendance = await db.DailyAttendanceResults.AsNoTracking()
            .Include(x => x.LeaveSegments)
            .Where(x => x.EmployeeId == employeeId &&
                x.WorkDate >= period.PeriodStart && x.WorkDate <= period.PeriodEnd)
            .ToListAsync(ct);
        var policies = await db.PayrollLeaveDeductionPolicies.AsNoTracking()
            .Where(x => x.IsActive && x.EffectiveFrom <= period.PeriodEnd &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= period.PeriodStart))
            .ToListAsync(ct);
        var componentFlags = await db.PayrollComponentDefinitions.AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.IncludeInOvertimeHourlyBase, ct);
        var overtimeRequests = await db.OvertimeRequests.AsNoTracking()
            .Include(x => x.Recognition)
            .Where(x => x.EmployeeId == employeeId &&
                x.Status == OvertimeRequestStatus.Approved &&
                x.OvertimeDate >= period.PeriodStart &&
                x.OvertimeDate <= period.PeriodEnd).ToListAsync(ct);
        var overtimeRates = await db.OvertimePayRatePolicies.AsNoTracking()
            .Where(x => x.IsActive && x.EffectiveFrom <= period.PeriodEnd &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= period.PeriodStart))
            .ToListAsync(ct);
        var insuranceEnrollments = await db.EmployeeLaborInsuranceEnrollments.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.IsActive &&
                x.EffectiveFrom <= period.PeriodEnd &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= period.PeriodStart))
            .ToListAsync(ct);
        var insurancePolicies = await db.LaborInsuranceRatePolicies.AsNoTracking()
            .Where(x => x.IsActive && x.EffectiveFrom <= period.PeriodEnd &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= period.PeriodStart))
            .ToListAsync(ct);
        var healthEnrollments = await db.EmployeeHealthInsuranceEnrollments.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.IsActive)
            .ToListAsync(ct);
        var healthPolicies = await db.HealthInsuranceRatePolicies.AsNoTracking()
            .Where(x => x.IsActive && x.EffectiveFrom <= period.PeriodEnd &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= period.PeriodStart))
            .ToListAsync(ct);
        var p3 = PayrollP3Calculation.Resolve(resolved, attendance, policies,
            period.PeriodStart, period.PeriodEnd, employee.HireDate,
            employee.TerminationDate);
        var attendanceIndex = attendance.ToDictionary(
            x => (x.EmployeeId, x.WorkDate));
        var p4 = PayrollP4Calculation.Resolve(p3.Components, componentFlags,
            overtimeRequests, attendanceIndex, overtimeRates,
            period.PeriodStart, period.PeriodEnd, employee.HireDate,
            employee.TerminationDate);
        var p5a = PayrollP5ALaborInsurance.Resolve(p4.Components,
            insuranceEnrollments, insurancePolicies,
            period.PeriodStart, period.PeriodEnd);
        var p5b = PayrollP5BHealthInsurance.Resolve(p5a.Components,
            healthEnrollments, healthPolicies, period.PeriodStart, period.PeriodEnd,
            employee.HireDate, employee.TerminationDate);
        PayrollOccupationalInsuranceDto? occupational = null;
        if (currentUser.HasPermission(PolicyNames.InsuranceView))
        {
            var source = await db.EmployeeOccupationalInsuranceEnrollments
                .AsNoTracking().Where(x => x.EmployeeId == employeeId && x.IsActive &&
                    x.EffectiveFrom <= period.PeriodEnd &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= period.PeriodStart))
                .ToListAsync(ct);
            var readiness = OccupationalInsuranceReadinessEvaluator.Evaluate(
                source, period.PeriodStart, period.PeriodEnd);
            occupational = new(readiness.EnrollmentStatus, readiness.EnrollmentId,
                readiness.MonthlyInsuredSalary, readiness.EnrollmentFrom,
                readiness.EnrollmentTo, readiness.CoveredDays,
                readiness.CoverageFactor, readiness.CalculationStatus,
                OccupationalInsuranceSourceFingerprintV1.Version,
                Convert.ToHexString(readiness.SourceFingerprint));
        }
        return new(
            employee.Id,
            employee.EmployeeNumber,
            employee.ChineseName,
            year,
            month,
            period.PeriodStart,
            period.PeriodEnd,
            employee.HireDate,
            employee.TerminationDate,
            plan.Code,
            FixedSubtotal(p5b.Components),
            p5b.Components.Select(result => ToDto(result,
                result.ComponentCode == PayrollP3Calculation.AttendanceAllowanceCode
                    ? p3.AttendanceAllowance : null,
                result.ComponentCode == PayrollP3Calculation.LeaveDeductionCode
                    ? p3.LeaveDeduction : null,
                result.ComponentCode == PayrollP5ALaborInsurance.ComponentCode
                    ? p5a.LaborInsurance : null,
                result.ComponentCode == PayrollP5BHealthInsurance.ComponentCode
                    ? p5b.HealthInsurance : null)).ToArray(),
            ToDto(p4.OvertimePay), eligibility.Participation.PayCycleType,
            eligibility.Participation.Status,
            eligibility.Participation.Message,
            eligibility.Participation.PeriodicFixedAmount, occupational);
    }

    public Task<Guid> AssignPlanAsync(CreatePayrollAssignmentRequest request, CancellationToken ct = default)
    {
        EnsureManage();
        return db.ExecuteSerializableAsync(async token =>
        {
            if (!await db.Employees.AnyAsync(x => x.Id == request.EmployeeId, token) ||
                !await db.PayrollPlans.AnyAsync(x => x.Id == request.PayrollPlanId && x.IsActive &&
                    x.EffectiveFrom <= request.EffectiveFrom &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= request.EffectiveFrom), token))
                throw new ApplicationValidationException("員工或有效薪資方案不存在。");
            if (await db.EmployeePayrollAssignments.AnyAsync(x => x.EmployeeId == request.EmployeeId && x.IsActive &&
                x.EffectiveFrom <= (request.EffectiveTo ?? DateOnly.MaxValue) &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= request.EffectiveFrom), token))
                throw new ApplicationValidationException("員工的有效薪資方案期間不可重疊。");
            var entity = new EmployeePayrollAssignment(Guid.NewGuid(), request.EmployeeId,
                request.PayrollPlanId, request.EffectiveFrom, request.EffectiveTo);
            db.EmployeePayrollAssignments.Add(entity);
            AddAudit(AuditActions.PayrollPlanAssigned, nameof(EmployeePayrollAssignment), entity.Id,
                new { entity.EmployeeId, entity.PayrollPlanId, entity.EffectiveFrom, entity.EffectiveTo });
            await db.SaveChangesAsync(token);
            return entity.Id;
        }, ct);
    }

    public Task<Guid> CreateOverrideAsync(CreatePayrollOverrideRequest request, CancellationToken ct = default)
    {
        EnsureManage();
        return db.ExecuteSerializableAsync(async token =>
        {
            var component = await db.PayrollComponentDefinitions.SingleOrDefaultAsync(x =>
                x.Id == request.ComponentDefinitionId && x.IsActive &&
                x.EffectiveFrom <= request.EffectiveFrom &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= request.EffectiveFrom), token)
                ?? throw new ApplicationValidationException("薪資項目不存在或已停用。");
            if (!await db.Employees.AnyAsync(x => x.Id == request.EmployeeId, token))
                throw new ApplicationValidationException("員工不存在。");
            if (await db.EmployeePayrollComponentOverrides.AnyAsync(x =>
                x.EmployeeId == request.EmployeeId &&
                x.PayrollComponentDefinitionId == request.ComponentDefinitionId &&
                x.EffectiveFrom <= (request.EffectiveTo ?? DateOnly.MaxValue) &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= request.EffectiveFrom), token))
                throw new ApplicationValidationException("同一薪資項目的覆蓋期間不可重疊。");
            var entity = new EmployeePayrollComponentOverride(Guid.NewGuid(), request.EmployeeId,
                component.Id, request.Amount, request.Mode, request.EffectiveFrom,
                request.EffectiveTo, request.ReasonCode);
            db.EmployeePayrollComponentOverrides.Add(entity);
            AddAudit(AuditActions.PayrollComponentOverrideCreated,
                nameof(EmployeePayrollComponentOverride), entity.Id,
                new { entity.EmployeeId, ComponentId = component.Id, entity.OverrideMode,
                    entity.EffectiveFrom, entity.EffectiveTo });
            await db.SaveChangesAsync(token);
            return entity.Id;
        }, ct);
    }

    public Task<Guid> CreatePayCycleAsync(CreatePayrollPayCycleRequest request,
        CancellationToken ct = default)
    {
        EnsureManage();
        return db.ExecuteSerializableAsync(async token =>
        {
            if (!await db.Employees.AnyAsync(x => x.Id == request.EmployeeId,
                    token))
                throw new ApplicationValidationException("員工不存在。");
            if (await db.EmployeePayrollPayCycles.AnyAsync(x =>
                x.EmployeeId == request.EmployeeId && x.IsActive &&
                x.EffectiveFrom <= (request.EffectiveTo ?? DateOnly.MaxValue) &&
                (!x.EffectiveTo.HasValue ||
                    x.EffectiveTo >= request.EffectiveFrom), token))
                throw new ApplicationValidationException("員工的發薪方式期間不可重疊。");
            var entity = new EmployeePayrollPayCycle(Guid.NewGuid(),
                request.EmployeeId, request.Type, request.EffectiveFrom,
                request.EffectiveTo, request.AnchorPayMonth,
                request.FixedPaymentAmount, request.Reason,
                monthlyFixedAmount: request.MonthlyFixedAmount,
                cycleMonths: request.CycleMonths,
                paymentTiming: request.PaymentTiming);
            db.EmployeePayrollPayCycles.Add(entity);

            var candidatePeriods = await db.PayrollPeriods.AsNoTracking()
                .Where(x => x.Status != PayrollPeriodStatus.Finalized)
                .Select(x => new { x.Id, x.PeriodStart, x.PeriodEnd })
                .ToListAsync(token);
            var affectedPeriods = candidatePeriods.Where(x =>
                    x.PeriodEnd >= entity.EffectiveFrom &&
                    (!entity.EffectiveTo.HasValue ||
                        x.PeriodStart <= entity.EffectiveTo) ||
                    PeriodicPaymentWindowOverlaps(entity,
                        x.PeriodStart))
                .Select(x => x.Id).ToArray();
            var pointers = await db.PayrollPeriodEmployeeCurrentSnapshots
                .Where(x => x.EmployeeId == request.EmployeeId &&
                    affectedPeriods.Contains(x.PayrollPeriodId))
                .ToListAsync(token);
            foreach (var pointer in pointers)
                pointer.MarkSourceChanged(timeProvider.GetUtcNow());

            AddAudit(AuditActions.PayrollPayCycleCreated,
                nameof(EmployeePayrollPayCycle), entity.Id,
                new { entity.EmployeeId, entity.Type, entity.EffectiveFrom,
                    entity.EffectiveTo, entity.AnchorPayMonth,
                    HasFixedPaymentAmount = entity.FixedPaymentAmount.HasValue,
                    HasMonthlyFixedAmount = entity.MonthlyFixedAmount.HasValue,
                    entity.CycleMonths, entity.PaymentTiming,
                    Reason = entity.Reason });
            await db.SaveChangesAsync(token);
            return entity.Id;
        }, ct);
    }

    public Task<Guid> CreateLaborInsuranceEnrollmentAsync(
        CreateEmployeeLaborInsuranceEnrollmentRequest request,
        CancellationToken ct = default)
    {
        EnsureInsuranceManage();
        return db.ExecuteSerializableAsync(async token =>
        {
            if (!await db.Employees.AnyAsync(x => x.Id == request.EmployeeId, token))
                throw new ApplicationValidationException("員工不存在。");
            if (await db.EmployeeLaborInsuranceEnrollments.AnyAsync(x =>
                x.EmployeeId == request.EmployeeId && x.IsActive &&
                x.EffectiveFrom <= (request.EffectiveTo ?? DateOnly.MaxValue) &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= request.EffectiveFrom), token))
                throw new ApplicationValidationException("員工勞保設定期間不可重疊。");
            var entity = new EmployeeLaborInsuranceEnrollment(Guid.NewGuid(),
                request.EmployeeId, request.Status,
                request.MonthlyLaborInsuredSalary,
                request.EffectiveFrom, request.EffectiveTo);
            db.EmployeeLaborInsuranceEnrollments.Add(entity);
            AddAudit(AuditActions.EmployeeLaborInsuranceEnrollmentCreated,
                nameof(EmployeeLaborInsuranceEnrollment), entity.Id,
                new { entity.EmployeeId, entity.Status, entity.EffectiveFrom,
                    entity.EffectiveTo,
                    HasLaborInsuredSalary =
                        entity.MonthlyLaborInsuredSalary.HasValue });
            await db.SaveChangesAsync(token);
            return entity.Id;
        }, ct);
    }

    public Task<Guid> CreateHealthInsuranceEnrollmentAsync(
        CreateEmployeeHealthInsuranceEnrollmentRequest request,
        CancellationToken ct = default)
    {
        EnsureInsuranceManage();
        return db.ExecuteSerializableAsync(async token =>
        {
            if (!await db.Employees.AnyAsync(x => x.Id == request.EmployeeId, token))
                throw new ApplicationValidationException("員工不存在。");
            if (await db.EmployeeHealthInsuranceEnrollments.AnyAsync(x =>
                x.EmployeeId == request.EmployeeId && x.IsActive &&
                x.EffectiveFrom <= (request.EffectiveTo ?? DateOnly.MaxValue) &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= request.EffectiveFrom), token))
                throw new ApplicationValidationException("員工健保設定期間不可重疊。");
            var entity = new EmployeeHealthInsuranceEnrollment(Guid.NewGuid(),
                request.EmployeeId, request.Status, request.MonthlyInsuredAmount,
                request.DependentCount, request.EffectiveFrom, request.EffectiveTo);
            db.EmployeeHealthInsuranceEnrollments.Add(entity);
            AddAudit(AuditActions.EmployeeHealthInsuranceEnrollmentCreated,
                nameof(EmployeeHealthInsuranceEnrollment), entity.Id,
                new { entity.EmployeeId, entity.Status, entity.EffectiveFrom,
                    entity.EffectiveTo, HasInsuredAmount = entity.MonthlyInsuredAmount.HasValue,
                    HasDependentCount = entity.DependentCount.HasValue });
            await db.SaveChangesAsync(token);
            return entity.Id;
        }, ct);
    }

    public Task<Guid> CreatePeriodAsync(int year, int month, CancellationToken ct = default)
    {
        EnsureManage();
        return db.ExecuteSerializableAsync(async token =>
        {
            if (await db.PayrollPeriods.AnyAsync(x => x.Year == year && x.Month == month, token))
                throw new ApplicationValidationException("此薪資月份已存在。");
            var entity = new PayrollPeriod(Guid.NewGuid(), year, month);
            db.PayrollPeriods.Add(entity);
            AddAudit(AuditActions.PayrollPeriodCreated, nameof(PayrollPeriod), entity.Id,
                new { entity.Year, entity.Month });
            await db.SaveChangesAsync(token);
            return entity.Id;
        }, ct);
    }

    public Task<Guid> CreateAdjustmentAsync(CreatePayrollAdjustmentRequest request, CancellationToken ct = default)
    {
        EnsureManage();
        return db.ExecuteSerializableAsync(async token =>
        {
            var period = await db.PayrollPeriods.SingleOrDefaultAsync(x => x.Id == request.PayrollPeriodId, token)
                ?? throw new ApplicationValidationException("薪資月份不存在。");
            if (period.Status == PayrollPeriodStatus.Finalized)
                throw new ApplicationValidationException("已結算薪資月份不可調整臨時項目。");
            if ((await PayrollEligibilityResolver.ResolveAsync(db, period,
                    [request.EmployeeId], token)).Count != 1)
                throw new ApplicationValidationException("員工不在此薪資月份的發薪人口內。");
            var component = await db.PayrollComponentDefinitions.SingleOrDefaultAsync(x =>
                x.Id == request.ComponentDefinitionId && x.IsActive &&
                x.EffectiveFrom <= period.PeriodEnd &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= period.PeriodStart), token)
                ?? throw new ApplicationValidationException("薪資項目不存在或已停用。");
            if (component.CalculationKind != PayrollCalculationKind.ManualAdjustment ||
                (component.Category == PayrollComponentCategory.Earning) !=
                (request.Direction == PayrollAdjustmentDirection.Earning))
                throw new ApplicationValidationException("臨時項目必須使用方向一致的 ManualAdjustment 薪資項目。");
            var normalizedReason = request.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedReason))
                throw new ApplicationValidationException("調整原因為必填欄位。");
            if (PayrollLegacyAdjustmentComponents.IsLegacy(component.Code) &&
                await db.PayrollAdjustments.AnyAsync(x =>
                    x.PayrollPeriodId == period.Id &&
                    x.EmployeeId == request.EmployeeId &&
                    x.PayrollComponentDefinitionId == component.Id &&
                    x.Reason == normalizedReason, token))
                throw new ApplicationValidationException(
                    "此員工於該薪資月份已有相同的歷史過渡調整。");
            var entity = new PayrollAdjustment(Guid.NewGuid(), period.Id, request.EmployeeId,
                component.Id, request.Amount, request.Direction, normalizedReason,
                Actor(), timeProvider.GetUtcNow());
            db.PayrollAdjustments.Add(entity);
            await MarkCurrentSourceChangedAsync(period.Id, request.EmployeeId, token);
            AddAudit(AuditActions.PayrollAdjustmentCreated, nameof(PayrollAdjustment), entity.Id,
                new { entity.PayrollPeriodId, entity.EmployeeId, ComponentId = component.Id, entity.Direction });
            await db.SaveChangesAsync(token);
            return entity.Id;
        }, ct);
    }

    public Task UpdateAdjustmentAsync(Guid adjustmentId, decimal amount,
        PayrollAdjustmentDirection direction, string reason, string rowVersion,
        CancellationToken ct = default)
    {
        EnsureManage();
        return db.ExecuteSerializableAsync(async token =>
        {
            var entity = await db.PayrollAdjustments.Include(x => x.PayrollPeriod)
                .Include(x => x.ComponentDefinition).SingleOrDefaultAsync(x => x.Id == adjustmentId, token)
                ?? throw new ApplicationValidationException("找不到臨時薪資項目。");
            EnsureRowVersion(entity.RowVersion, rowVersion);
            if (entity.PayrollPeriod.Status == PayrollPeriodStatus.Finalized)
                throw new ApplicationValidationException("已結算薪資月份的臨時項目不可修改。");
            if ((entity.ComponentDefinition.Category == PayrollComponentCategory.Earning) !=
                (direction == PayrollAdjustmentDirection.Earning))
                throw new ApplicationValidationException("臨時項目方向與薪資項目類別不一致。");
            entity.Update(amount, direction, reason);
            await MarkCurrentSourceChangedAsync(entity.PayrollPeriodId,
                entity.EmployeeId, token);
            AddAudit(AuditActions.PayrollAdjustmentUpdated, nameof(PayrollAdjustment), entity.Id,
                new { entity.PayrollPeriodId, entity.EmployeeId,
                    ComponentId = entity.PayrollComponentDefinitionId, entity.Direction });
            await SaveConcurrencyAsync(token);
        }, ct);
    }

    public Task RemoveAdjustmentAsync(Guid adjustmentId, string rowVersion,
        CancellationToken ct = default)
    {
        EnsureManage();
        return db.ExecuteSerializableAsync(async token =>
        {
            var entity = await db.PayrollAdjustments.Include(x => x.PayrollPeriod)
                .SingleOrDefaultAsync(x => x.Id == adjustmentId, token)
                ?? throw new ApplicationValidationException("找不到臨時薪資項目。");
            EnsureRowVersion(entity.RowVersion, rowVersion);
            if (entity.PayrollPeriod.Status == PayrollPeriodStatus.Finalized)
                throw new ApplicationValidationException("已結算薪資月份的臨時項目不可刪除。");
            await MarkCurrentSourceChangedAsync(entity.PayrollPeriodId,
                entity.EmployeeId, token);
            AddAudit(AuditActions.PayrollAdjustmentRemoved, nameof(PayrollAdjustment), entity.Id,
                new { entity.PayrollPeriodId, entity.EmployeeId,
                    ComponentId = entity.PayrollComponentDefinitionId, entity.Direction });
            db.PayrollAdjustments.Remove(entity);
            await SaveConcurrencyAsync(token);
        }, ct);
    }

    public async Task<Guid> CreateDraftAsync(Guid periodId,
        CancellationToken ct = default)
    {
        var result = await CreateInitialDraftAsync(periodId, ct);
        if (result.RunId.HasValue) return result.RunId.Value;
        var existing = await db.PayrollRuns.AsNoTracking()
            .Where(x => x.PayrollPeriodId == periodId)
            .OrderByDescending(x => x.RevisionNumber)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(ct);
        return existing != Guid.Empty ? existing : throw new ApplicationValidationException(
            "沒有員工可建立薪資試算。");
    }

    public Task<PayrollBatchResultDto> CreateInitialDraftAsync(Guid periodId,
        CancellationToken ct = default) => CalculateBatchAsync(periodId, null,
            PayrollRunTrigger.InitialBatch, onlyChanged: false, ct);

    public Task<PayrollBatchResultDto> RecalculateEmployeeAsync(Guid periodId,
        Guid employeeId, CancellationToken ct = default) =>
        CalculateBatchAsync(periodId, [employeeId],
            PayrollRunTrigger.EmployeeRecalculation, onlyChanged: false, ct);

    public Task<PayrollBatchResultDto> RecalculateChangedAsync(Guid periodId,
        CancellationToken ct = default) => CalculateBatchAsync(periodId, null,
            PayrollRunTrigger.ChangedSources, onlyChanged: true, ct);

    private Task<PayrollBatchResultDto> CalculateBatchAsync(Guid periodId,
        Guid[]? requestedEmployeeIds, PayrollRunTrigger trigger, bool onlyChanged,
        CancellationToken ct)
    {
        EnsureManage();
        return db.ExecuteSerializableAsync(async token =>
        {
            var period = await db.PayrollPeriods.SingleOrDefaultAsync(x => x.Id == periodId, token)
                ?? throw new ApplicationValidationException("薪資月份不存在。");
            if (period.Status == PayrollPeriodStatus.Finalized)
                throw new ApplicationValidationException("已結算的薪資月份不可重新試算。");
            var eligibleEmployees = await PayrollEligibilityResolver.ResolveAsync(
                db, period, requestedEmployeeIds, token);
            var employees = eligibleEmployees.Select(x => x.Employee).ToList();
            var eligibilityByEmployee = eligibleEmployees.ToDictionary(
                x => x.Employee.Id);
            var employeeIds = employees.Select(x => x.Id).ToArray();
            var assignments = await db.EmployeePayrollAssignments.Include(x => x.PayrollPlan)
                .ThenInclude(x => x.Components).ThenInclude(x => x.ComponentDefinition)
                .Include(x => x.PayrollPlan).ThenInclude(x => x.Components)
                .ThenInclude(x => x.SeniorityTiers)
                .Where(x => employeeIds.Contains(x.EmployeeId) && x.IsActive).ToListAsync(token);
            var overrides = await db.EmployeePayrollComponentOverrides.Include(x => x.ComponentDefinition)
                .Where(x => employeeIds.Contains(x.EmployeeId)).ToListAsync(token);
            var adjustments = await db.PayrollAdjustments.Include(x => x.ComponentDefinition)
                .Where(x => x.PayrollPeriodId == periodId).ToListAsync(token);
            var attendanceResults = await db.DailyAttendanceResults
                .AsNoTracking().Include(x => x.LeaveSegments)
                .Where(x => employeeIds.Contains(x.EmployeeId) &&
                    x.WorkDate >= period.PeriodStart && x.WorkDate <= period.PeriodEnd)
                .ToListAsync(token);
            var leaveDeductionPolicies = await db.PayrollLeaveDeductionPolicies
                .AsNoTracking()
                .Where(x => x.IsActive && x.EffectiveFrom <= period.PeriodEnd &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= period.PeriodStart))
                .ToListAsync(token);
            var componentFlags = await db.PayrollComponentDefinitions.AsNoTracking()
                .ToDictionaryAsync(x => x.Id, x => x.IncludeInOvertimeHourlyBase, token);
            var overtimeRequests = await db.OvertimeRequests.AsNoTracking()
                .Include(x => x.Recognition)
                .Where(x => employeeIds.Contains(x.EmployeeId) &&
                    x.Status == OvertimeRequestStatus.Approved &&
                    x.OvertimeDate >= period.PeriodStart &&
                    x.OvertimeDate <= period.PeriodEnd).ToListAsync(token);
            var overtimeRates = await db.OvertimePayRatePolicies.AsNoTracking()
                .Where(x => x.IsActive && x.EffectiveFrom <= period.PeriodEnd &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= period.PeriodStart))
                .ToListAsync(token);
            var insuranceEnrollments = await db.EmployeeLaborInsuranceEnrollments
                .AsNoTracking().Where(x => employeeIds.Contains(x.EmployeeId) &&
                    x.IsActive && x.EffectiveFrom <= period.PeriodEnd &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= period.PeriodStart))
                .ToListAsync(token);
            var insurancePolicies = await db.LaborInsuranceRatePolicies.AsNoTracking()
                .Where(x => x.IsActive && x.EffectiveFrom <= period.PeriodEnd &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= period.PeriodStart))
                .ToListAsync(token);
            var healthEnrollments = await db.EmployeeHealthInsuranceEnrollments
                .AsNoTracking().Where(x => employeeIds.Contains(x.EmployeeId) &&
                    x.IsActive)
                .ToListAsync(token);
            var healthPolicies = await db.HealthInsuranceRatePolicies.AsNoTracking()
                .Where(x => x.IsActive && x.EffectiveFrom <= period.PeriodEnd &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= period.PeriodStart))
                .ToListAsync(token);
            var attendanceIndex = attendanceResults.ToDictionary(
                x => (x.EmployeeId, x.WorkDate));
            var now = timeProvider.GetUtcNow();
            var revision = (await db.PayrollRuns.AsNoTracking()
                .Where(x => x.PayrollPeriodId == periodId)
                .Select(x => (int?)x.RevisionNumber).MaxAsync(token) ?? 0) + 1;
            var run = new PayrollRun(Guid.NewGuid(), period.Id, revision,
                trigger, Actor(), now);
            var allCurrentPointers = await PayrollCurrentSnapshotSet.Query(
                    db, tracking: true)
                .Where(x => x.PayrollPeriodId == periodId &&
                    (requestedEmployeeIds == null ||
                        requestedEmployeeIds.Contains(x.EmployeeId)))
                .ToListAsync(token);
            var eligibleIdSet = employeeIds.ToHashSet();
            var currentPointers = allCurrentPointers
                .Where(x => eligibleIdSet.Contains(x.EmployeeId))
                .ToDictionary(x => x.EmployeeId);
            foreach (var excludedPointer in allCurrentPointers.Where(x =>
                         !eligibleIdSet.Contains(x.EmployeeId)))
                db.PayrollPeriodEmployeeCurrentSnapshots.Remove(excludedPointer);
            var results = new List<PayrollBatchItemResultDto>();
            var periodicPlan = eligibleEmployees.Any(x =>
                    x.Participation.PayCycleType is
                        PayrollPayCycleType.SemiannualFixed or
                        PayrollPayCycleType.PeriodicAccruedFixed)
                ? await db.PayrollPlans.SingleAsync(x =>
                    x.Code == "PERIODIC_FIXED", token)
                : null;

            foreach (var employee in employees)
            {
                var date = ResolutionDate(employee, period);
                if (trigger == PayrollRunTrigger.InitialBatch &&
                    currentPointers.ContainsKey(employee.Id))
                {
                    results.Add(new(employee.Id, employee.EmployeeNumber,
                        PayrollBatchItemOutcome.AlreadyCurrent,
                        "已有當前薪資試算。"));
                    continue;
                }
                var eligibility = eligibilityByEmployee[employee.Id];
                PayrollPlan plan;
                IReadOnlyList<PayrollComponentResolutionResult> fixedComponents;
                if (eligibility.Participation.PayCycleType is
                    PayrollPayCycleType.SemiannualFixed or
                    PayrollPayCycleType.PeriodicAccruedFixed)
                {
                    plan = periodicPlan!;
                    fixedComponents = await ResolvePeriodicComponentsAsync(
                        eligibility.ExplicitPayCycle!, eligibility.Participation,
                        period.PeriodStart, token);
                }
                else
                {
                    var effectiveAssignments = Effective(
                        assignments.Where(x => x.EmployeeId == employee.Id), date);
                    if (effectiveAssignments.Count != 1)
                    {
                        results.Add(new(employee.Id, employee.EmployeeNumber,
                            PayrollBatchItemOutcome.NeedsSetup,
                            "缺少唯一有效薪資方案。"));
                        continue;
                    }
                    plan = effectiveAssignments[0].PayrollPlan;
                    if (!plan.IsEffectiveOn(date))
                    {
                        results.Add(new(employee.Id, employee.EmployeeNumber,
                            PayrollBatchItemOutcome.NeedsSetup,
                            "薪資方案不在該月有效期間。"));
                        continue;
                    }
                    fixedComponents = ResolveEmployeeComponents(
                        employee, plan,
                        overrides.Where(x => x.EmployeeId == employee.Id),
                        period);
                }
                try
                {
                var snapshot = new PayrollEmployeeSnapshot(Guid.NewGuid(), period.Id,
                    run.Id, employee.Id,
                    employee.EmployeeNumber, employee.ChineseName, employee.DepartmentId,
                    employee.Department.Name, employee.HireDate, employee.TerminationDate,
                    plan.Id, plan.Code, now, PayrollEmployeeSetupStatus.Ready);
                var missingFixedSetting = fixedComponents.FirstOrDefault(x =>
                    FixedEarningsCalculator.SupportsComponent(x.ComponentCode) &&
                    x.CalculationStatus == PayrollCalculationStatus.NeedsSetup);
                if (missingFixedSetting is not null)
                {
                    results.Add(new(employee.Id, employee.EmployeeNumber,
                        PayrollBatchItemOutcome.NeedsSetup,
                        $"{missingFixedSetting.ComponentCode} 缺少必要設定。"));
                    continue;
                }
                var p3 = PayrollP3Calculation.Resolve(
                    fixedComponents,
                    attendanceResults.Where(x => x.EmployeeId == employee.Id).ToArray(),
                    leaveDeductionPolicies,
                    period.PeriodStart,
                    period.PeriodEnd,
                    employee.HireDate,
                    employee.TerminationDate);
                var p4 = PayrollP4Calculation.Resolve(
                    p3.Components, componentFlags,
                    overtimeRequests.Where(x => x.EmployeeId == employee.Id).ToArray(),
                    attendanceIndex, overtimeRates,
                    period.PeriodStart, period.PeriodEnd,
                    employee.HireDate, employee.TerminationDate);
                var p5a = PayrollP5ALaborInsurance.Resolve(p4.Components,
                    insuranceEnrollments.Where(x => x.EmployeeId == employee.Id),
                    insurancePolicies, period.PeriodStart, period.PeriodEnd);
                var p5b = PayrollP5BHealthInsurance.Resolve(p5a.Components,
                    healthEnrollments.Where(x => x.EmployeeId == employee.Id),
                    healthPolicies, period.PeriodStart, period.PeriodEnd,
                    employee.HireDate, employee.TerminationDate);
                foreach (var resolved in p5b.Components)
                {
                    var component = ToSnapshot(snapshot.Id, resolved);
                    if (resolved.PeriodicAccrual is not null)
                        component.AttachPeriodicAccrual(
                            new PayrollPeriodicAccrualSnapshot(Guid.NewGuid(),
                                component.Id, resolved.PeriodicAccrual));
                    if (resolved.ComponentCode == PayrollP3Calculation.AttendanceAllowanceCode &&
                        p3.AttendanceAllowance is not null)
                    {
                        var evidence = new PayrollAttendanceAllowanceSnapshot(
                            Guid.NewGuid(), component.Id, p3.AttendanceAllowance,
                            p3.AttendanceSourceFingerprint);
                        foreach (var item in p3.AttendanceAllowance.Evidence)
                            evidence.Evidence.Add(new PayrollAttendanceAllowanceEvidence(
                                Guid.NewGuid(), evidence.Id, item.WorkDate, item.Reasons));
                        component.AttachAttendanceAllowance(evidence);
                    }
                    if (resolved.ComponentCode == PayrollP3Calculation.LeaveDeductionCode &&
                        p3.LeaveDeduction is not null)
                    {
                        var evidence = new PayrollLeaveDeductionSnapshot(
                            Guid.NewGuid(), component.Id, p3.LeaveDeduction,
                            p3.LeaveSourceFingerprint);
                        foreach (var item in p3.LeaveDeduction.Evidence)
                            evidence.Evidence.Add(new PayrollLeaveDeductionEvidence(
                                Guid.NewGuid(), evidence.Id, item));
                        component.AttachLeaveDeduction(evidence);
                    }
                    if (resolved.ComponentCode == PayrollP5ALaborInsurance.ComponentCode)
                    {
                        var evidence = new PayrollLaborInsuranceSnapshot(
                            Guid.NewGuid(), component.Id, p5a.LaborInsurance);
                        foreach (var item in p5a.LaborInsurance.Contributions)
                            evidence.Contributions.Add(
                                new PayrollLaborInsuranceContributionEvidence(
                                    Guid.NewGuid(), evidence.Id, item));
                        component.AttachLaborInsurance(evidence);
                    }
                    if (resolved.ComponentCode == PayrollP5BHealthInsurance.ComponentCode)
                    {
                        var evidence = new PayrollHealthInsuranceSnapshot(
                            Guid.NewGuid(), component.Id, p5b.HealthInsurance);
                        evidence.AttachEvidence(new PayrollHealthInsuranceEvidence(
                            Guid.NewGuid(), evidence.Id, p5b.HealthInsurance));
                        component.AttachHealthInsurance(evidence);
                    }
                    snapshot.Components.Add(component);
                }
                snapshot.AttachOvertimePay(new PayrollOvertimePaySnapshot(
                    Guid.NewGuid(), snapshot.Id, p4.OvertimePay));
                foreach (var adjustment in adjustments.Where(x => x.EmployeeId == employee.Id))
                    snapshot.Components.Add(new PayrollEmployeeSnapshotComponent(Guid.NewGuid(), snapshot.Id,
                        adjustment.PayrollComponentDefinitionId, adjustment.ComponentDefinition.Code,
                        adjustment.ComponentDefinition.Name, adjustment.ComponentDefinition.Category,
                        PayrollSnapshotSourceType.ManualAdjustment, adjustment.Id, null, adjustment.Amount,
                        adjustment.Amount, PayrollCalculationStatus.Resolved, period.PeriodStart));
                snapshot.ApplyTotals(PayrollTotalCalculator.Calculate(
                    snapshot.Components.Select(ToTotalInput)), now);

                if (onlyChanged && currentPointers.TryGetValue(employee.Id,
                        out var current) &&
                    PayrollEmployeeCalculationFingerprintV1.Calculate(snapshot)
                        .SequenceEqual(PayrollEmployeeCalculationFingerprintV1.Calculate(
                            current.PayrollEmployeeSnapshot)))
                {
                    if (current.IsSourceChanged) current.ConfirmSourceCurrent(now);
                    results.Add(new(employee.Id, employee.EmployeeNumber,
                        PayrollBatchItemOutcome.AlreadyCurrent,
                        "來源資料與當前試算一致。",
                        current.PayrollEmployeeSnapshotId));
                    continue;
                }

                run.EmployeeSnapshots.Add(snapshot);
                if (currentPointers.TryGetValue(employee.Id, out var pointer))
                    pointer.SwitchTo(snapshot, now);
                else
                    db.PayrollPeriodEmployeeCurrentSnapshots.Add(
                        new PayrollPeriodEmployeeCurrentSnapshot(
                            period.Id, employee.Id, snapshot.Id, now));
                results.Add(new(employee.Id, employee.EmployeeNumber,
                    BatchOutcome(snapshot), BatchMessage(snapshot), snapshot.Id));
                }
                catch (ApplicationValidationException ex)
                {
                    results.Add(new(employee.Id, employee.EmployeeNumber,
                        PayrollBatchItemOutcome.NeedsSetup, ex.Message));
                }
            }
            if (requestedEmployeeIds is not null && employees.Count == 0)
            {
                var requested = await PayrollEligibilityResolver.ResolveEmployeeAsync(
                    db, requestedEmployeeIds[0], period, token);
                results.Add(new(requestedEmployeeIds[0],
                    requested.Employee.EmployeeNumber,
                    PayrollBatchItemOutcome.Excluded,
                    requested.Participation.Message));
            }
            Guid? runId = null;
            int? createdRevision = null;
            if (run.EmployeeSnapshots.Count > 0)
            {
                db.PayrollRuns.Add(run);
                period.MarkDraftCreated();
                runId = run.Id;
                createdRevision = run.RevisionNumber;
                AddAudit(AuditActions.PayrollRunDraftCreated, nameof(PayrollRun), run.Id,
                    new { PeriodId = period.Id, Revision = run.RevisionNumber,
                        Trigger = run.Trigger, EmployeeCount = run.EmployeeSnapshots.Count });
                AddAudit(AuditActions.PayrollSnapshotCreated, nameof(PayrollRun), run.Id,
                    new { SnapshotCount = run.EmployeeSnapshots.Count,
                        ComponentCount = run.EmployeeSnapshots.Sum(x => x.Components.Count) });
            }
            await db.SaveChangesAsync(token);
            return new PayrollBatchResultDto(period.Id, runId, createdRevision, results);
        }, ct);
    }

    private static IReadOnlyList<PayrollComponentResolutionResult> ResolveEmployeeComponents(
        Domain.MasterData.Employee employee,
        PayrollPlan plan,
        IEnumerable<EmployeePayrollComponentOverride> overrides,
        PayrollPeriod period)
    {
        var effectiveDate = ResolutionDate(employee, period);
        var effectivePlanComponents = Effective(plan.Components, effectiveDate);
        var effectiveOverrides = Effective(overrides, effectiveDate);
        var results = new List<PayrollComponentResolutionResult>();

        foreach (var planComponent in effectivePlanComponents)
        {
            var matches = effectiveOverrides.Where(x =>
                x.PayrollComponentDefinitionId == planComponent.PayrollComponentDefinitionId)
                .ToArray();
            if (matches.Length > 1)
                throw new ApplicationValidationException(
                    $"薪資項目 {planComponent.ComponentDefinition.Code} 存在重疊覆蓋設定。");
            results.Add(FixedEarningsCalculator.ResolvePlanComponent(
                planComponent,
                matches.SingleOrDefault(),
                employee.HireDate,
                employee.TerminationDate,
                period.PeriodStart,
                period.PeriodEnd,
                effectiveDate));
        }

        var planComponentIds = effectivePlanComponents
            .Select(x => x.PayrollComponentDefinitionId)
            .ToHashSet();
        foreach (var group in effectiveOverrides
                     .Where(x => !planComponentIds.Contains(x.PayrollComponentDefinitionId))
                     .GroupBy(x => x.PayrollComponentDefinitionId))
        {
            var matches = group.ToArray();
            if (matches.Length != 1)
                throw new ApplicationValidationException(
                    $"薪資項目 {matches[0].ComponentDefinition.Code} 存在重疊覆蓋設定。");
            if (matches[0].OverrideMode == PayrollOverrideMode.Add)
                throw new ApplicationValidationException(
                    $"薪資項目 {matches[0].ComponentDefinition.Code} 沒有方案基準值，不能使用 Add 覆蓋。");
            results.Add(FixedEarningsCalculator.ResolveDirectOverride(
                matches[0],
                employee.HireDate,
                employee.TerminationDate,
                period.PeriodStart,
                period.PeriodEnd));
        }

        return results;
    }

    private async Task<IReadOnlyList<PayrollComponentResolutionResult>>
        ResolvePeriodicComponentsAsync(EmployeePayrollPayCycle payCycle,
            PayrollParticipationDecision participation, DateOnly sourceDate,
            CancellationToken ct)
    {
        var definitions = await db.PayrollComponentDefinitions.AsNoTracking()
            .Where(x => x.Code == "PERIODIC_FIXED_PAY" ||
                x.Code == PayrollP5ALaborInsurance.ComponentCode ||
                x.Code == PayrollP5BHealthInsurance.ComponentCode)
            .ToDictionaryAsync(x => x.Code, ct);
        if (!definitions.TryGetValue("PERIODIC_FIXED_PAY", out var periodic) ||
            !definitions.TryGetValue(PayrollP5ALaborInsurance.ComponentCode,
                out var labor) ||
            !definitions.TryGetValue(PayrollP5BHealthInsurance.ComponentCode,
                out var health))
            throw new ApplicationValidationException("週期固定給付薪資項目尚未初始化。");

        PayrollComponentResolutionResult earning;
        if (payCycle.Type == PayrollPayCycleType.SemiannualFixed)
        {
            if (participation.PeriodicFixedAmount is not { } amount)
                throw new ApplicationValidationException("週期固定給付缺少有效金額。");
            earning = FixedEarningsCalculator.ResolvePeriodicFixed(periodic,
                payCycle.Id, amount, sourceDate);
        }
        else
        {
            var cycleMonths = payCycle.CycleMonths ??
                throw new ApplicationValidationException("週期月數未設定。");
            var coveredFrom = new DateOnly(sourceDate.Year, sourceDate.Month, 1);
            var finalMonth = coveredFrom.AddMonths(cycleMonths - 1);
            var coveredTo = new DateOnly(finalMonth.Year, finalMonth.Month,
                DateTime.DaysInMonth(finalMonth.Year, finalMonth.Month));
            var settings = await db.EmployeePayrollPayCycles.AsNoTracking()
                .Where(x => x.EmployeeId == payCycle.EmployeeId && x.IsActive &&
                    x.Type == PayrollPayCycleType.PeriodicAccruedFixed &&
                    x.EffectiveFrom <= coveredTo &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= coveredFrom))
                .ToListAsync(ct);
            var accrual = PayrollPeriodicAccrualCalculator.Calculate(payCycle,
                settings, coveredFrom);
            earning = FixedEarningsCalculator.ResolvePeriodicAccrued(periodic,
                payCycle.Id, accrual, sourceDate);
        }

        return
        [
            earning,
            PendingExternal(labor, payCycle.Id, sourceDate),
            PendingExternal(health, payCycle.Id, sourceDate)
        ];
    }

    private static bool PeriodicPaymentWindowOverlaps(
        EmployeePayrollPayCycle setting, DateOnly payrollMonth)
    {
        if (setting.Type != PayrollPayCycleType.PeriodicAccruedFixed ||
            setting.AnchorPayMonth is not { } anchor ||
            setting.CycleMonths is not { } cycleMonths)
            return false;
        var paymentMonth = new DateOnly(payrollMonth.Year, payrollMonth.Month, 1);
        var monthsFromAnchor = (paymentMonth.Year - anchor.Year) * 12 +
            paymentMonth.Month - anchor.Month;
        if (monthsFromAnchor < 0 || monthsFromAnchor % cycleMonths != 0)
            return false;
        var coveredEndMonth = paymentMonth.AddMonths(cycleMonths - 1);
        var coveredEnd = new DateOnly(coveredEndMonth.Year,
            coveredEndMonth.Month,
            DateTime.DaysInMonth(coveredEndMonth.Year,
                coveredEndMonth.Month));
        return setting.EffectiveFrom <= coveredEnd &&
            (!setting.EffectiveTo.HasValue ||
                setting.EffectiveTo >= paymentMonth);
    }

    private static PayrollComponentResolutionResult PendingExternal(
        PayrollComponentDefinition definition, Guid sourceId,
        DateOnly sourceDate) => new(definition.Id, definition.Code,
            definition.Name, definition.Category,
            PayrollSnapshotSourceType.ExternalPending, sourceId, null, null,
            PayrollProrationKind.None, null, null, null, null, null,
            PayrollCalculationStatus.NotCalculated, sourceDate);

    private static PayrollEmployeeSnapshotComponent ToSnapshot(
        Guid snapshotId,
        PayrollComponentResolutionResult result) =>
        new(
            Guid.NewGuid(),
            snapshotId,
            result.ComponentDefinitionId,
            result.ComponentCode,
            result.ComponentName,
            result.Category,
            result.SourceType,
            result.SourceId,
            result.StandardAmount,
            result.OverrideAmount,
            result.ResolvedAmount,
            result.CalculationStatus,
            result.EffectiveSourceDate,
            result.ProrationKind,
            result.FullMonthlyAmount,
            result.PayableDays,
            result.ProrationFactor,
            result.RawProratedAmount);

    private static PayrollSnapshotComponentDto ToDto(
        PayrollComponentResolutionResult result,
        AttendanceAllowanceCalculationResult? allowance = null,
        LeaveDeductionCalculationResult? leave = null,
        LaborInsuranceCalculationResult? laborInsurance = null,
        HealthInsuranceCalculationResult? healthInsurance = null) =>
        new(
            result.ComponentCode,
            result.ComponentName,
            result.Category,
            result.SourceType,
            result.StandardAmount,
            result.OverrideAmount,
            result.ResolvedAmount,
            result.CalculationStatus,
            result.ProrationKind,
            result.FullMonthlyAmount,
            result.PayableDays,
            result.ProrationFactor,
            result.RawProratedAmount,
            allowance is null ? null : new PayrollAttendanceAllowanceDto(
                allowance.FullMonthlyAmount, allowance.EmploymentProratedMaximum,
                allowance.EmploymentPayableDays, allowance.EligibleDays,
                allowance.IneligibleDays, allowance.RawCalculatedAmount,
                allowance.Evidence.Select(x => new PayrollAttendanceAllowanceEvidenceDto(
                    x.WorkDate, x.Reasons)).ToArray()),
            leave is null ? null : new PayrollLeaveDeductionDto(
                leave.FullMonthlyBaseAmount, leave.PersonalLeaveMinutes,
                leave.SickLeaveMinutes, leave.UnsupportedLeaveMinutes,
                leave.PersonalLeaveRawAmount, leave.SickLeaveRawAmount,
                leave.TotalRawAmount,
                leave.Evidence.Select(x => new PayrollLeaveDeductionEvidenceDto(
                    x.WorkDate, x.LeaveTypeCode, x.LeaveMinutes,
                    x.ScheduledMinutes, x.DeductionRate, x.RawAmount,
                    x.CalculationStatus)).ToArray()),
            laborInsurance is null ? null : ToDto(laborInsurance),
            healthInsurance is null ? null : ToDto(healthInsurance),
            result.PeriodicAccrual is null ? null :
                ToDto(result.PeriodicAccrual));

    private static PayrollPeriodicAccrualDto ToDto(
        PayrollPeriodicAccrualResult result) => new(
            result.CoveredFrom, result.CoveredTo, result.CycleMonths,
            result.PaymentTiming, result.TotalAmount,
            result.CalculationStatus,
            result.Months.OrderBy(x => x.CoveredMonth).Select(x =>
                new PayrollPeriodicAccrualMonthDto(x.CoveredMonth,
                    x.MonthlyFixedAmount, x.CalculationStatus)).ToArray());

    private static PayrollPeriodicAccrualDto? ToDto(
        PayrollPeriodicAccrualSnapshot? snapshot) => snapshot is null ? null :
        new(snapshot.CoveredFrom, snapshot.CoveredTo, snapshot.CycleMonths,
            snapshot.PaymentTiming, snapshot.TotalAmount,
            snapshot.CalculationStatus,
            snapshot.Months.OrderBy(x => x.CoveredMonth).Select(x =>
                new PayrollPeriodicAccrualMonthDto(x.CoveredMonth,
                    x.MonthlyFixedAmount, x.CalculationStatus)).ToArray());

    private static PayrollAttendanceAllowanceDto? ToDto(
        PayrollAttendanceAllowanceSnapshot? snapshot) => snapshot is null ? null :
        new(snapshot.FullMonthlyAmount, snapshot.EmploymentProratedMaximum,
            snapshot.EmploymentPayableDays, snapshot.EligibleDays,
            snapshot.IneligibleDays, snapshot.RawCalculatedAmount,
            snapshot.Evidence.OrderBy(x => x.WorkDate).Select(x =>
                new PayrollAttendanceAllowanceEvidenceDto(x.WorkDate, x.Reasons)).ToArray());

    private static PayrollLeaveDeductionDto? ToDto(
        PayrollLeaveDeductionSnapshot? snapshot) => snapshot is null ? null :
        new(snapshot.FullMonthlyBaseAmount, snapshot.PersonalLeaveMinutes,
            snapshot.SickLeaveMinutes, snapshot.UnsupportedLeaveMinutes,
            snapshot.PersonalLeaveRawAmount, snapshot.SickLeaveRawAmount,
            snapshot.TotalRawAmount,
            snapshot.Evidence.OrderBy(x => x.WorkDate).Select(x =>
                new PayrollLeaveDeductionEvidenceDto(x.WorkDate, x.LeaveTypeCode,
                    x.LeaveMinutes, x.ScheduledMinutes, x.DeductionRate,
                    x.RawAmount, x.CalculationStatus)).ToArray());

    private static PayrollLaborInsuranceDto ToDto(
        LaborInsuranceCalculationResult result) => new(
            result.EnrollmentStatus, result.MonthlyLaborInsuredSalary,
            null,
            result.EnrollmentFrom, result.EnrollmentTo, result.PolicyVersion,
            result.PolicyFrom, result.PolicyTo, result.Coverage,
            result.OrdinaryAccidentInsuranceRate,
            result.EmploymentInsuranceRate, result.EmployeeShareRate,
            result.FinalEmployeeDeduction, result.CalculationStatus,
            result.Contributions.Select(x => new PayrollLaborInsuranceContributionDto(
                x.Kind, x.Rate, x.EmployeeShareRate, x.RawEmployeeAmount,
                x.RoundedDisplayAmount)).ToArray());

    private static PayrollLaborInsuranceDto? ToDto(
        PayrollLaborInsuranceSnapshot? snapshot) => snapshot is null ? null : new(
            snapshot.EnrollmentStatus, snapshot.MonthlyLaborInsuredSalary,
            snapshot.MonthlyOccupationalInsuredSalary,
            snapshot.EnrollmentFrom, snapshot.EnrollmentTo,
            snapshot.PolicyVersion, snapshot.PolicyFrom, snapshot.PolicyTo,
            snapshot.Coverage, snapshot.OrdinaryAccidentInsuranceRate,
            snapshot.EmploymentInsuranceRate, snapshot.EmployeeShareRate,
            snapshot.FinalEmployeeDeduction, snapshot.CalculationStatus,
            snapshot.Contributions.OrderBy(x => x.Kind).Select(x =>
                new PayrollLaborInsuranceContributionDto(x.Kind, x.Rate,
                    x.EmployeeShareRate, x.RawEmployeeAmount,
                    x.RoundedDisplayAmount)).ToArray());

    private static PayrollHealthInsuranceDto ToDto(
        HealthInsuranceCalculationResult result) => new(
            result.EnrollmentStatus, result.MonthlyInsuredAmount,
            result.ActualDependentCount, result.EnrollmentFrom, result.EnrollmentTo,
            result.PolicyVersion, result.PolicyFrom, result.PolicyTo,
            result.GeneralPremiumRate, result.EmployeeShareRate,
            result.DependentCap, result.ChargeableDependentCount,
            result.ContributionUnits, result.RawEmployeeAmount,
            result.FinalEmployeeDeduction, result.CalculationStatus);

    private static PayrollHealthInsuranceDto? ToDto(
        PayrollHealthInsuranceSnapshot? snapshot) => snapshot is null ? null : new(
            snapshot.EnrollmentStatus, snapshot.MonthlyInsuredAmount,
            snapshot.ActualDependentCount, snapshot.EnrollmentFrom, snapshot.EnrollmentTo,
            snapshot.PolicyVersion, snapshot.PolicyFrom, snapshot.PolicyTo,
            snapshot.GeneralPremiumRate, snapshot.EmployeeShareRate,
            snapshot.DependentCap, snapshot.ChargeableDependentCount,
            snapshot.ContributionUnits, snapshot.RawEmployeeAmount,
            snapshot.FinalEmployeeDeduction, snapshot.CalculationStatus);

    private static PayrollOvertimePayDto ToDto(PayrollOvertimePayResult result) =>
        new(result.MonthlyOvertimeBase, result.HourlyBase,
            result.TotalRecognizedMinutes, result.TotalOvertimePay,
            result.CalculationStatus,
            result.IncludedComponents.Select(x => new PayrollOvertimeBaseComponentDto(
                x.Code, x.FullMonthlyAmount)).ToArray(),
            result.Buckets.Select(x => new PayrollOvertimeBucketDto(x.Bucket,
                x.Minutes, x.Multiplier, x.RawPay, x.FinalPay,
                x.RatePolicyVersion)).ToArray(),
            result.Days.Select(x => new PayrollOvertimeDayDto(x.WorkDate,
                x.RecognizedMinutes, x.FirstTwoHoursMinutes,
                x.AfterTwoHoursMinutes, x.AfterEightHoursMinutes)).ToArray());

    private static PayrollOvertimePayDto? ToDto(PayrollOvertimePaySnapshot? snapshot) =>
        snapshot is null ? null : new(snapshot.MonthlyOvertimeBase,
            snapshot.HourlyBase, snapshot.TotalRecognizedMinutes,
            snapshot.TotalOvertimePay, snapshot.CalculationStatus,
            snapshot.IncludedComponents.OrderBy(x => x.ComponentCode)
                .Select(x => new PayrollOvertimeBaseComponentDto(
                    x.ComponentCode, x.FullMonthlyAmount)).ToArray(),
            snapshot.Buckets.OrderBy(x => x.Bucket)
                .Select(x => new PayrollOvertimeBucketDto(x.Bucket, x.Minutes,
                    x.Multiplier, x.RawPay, x.FinalPay,
                    x.RatePolicyVersion)).ToArray(),
            snapshot.Days.OrderBy(x => x.WorkDate)
                .Select(x => new PayrollOvertimeDayDto(x.WorkDate,
                    x.RecognizedMinutes, x.FirstTwoHoursMinutes,
                    x.AfterTwoHoursMinutes, x.AfterEightHoursMinutes)).ToArray());

    private static PayrollEmployeeSnapshotDto ToEmployeeSnapshotDto(
        PayrollEmployeeSnapshot snapshot, bool forceSourceChanged = false)
    {
        var inputs = snapshot.Components.Select(ToTotalInput).ToArray();
        var sourceCurrent = snapshot.TotalsCalculatedAtUtc is null
            ? snapshot.TotalCalculationStatus == PayrollCalculationStatus.NotCalculated &&
                snapshot.TotalSourceFingerprint is null
            : snapshot.TotalSourceFingerprintVersion == PayrollTotalFingerprintV1.Version &&
                PayrollTotalFingerprintV1.Matches(
                    snapshot.TotalSourceFingerprint, inputs);
        var blockers = snapshot.TotalBlockingEvidence
            .OrderBy(x => x.ComponentCode)
            .Select(x => new PayrollTotalBlockingDto(x.ComponentDefinitionId,
                x.ComponentCode, x.ComponentStatus, x.Reason)).ToList();
        sourceCurrent = sourceCurrent && !forceSourceChanged;
        if (!sourceCurrent)
            blockers.Add(new(null, "PAYROLL_TOTAL",
                PayrollCalculationStatus.SourceChanged,
                PayrollTotalBlockingReason.SourceChanged));
        return new(snapshot.Id, snapshot.EmployeeCode, snapshot.EmployeeName,
            snapshot.DepartmentName, snapshot.PayrollPlanCode, snapshot.SetupStatus,
            FixedSubtotal(snapshot.Components),
            snapshot.Components.OrderBy(c => c.ComponentCode).Select(c =>
                new PayrollSnapshotComponentDto(c.ComponentCode, c.ComponentName,
                    c.Category, c.SourceType, c.StandardAmount, c.OverrideAmount,
                    c.ResolvedAmount, c.CalculationStatus, c.ProrationKind,
                    c.FullMonthlyAmount, c.PayableDays, c.ProrationFactor,
                    c.RawProratedAmount,
                    ToDto(c.AttendanceAllowanceSnapshot),
                    ToDto(c.LeaveDeductionSnapshot),
                    ToDto(c.LaborInsuranceSnapshot),
                    ToDto(c.HealthInsuranceSnapshot),
                    ToDto(c.PeriodicAccrualSnapshot))).ToArray(),
            ToDto(snapshot.OvertimePaySnapshot), snapshot.GrossPay,
            snapshot.TotalDeductions, snapshot.NetPay,
            sourceCurrent ? snapshot.TotalCalculationStatus :
                PayrollCalculationStatus.SourceChanged,
            sourceCurrent, blockers);
    }

    private static PayrollTotalComponentInput ToTotalInput(
        PayrollEmployeeSnapshotComponent component) => new(
            component.PayrollComponentDefinitionId, component.ComponentCode,
            component.Category, component.SourceType, component.SourceId,
            component.CalculationStatus, component.ResolvedAmount);

    private static decimal FixedSubtotal(
        IEnumerable<PayrollComponentResolutionResult> components) =>
        components.Where(x =>
                FixedEarningsCalculator.SupportsComponent(x.ComponentCode) &&
                x.Category == PayrollComponentCategory.Earning &&
                x.CalculationStatus == PayrollCalculationStatus.Resolved &&
                x.ResolvedAmount.HasValue)
            .Sum(x => x.ResolvedAmount!.Value);

    private static PayrollBatchItemOutcome BatchOutcome(
        PayrollEmployeeSnapshot snapshot)
    {
        var statuses = snapshot.Components.Select(x => x.CalculationStatus)
            .Append(snapshot.TotalCalculationStatus).ToArray();
        if (statuses.Contains(PayrollCalculationStatus.NeedsSetup))
            return PayrollBatchItemOutcome.NeedsSetup;
        if (statuses.Contains(PayrollCalculationStatus.PolicyPending))
            return PayrollBatchItemOutcome.PolicyPending;
        if (statuses.Any(x => x is PayrollCalculationStatus.NeedsReview or
                PayrollCalculationStatus.SourceChanged or
                PayrollCalculationStatus.NotCalculated))
            return PayrollBatchItemOutcome.NeedsReview;
        return PayrollBatchItemOutcome.Created;
    }

    private static string BatchMessage(PayrollEmployeeSnapshot snapshot) =>
        BatchOutcome(snapshot) switch
        {
            PayrollBatchItemOutcome.Created => "薪資試算已建立。",
            PayrollBatchItemOutcome.NeedsSetup => "已建立快照，但尚缺必要設定。",
            PayrollBatchItemOutcome.PolicyPending => "已建立快照，但業務政策待確認。",
            _ => "已建立快照，但尚需處理。"
        };

    private static decimal FixedSubtotal(
        IEnumerable<PayrollEmployeeSnapshotComponent> components) =>
        components.Where(x =>
                FixedEarningsCalculator.SupportsComponent(x.ComponentCode) &&
                x.Category == PayrollComponentCategory.Earning &&
                x.CalculationStatus == PayrollCalculationStatus.Resolved &&
                x.ResolvedAmount.HasValue)
            .Sum(x => x.ResolvedAmount!.Value);

    private void EnsureManage()
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(PolicyNames.PayrollManage))
            throw new ForbiddenAccessException("您沒有薪資管理權限。");
    }
    private void EnsureView()
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(PolicyNames.PayrollView))
            throw new ForbiddenAccessException("您沒有薪資查看權限。");
    }
    private void EnsureInsuranceManage()
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(PolicyNames.InsuranceManage))
            throw new ForbiddenAccessException("您沒有勞健保管理權限。");
    }
    private async Task SaveConcurrencyAsync(CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
    }
    private async Task MarkCurrentSourceChangedAsync(Guid periodId, Guid employeeId,
        CancellationToken token)
    {
        var pointer = await db.PayrollPeriodEmployeeCurrentSnapshots
            .SingleOrDefaultAsync(x => x.PayrollPeriodId == periodId &&
                x.EmployeeId == employeeId, token);
        pointer?.MarkSourceChanged(timeProvider.GetUtcNow());
    }
    private static void EnsureRowVersion(byte[] current, string supplied)
    {
        byte[] expected;
        try { expected = Convert.FromBase64String(supplied); }
        catch (FormatException) { throw new ConcurrencyConflictException(); }
        if (!current.SequenceEqual(expected)) throw new ConcurrencyConflictException();
    }
    private void AddAudit(string action, string type, Guid id, object values) =>
        db.AuditLogs.Add(AuditLogFactory.Create(currentUser, timeProvider, action, type,
            id.ToString(), null, values));
    private string Actor() => currentUser.UserId ?? currentUser.DisplayName ?? "payroll-admin";
    private DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), Taipei).DateTime);
    private static DateOnly ResolutionDate(Domain.MasterData.Employee employee, PayrollPeriod period) =>
        employee.TerminationDate is { } end && end < period.PeriodEnd ? end : period.PeriodEnd;
    private static List<T> Effective<T>(IEnumerable<T> values, DateOnly date) where T : class => values.Where(value => value switch
    {
        EmployeePayrollAssignment x => x.IsEffectiveOn(date),
        EmployeePayrollComponentOverride x => x.IsEffectiveOn(date),
        PayrollPlanComponent x => x.IsEffectiveOn(date),
        _ => false
    }).ToList();
    private static TimeZoneInfo ResolveTaipei()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time"); }
    }
    private static PayrollComponentDto ToDto(PayrollComponentDefinition x) => new(x.Id,
        x.Code, x.Name, x.Category, x.CalculationKind, x.IsRecurring, x.IsActive,
        x.SortOrder, x.EffectiveFrom, x.EffectiveTo,
        x.IncludeInOvertimeHourlyBase);
    private static PayrollPlanDto ToDto(PayrollPlan x) => new(x.Id, x.Code, x.Name,
        x.Description, x.EffectiveFrom, x.EffectiveTo, x.IsActive,
        x.Components.OrderBy(c => c.ComponentDefinition.SortOrder).Select(c =>
            new PayrollPlanComponentDto(c.Id, c.PayrollComponentDefinitionId,
                c.ComponentDefinition.Code, c.ComponentDefinition.Name, c.DefaultAmount,
                c.RuleKind, c.ProrationKind, c.EffectiveFrom, c.EffectiveTo,
                c.SeniorityTiers.OrderBy(t => t.MinMonthsInclusive).Select(t =>
                    new PayrollTierDto(t.MinMonthsInclusive, t.MaxMonthsExclusive, t.Amount)).ToArray())).ToArray());
}
