namespace HRSystem.Domain.Attendance;

using HRSystem.Domain.AttendanceExceptions;

public sealed record AttendancePunchCandidate(
    Guid EventId,
    DateTime LocalTime);

public sealed record AttendanceShiftSnapshot(
    Guid ShiftId,
    string ShiftName,
    TimeOnly ScheduledStartTime,
    TimeOnly LateThresholdTime,
    TimeOnly LunchBreakStartTime,
    TimeOnly LunchBreakEndTime,
    TimeOnly ScheduledEndTime,
    int ExpectedWorkMinutes,
    bool IsLunchPunchRequired,
    bool IsOvernightShift);

public sealed record AttendanceApprovedLeaveInterval(
    Guid LeaveRequestId,
    Guid LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    DateTime StartLocal,
    DateTime EndLocal);

public sealed record AttendanceLeaveSegmentCalculation(
    Guid LeaveRequestId,
    Guid LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    DateTime StartLocal,
    DateTime EndLocal,
    int CoveredMinutes);

public sealed record AttendanceApprovedException(
    Guid AttendanceExceptionId,
    AttendanceExceptionType ExceptionType,
    NaturalDisasterReasonType ReasonType,
    AttendanceExceptionImpactType ImpactType,
    TimeOnly? ExemptFromTime,
    TimeOnly? ExemptToTime);

public sealed record AttendanceDailyCalculation(
    AttendancePunchCandidate? RawClockIn,
    AttendancePunchCandidate? RawClockOut,
    AttendanceDailyStatus Status,
    bool IsLate,
    bool IsEarlyLeave,
    bool MissingClockIn,
    bool MissingClockOut,
    int LateSeconds,
    int EarlyLeaveSeconds,
    int ApprovedLeaveMinutes,
    int RequiredAttendanceMinutes,
    int RecognizedWorkMinutes,
    int MissingMinutes,
    int WorkedDuringApprovedLeaveMinutes,
    LeaveCoverageStatus LeaveCoverageStatus,
    IReadOnlyList<AttendanceLeaveSegmentCalculation> LeaveSegments,
    int RawPunchCount,
    bool HasOnlyUnrecognizedPunches,
    bool IsEmploymentSuspended = false,
    Guid? EmploymentSuspensionSourceId = null,
    bool IsAttendanceExempted = false,
    int AttendanceExceptionMinutes = 0,
    Guid? AttendanceExceptionSourceId = null,
    AttendanceExceptionType? AttendanceExceptionType = null,
    NaturalDisasterReasonType? AttendanceExceptionReasonType = null,
    AttendanceExceptionImpactType? AttendanceExceptionImpactType = null,
    TimeOnly? AttendanceExceptionFromTime = null,
    TimeOnly? AttendanceExceptionToTime = null);

