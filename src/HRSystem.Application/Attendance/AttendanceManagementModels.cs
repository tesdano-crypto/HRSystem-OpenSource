using System.ComponentModel.DataAnnotations;

namespace HRSystem.Application.Attendance;

public sealed record AttendanceShiftDto(
    Guid Id,
    string Code,
    string Name,
    TimeOnly ScheduledStartTime,
    TimeOnly LateThresholdTime,
    TimeOnly LunchBreakStartTime,
    TimeOnly LunchBreakEndTime,
    TimeOnly ScheduledEndTime,
    int ExpectedWorkMinutes,
    bool IsLunchPunchRequired,
    bool IsOvernightShift,
    bool IsActive,
    string RowVersion);

public sealed class SaveAttendanceShiftRequest : IValidatableObject
{
    public Guid? Id { get; set; }

    [Required]
    [StringLength(30)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public TimeOnly ScheduledStartTime { get; set; } = new(8, 0);
    public TimeOnly LateThresholdTime { get; set; } = new(8, 1);
    public TimeOnly LunchBreakStartTime { get; set; } = new(12, 0);
    public TimeOnly LunchBreakEndTime { get; set; } = new(13, 30);
    public TimeOnly ScheduledEndTime { get; set; } = new(17, 30);

    [Range(1, 1440)]
    public int ExpectedWorkMinutes { get; set; } = 480;

    public bool IsLunchPunchRequired { get; set; }
    public bool IsOvernightShift { get; set; }
    public string RowVersion { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (LateThresholdTime < ScheduledStartTime)
        {
            yield return new ValidationResult(
                "Late threshold cannot be earlier than the scheduled start.",
                [nameof(LateThresholdTime)]);
        }

        if (LunchBreakStartTime >= LunchBreakEndTime)
        {
            yield return new ValidationResult(
                "Lunch break end must be after lunch break start.",
                [nameof(LunchBreakEndTime)]);
        }

        if (!IsOvernightShift &&
            (ScheduledStartTime >= LunchBreakStartTime ||
             LunchBreakEndTime >= ScheduledEndTime))
        {
            yield return new ValidationResult(
                "A non-overnight shift must have start, lunch, and end times in order.");
        }
    }
}

public sealed record EmployeeShiftAssignmentDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    Guid ShiftId,
    string ShiftCode,
    string ShiftName,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    string RowVersion);

public sealed class SaveEmployeeShiftAssignmentRequest : IValidatableObject
{
    public Guid? Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid ShiftId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string RowVersion { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EmployeeId == Guid.Empty)
        {
            yield return new ValidationResult("Employee is required.", [nameof(EmployeeId)]);
        }

        if (ShiftId == Guid.Empty)
        {
            yield return new ValidationResult("Shift is required.", [nameof(ShiftId)]);
        }

        if (EffectiveFrom == default)
        {
            yield return new ValidationResult(
                "Effective start date is required.",
                [nameof(EffectiveFrom)]);
        }

        if (EffectiveTo.HasValue && EffectiveTo.Value < EffectiveFrom)
        {
            yield return new ValidationResult(
                "Effective end date cannot be earlier than the start date.",
                [nameof(EffectiveTo)]);
        }
    }
}

public sealed record AttendanceEmployeeOptionDto(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName);

public sealed class DailyAttendanceQuery
{
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public Guid? EmployeeId { get; set; }
    public string? Keyword { get; set; }
    public Domain.Attendance.AttendanceDailyStatus? Status { get; set; }
    public bool ExceptionsOnly { get; set; }
    public bool IncludeInactiveEmployees { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed record DailyAttendanceResultDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    DateOnly WorkDate,
    bool IsRequiredWorkday,
    string CalendarClassification,
    string? ShiftCode,
    string? ShiftName,
    DateTime? RawClockInLocalTime,
    DateTime? RawClockOutLocalTime,
    DateTime? EffectiveClockInLocalTime,
    DateTime? EffectiveClockOutLocalTime,
    string Status,
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
    string LeaveCoverageStatus,
    bool IsEmploymentSuspended,
    Guid? EmploymentSuspensionSourceId,
    bool IsAttendanceExempted,
    int AttendanceExceptionMinutes,
    Guid? AttendanceExceptionSourceId,
    string? AttendanceExceptionType,
    string? AttendanceExceptionReasonType,
    string? AttendanceExceptionImpactType,
    TimeOnly? AttendanceExceptionFromTime,
    TimeOnly? AttendanceExceptionToTime,
    IReadOnlyList<DailyAttendanceLeaveSegmentDto> LeaveSegments,
    bool IsAdjusted,
    string? CurrentAdjustmentReason,
    string? RecognitionSource,
    DateTimeOffset CalculatedAtUtc,
    string RowVersion)
{
    // Read-only evidence is deliberately separate from persisted recognition fields.
    public NonWorkingDayPunchEvidence? PunchEvidence { get; init; }
    public IReadOnlyList<AttendanceOvertimeLink> OvertimeLinks { get; init; } = [];
    public IReadOnlyList<MyAttendanceReviewItemDto> PunchReviewItems { get; init; } = [];
    public bool HasNonWorkingPunch => PunchEvidence?.HasNonWorkingPunch == true;
    public bool NonWorkingPunchPending => HasNonWorkingPunch && PunchReviewItems.Any(x =>
        x.AnomalyType == Domain.Attendance.AttendanceReviewAnomalyType.NonWorkingDayPunch &&
        x.State != AttendanceReviewState.Resolved);
}

public sealed record DailyAttendanceLeaveSegmentDto(
    Guid Id,
    Guid LeaveRequestId,
    Guid LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc,
    int CoveredMinutes);

public sealed class AttendanceRecalculationRequest : IValidatableObject
{
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public Guid? EmployeeId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateFrom == default || DateTo == default)
        {
            yield return new ValidationResult("A date range is required.");
            yield break;
        }

