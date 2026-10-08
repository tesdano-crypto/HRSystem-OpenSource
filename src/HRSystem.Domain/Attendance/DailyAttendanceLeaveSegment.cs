using HRSystem.Domain.Common;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Attendance;

public sealed class DailyAttendanceLeaveSegment
{
    private DailyAttendanceLeaveSegment()
    {
    }

    public DailyAttendanceLeaveSegment(
        Guid id,
        Guid dailyAttendanceResultId,
        Guid leaveRequestId,
        Guid leaveTypeId,
        string leaveTypeCodeSnapshot,
        string leaveTypeNameSnapshot,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        int coveredMinutes,
        DateTimeOffset createdAtUtc)
    {
        if (dailyAttendanceResultId == Guid.Empty ||
            leaveRequestId == Guid.Empty ||
            leaveTypeId == Guid.Empty)
        {
            throw new DomainValidationException(
                "Attendance result, leave request, and leave type are required.");
        }

        var startUtc = startAtUtc.ToUniversalTime();
        var endUtc = endAtUtc.ToUniversalTime();
        if (endUtc <= startUtc)
        {
            throw new DomainValidationException(
                "Leave segment end must be after its start.");
        }

        if (coveredMinutes <= 0)
        {
            throw new DomainValidationException(
                "Leave segment covered minutes must be positive.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        DailyAttendanceResultId = dailyAttendanceResultId;
        LeaveRequestId = leaveRequestId;
        LeaveTypeId = leaveTypeId;
        LeaveTypeCodeSnapshot = AttendanceRules.Required(
            leaveTypeCodeSnapshot, "Leave type code", 20);
        LeaveTypeNameSnapshot = AttendanceRules.Required(
            leaveTypeNameSnapshot, "Leave type name", 100);
        StartAtUtc = startUtc;
        EndAtUtc = endUtc;
        CoveredMinutes = coveredMinutes;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid DailyAttendanceResultId { get; private set; }
    public Guid LeaveRequestId { get; private set; }
    public Guid LeaveTypeId { get; private set; }
    public string LeaveTypeCodeSnapshot { get; private set; } = string.Empty;
    public string LeaveTypeNameSnapshot { get; private set; } = string.Empty;
    public DateTimeOffset StartAtUtc { get; private set; }
    public DateTimeOffset EndAtUtc { get; private set; }
    public int CoveredMinutes { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DailyAttendanceResult DailyAttendanceResult { get; private set; } = null!;
    public LeaveRequest LeaveRequest { get; private set; } = null!;
    public LeaveType LeaveType { get; private set; } = null!;
}
