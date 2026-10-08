using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.ParentalLeave;
using HRSystem.Domain.AttendanceExceptions;

namespace HRSystem.Domain.Attendance;

public sealed class DailyAttendanceResult
{
    private DailyAttendanceResult()
    {
    }

    public DailyAttendanceResult(
        Guid id,
        Guid employeeId,
        DateOnly workDate,
        DateTimeOffset nowUtc)
    {
        if (employeeId == Guid.Empty)
        {
            throw new DomainValidationException("Employee is required.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        EmployeeId = employeeId;
        WorkDate = workDate;
        CreatedAtUtc = nowUtc.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public Guid? ShiftId { get; private set; }
    public bool IsRequiredWorkday { get; private set; }
    public AttendanceCalendarClassification CalendarClassification { get; private set; }
    public string? ShiftCodeSnapshot { get; private set; }
    public string? ShiftNameSnapshot { get; private set; }
    public TimeOnly? ScheduledStartTimeSnapshot { get; private set; }
    public TimeOnly? LateThresholdTimeSnapshot { get; private set; }
    public TimeOnly? LunchBreakStartTimeSnapshot { get; private set; }
    public TimeOnly? LunchBreakEndTimeSnapshot { get; private set; }
    public TimeOnly? ScheduledEndTimeSnapshot { get; private set; }
    public int? ExpectedWorkMinutesSnapshot { get; private set; }
    public bool? IsLunchPunchRequiredSnapshot { get; private set; }
    public bool? IsOvernightShiftSnapshot { get; private set; }
    public Guid? RawClockInEventId { get; private set; }
    public DateTime? RawClockInLocalTime { get; private set; }
    public Guid? RawClockOutEventId { get; private set; }
    public DateTime? RawClockOutLocalTime { get; private set; }
    public DateTime? EffectiveClockInLocalTime { get; private set; }
    public DateTime? EffectiveClockOutLocalTime { get; private set; }
    public AttendanceDailyStatus Status { get; private set; }
    public bool IsLate { get; private set; }
    public bool IsEarlyLeave { get; private set; }
    public bool MissingClockIn { get; private set; }
    public bool MissingClockOut { get; private set; }
    public int LateSeconds { get; private set; }
    public int EarlyLeaveSeconds { get; private set; }
    public int ApprovedLeaveMinutes { get; private set; }
    public int RequiredAttendanceMinutes { get; private set; }
    public int RecognizedWorkMinutes { get; private set; }
    public int MissingMinutes { get; private set; }
    public int WorkedDuringApprovedLeaveMinutes { get; private set; }
    public LeaveCoverageStatus LeaveCoverageStatus { get; private set; }
    public bool IsEmploymentSuspended { get; private set; }
    public Guid? EmploymentSuspensionSourceId { get; private set; }
    public bool IsAttendanceExempted { get; private set; }
    public int AttendanceExceptionMinutes { get; private set; }
    public Guid? AttendanceExceptionSourceId { get; private set; }
    public AttendanceExceptionType? AttendanceExceptionType { get; private set; }
    public NaturalDisasterReasonType? AttendanceExceptionReasonType { get; private set; }
    public AttendanceExceptionImpactType? AttendanceExceptionImpactType { get; private set; }
    public TimeOnly? AttendanceExceptionFromTime { get; private set; }
    public TimeOnly? AttendanceExceptionToTime { get; private set; }
    public bool IsAdjusted { get; private set; }
    public Guid? CurrentAdjustmentId { get; private set; }
    public DateTimeOffset CalculatedAtUtc { get; private set; }
    public string CalculationVersion { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Employee Employee { get; private set; } = null!;
    public AttendanceShift? Shift { get; private set; }
    public AttendanceAdjustment? CurrentAdjustment { get; private set; }
    public ParentalLeaveRequest? EmploymentSuspensionSource { get; private set; }
    public AttendanceException? AttendanceExceptionSource { get; private set; }
    public ICollection<AttendanceAdjustment> Adjustments { get; } =
        new List<AttendanceAdjustment>();
    public ICollection<DailyAttendanceLeaveSegment> LeaveSegments { get; } =
        new List<DailyAttendanceLeaveSegment>();

    public void Recalculate(
        bool isRequiredWorkday,
        AttendanceCalendarClassification calendarClassification,
        AttendanceShift? shift,
        AttendanceDailyCalculation calculation,
        string calculationVersion,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(calculation);
        ArgumentException.ThrowIfNullOrWhiteSpace(calculationVersion);

        IsRequiredWorkday = isRequiredWorkday;
        CalendarClassification = calendarClassification;
        ShiftId = shift?.Id;
        ShiftCodeSnapshot = shift?.Code;
        ShiftNameSnapshot = shift?.Name;
        ScheduledStartTimeSnapshot = shift?.ScheduledStartTime;
        LateThresholdTimeSnapshot = shift?.LateThresholdTime;
        LunchBreakStartTimeSnapshot = shift?.LunchBreakStartTime;
        LunchBreakEndTimeSnapshot = shift?.LunchBreakEndTime;
        ScheduledEndTimeSnapshot = shift?.ScheduledEndTime;
        ExpectedWorkMinutesSnapshot = shift?.ExpectedWorkMinutes;
        IsLunchPunchRequiredSnapshot = shift?.IsLunchPunchRequired;
        IsOvernightShiftSnapshot = shift?.IsOvernightShift;
        RawClockInEventId = calculation.RawClockIn?.EventId;
        RawClockInLocalTime = calculation.RawClockIn?.LocalTime;
        RawClockOutEventId = calculation.RawClockOut?.EventId;
        RawClockOutLocalTime = calculation.RawClockOut?.LocalTime;
        EffectiveClockInLocalTime = RawClockInLocalTime;
        EffectiveClockOutLocalTime = RawClockOutLocalTime;
        IsAdjusted = false;
        CurrentAdjustmentId = null;
        ApplyCalculation(calculation);
        CalculationVersion = calculationVersion.Trim();
        CalculatedAtUtc = nowUtc.ToUniversalTime();
        UpdatedAtUtc = CalculatedAtUtc;
    }

    public void ReplaceLeaveSegments(
        IEnumerable<DailyAttendanceLeaveSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var replacements = segments.ToArray();
        if (replacements.Any(item => item.DailyAttendanceResultId != Id))
        {
            throw new DomainValidationException(
                "Leave segments must belong to this attendance result.");
        }

        LeaveSegments.Clear();
        foreach (var segment in replacements)
        {
            LeaveSegments.Add(segment);
        }
    }

    public void ApplyAdjustment(
        Guid adjustmentId,
        DateTime? effectiveClockInLocalTime,
        DateTime? effectiveClockOutLocalTime,
        AttendanceDailyCalculation calculation,
        bool isAdjusted,
        DateTimeOffset nowUtc)
    {
        if (adjustmentId == Guid.Empty)
        {
            throw new DomainValidationException("Attendance adjustment is required.");
        }

        ArgumentNullException.ThrowIfNull(calculation);
        EffectiveClockInLocalTime = NormalizeLocal(effectiveClockInLocalTime);
        EffectiveClockOutLocalTime = NormalizeLocal(effectiveClockOutLocalTime);
        CurrentAdjustmentId = adjustmentId;
        IsAdjusted = isAdjusted;
        ApplyCalculation(calculation);
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    public AttendanceShiftSnapshot? GetShiftSnapshot()
    {
        if (!ShiftId.HasValue ||
            string.IsNullOrWhiteSpace(ShiftNameSnapshot) ||
            !ScheduledStartTimeSnapshot.HasValue ||
            !LateThresholdTimeSnapshot.HasValue ||
            !LunchBreakStartTimeSnapshot.HasValue ||
            !LunchBreakEndTimeSnapshot.HasValue ||
            !ScheduledEndTimeSnapshot.HasValue ||
            !ExpectedWorkMinutesSnapshot.HasValue ||
            !IsLunchPunchRequiredSnapshot.HasValue ||
            !IsOvernightShiftSnapshot.HasValue)
        {
            return null;
        }

        return new AttendanceShiftSnapshot(
            ShiftId.Value,
            ShiftNameSnapshot,
            ScheduledStartTimeSnapshot.Value,
            LateThresholdTimeSnapshot.Value,
            LunchBreakStartTimeSnapshot.Value,
            LunchBreakEndTimeSnapshot.Value,
            ScheduledEndTimeSnapshot.Value,
            ExpectedWorkMinutesSnapshot.Value,
            IsLunchPunchRequiredSnapshot.Value,
            IsOvernightShiftSnapshot.Value);
    }

    private void ApplyCalculation(AttendanceDailyCalculation calculation)
    {
        Status = calculation.Status;
        IsLate = calculation.IsLate;
        IsEarlyLeave = calculation.IsEarlyLeave;
        MissingClockIn = calculation.MissingClockIn;
        MissingClockOut = calculation.MissingClockOut;
        LateSeconds = calculation.LateSeconds;
        EarlyLeaveSeconds = calculation.EarlyLeaveSeconds;
        ApprovedLeaveMinutes = calculation.ApprovedLeaveMinutes;
        RequiredAttendanceMinutes = calculation.RequiredAttendanceMinutes;
        RecognizedWorkMinutes = calculation.RecognizedWorkMinutes;
        MissingMinutes = calculation.MissingMinutes;
        WorkedDuringApprovedLeaveMinutes =
            calculation.WorkedDuringApprovedLeaveMinutes;
        LeaveCoverageStatus = calculation.LeaveCoverageStatus;
        IsEmploymentSuspended = calculation.IsEmploymentSuspended;
        EmploymentSuspensionSourceId = calculation.EmploymentSuspensionSourceId;
        IsAttendanceExempted = calculation.IsAttendanceExempted;
        AttendanceExceptionMinutes = calculation.AttendanceExceptionMinutes;
        AttendanceExceptionSourceId = calculation.AttendanceExceptionSourceId;
        AttendanceExceptionType = calculation.AttendanceExceptionType;
        AttendanceExceptionReasonType = calculation.AttendanceExceptionReasonType;
        AttendanceExceptionImpactType = calculation.AttendanceExceptionImpactType;
        AttendanceExceptionFromTime = calculation.AttendanceExceptionFromTime;
        AttendanceExceptionToTime = calculation.AttendanceExceptionToTime;
    }

    private static DateTime? NormalizeLocal(DateTime? value) =>
        value.HasValue
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified)
            : null;
}
