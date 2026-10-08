using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Common.Models;
using HRSystem.Application.Common.Validation;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.CompanyCalendars;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Attendance;

public sealed class AttendanceManagementService(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IAttendanceRecalculationEngine? recalculationEngine = null)
    : IAttendanceManagementService
{
    private readonly IAttendanceRecalculationEngine _recalculationEngine =
        recalculationEngine ??
        new AttendanceRecalculationEngine(dbContext, timeProvider);
    private const string ShiftCodeIndex = "UX_AttendanceShifts_Code";
    private const string DailyResultIndex =
        "UX_DailyAttendanceResults_Employee_WorkDate";
    private static readonly TimeZoneInfo TaipeiZone = ResolveTaipeiZone();

    public async Task<IReadOnlyList<AttendanceShiftDto>> GetShiftsAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureManage(currentUser);
        var query = dbContext.AttendanceShifts.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(item => item.IsActive);
        }

        return await query
            .OrderBy(item => item.Code)
            .Select(item => MapShift(item))
            .ToListAsync(cancellationToken);
    }

    public async Task<AttendanceShiftDto> SaveShiftAsync(
        SaveAttendanceShiftRequest request,
        CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureManage(currentUser);
        RequestValidator.Validate(request);
        return await dbContext.ExecuteSerializableAsync(async transactionToken =>
        {
            var now = timeProvider.GetUtcNow();
            AttendanceShift entity;
            object? oldValues = null;
            string action;
            if (request.Id.HasValue)
            {
                entity = await dbContext.AttendanceShifts.SingleOrDefaultAsync(
                        item => item.Id == request.Id.Value,
                        transactionToken)
                    ?? throw new EntityNotFoundException("Attendance shift was not found.");
                EnsureRowVersion(entity.RowVersion, request.RowVersion);
                oldValues = ShiftSnapshot(entity);
                entity.Update(
                    request.Code,
                    request.Name,
                    request.ScheduledStartTime,
                    request.LateThresholdTime,
                    request.LunchBreakStartTime,
                    request.LunchBreakEndTime,
                    request.ScheduledEndTime,
                    request.ExpectedWorkMinutes,
                    request.IsLunchPunchRequired,
                    request.IsOvernightShift,
                    now);
                action = AuditActions.AttendanceShiftUpdated;
            }
            else
            {
                entity = new AttendanceShift(
                    Guid.NewGuid(),
                    request.Code,
                    request.Name,
                    request.ScheduledStartTime,
                    request.LateThresholdTime,
                    request.LunchBreakStartTime,
                    request.LunchBreakEndTime,
                    request.ScheduledEndTime,
                    request.ExpectedWorkMinutes,
                    request.IsLunchPunchRequired,
                    request.IsOvernightShift,
                    now);
                dbContext.AttendanceShifts.Add(entity);
                action = AuditActions.AttendanceShiftCreated;
            }

            dbContext.AuditLogs.Add(AuditLogFactory.Create(
                currentUser,
                timeProvider,
                action,
                nameof(AttendanceShift),
                entity.Id.ToString(),
                oldValues,
                ShiftSnapshot(entity)));
            await SaveAsync(transactionToken);
            return MapShift(entity);
        }, cancellationToken);
    }

    public async Task SetShiftActiveAsync(
        Guid id,
        bool isActive,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureManage(currentUser);
        await dbContext.ExecuteSerializableAsync(async transactionToken =>
        {
            var entity = await dbContext.AttendanceShifts.SingleOrDefaultAsync(
                    item => item.Id == id,
                    transactionToken)
                ?? throw new EntityNotFoundException("Attendance shift was not found.");
            EnsureRowVersion(entity.RowVersion, rowVersion);
            var oldValues = ShiftSnapshot(entity);
            if (isActive)
            {
                entity.Activate(timeProvider.GetUtcNow());
            }
            else
            {
                entity.Deactivate(timeProvider.GetUtcNow());
            }

            dbContext.AuditLogs.Add(AuditLogFactory.Create(
                currentUser,
                timeProvider,
                isActive
                    ? AuditActions.AttendanceShiftActivated
                    : AuditActions.AttendanceShiftDeactivated,
                nameof(AttendanceShift),
                entity.Id.ToString(),
                oldValues,
                ShiftSnapshot(entity)));
            await SaveAsync(transactionToken);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeShiftAssignmentDto>>
        GetAssignmentsAsync(
            Guid? employeeId,
            bool includeInactive,
            CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureManage(currentUser);
        var query = dbContext.EmployeeShiftAssignments
            .AsNoTracking()
            .Include(item => item.Employee)
            .Include(item => item.Shift)
            .AsQueryable();
        if (employeeId.HasValue && employeeId.Value != Guid.Empty)
        {
            query = query.Where(item => item.EmployeeId == employeeId.Value);
        }

        if (!includeInactive)
        {
            query = query.Where(item => item.IsActive);
        }

        return await query
            .OrderBy(item => item.Employee.EmployeeNumber)
            .ThenByDescending(item => item.EffectiveFrom)
            .Select(item => MapAssignment(item))
            .ToListAsync(cancellationToken);
    }

    public async Task<EmployeeShiftAssignmentDto> SaveAssignmentAsync(
        SaveEmployeeShiftAssignmentRequest request,
        CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureManage(currentUser);
        RequestValidator.Validate(request);
        return await dbContext.ExecuteSerializableAsync(async transactionToken =>
        {
            var employeeExists = await dbContext.Employees.AsNoTracking()
                .AnyAsync(item => item.Id == request.EmployeeId, transactionToken);
            if (!employeeExists)
            {
                throw new ApplicationValidationException("Employee was not found.");
            }

            var shift = await dbContext.AttendanceShifts.AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == request.ShiftId,
                    transactionToken)
                ?? throw new ApplicationValidationException("Attendance shift was not found.");
            if (!shift.IsActive)
            {
                throw new ApplicationValidationException(
                    "An inactive shift cannot be assigned.");
            }

            await EnsureNoAssignmentOverlapAsync(
                request.EmployeeId,
                request.EffectiveFrom,
                request.EffectiveTo,
                request.Id,
                transactionToken);

            var now = timeProvider.GetUtcNow();
            EmployeeShiftAssignment entity;
            object? oldValues = null;
            string action;
            if (request.Id.HasValue)
            {
                entity = await dbContext.EmployeeShiftAssignments
                    .SingleOrDefaultAsync(
                        item => item.Id == request.Id.Value,
                        transactionToken)
                    ?? throw new EntityNotFoundException(
                        "Employee shift assignment was not found.");
                EnsureRowVersion(entity.RowVersion, request.RowVersion);
                if (entity.EmployeeId != request.EmployeeId)
                {
                    throw new ApplicationValidationException(
                        "The employee of an existing assignment cannot be changed.");
                }

                oldValues = AssignmentSnapshot(entity);
                entity.Update(
                    request.ShiftId,
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    now);
                action = AuditActions.EmployeeShiftAssignmentUpdated;
            }
            else
            {
                entity = new EmployeeShiftAssignment(
                    Guid.NewGuid(),
                    request.EmployeeId,
                    request.ShiftId,
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    now);
                dbContext.EmployeeShiftAssignments.Add(entity);
                action = AuditActions.EmployeeShiftAssigned;
            }

            dbContext.AuditLogs.Add(AuditLogFactory.Create(
                currentUser,
                timeProvider,
                action,
                nameof(EmployeeShiftAssignment),
                entity.Id.ToString(),
                oldValues,
                AssignmentSnapshot(entity)));
            await SaveAsync(transactionToken);
            return await LoadAssignmentDtoAsync(entity.Id, transactionToken);
        }, cancellationToken);
    }

    public async Task SetAssignmentActiveAsync(
        Guid id,
        bool isActive,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureManage(currentUser);
        await dbContext.ExecuteSerializableAsync(async transactionToken =>
        {
            var entity = await dbContext.EmployeeShiftAssignments
                .SingleOrDefaultAsync(item => item.Id == id, transactionToken)
                ?? throw new EntityNotFoundException(
                    "Employee shift assignment was not found.");
            EnsureRowVersion(entity.RowVersion, rowVersion);
            if (isActive)
            {
                await EnsureNoAssignmentOverlapAsync(
                    entity.EmployeeId,
                    entity.EffectiveFrom,
                    entity.EffectiveTo,
                    entity.Id,
                    transactionToken);
                entity.Activate(timeProvider.GetUtcNow());
            }
            else
            {
                entity.Deactivate(timeProvider.GetUtcNow());
            }

            dbContext.AuditLogs.Add(AuditLogFactory.Create(
                currentUser,
                timeProvider,
                isActive
                    ? AuditActions.EmployeeShiftAssignmentActivated
                    : AuditActions.EmployeeShiftAssignmentDeactivated,
                nameof(EmployeeShiftAssignment),
                entity.Id.ToString(),
                null,
                AssignmentSnapshot(entity)));
            await SaveAsync(transactionToken);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<AttendanceEmployeeOptionDto>>
        GetEmployeeOptionsAsync(CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureManage(currentUser);
        return await dbContext.Employees
            .AsNoTracking()
            .OrderBy(item => item.EmployeeNumber)
            .Select(item => new AttendanceEmployeeOptionDto(
                item.Id,
                item.EmployeeNumber,
                item.ChineseName))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AttendanceEmployeeOptionDto>>
        GetDailyEmployeeOptionsAsync(
            DateOnly dateFrom,
            DateOnly dateTo,
            bool includeInactiveEmployees = false,
            CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureManage(currentUser);
        var (from, to) = ResolveQueryRange(dateFrom, dateTo);
        var employees = dbContext.Employees.AsNoTracking();
        if (!includeInactiveEmployees)
        {
            employees = employees.Where(item =>
                item.IsActive &&
                item.HireDate <= to &&
                (!item.TerminationDate.HasValue ||
                 item.TerminationDate.Value >= from));
        }

        return await employees
            .OrderBy(item => item.EmployeeNumber)
            .Select(item => new AttendanceEmployeeOptionDto(
                item.Id,
                item.EmployeeNumber,
                item.ChineseName))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<DailyAttendanceResultDto>> GetDailyResultsAsync(
        DailyAttendanceQuery query,
        CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureRead(currentUser);
        var (from, to) = ResolveQueryRange(query.DateFrom, query.DateTo);
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var results = dbContext.DailyAttendanceResults
            .AsNoTracking()
            .Include(item => item.Employee)
            .Include(item => item.CurrentAdjustment)
            .Include(item => item.LeaveSegments)
            .Where(item => item.WorkDate >= from && item.WorkDate <= to);

        if (!query.IncludeInactiveEmployees)
        {
            results = results.Where(item =>
                item.Employee.IsActive &&
                item.Employee.HireDate <= item.WorkDate &&
                (!item.Employee.TerminationDate.HasValue ||
                 item.Employee.TerminationDate.Value >= item.WorkDate));
        }

        if (!currentUser.HasPermission(PolicyNames.AttendanceViewAll))
        {
            var employeeId = currentUser.EmployeeId
                ?? throw new ForbiddenAccessException(
                    "Your account is not linked to an employee.");
            results = results.Where(item => item.EmployeeId == employeeId);
        }
        else if (query.EmployeeId.HasValue &&
                 query.EmployeeId.Value != Guid.Empty)
        {
            results = results.Where(
                item => item.EmployeeId == query.EmployeeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            results = results.Where(item =>
                item.Employee.EmployeeNumber.Contains(keyword) ||
                item.Employee.ChineseName.Contains(keyword));
        }

        // Only employees admitted by the daily query's authorization, lifecycle and
        // keyword scope are passed to the shared, batch-loaded review projection.
        var employeeIds = await results.Select(item => item.EmployeeId).Distinct()
            .ToArrayAsync(cancellationToken);
        var punchProjection = await new AttendanceReviewService(dbContext, currentUser, timeProvider)
            .ReadDailyPunchProjectionAsync(employeeIds, from, to, cancellationToken);
        var pendingResultIds = punchProjection.Values.Where(item => item.NonWorkingPunchPending)
            .Select(item => item.DailyAttendanceResultId).ToArray();

        if (query.ExceptionsOnly)
        {
            results = results.Where(item =>
                item.Status != AttendanceDailyStatus.Normal &&
                item.Status != AttendanceDailyStatus.RestDay ||
                item.MissingMinutes > 0 ||
                item.WorkedDuringApprovedLeaveMinutes > 0 ||
                item.IsAttendanceExempted ||
                pendingResultIds.Contains(item.Id));
        }

        if (query.Status.HasValue)
        {
            results = results.Where(item => item.Status == query.Status.Value);
        }

        var totalCount = await results.CountAsync(cancellationToken);
        var items = await results
            .OrderByDescending(item => item.WorkDate)
            .ThenBy(item => item.Employee.EmployeeNumber)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(item => MapDaily(item))
            .ToListAsync(cancellationToken);
        items = items.Select(item => punchProjection.TryGetValue(item.Id, out var projection)
            ? item with
            {
                PunchEvidence = projection.PunchEvidence,
                OvertimeLinks = projection.OvertimeLinks,
                PunchReviewItems = AttendanceReviewService.PublicReviewItems(projection)
            }
            : item).ToList();
        return new PagedResult<DailyAttendanceResultDto>(
            items,
            totalCount,
            pageNumber,
            pageSize);
    }

    public async Task<AttendanceRecalculationPreview> GetRecalculationPreviewAsync(
        AttendanceRecalculationRequest request,
        CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureManage(currentUser);
        RequestValidator.Validate(request);
        var employeeCount = await dbContext.Employees
            .AsNoTracking()
            .Where(item =>
                item.IsActive &&
                item.HireDate <= request.DateTo &&
                (!item.TerminationDate.HasValue ||
                 item.TerminationDate.Value >= request.DateFrom))
            .Where(item =>
                !request.EmployeeId.HasValue ||
                item.Id == request.EmployeeId.Value)
            .CountAsync(cancellationToken);
        if (request.EmployeeId.HasValue && employeeCount == 0)
        {
            throw new ApplicationValidationException(
                "Employee was not found in the requested date range.");
        }

        return AttendanceRecalculationPreview.Create(
            request.DateFrom,
            request.DateTo,
            employeeCount,
            !request.EmployeeId.HasValue);
    }

    public async Task<AttendanceRecalculationResultDto> RecalculateAsync(
        AttendanceRecalculationRequest request,
        CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureManage(currentUser);
        RequestValidator.Validate(request);
        var actor = AttendanceManagementAuthorization.Actor(currentUser);
        dbContext.AuditLogs.Add(AuditLogFactory.Create(
            currentUser,
            timeProvider,
            AuditActions.AttendanceRecalculationRequested,
            nameof(DailyAttendanceResult),
            $"{request.DateFrom:yyyy-MM-dd}:{request.DateTo:yyyy-MM-dd}",
            null,
            new
            {
                request.DateFrom,
                request.DateTo,
                HasEmployeeFilter = request.EmployeeId.HasValue
            }));
        await SaveAsync(cancellationToken);

        try
        {
            return await dbContext.ExecuteSerializableAsync(
                async transactionToken =>
                {
                    var outcome =
                        await _recalculationEngine.RecalculateRangeAsync(
                            request.DateFrom,
                            request.DateTo,
                            request.EmployeeId,
                            cancellationToken: transactionToken);
                    dbContext.AuditLogs.Add(AuditLogFactory.Create(
                        currentUser,
                        timeProvider,
                        AuditActions.AttendanceRecalculationCompleted,
                        nameof(DailyAttendanceResult),
                        $"{request.DateFrom:yyyy-MM-dd}:{request.DateTo:yyyy-MM-dd}",
                        null,
                        new
                        {
                            request.DateFrom,
                            request.DateTo,
                            outcome.EmployeeCount,
                            outcome.ResultCount,
                            Actor = actor,
                            CalculationVersion =
                                AttendanceRecalculationEngine
                                    .CurrentCalculationVersion
                        }));
                    await SaveAsync(transactionToken);
                    return new AttendanceRecalculationResultDto(
                        outcome.DateFrom,
                        outcome.DateTo,
                        outcome.EmployeeCount,
                        outcome.ResultCount,
                        outcome.CompletedAtUtc);
                },
                cancellationToken);
        }
        catch
        {
            dbContext.AuditLogs.Add(AuditLogFactory.Create(
                currentUser,
                timeProvider,
                AuditActions.AttendanceRecalculationFailed,
                nameof(DailyAttendanceResult),
                $"{request.DateFrom:yyyy-MM-dd}:{request.DateTo:yyyy-MM-dd}",
                null,
                new
                {
                    request.DateFrom,
                    request.DateTo,
                    Failure = "Attendance recalculation did not complete."
                }));
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public Task<DailyAttendanceResultDto> AdjustAsync(
        AttendanceAdjustmentRequest request,
        CancellationToken cancellationToken = default) =>
        ApplyNewAdjustmentAsync(request, cancellationToken);

    public async Task<AttendanceCorrectionAdjustmentResult>
        ApplyCorrectionAdjustmentAsync(
            AttendanceAdjustmentRequest request,
            CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureAdjust(currentUser);
        RequestValidator.Validate(request);
        var outcome = await ApplyNewAdjustmentCoreAsync(
            request,
            cancellationToken);
        return new AttendanceCorrectionAdjustmentResult(
            outcome.AdjustmentId,
            outcome.ResultId,
            outcome.EmployeeId,
            outcome.WorkDate);
    }

    public async Task<DailyAttendanceResultDto> RevertAdjustmentAsync(
        RevertAttendanceAdjustmentRequest request,
        CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureAdjust(currentUser);
        RequestValidator.Validate(request);
        return await dbContext.ExecuteSerializableAsync(async transactionToken =>
        {
            var result = await dbContext.DailyAttendanceResults
                .Include(item => item.LeaveSegments)
                .SingleOrDefaultAsync(
                    item => item.Id == request.DailyAttendanceResultId,
                    transactionToken)
                ?? throw new EntityNotFoundException(
                    "Daily attendance result was not found.");
            EnsureRowVersion(result.RowVersion, request.RowVersion);
            if (!result.CurrentAdjustmentId.HasValue || !result.IsAdjusted)
            {
                throw new ApplicationValidationException(
                    "There is no active attendance adjustment to revert.");
            }

            var currentAdjustment = await dbContext.AttendanceAdjustments
                .AsNoTracking()
                .SingleAsync(
                    item => item.Id == result.CurrentAdjustmentId.Value,
                    transactionToken);
            var revision = await NextRevisionAsync(result.Id, transactionToken);
            var now = timeProvider.GetUtcNow();
            var adjustment = new AttendanceAdjustment(
                Guid.NewGuid(),
                result.Id,
                result.EmployeeId,
                result.WorkDate,
                revision,
                AttendanceAdjustmentAction.Reverted,
                result.EffectiveClockInLocalTime,
                result.EffectiveClockOutLocalTime,
                result.RawClockInLocalTime,
                result.RawClockOutLocalTime,
                currentAdjustment.Reason,
                request.Note,
                AttendanceManagementAuthorization.Actor(currentUser),
                now,
                currentAdjustment.Id);
            var shift = result.GetShiftSnapshot()
                ?? throw new ApplicationValidationException(
                    "A result without a shift snapshot cannot be adjusted.");
            var rawCalculation = RawCalculation(result);
            var calculation = AttendanceDailyCalculator.ApplyEffectiveTimes(
                result.WorkDate,
                shift,
                rawCalculation,
                result.RawClockInLocalTime,
                result.RawClockOutLocalTime);
            dbContext.AttendanceAdjustments.Add(adjustment);
            result.ApplyAdjustment(
                adjustment.Id,
                adjustment.NewClockInLocalTime,
                adjustment.NewClockOutLocalTime,
                calculation,
                false,
                now);
            AddAdjustmentAudit(
                AuditActions.AttendanceAdjustmentReverted,
                result,
                adjustment);
            await SaveAsync(transactionToken);
            return await LoadDailyDtoAsync(result.Id, transactionToken);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<AttendanceAdjustmentDto>>
        GetAdjustmentHistoryAsync(
            Guid dailyAttendanceResultId,
            CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureRead(currentUser);
        var result = await dbContext.DailyAttendanceResults
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == dailyAttendanceResultId,
                cancellationToken)
            ?? throw new EntityNotFoundException(
                "Daily attendance result was not found.");
        EnsureCanReadEmployee(result.EmployeeId);
        return await dbContext.AttendanceAdjustments
            .AsNoTracking()
            .Where(item =>
                item.DailyAttendanceResultId == dailyAttendanceResultId)
            .OrderByDescending(item => item.RevisionNumber)
            .Select(item => new AttendanceAdjustmentDto(
                item.Id,
                item.RevisionNumber,
                item.Action.ToString(),
                item.PreviousClockInLocalTime,
                item.PreviousClockOutLocalTime,
                item.NewClockInLocalTime,
                item.NewClockOutLocalTime,
                item.Reason.ToString(),
                item.Note,
                item.AdjustedByUserId,
                item.AdjustedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<DailyAttendanceResultDto> ApplyNewAdjustmentAsync(
        AttendanceAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        AttendanceManagementAuthorization.EnsureAdjust(currentUser);
        RequestValidator.Validate(request);
        return await dbContext.ExecuteSerializableAsync(async transactionToken =>
        {
            var outcome = await ApplyNewAdjustmentCoreAsync(
                request,
                transactionToken);
            return await LoadDailyDtoAsync(outcome.ResultId, transactionToken);
        }, cancellationToken);
    }

    private async Task<AdjustmentApplicationOutcome> ApplyNewAdjustmentCoreAsync(
        AttendanceAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
            var result = await dbContext.DailyAttendanceResults
                .Include(item => item.LeaveSegments)
                .SingleOrDefaultAsync(
                    item => item.Id == request.DailyAttendanceResultId,
                    cancellationToken)
                ?? throw new EntityNotFoundException(
                    "Daily attendance result was not found.");
            EnsureRowVersion(result.RowVersion, request.RowVersion);
            if (!result.IsRequiredWorkday)
            {
                throw new ApplicationValidationException(
                    "A non-working day cannot be adjusted in this phase.");
            }

            var shift = result.GetShiftSnapshot()
                ?? throw new ApplicationValidationException(
                    "A result without a shift snapshot cannot be adjusted.");
            var newClockIn = request.AdjustClockIn
                ? request.RecognizedClockInLocalTime
                : result.EffectiveClockInLocalTime;
            var newClockOut = request.AdjustClockOut
                ? request.RecognizedClockOutLocalTime
                : result.EffectiveClockOutLocalTime;
            EnsureLocalWorkWindow(result.WorkDate, newClockIn, "clock-in", false);
            EnsureLocalWorkWindow(result.WorkDate, newClockOut, "clock-out", true);
            var revision = await NextRevisionAsync(result.Id, cancellationToken);
            AttendanceAdjustment? previous = null;
            if (result.CurrentAdjustmentId.HasValue)
            {
                previous = await dbContext.AttendanceAdjustments
                    .AsNoTracking()
                    .SingleAsync(
                        item => item.Id == result.CurrentAdjustmentId.Value,
                        cancellationToken);
            }

            var action = previous is null
                ? AttendanceAdjustmentAction.Created
                : AttendanceAdjustmentAction.Revised;
            var now = timeProvider.GetUtcNow();
            var adjustment = new AttendanceAdjustment(
                Guid.NewGuid(),
                result.Id,
                result.EmployeeId,
                result.WorkDate,
                revision,
                action,
                result.EffectiveClockInLocalTime,
                result.EffectiveClockOutLocalTime,
                newClockIn,
                newClockOut,
                request.Reason,
                request.Note,
                AttendanceManagementAuthorization.Actor(currentUser),
                now,
                previous?.Id);
            var calculation = AttendanceDailyCalculator.ApplyEffectiveTimes(
                result.WorkDate,
                shift,
                RawCalculation(result),
                newClockIn,
                newClockOut);
            dbContext.AttendanceAdjustments.Add(adjustment);
            result.ApplyAdjustment(
                adjustment.Id,
                newClockIn,
                newClockOut,
                calculation,
                true,
                now);
            AddAdjustmentAudit(
                action == AttendanceAdjustmentAction.Created
                    ? AuditActions.AttendanceAdjusted
                    : AuditActions.AttendanceAdjustmentRevised,
                result,
                adjustment);
            await SaveAsync(cancellationToken);
            return new AdjustmentApplicationOutcome(
                adjustment.Id,
                result.Id,
                result.EmployeeId,
                result.WorkDate);
    }

    private async Task EnsureNoAssignmentOverlapAsync(
        Guid employeeId,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        var newEnd = effectiveTo ?? DateOnly.MaxValue;
        var overlaps = await dbContext.EmployeeShiftAssignments
            .AsNoTracking()
            .AnyAsync(item =>
                item.EmployeeId == employeeId &&
                item.IsActive &&
                (!excludedId.HasValue || item.Id != excludedId.Value) &&
                item.EffectiveFrom <= newEnd &&
                (!item.EffectiveTo.HasValue ||
                 item.EffectiveTo.Value >= effectiveFrom),
                cancellationToken);
        if (overlaps)
        {
            throw new ApplicationValidationException(
                "The employee already has an active shift assignment in this period.");
        }
    }

    private async Task<EmployeeShiftAssignmentDto> LoadAssignmentDtoAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await dbContext.EmployeeShiftAssignments
            .AsNoTracking()
            .Include(item => item.Employee)
            .Include(item => item.Shift)
            .Where(item => item.Id == id)
            .Select(item => MapAssignment(item))
            .SingleAsync(cancellationToken);

    private async Task<DailyAttendanceResultDto> LoadDailyDtoAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await dbContext.DailyAttendanceResults
            .AsNoTracking()
            .Include(item => item.Employee)
            .Include(item => item.CurrentAdjustment)
            .Include(item => item.LeaveSegments)
            .Where(item => item.Id == id)
            .Select(item => MapDaily(item))
            .SingleAsync(cancellationToken);

    private async Task<int> NextRevisionAsync(
        Guid resultId,
        CancellationToken cancellationToken)
    {
        var current = await dbContext.AttendanceAdjustments
            .Where(item => item.DailyAttendanceResultId == resultId)
            .Select(item => (int?)item.RevisionNumber)
            .MaxAsync(cancellationToken);
        return (current ?? 0) + 1;
    }

    private void EnsureCanReadEmployee(Guid employeeId)
    {
        if (!currentUser.HasPermission(PolicyNames.AttendanceViewAll) &&
            currentUser.EmployeeId != employeeId)
        {
            throw new ForbiddenAccessException(
                "You may view only your own daily attendance.");
        }
    }

    private static void EnsureLocalWorkWindow(
        DateOnly workDate,
        DateTime? value,
        string field,
        bool allowFollowingDate)
    {
        if (!value.HasValue)
        {
            return;
        }

        var localDate = DateOnly.FromDateTime(value.Value);
        var isAllowed = localDate == workDate ||
            allowFollowingDate && localDate == workDate.AddDays(1);
        if (!isAllowed)
        {
            throw new ApplicationValidationException(
                allowFollowingDate
                    ? $"Recognized {field} must be on the attendance work date or the following date."
                    : $"Recognized {field} must be on the attendance work date.");
        }
    }

    private sealed record AdjustmentApplicationOutcome(
        Guid AdjustmentId,
        Guid ResultId,
        Guid EmployeeId,
        DateOnly WorkDate);

    private static AttendanceDailyCalculation RawCalculation(
        DailyAttendanceResult result) =>
        new(
            result.RawClockInEventId.HasValue &&
            result.RawClockInLocalTime.HasValue
                ? new AttendancePunchCandidate(
                    result.RawClockInEventId.Value,
                    result.RawClockInLocalTime.Value)
                : null,
            result.RawClockOutEventId.HasValue &&
            result.RawClockOutLocalTime.HasValue
                ? new AttendancePunchCandidate(
                    result.RawClockOutEventId.Value,
                    result.RawClockOutLocalTime.Value)
                : null,
            result.Status,
            result.IsLate,
            result.IsEarlyLeave,
            result.MissingClockIn,
            result.MissingClockOut,
            result.LateSeconds,
            result.EarlyLeaveSeconds,
            result.ApprovedLeaveMinutes,
            result.RequiredAttendanceMinutes,
            result.RecognizedWorkMinutes,
            result.MissingMinutes,
            result.WorkedDuringApprovedLeaveMinutes,
            result.LeaveCoverageStatus,
            result.LeaveSegments
                .OrderBy(item => item.StartAtUtc)
                .Select(item => new AttendanceLeaveSegmentCalculation(
                    item.LeaveRequestId,
                    item.LeaveTypeId,
                    item.LeaveTypeCodeSnapshot,
                    item.LeaveTypeNameSnapshot,
                    ToLocal(item.StartAtUtc),
                    ToLocal(item.EndAtUtc),
                    item.CoveredMinutes))
                .ToArray(),
            (result.RawClockInEventId.HasValue ? 1 : 0) +
            (result.RawClockOutEventId.HasValue &&
             result.RawClockOutEventId != result.RawClockInEventId ? 1 : 0),
            result.Status == AttendanceDailyStatus.AmbiguousPunch &&
            !result.RawClockInEventId.HasValue &&
            !result.RawClockOutEventId.HasValue,
            result.IsEmploymentSuspended,
            result.EmploymentSuspensionSourceId);

    private static AttendanceShiftSnapshot ToShiftSnapshot(
        AttendanceShift shift) =>
        new(
            shift.Id,
            shift.Name,
            shift.ScheduledStartTime,
            shift.LateThresholdTime,
            shift.LunchBreakStartTime,
            shift.LunchBreakEndTime,
            shift.ScheduledEndTime,
            shift.ExpectedWorkMinutes,
            shift.IsLunchPunchRequired,
            shift.IsOvernightShift);

    private static (
        bool IsRequired,
        AttendanceCalendarClassification Classification)
        ResolveCalendar(
            DateOnly date,
            IReadOnlyDictionary<DateOnly, CompanyCalendarDay> calendarDays)
    {
        if (calendarDays.TryGetValue(date, out var day))
        {
            return day.DayType switch
            {
                CompanyCalendarDayType.WorkingDay =>
                    (true, AttendanceCalendarClassification.WorkingDay),
                CompanyCalendarDayType.ExceptionalWorkingDay =>
                    (true, AttendanceCalendarClassification.ExceptionalWorkingDay),
                CompanyCalendarDayType.Saturday or
                CompanyCalendarDayType.Sunday =>
                    (false, AttendanceCalendarClassification.Weekend),
                _ => (false, AttendanceCalendarClassification.Holiday)
            };
        }

        return date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? (false, AttendanceCalendarClassification.FallbackRestDay)
            : (true, AttendanceCalendarClassification.FallbackWorkingDay);
    }

    private (DateOnly From, DateOnly To) ResolveQueryRange(
        DateOnly? dateFrom,
        DateOnly? dateTo)
    {
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TaipeiZone).DateTime);
        var from = dateFrom ?? dateTo?.AddDays(-6) ?? today.AddDays(-6);
        var to = dateTo ?? from.AddDays(6);
        if (to < from)
        {
            throw new ApplicationValidationException(
                "End date cannot be earlier than start date.");
        }

        if (to.DayNumber - from.DayNumber + 1 > 366)
        {
            throw new ApplicationValidationException(
                "Daily attendance queries cannot exceed 366 calendar days.");
        }

        return (from, to);
    }

    private static TimeZoneInfo ResolveTaipeiZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
        }
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
            dbContext.IsUniqueConstraintViolation(exception, ShiftCodeIndex))
        {
            throw new ApplicationValidationException(
                "The attendance shift code already exists.");
        }
        catch (DbUpdateException exception) when (
            dbContext.IsUniqueConstraintViolation(exception, DailyResultIndex))
        {
            throw new ConcurrencyConflictException(
                "The daily attendance result changed during recalculation.");
        }
    }

    private static void EnsureRowVersion(byte[] current, string supplied)
    {
        var expected = string.IsNullOrWhiteSpace(supplied)
            ? []
            : Convert.FromBase64String(supplied);
        if (!current.SequenceEqual(expected))
        {
            throw new ConcurrencyConflictException();
        }
    }

    private void AddAdjustmentAudit(
        string action,
        DailyAttendanceResult result,
        AttendanceAdjustment adjustment)
    {
        dbContext.AuditLogs.Add(AuditLogFactory.Create(
            currentUser,
            timeProvider,
            action,
            nameof(AttendanceAdjustment),
            adjustment.Id.ToString(),
            null,
            new
            {
                result.EmployeeId,
                result.WorkDate,
                adjustment.RevisionNumber,
                adjustment.Action,
                adjustment.Reason
            }));
    }

    private static object ShiftSnapshot(AttendanceShift shift) => new
    {
        shift.Code,
        shift.Name,
        shift.ScheduledStartTime,
        shift.LateThresholdTime,
        shift.LunchBreakStartTime,
        shift.LunchBreakEndTime,
        shift.ScheduledEndTime,
        shift.ExpectedWorkMinutes,
        shift.IsLunchPunchRequired,
        shift.IsOvernightShift,
        shift.IsActive
    };

    private static object AssignmentSnapshot(EmployeeShiftAssignment assignment) =>
        new
        {
            assignment.EmployeeId,
            assignment.ShiftId,
            assignment.EffectiveFrom,
            assignment.EffectiveTo,
            assignment.IsActive
        };

    private static AttendanceShiftDto MapShift(AttendanceShift shift) =>
        new(
            shift.Id,
            shift.Code,
            shift.Name,
            shift.ScheduledStartTime,
            shift.LateThresholdTime,
            shift.LunchBreakStartTime,
            shift.LunchBreakEndTime,
            shift.ScheduledEndTime,
            shift.ExpectedWorkMinutes,
            shift.IsLunchPunchRequired,
            shift.IsOvernightShift,
            shift.IsActive,
            Convert.ToBase64String(shift.RowVersion));

    private static EmployeeShiftAssignmentDto MapAssignment(
        EmployeeShiftAssignment assignment) =>
        new(
            assignment.Id,
            assignment.EmployeeId,
            assignment.Employee.EmployeeNumber,
            assignment.Employee.ChineseName,
            assignment.ShiftId,
            assignment.Shift.Code,
            assignment.Shift.Name,
            assignment.EffectiveFrom,
            assignment.EffectiveTo,
            assignment.IsActive,
            Convert.ToBase64String(assignment.RowVersion));

    private static DailyAttendanceResultDto MapDaily(
        DailyAttendanceResult result) =>
        new(
            result.Id,
            result.EmployeeId,
            result.Employee.EmployeeNumber,
            result.Employee.ChineseName,
            result.WorkDate,
            result.IsRequiredWorkday,
            result.CalendarClassification.ToString(),
            result.ShiftCodeSnapshot,
            result.ShiftNameSnapshot,
            result.RawClockInLocalTime,
            result.RawClockOutLocalTime,
            result.EffectiveClockInLocalTime,
            result.EffectiveClockOutLocalTime,
            result.Status.ToString(),
            result.IsLate,
            result.IsEarlyLeave,
            result.MissingClockIn,
            result.MissingClockOut,
            result.LateSeconds,
            result.EarlyLeaveSeconds,
            result.ApprovedLeaveMinutes,
            result.RequiredAttendanceMinutes,
            result.RecognizedWorkMinutes,
            result.MissingMinutes,
            result.WorkedDuringApprovedLeaveMinutes,
            result.LeaveCoverageStatus.ToString(),
            result.IsEmploymentSuspended,
            result.EmploymentSuspensionSourceId,
            result.IsAttendanceExempted,
            result.AttendanceExceptionMinutes,
            result.AttendanceExceptionSourceId,
            result.AttendanceExceptionType.HasValue ? result.AttendanceExceptionType.Value.ToString() : null,
            result.AttendanceExceptionReasonType.HasValue ? result.AttendanceExceptionReasonType.Value.ToString() : null,
            result.AttendanceExceptionImpactType.HasValue ? result.AttendanceExceptionImpactType.Value.ToString() : null,
            result.AttendanceExceptionFromTime,
            result.AttendanceExceptionToTime,
            result.LeaveSegments
                .OrderBy(item => item.StartAtUtc)
                .Select(item => new DailyAttendanceLeaveSegmentDto(
                    item.Id,
                    item.LeaveRequestId,
                    item.LeaveTypeId,
                    item.LeaveTypeCodeSnapshot,
                    item.LeaveTypeNameSnapshot,
                    item.StartAtUtc,
                    item.EndAtUtc,
                    item.CoveredMinutes))
                .ToList(),
            result.IsAdjusted,
            result.CurrentAdjustment == null
                ? null
                : result.CurrentAdjustment.Reason.ToString(),
            null,
            result.CalculatedAtUtc,
            Convert.ToBase64String(result.RowVersion));

    private static DateTime ToLocal(DateTimeOffset utc) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTime(utc, TaipeiZone).DateTime,
            DateTimeKind.Unspecified);
}