public static class AttendanceDailyCalculator
{
    public static AttendanceDailyCalculation Calculate(
        DateOnly workDate,
        bool isRequiredWorkday,
        AttendanceShiftSnapshot? shift,
        IEnumerable<AttendancePunchCandidate> punches,
        IEnumerable<AttendanceApprovedLeaveInterval>? approvedLeaves = null,
        Guid? employmentSuspensionSourceId = null,
        AttendanceApprovedException? approvedException = null)
    {
        ArgumentNullException.ThrowIfNull(punches);
        var ordered = punches
            .OrderBy(item => item.LocalTime)
            .ToArray();
        if (employmentSuspensionSourceId.HasValue)
        {
            return EmploymentSuspended(
                ordered,
                employmentSuspensionSourceId.Value);
        }

        if (!isRequiredWorkday)
        {
            return Empty(AttendanceDailyStatus.RestDay);
        }

        if (shift is null)
        {
            return Empty(AttendanceDailyStatus.NoShift);
        }

        if (shift.IsOvernightShift)
        {
            throw new InvalidOperationException(
                "跨日班別計算尚未在本階段啟用。");
        }

        var workIntervals = BuildWorkingIntervals(workDate, shift);
        var leaveSegments = NormalizeLeaveSegments(
            workIntervals,
            approvedLeaves ?? []);
        var requiredIntervals = Subtract(
            workIntervals,
            leaveSegments.Select(item => new TimeInterval(
                item.StartLocal,
                item.EndLocal)));
        var exceptionIntervals = BuildExceptionIntervals(
            workDate,
            shift,
            requiredIntervals,
            approvedException);
        var dutyIntervals = Subtract(requiredIntervals, exceptionIntervals);
        var selectionIntervals = dutyIntervals.Count > 0
            ? dutyIntervals
            : requiredIntervals.Count > 0
                ? requiredIntervals
            : workIntervals;
        var clockIn = selectionIntervals.Count == 0
            ? null
            : ordered.FirstOrDefault(item =>
                item.LocalTime < selectionIntervals[0].End);
        var clockOut = selectionIntervals.Count == 0
            ? null
            : ordered.LastOrDefault(item =>
                item.LocalTime >= selectionIntervals[^1].Start);
        if (clockIn is not null &&
            clockOut is not null &&
            clockIn.EventId == clockOut.EventId)
        {
            var fromStart = Math.Abs(
                (clockIn.LocalTime - selectionIntervals[0].Start)
                .TotalSeconds);
            var fromEnd = Math.Abs(
                (selectionIntervals[^1].End - clockOut.LocalTime)
                .TotalSeconds);
            if (fromStart <= fromEnd)
            {
                clockOut = null;
            }
            else
            {
                clockIn = null;
            }
        }

        var onlyUnrecognized = ordered.Length > 0 &&
            clockIn is null &&
            clockOut is null;
        return CalculateCore(
            workDate,
            shift,
            clockIn,
            clockOut,
            clockIn?.LocalTime,
            clockOut?.LocalTime,
            ordered.Length,
            onlyUnrecognized,
            workIntervals,
            leaveSegments,
            approvedException);
    }

