using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.CompanyCalendars;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.ParentalLeave;
using HRSystem.Domain.AttendanceExceptions;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Attendance;

public sealed record AttendanceRecalculationKey(
    Guid EmployeeId,
    DateOnly WorkDate);

public sealed record PendingApprovedLeave(
    Guid LeaveRequestId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc);

public sealed record PendingEmploymentSuspension(
    Guid ParentalLeaveRequestId,
    Guid EmployeeId,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record PendingAttendanceException(
    Guid AttendanceExceptionId,
    Guid EmployeeId,
    DateOnly WorkDate,
    AttendanceExceptionType ExceptionType,
    NaturalDisasterReasonType ReasonType,
    AttendanceExceptionImpactType ImpactType,
    TimeOnly? ExemptFromTime,
    TimeOnly? ExemptToTime);

public sealed record AttendanceRecalculationOutcome(
    DateOnly DateFrom,
    DateOnly DateTo,
    int EmployeeCount,
    int ResultCount,
    DateTimeOffset CompletedAtUtc);

public interface IAttendanceRecalculationEngine
{
    Task<AttendanceRecalculationOutcome> RecalculateRangeAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        Guid? employeeId = null,
        IReadOnlyCollection<PendingApprovedLeave>? pendingApprovedLeaves = null,
        CancellationToken cancellationToken = default);

    Task<AttendanceRecalculationOutcome>
        RecalculateRangeWithEmploymentSuspensionsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        Guid? employeeId,
        IReadOnlyCollection<PendingEmploymentSuspension>
            pendingEmploymentSuspensions,
        IReadOnlyCollection<Guid>? excludedParentalLeaveRequestIds = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This recalculation engine does not support employment suspensions.");

    Task<AttendanceRecalculationOutcome> RecalculateKeysAsync(
        IReadOnlyCollection<AttendanceRecalculationKey> keys,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<Guid>? excludedApprovedLeaveRequestIds = null);

    Task<AttendanceRecalculationOutcome>
        RecalculateKeysWithEmploymentSuspensionsAsync(
        IReadOnlyCollection<AttendanceRecalculationKey> keys,
        IReadOnlyCollection<PendingEmploymentSuspension>?
            pendingEmploymentSuspensions = null,
        IReadOnlyCollection<Guid>?
            excludedParentalLeaveRequestIds = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This recalculation engine does not support employment suspensions.");

    Task<AttendanceRecalculationOutcome> RecalculateKeysWithAttendanceExceptionsAsync(
        IReadOnlyCollection<AttendanceRecalculationKey> keys,
        IReadOnlyCollection<PendingAttendanceException>? pendingAttendanceExceptions = null,
        IReadOnlyCollection<Guid>? excludedAttendanceExceptionIds = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This recalculation engine does not support attendance exceptions.");
}

public sealed class AttendanceRecalculationEngine(
    IApplicationDbContext dbContext,
    TimeProvider timeProvider) : IAttendanceRecalculationEngine
{
    public const string CurrentCalculationVersion = "8.4.0";
    private static readonly TimeZoneInfo TaipeiZone = ResolveTaipeiZone();

    public Task<AttendanceRecalculationOutcome> RecalculateRangeAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        Guid? employeeId = null,
        IReadOnlyCollection<PendingApprovedLeave>? pendingApprovedLeaves = null,
        CancellationToken cancellationToken = default) =>
        RecalculateCoreAsync(
            dateFrom,
            dateTo,
            employeeId,
            null,
            pendingApprovedLeaves ?? [],
            [],
            [],
            [],
            [],
            [],
            cancellationToken);

    public Task<AttendanceRecalculationOutcome>
        RecalculateRangeWithEmploymentSuspensionsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        Guid? employeeId,
        IReadOnlyCollection<PendingEmploymentSuspension>
            pendingEmploymentSuspensions,
        IReadOnlyCollection<Guid>? excludedParentalLeaveRequestIds = null,
        CancellationToken cancellationToken = default) =>
        RecalculateCoreAsync(
            dateFrom,
            dateTo,
            employeeId,
            null,
            [],
            [],
            pendingEmploymentSuspensions,
            excludedParentalLeaveRequestIds ?? [],
            [],
            [],
            cancellationToken);

    public Task<AttendanceRecalculationOutcome> RecalculateKeysAsync(
        IReadOnlyCollection<AttendanceRecalculationKey> keys,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<Guid>? excludedApprovedLeaveRequestIds = null) =>
        RecalculateKeysCoreAsync(
            keys,
            excludedApprovedLeaveRequestIds ?? [],
            [],
            [],
            [],
            [],
            cancellationToken);

    public Task<AttendanceRecalculationOutcome>
        RecalculateKeysWithEmploymentSuspensionsAsync(
        IReadOnlyCollection<AttendanceRecalculationKey> keys,
        IReadOnlyCollection<PendingEmploymentSuspension>?
            pendingEmploymentSuspensions = null,
        IReadOnlyCollection<Guid>?
            excludedParentalLeaveRequestIds = null,
        CancellationToken cancellationToken = default) =>
        RecalculateKeysCoreAsync(
            keys,
            [],
            pendingEmploymentSuspensions ?? [],
            excludedParentalLeaveRequestIds ?? [],
            [],
            [],
            cancellationToken);

    public Task<AttendanceRecalculationOutcome> RecalculateKeysWithAttendanceExceptionsAsync(
        IReadOnlyCollection<AttendanceRecalculationKey> keys,
        IReadOnlyCollection<PendingAttendanceException>? pendingAttendanceExceptions = null,
        IReadOnlyCollection<Guid>? excludedAttendanceExceptionIds = null,
        CancellationToken cancellationToken = default) =>
        RecalculateKeysCoreAsync(keys, [], [], [], pendingAttendanceExceptions ?? [],
            excludedAttendanceExceptionIds ?? [], cancellationToken);

    private Task<AttendanceRecalculationOutcome> RecalculateKeysCoreAsync(
        IReadOnlyCollection<AttendanceRecalculationKey> keys,
        IReadOnlyCollection<Guid> excludedApprovedLeaveRequestIds,
        IReadOnlyCollection<PendingEmploymentSuspension>
            pendingEmploymentSuspensions,
        IReadOnlyCollection<Guid> excludedParentalLeaveRequestIds,
        IReadOnlyCollection<PendingAttendanceException> pendingAttendanceExceptions,
        IReadOnlyCollection<Guid> excludedAttendanceExceptionIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var distinct = keys
            .Where(item => item.EmployeeId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (distinct.Length == 0)
        {
            var today = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(
                    timeProvider.GetUtcNow(),
                    TaipeiZone).DateTime);
            return Task.FromResult(new AttendanceRecalculationOutcome(
                today,
                today,
                0,
                0,
                timeProvider.GetUtcNow()));
        }

        return RecalculateCoreAsync(
            distinct.Min(item => item.WorkDate),
            distinct.Max(item => item.WorkDate),
            null,
            distinct.ToHashSet(),
            [],
            excludedApprovedLeaveRequestIds,
            pendingEmploymentSuspensions,
            excludedParentalLeaveRequestIds,
            pendingAttendanceExceptions,
            excludedAttendanceExceptionIds,
            cancellationToken);
    }

    private async Task<AttendanceRecalculationOutcome> RecalculateCoreAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        Guid? employeeId,
        IReadOnlySet<AttendanceRecalculationKey>? exactKeys,
        IReadOnlyCollection<PendingApprovedLeave> pendingApprovedLeaves,
        IReadOnlyCollection<Guid> excludedApprovedLeaveRequestIds,
        IReadOnlyCollection<PendingEmploymentSuspension>
            pendingEmploymentSuspensions,
        IReadOnlyCollection<Guid> excludedParentalLeaveRequestIds,
        IReadOnlyCollection<PendingAttendanceException> pendingAttendanceExceptions,
        IReadOnlyCollection<Guid> excludedAttendanceExceptionIds,
        CancellationToken cancellationToken)
    {
        if (dateFrom == default || dateTo < dateFrom)
        {
            throw new ApplicationValidationException(
                "A valid attendance recalculation date range is required.");
        }

        var exactEmployeeIds = exactKeys?
            .Select(item => item.EmployeeId)
            .Distinct()
            .ToArray();
        var employees = await dbContext.Employees
            .AsNoTracking()
            .Where(item =>
                item.IsActive &&
                item.HireDate <= dateTo &&
                (!item.TerminationDate.HasValue ||
                 item.TerminationDate.Value >= dateFrom))
            .Where(item =>
                (!employeeId.HasValue || item.Id == employeeId.Value) &&
                (exactEmployeeIds == null || exactEmployeeIds.Contains(item.Id)))
            .OrderBy(item => item.EmployeeNumber)
            .ToListAsync(cancellationToken);
        if (employeeId.HasValue && employees.Count == 0)
        {
            throw new ApplicationValidationException(
                "Employee was not found in the requested date range.");
        }

        var employeeIds = employees.Select(item => item.Id).ToArray();
        var assignments = await dbContext.EmployeeShiftAssignments
            .AsNoTracking()
            .Include(item => item.Shift)
            .Where(item =>
                employeeIds.Contains(item.EmployeeId) &&
                item.IsActive &&
                item.EffectiveFrom <= dateTo &&
                (!item.EffectiveTo.HasValue ||
                 item.EffectiveTo.Value >= dateFrom))
            .ToListAsync(cancellationToken);
        var calendarDays = await dbContext.CompanyCalendarDays
            .AsNoTracking()
            .Include(item => item.Year)
            .Where(item =>
                item.Date >= dateFrom &&
                item.Date <= dateTo &&
                item.Year.Status == CompanyCalendarStatus.Published)
            .ToDictionaryAsync(item => item.Date, cancellationToken);
        var rawFrom = DateTime.SpecifyKind(
            dateFrom.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        var rawToExclusive = DateTime.SpecifyKind(
            dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        var rawEvents = await dbContext.AttendanceRawEvents
            .AsNoTracking()
            .Where(item =>
                item.SourceSystem == AttendanceSourceSystems.BioWebTa &&
                item.EmployeeId.HasValue &&
                employeeIds.Contains(item.EmployeeId.Value) &&
                item.EventLocalDateTime >= rawFrom &&
                item.EventLocalDateTime < rawToExclusive)
            .Select(item => new
            {
                item.Id,
                EmployeeId = item.EmployeeId!.Value,
                item.EventLocalDateTime
            })
            .ToListAsync(cancellationToken);
        var existing = await dbContext.DailyAttendanceResults
            .Include(item => item.LeaveSegments)
            .Where(item =>
                employeeIds.Contains(item.EmployeeId) &&
                item.WorkDate >= dateFrom &&
                item.WorkDate <= dateTo)
            .ToListAsync(cancellationToken);
        var currentAdjustmentIds = existing
            .Where(item => item.CurrentAdjustmentId.HasValue)
            .Select(item => item.CurrentAdjustmentId!.Value)
            .ToArray();
        var currentAdjustments = await dbContext.AttendanceAdjustments
            .AsNoTracking()
            .Where(item => currentAdjustmentIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var approvedLeaves = await LoadApprovedLeavesAsync(
            employeeIds,
            dateFrom,
            dateTo,
            pendingApprovedLeaves,
            excludedApprovedLeaveRequestIds,
            cancellationToken);
        var employmentSuspensions = await LoadEmploymentSuspensionsAsync(
            employeeIds,
            dateFrom,
            dateTo,
            pendingEmploymentSuspensions,
            excludedParentalLeaveRequestIds,
            cancellationToken);
        var attendanceExceptions = await LoadAttendanceExceptionsAsync(
            employeeIds, dateFrom, dateTo, pendingAttendanceExceptions,
            excludedAttendanceExceptionIds, cancellationToken);
        var existingByKey = existing.ToDictionary(
            item => new AttendanceRecalculationKey(
                item.EmployeeId,
                item.WorkDate));
        var rawByKey = rawEvents
            .GroupBy(item => new AttendanceRecalculationKey(
                item.EmployeeId,
                DateOnly.FromDateTime(item.EventLocalDateTime)))
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => new AttendancePunchCandidate(
                        item.Id,
                        item.EventLocalDateTime))
                    .ToArray());
        var assignmentsByEmployee = assignments
            .GroupBy(item => item.EmployeeId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var leavesByEmployee = approvedLeaves
            .GroupBy(item => item.EmployeeId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var suspensionsByEmployee = employmentSuspensions
            .GroupBy(item => item.EmployeeId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var exceptionsByEmployee = attendanceExceptions
            .GroupBy(item => item.EmployeeId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var completedAt = timeProvider.GetUtcNow();
        var resultCount = 0;
        foreach (var employee in employees)
        {
            for (var date = dateFrom; date <= dateTo; date = date.AddDays(1))
            {
                var key = new AttendanceRecalculationKey(employee.Id, date);
                if (exactKeys is not null && !exactKeys.Contains(key))
                {
                    continue;
                }

                if (date < employee.HireDate ||
                    (employee.TerminationDate.HasValue &&
                     date > employee.TerminationDate.Value))
                {
                    continue;
                }

                var (isRequired, classification) = ResolveCalendar(
                    date,
                    calendarDays);
                assignmentsByEmployee.TryGetValue(
                    employee.Id,
                    out var employeeAssignments);
                var applicableAssignments = employeeAssignments?
                    .Where(item => item.AppliesOn(date))
                    .ToArray() ?? [];
                if (applicableAssignments.Length > 1)
                {
                    throw new ApplicationValidationException(
                        "Overlapping active shift assignments were found.");
                }

                var shift = applicableAssignments.SingleOrDefault()?.Shift;
                rawByKey.TryGetValue(key, out var punches);
                leavesByEmployee.TryGetValue(
                    employee.Id,
                    out var employeeLeaves);
                var leaveIntervals = employeeLeaves?
                    .Where(item => item.Overlaps(date))
                    .Select(item => item.ToCalculationInterval())
                    .ToArray() ?? [];
                suspensionsByEmployee.TryGetValue(
                    employee.Id,
                    out var employeeSuspensions);
                var applicableSuspensions = employeeSuspensions?
                    .Where(item => item.AppliesOn(date))
                    .ToArray() ?? [];
                if (applicableSuspensions.Length > 1)
                {
                    throw new ApplicationValidationException(
                        "Overlapping employment suspension periods were found.");
                }
                exceptionsByEmployee.TryGetValue(employee.Id, out var employeeExceptions);
                var applicableExceptions = employeeExceptions?.Where(item => item.WorkDate == date).ToArray() ?? [];
                if (applicableExceptions.Length > 1)
                {
                    throw new ApplicationValidationException("Multiple active attendance exceptions were found for the same date.");
                }
                var exception = applicableExceptions.SingleOrDefault();

                var calculation = AttendanceDailyCalculator.Calculate(
                    date,
                    isRequired,
                    shift is null ? null : ToShiftSnapshot(shift),
                    punches ?? [],
                    leaveIntervals,
                    applicableSuspensions.SingleOrDefault()?.
                        ParentalLeaveRequestId,
                    applicableSuspensions.Length == 0 && exception is not null
                        ? exception.ToCalculation()
                        : null);
                if (!existingByKey.TryGetValue(key, out var result))
                {
                    result = new DailyAttendanceResult(
                        Guid.NewGuid(),
                        employee.Id,
                        date,
                        completedAt);
                    dbContext.DailyAttendanceResults.Add(result);
                    existingByKey[key] = result;
                }

                var adjustmentId = result.CurrentAdjustmentId;
                result.Recalculate(
                    isRequired,
                    classification,
                    shift,
                    calculation,
                    CurrentCalculationVersion,
                    completedAt);
                if (result.LeaveSegments.Count > 0)
                {
                    dbContext.DailyAttendanceLeaveSegments.RemoveRange(
                        result.LeaveSegments);
                }
                result.ReplaceLeaveSegments(calculation.LeaveSegments.Select(
                    segment => new DailyAttendanceLeaveSegment(
                        Guid.NewGuid(),
                        result.Id,
                        segment.LeaveRequestId,
                        segment.LeaveTypeId,
                        segment.LeaveTypeCode,
                        segment.LeaveTypeName,
                        ToUtc(segment.StartLocal),
                        ToUtc(segment.EndLocal),
                        segment.CoveredMinutes,
                        completedAt)));
                if (adjustmentId.HasValue &&
                    currentAdjustments.TryGetValue(
                        adjustmentId.Value,
                        out var adjustment) &&
                    shift is not null)
                {
                    var adjustedCalculation =
                        AttendanceDailyCalculator.ApplyEffectiveTimes(
                            date,
                            ToShiftSnapshot(shift),
                            calculation,
                            adjustment.NewClockInLocalTime,
                            adjustment.NewClockOutLocalTime);
                    result.ApplyAdjustment(
                        adjustment.Id,
                        adjustment.NewClockInLocalTime,
                        adjustment.NewClockOutLocalTime,
                        adjustedCalculation,
                        adjustment.Action !=
                            AttendanceAdjustmentAction.Reverted,
                        completedAt);
                }

                resultCount++;
            }
        }

        return new AttendanceRecalculationOutcome(
            dateFrom,
            dateTo,
            employees.Count,
            resultCount,
            completedAt);
    }

    private async Task<IReadOnlyList<ApprovedLeaveSource>>
        LoadApprovedLeavesAsync(
            IReadOnlyCollection<Guid> employeeIds,
            DateOnly dateFrom,
            DateOnly dateTo,
            IReadOnlyCollection<PendingApprovedLeave> pending,
            IReadOnlyCollection<Guid> excludedLeaveRequestIds,
            CancellationToken cancellationToken)
    {
        var fromUtc = ToUtc(DateTime.SpecifyKind(
            dateFrom.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified));
        var toUtcExclusive = ToUtc(DateTime.SpecifyKind(
            dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified));
        var ignoredIds = pending
            .Select(item => item.LeaveRequestId)
            .Concat(excludedLeaveRequestIds)
            .Distinct()
            .ToArray();
        var persisted = await dbContext.LeaveRequests
            .AsNoTracking()
            .Where(item =>
                employeeIds.Contains(item.EmployeeId) &&
                (item.Status == LeaveRequestStatus.Approved ||
                    item.Status == LeaveRequestStatus.CancellationRequested) &&
                item.StartAt < toUtcExclusive &&
                item.EndAt > fromUtc &&
                !ignoredIds.Contains(item.Id))
            .Select(item => new ApprovedLeaveSource(
                item.Id,
                item.EmployeeId,
                item.LeaveTypeId,
                item.LeaveType.Code,
                item.LeaveType.Name,
                item.StartAt,
                item.EndAt))
            .ToListAsync(cancellationToken);
        persisted.AddRange(pending.Select(item => new ApprovedLeaveSource(
            item.LeaveRequestId,
            item.EmployeeId,
            item.LeaveTypeId,
            item.LeaveTypeCode,
            item.LeaveTypeName,
            item.StartAtUtc.ToUniversalTime(),
            item.EndAtUtc.ToUniversalTime())));
        return persisted;
    }

    private async Task<IReadOnlyList<EmploymentSuspensionSource>>
        LoadEmploymentSuspensionsAsync(
            IReadOnlyCollection<Guid> employeeIds,
            DateOnly dateFrom,
            DateOnly dateTo,
            IReadOnlyCollection<PendingEmploymentSuspension> pending,
            IReadOnlyCollection<Guid> excludedRequestIds,
            CancellationToken cancellationToken)
    {
        var ignoredIds = pending
            .Select(item => item.ParentalLeaveRequestId)
            .Concat(excludedRequestIds)
            .Distinct()
            .ToArray();
        var persisted = await dbContext.ParentalLeaveRequests
            .AsNoTracking()
            .Where(item =>
                employeeIds.Contains(item.EmployeeId) &&
                (item.Status == ParentalLeaveStatus.Approved ||
                    item.Status == ParentalLeaveStatus.CancellationRequested) &&
                item.StartDate <= dateTo &&
                (item.EarlyReturnApprovedAtUtc.HasValue &&
                    item.EarlyReturnDate.HasValue
                        ? item.EarlyReturnDate.Value.AddDays(-1)
                        : item.EndDate) >= dateFrom &&
                !ignoredIds.Contains(item.Id))
            .Select(item => new EmploymentSuspensionSource(
                item.Id,
                item.EmployeeId,
                item.StartDate,
                item.EarlyReturnApprovedAtUtc.HasValue &&
                    item.EarlyReturnDate.HasValue
                        ? item.EarlyReturnDate.Value.AddDays(-1)
                        : item.EndDate))
            .ToListAsync(cancellationToken);
        persisted.AddRange(pending.Select(item =>
            new EmploymentSuspensionSource(
                item.ParentalLeaveRequestId,
                item.EmployeeId,
                item.StartDate,
                item.EndDate)));
        return persisted;
    }

    private async Task<IReadOnlyList<AttendanceExceptionSource>> LoadAttendanceExceptionsAsync(
        IReadOnlyCollection<Guid> employeeIds, DateOnly dateFrom, DateOnly dateTo,
        IReadOnlyCollection<PendingAttendanceException> pending,
        IReadOnlyCollection<Guid> excludedIds, CancellationToken cancellationToken)
    {
        var ignored = pending.Select(x => x.AttendanceExceptionId).Concat(excludedIds).Distinct().ToArray();
        var persisted = await dbContext.AttendanceExceptions.AsNoTracking()
            .Where(x => employeeIds.Contains(x.EmployeeId) && x.WorkDate >= dateFrom && x.WorkDate <= dateTo &&
                (x.Status == AttendanceExceptionStatus.Approved || x.Status == AttendanceExceptionStatus.CancellationRequested) &&
                !ignored.Contains(x.Id))
            .Select(x => new AttendanceExceptionSource(x.Id, x.EmployeeId, x.WorkDate, x.ExceptionType,
                x.ReasonType, x.ImpactType, x.ExemptFromTime, x.ExemptToTime))
            .ToListAsync(cancellationToken);
        persisted.AddRange(pending.Select(x => new AttendanceExceptionSource(x.AttendanceExceptionId, x.EmployeeId,
            x.WorkDate, x.ExceptionType, x.ReasonType, x.ImpactType, x.ExemptFromTime, x.ExemptToTime)));
        return persisted;
    }

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
        calendarDays.TryGetValue(date, out var day);
        return AttendanceCalendarResolver.Resolve(date, day);
    }

    private static DateTimeOffset ToUtc(DateTime local) =>
        new(
            TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
                TaipeiZone),
            TimeSpan.Zero);

    private static DateTime ToLocal(DateTimeOffset utc) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTime(utc, TaipeiZone).DateTime,
            DateTimeKind.Unspecified);

    private static TimeZoneInfo ResolveTaipeiZone()
    {
        foreach (var id in new[] { "Taipei Standard Time", "Asia/Taipei" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        throw new InvalidOperationException("Taipei time zone was not found.");
    }

    private sealed record ApprovedLeaveSource(
        Guid LeaveRequestId,
        Guid EmployeeId,
        Guid LeaveTypeId,
        string LeaveTypeCode,
        string LeaveTypeName,
        DateTimeOffset StartAtUtc,
        DateTimeOffset EndAtUtc)
    {
        public bool Overlaps(DateOnly workDate)
        {
            var startLocal = ToLocal(StartAtUtc);
            var endLocal = ToLocal(EndAtUtc);
            var dayStart = workDate.ToDateTime(TimeOnly.MinValue);
            var dayEnd = workDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
            return startLocal < dayEnd && endLocal > dayStart;
        }

        public AttendanceApprovedLeaveInterval ToCalculationInterval() =>
            new(
                LeaveRequestId,
                LeaveTypeId,
                LeaveTypeCode,
                LeaveTypeName,
                ToLocal(StartAtUtc),
                ToLocal(EndAtUtc));
    }

    private sealed record EmploymentSuspensionSource(
        Guid ParentalLeaveRequestId,
        Guid EmployeeId,
        DateOnly StartDate,
        DateOnly EndDate)
    {
        public bool AppliesOn(DateOnly date) =>
            date >= StartDate && date <= EndDate;
    }

    private sealed record AttendanceExceptionSource(Guid AttendanceExceptionId, Guid EmployeeId,
        DateOnly WorkDate, AttendanceExceptionType ExceptionType, NaturalDisasterReasonType ReasonType,
        AttendanceExceptionImpactType ImpactType, TimeOnly? ExemptFromTime, TimeOnly? ExemptToTime)
    {
        public AttendanceApprovedException ToCalculation() => new(AttendanceExceptionId, ExceptionType,
            ReasonType, ImpactType, ExemptFromTime, ExemptToTime);
    }
}