        if (DateTo < DateFrom)
        {
            yield return new ValidationResult(
                "End date cannot be earlier than start date.",
                [nameof(DateTo)]);
        }

        if (DateTo.DayNumber - DateFrom.DayNumber + 1 > 31)
        {
            yield return new ValidationResult(
                "A recalculation request cannot exceed 31 calendar days.");
        }
    }
}

public sealed record AttendanceRecalculationResultDto(
    DateOnly DateFrom,
    DateOnly DateTo,
    int EmployeeCount,
    int ResultCount,
    DateTimeOffset CompletedAtUtc);

public sealed record AttendanceRecalculationPreview(
    DateOnly DateFrom,
    DateOnly DateTo,
    int EmployeeCount,
    int CalendarDayCount,
    long EstimatedEmployeeDays,
    bool IsBulk)
{
    public static AttendanceRecalculationPreview Create(
        DateOnly dateFrom,
        DateOnly dateTo,
        int employeeCount,
        bool isBulk)
    {
        if (dateFrom == default || dateTo == default)
        {
            throw new ValidationException("A date range is required.");
        }

        if (dateTo < dateFrom)
        {
            throw new ValidationException("End date cannot be earlier than start date.");
        }

        var calendarDayCount = checked((long)dateTo.DayNumber - dateFrom.DayNumber + 1L);
        if (calendarDayCount > 31)
        {
            throw new ValidationException("A recalculation request cannot exceed 31 calendar days.");
        }

        if (employeeCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(employeeCount),
                "Employee count cannot be negative.");
        }

        return new AttendanceRecalculationPreview(
            dateFrom,
            dateTo,
            employeeCount,
            checked((int)calendarDayCount),
            checked(calendarDayCount * employeeCount),
            isBulk);
    }
}

public sealed class AttendanceAdjustmentRequest : IValidatableObject
{
    public Guid DailyAttendanceResultId { get; set; }
    public bool AdjustClockIn { get; set; }
    public DateTime? RecognizedClockInLocalTime { get; set; }
    public bool AdjustClockOut { get; set; }
    public DateTime? RecognizedClockOutLocalTime { get; set; }
    public Domain.Attendance.AttendanceAdjustmentReason Reason { get; set; }

    [Required]
    [StringLength(500)]
    public string Note { get; set; } = string.Empty;

    public string RowVersion { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DailyAttendanceResultId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Daily attendance result is required.",
                [nameof(DailyAttendanceResultId)]);
        }

        if (!AdjustClockIn && !AdjustClockOut)
        {
            yield return new ValidationResult(
                "Select at least one recognized punch time to adjust.");
        }
    }
}

public sealed class RevertAttendanceAdjustmentRequest
{
    public Guid DailyAttendanceResultId { get; set; }

    [Required]
    [StringLength(500)]
    public string Note { get; set; } = string.Empty;

    public string RowVersion { get; set; } = string.Empty;
}

public sealed record AttendanceAdjustmentDto(
    Guid Id,
    int RevisionNumber,
    string Action,
    DateTime? PreviousClockInLocalTime,
    DateTime? PreviousClockOutLocalTime,
    DateTime? NewClockInLocalTime,
    DateTime? NewClockOutLocalTime,
    string Reason,
    string Note,
    string AdjustedByUserId,
    DateTimeOffset AdjustedAtUtc);