    public static AttendanceDailyCalculation ApplyEffectiveTimes(
        DateOnly workDate,
        AttendanceShiftSnapshot shift,
        AttendanceDailyCalculation raw,
        DateTime? effectiveClockIn,
        DateTime? effectiveClockOut)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.IsEmploymentSuspended)
        {
            return raw;
        }

        var workIntervals = BuildWorkingIntervals(workDate, shift);
        return CalculateCore(
            workDate,
            shift,
            raw.RawClockIn,
            raw.RawClockOut,
            NormalizeLocal(effectiveClockIn),
            NormalizeLocal(effectiveClockOut),
            raw.RawPunchCount,
            raw.HasOnlyUnrecognizedPunches,
            workIntervals,
            raw.LeaveSegments,
            raw.AttendanceExceptionSourceId.HasValue &&
                raw.AttendanceExceptionType.HasValue &&
                raw.AttendanceExceptionReasonType.HasValue &&
                raw.AttendanceExceptionImpactType.HasValue
                ? new AttendanceApprovedException(
                    raw.AttendanceExceptionSourceId.Value,
                    raw.AttendanceExceptionType.Value,
                    raw.AttendanceExceptionReasonType.Value,
                    raw.AttendanceExceptionImpactType.Value,
                    raw.AttendanceExceptionFromTime,
                    raw.AttendanceExceptionToTime)
                : null);
    }

    private static AttendanceDailyCalculation CalculateCore(
        DateOnly workDate,
        AttendanceShiftSnapshot shift,
        AttendancePunchCandidate? rawClockIn,
        AttendancePunchCandidate? rawClockOut,
        DateTime? effectiveClockIn,
        DateTime? effectiveClockOut,
        int rawPunchCount,
        bool onlyUnrecognized,
        IReadOnlyList<TimeInterval> workIntervals,
        IReadOnlyList<AttendanceLeaveSegmentCalculation> leaveSegments,
        AttendanceApprovedException? approvedException)
    {
        var leaveIntervals = leaveSegments
            .Select(item => new TimeInterval(item.StartLocal, item.EndLocal))
            .ToArray();
        var requiredIntervals = Subtract(workIntervals, leaveIntervals);
        var exceptionIntervals = BuildExceptionIntervals(
            workDate,
            shift,
            requiredIntervals,
            approvedException);
        var dutyIntervals = Subtract(requiredIntervals, exceptionIntervals);
        var attendanceIntervals = effectiveClockIn.HasValue &&
            effectiveClockOut.HasValue &&
            effectiveClockOut.Value > effectiveClockIn.Value
                ? new[]
                {
                    new TimeInterval(
                        effectiveClockIn.Value,
                        effectiveClockOut.Value)
                }
                : [];
        var recognizedIntervals = Intersect(
            requiredIntervals,
            attendanceIntervals);
        var missingIntervals = Subtract(
            dutyIntervals,
            attendanceIntervals);
        var workedDuringLeave = Intersect(
            leaveIntervals,
            attendanceIntervals);
        var requiredMinutes = Minutes(requiredIntervals);
        var approvedLeaveMinutes = Minutes(leaveIntervals);
        var missingIn = dutyIntervals.Count > 0 &&
            !effectiveClockIn.HasValue;
        var missingOut = dutyIntervals.Count > 0 &&
            !effectiveClockOut.HasValue;

        var isLate = false;
        var lateSeconds = 0;
        if (dutyIntervals.Count > 0 && effectiveClockIn.HasValue)
        {
            var requiredStart = dutyIntervals[0].Start;
            var scheduledStart = LocalDateTime(
                workDate,
                shift.ScheduledStartTime);
            var usesScheduledThreshold = requiredStart == scheduledStart;
            var lateThreshold = usesScheduledThreshold
                ? LocalDateTime(workDate, shift.LateThresholdTime)
                : requiredStart;
            isLate = usesScheduledThreshold
                ? effectiveClockIn.Value >= lateThreshold
                : effectiveClockIn.Value > lateThreshold;
            lateSeconds = isLate
                ? Math.Max(0, checked((int)(
                    effectiveClockIn.Value - requiredStart).TotalSeconds))
                : 0;
        }

        var isEarly = false;
        var earlySeconds = 0;
        if (dutyIntervals.Count > 0 && effectiveClockOut.HasValue)
        {
            var requiredEnd = dutyIntervals[^1].End;
            isEarly = effectiveClockOut.Value < requiredEnd;
            earlySeconds = isEarly
                ? Math.Max(0, checked((int)(
                    requiredEnd - effectiveClockOut.Value).TotalSeconds))
                : 0;
        }

        var leaveCoverage = approvedLeaveMinutes == 0
            ? LeaveCoverageStatus.None
            : requiredMinutes == 0
                ? LeaveCoverageStatus.Full
                : LeaveCoverageStatus.Partial;
        var exceptionMinutes = Minutes(exceptionIntervals);
        return new AttendanceDailyCalculation(
            rawClockIn,
            rawClockOut,
            exceptionMinutes > 0 && dutyIntervals.Count == 0
                ? AttendanceDailyStatus.AttendanceExempted
                : ResolveStatus(rawPunchCount, onlyUnrecognized && missingIn && missingOut,
                    missingIn, missingOut, isLate, isEarly),
            isLate,
            isEarly,
            missingIn,
            missingOut,
            lateSeconds,
            earlySeconds,
            approvedLeaveMinutes,
            requiredMinutes,
            Minutes(recognizedIntervals),
            Minutes(missingIntervals),
            Minutes(workedDuringLeave),
            leaveCoverage,
            leaveSegments,
            rawPunchCount,
            onlyUnrecognized,
            false,
            null,
            exceptionMinutes > 0,
            exceptionMinutes,
            exceptionMinutes > 0 ? approvedException?.AttendanceExceptionId : null,
            exceptionMinutes > 0 ? approvedException?.ExceptionType : null,
            exceptionMinutes > 0 ? approvedException?.ReasonType : null,
            exceptionMinutes > 0 ? approvedException?.ImpactType : null,
            exceptionMinutes > 0 ? approvedException?.ExemptFromTime : null,
            exceptionMinutes > 0 ? approvedException?.ExemptToTime : null);
    }

    private static IReadOnlyList<TimeInterval> BuildExceptionIntervals(
        DateOnly workDate,
        AttendanceShiftSnapshot shift,
        IReadOnlyList<TimeInterval> requiredIntervals,
        AttendanceApprovedException? approvedException)
    {
        if (approvedException is null || requiredIntervals.Count == 0) return [];
        IReadOnlyList<TimeInterval> requested = approvedException.ImpactType switch
        {
            AttendanceExceptionImpactType.FullDay => BuildWorkingIntervals(workDate, shift),
            AttendanceExceptionImpactType.LateArrival when approvedException.ExemptToTime.HasValue =>
                [new(LocalDateTime(workDate, shift.ScheduledStartTime), LocalDateTime(workDate, approvedException.ExemptToTime.Value))],
            AttendanceExceptionImpactType.EarlyDeparture when approvedException.ExemptFromTime.HasValue =>
                [new(LocalDateTime(workDate, approvedException.ExemptFromTime.Value), LocalDateTime(workDate, shift.ScheduledEndTime))],
            _ => []
        };
        return Intersect(requiredIntervals, requested);
    }

    private static IReadOnlyList<TimeInterval> BuildWorkingIntervals(
        DateOnly workDate,
        AttendanceShiftSnapshot shift) =>
    [
        new(
            LocalDateTime(workDate, shift.ScheduledStartTime),
            LocalDateTime(workDate, shift.LunchBreakStartTime)),
        new(
            LocalDateTime(workDate, shift.LunchBreakEndTime),
            LocalDateTime(workDate, shift.ScheduledEndTime))
    ];

    private static IReadOnlyList<AttendanceLeaveSegmentCalculation>
        NormalizeLeaveSegments(
            IReadOnlyList<TimeInterval> workIntervals,
            IEnumerable<AttendanceApprovedLeaveInterval> approvedLeaves)
    {
        var clipped = approvedLeaves
            .SelectMany(source => workIntervals.Select(work => new ClippedLeave(
                source,
                Later(NormalizeLocal(source.StartLocal), work.Start),
                Earlier(NormalizeLocal(source.EndLocal), work.End))))
            .Where(item => item.End > item.Start)
            .ToArray();
        if (clipped.Length == 0)
        {
            return [];
        }

        var boundaries = clipped
            .SelectMany(item => new[] { item.Start, item.End })
            .Distinct()
            .OrderBy(item => item)
            .ToArray();
        var normalized = new List<AttendanceLeaveSegmentCalculation>();
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var start = boundaries[index];
            var end = boundaries[index + 1];
            var owner = clipped
                .Where(item => item.Start < end && item.End > start)
                .OrderBy(item => item.Source.StartLocal)
                .ThenBy(item => item.Source.LeaveRequestId)
                .FirstOrDefault();
            if (owner is null)
            {
                continue;
            }

            if (normalized.LastOrDefault() is { } previous &&
                previous.LeaveRequestId == owner.Source.LeaveRequestId &&
                previous.LeaveTypeId == owner.Source.LeaveTypeId &&
                previous.EndLocal == start)
            {
                normalized[^1] = previous with
                {
                    EndLocal = end,
                    CoveredMinutes = Minutes([new(previous.StartLocal, end)])
                };
                continue;
            }

            normalized.Add(new AttendanceLeaveSegmentCalculation(
                owner.Source.LeaveRequestId,
                owner.Source.LeaveTypeId,
                owner.Source.LeaveTypeCode,
                owner.Source.LeaveTypeName,
                start,
                end,
                Minutes([new(start, end)])));
        }

        return normalized;
    }

    private static IReadOnlyList<TimeInterval> Subtract(
        IEnumerable<TimeInterval> minuend,
        IEnumerable<TimeInterval> subtrahend)
    {
        var exclusions = subtrahend.OrderBy(item => item.Start).ToArray();
        var result = new List<TimeInterval>();
        foreach (var source in minuend.OrderBy(item => item.Start))
        {
            var cursor = source.Start;
            foreach (var exclusion in exclusions.Where(item =>
                         item.End > source.Start && item.Start < source.End))
            {
                if (exclusion.Start > cursor)
                {
                    result.Add(new TimeInterval(
                        cursor,
                        Earlier(exclusion.Start, source.End)));
                }

                cursor = Later(cursor, exclusion.End);
                if (cursor >= source.End)
                {
                    break;
                }
            }

            if (cursor < source.End)
            {
                result.Add(new TimeInterval(cursor, source.End));
            }
        }

        return result.Where(item => item.End > item.Start).ToArray();
    }

    private static IReadOnlyList<TimeInterval> Intersect(
        IEnumerable<TimeInterval> left,
        IEnumerable<TimeInterval> right) =>
        left.SelectMany(first => right.Select(second => new TimeInterval(
                Later(first.Start, second.Start),
                Earlier(first.End, second.End))))
            .Where(item => item.End > item.Start)
            .ToArray();

    private static int Minutes(IEnumerable<TimeInterval> intervals) =>
        checked((int)Math.Round(
            intervals.Sum(item => (item.End - item.Start).TotalMinutes),
            0,
            MidpointRounding.AwayFromZero));

    private static AttendanceDailyCalculation Empty(
        AttendanceDailyStatus status) =>
        new(
            null,
            null,
            status,
            false,
            false,
            false,
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            LeaveCoverageStatus.None,
            [],
            0,
            false);

    private static AttendanceDailyCalculation EmploymentSuspended(
        IReadOnlyList<AttendancePunchCandidate> punches,
        Guid sourceId)
    {
        var clockIn = punches.FirstOrDefault();
        var clockOut = punches.Count > 1 ? punches[^1] : null;
        return new AttendanceDailyCalculation(
            clockIn,
            clockOut,
            AttendanceDailyStatus.EmploymentSuspended,
            false,
            false,
            false,
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            LeaveCoverageStatus.None,
            [],
            punches.Count,
            false,
            true,
            sourceId);
    }

    private static AttendanceDailyStatus ResolveStatus(
        int punchCount,
        bool onlyUnrecognized,
        bool missingIn,
        bool missingOut,
        bool isLate,
        bool isEarly)
    {
        if (onlyUnrecognized)
        {
            return AttendanceDailyStatus.AmbiguousPunch;
        }

        if (missingIn && missingOut)
        {
            return punchCount == 0
                ? AttendanceDailyStatus.NoPunch
                : AttendanceDailyStatus.AmbiguousPunch;
        }

        if (missingIn)
        {
            return AttendanceDailyStatus.MissingClockIn;
        }

        if (missingOut)
        {
            return AttendanceDailyStatus.MissingClockOut;
        }

        if (isLate && isEarly)
        {
            return AttendanceDailyStatus.LateAndEarlyLeave;
        }

        if (isLate)
        {
            return AttendanceDailyStatus.Late;
        }

        return isEarly
            ? AttendanceDailyStatus.EarlyLeave
            : AttendanceDailyStatus.Normal;
    }

    private static DateTime LocalDateTime(DateOnly date, TimeOnly time) =>
        DateTime.SpecifyKind(
            date.ToDateTime(time),
            DateTimeKind.Unspecified);

    private static DateTime NormalizeLocal(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static DateTime? NormalizeLocal(DateTime? value) =>
        value.HasValue ? NormalizeLocal(value.Value) : null;

    private static DateTime Earlier(DateTime left, DateTime right) =>
        left <= right ? left : right;

    private static DateTime Later(DateTime left, DateTime right) =>
        left >= right ? left : right;

    private sealed record TimeInterval(DateTime Start, DateTime End);

    private sealed record ClippedLeave(
        AttendanceApprovedLeaveInterval Source,
        DateTime Start,
        DateTime End);
}
