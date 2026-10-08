using HRSystem.Domain.Common;

namespace HRSystem.Domain.Attendance;

public sealed class AttendanceShift
{
    private AttendanceShift()
    {
    }

    public AttendanceShift(
        Guid id,
        string code,
        string name,
        TimeOnly scheduledStartTime,
        TimeOnly lateThresholdTime,
        TimeOnly lunchBreakStartTime,
        TimeOnly lunchBreakEndTime,
        TimeOnly scheduledEndTime,
        int expectedWorkMinutes,
        bool isLunchPunchRequired,
        bool isOvernightShift,
        DateTimeOffset nowUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CreatedAtUtc = nowUtc.ToUniversalTime();
        IsActive = true;
        SetDetails(
            code,
            name,
            scheduledStartTime,
            lateThresholdTime,
            lunchBreakStartTime,
            lunchBreakEndTime,
            scheduledEndTime,
            expectedWorkMinutes,
            isLunchPunchRequired,
            isOvernightShift,
            nowUtc);
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public TimeOnly ScheduledStartTime { get; private set; }
    public TimeOnly LateThresholdTime { get; private set; }
    public TimeOnly LunchBreakStartTime { get; private set; }
    public TimeOnly LunchBreakEndTime { get; private set; }
    public TimeOnly ScheduledEndTime { get; private set; }
    public int ExpectedWorkMinutes { get; private set; }
    public bool IsLunchPunchRequired { get; private set; }
    public bool IsOvernightShift { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public ICollection<EmployeeShiftAssignment> Assignments { get; } =
        new List<EmployeeShiftAssignment>();
    public ICollection<DailyAttendanceResult> DailyAttendanceResults { get; } =
        new List<DailyAttendanceResult>();

    public void Update(
        string code,
        string name,
        TimeOnly scheduledStartTime,
        TimeOnly lateThresholdTime,
        TimeOnly lunchBreakStartTime,
        TimeOnly lunchBreakEndTime,
        TimeOnly scheduledEndTime,
        int expectedWorkMinutes,
        bool isLunchPunchRequired,
        bool isOvernightShift,
        DateTimeOffset nowUtc) =>
        SetDetails(
            code,
            name,
            scheduledStartTime,
            lateThresholdTime,
            lunchBreakStartTime,
            lunchBreakEndTime,
            scheduledEndTime,
            expectedWorkMinutes,
            isLunchPunchRequired,
            isOvernightShift,
            nowUtc);

    public void Activate(DateTimeOffset nowUtc)
    {
        IsActive = true;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        IsActive = false;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    private void SetDetails(
        string code,
        string name,
        TimeOnly scheduledStartTime,
        TimeOnly lateThresholdTime,
        TimeOnly lunchBreakStartTime,
        TimeOnly lunchBreakEndTime,
        TimeOnly scheduledEndTime,
        int expectedWorkMinutes,
        bool isLunchPunchRequired,
        bool isOvernightShift,
        DateTimeOffset nowUtc)
    {
        var normalizedCode = AttendanceRules.Required(code, "班別代碼", 30)
            .ToUpperInvariant();
        var normalizedName = AttendanceRules.Required(name, "班別名稱", 100);
        if (lateThresholdTime < scheduledStartTime)
        {
            throw new DomainValidationException("遲到門檻不得早於排定上班時間。");
        }

        if (lunchBreakStartTime >= lunchBreakEndTime)
        {
            throw new DomainValidationException("午休開始時間必須早於午休結束時間。");
        }

        if (!isOvernightShift &&
            (scheduledStartTime >= lunchBreakStartTime ||
             lunchBreakEndTime >= scheduledEndTime))
        {
            throw new DomainValidationException("非跨日班別時間順序不正確。");
        }

        if (expectedWorkMinutes is < 1 or > 1440)
        {
            throw new DomainValidationException("預期工作分鐘必須介於 1 與 1440。");
        }

        Code = normalizedCode;
        Name = normalizedName;
        ScheduledStartTime = scheduledStartTime;
        LateThresholdTime = lateThresholdTime;
        LunchBreakStartTime = lunchBreakStartTime;
        LunchBreakEndTime = lunchBreakEndTime;
        ScheduledEndTime = scheduledEndTime;
        ExpectedWorkMinutes = expectedWorkMinutes;
        IsLunchPunchRequired = isLunchPunchRequired;
        IsOvernightShift = isOvernightShift;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }
}
