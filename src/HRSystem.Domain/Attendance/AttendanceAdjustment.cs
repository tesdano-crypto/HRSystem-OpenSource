using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Attendance;

public sealed class AttendanceAdjustment
{
    private AttendanceAdjustment()
    {
    }

    public AttendanceAdjustment(
        Guid id,
        Guid dailyAttendanceResultId,
        Guid employeeId,
        DateOnly workDate,
        int revisionNumber,
        AttendanceAdjustmentAction action,
        DateTime? previousClockInLocalTime,
        DateTime? previousClockOutLocalTime,
        DateTime? newClockInLocalTime,
        DateTime? newClockOutLocalTime,
        AttendanceAdjustmentReason reason,
        string note,
        string adjustedByUserId,
        DateTimeOffset adjustedAtUtc,
        Guid? supersedesAdjustmentId)
    {
        if (dailyAttendanceResultId == Guid.Empty || employeeId == Guid.Empty)
        {
            throw new DomainValidationException("Attendance result and employee are required.");
        }

        if (revisionNumber < 1)
        {
            throw new DomainValidationException("Revision number must be positive.");
        }

        if (action is not (AttendanceAdjustmentAction.Created or
            AttendanceAdjustmentAction.Revised or
            AttendanceAdjustmentAction.Reverted))
        {
            throw new DomainValidationException("Adjustment action is invalid.");
        }

        if (!Enum.IsDefined(reason))
        {
            throw new DomainValidationException("Adjustment reason is required.");
        }

        if (action != AttendanceAdjustmentAction.Created &&
            !supersedesAdjustmentId.HasValue)
        {
            throw new DomainValidationException("A revised or reverted adjustment must reference its previous revision.");
        }

        if (previousClockInLocalTime == newClockInLocalTime &&
            previousClockOutLocalTime == newClockOutLocalTime)
        {
            throw new DomainValidationException("The adjustment must change at least one recognized punch time.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        DailyAttendanceResultId = dailyAttendanceResultId;
        EmployeeId = employeeId;
        WorkDate = workDate;
        RevisionNumber = revisionNumber;
        Action = action;
        PreviousClockInLocalTime = NormalizeLocal(previousClockInLocalTime);
        PreviousClockOutLocalTime = NormalizeLocal(previousClockOutLocalTime);
        NewClockInLocalTime = NormalizeLocal(newClockInLocalTime);
        NewClockOutLocalTime = NormalizeLocal(newClockOutLocalTime);
        Reason = reason;
        Note = AttendanceRules.Required(note, "Adjustment note", 500);
        AdjustedByUserId = AttendanceRules.Required(
            adjustedByUserId, "Adjusted by user", 450);
        AdjustedAtUtc = adjustedAtUtc.ToUniversalTime();
        SupersedesAdjustmentId = supersedesAdjustmentId;
    }

    public Guid Id { get; private set; }
    public Guid DailyAttendanceResultId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public int RevisionNumber { get; private set; }
    public AttendanceAdjustmentAction Action { get; private set; }
    public DateTime? PreviousClockInLocalTime { get; private set; }
    public DateTime? PreviousClockOutLocalTime { get; private set; }
    public DateTime? NewClockInLocalTime { get; private set; }
    public DateTime? NewClockOutLocalTime { get; private set; }
    public AttendanceAdjustmentReason Reason { get; private set; }
    public string Note { get; private set; } = string.Empty;
    public string AdjustedByUserId { get; private set; } = string.Empty;
    public DateTimeOffset AdjustedAtUtc { get; private set; }
    public Guid? SupersedesAdjustmentId { get; private set; }
    public DailyAttendanceResult DailyAttendanceResult { get; private set; } = null!;
    public Employee Employee { get; private set; } = null!;
    public AttendanceAdjustment? SupersedesAdjustment { get; private set; }

    private static DateTime? NormalizeLocal(DateTime? value) =>
        value.HasValue
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified)
            : null;
}
