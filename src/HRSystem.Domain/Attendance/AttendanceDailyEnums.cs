namespace HRSystem.Domain.Attendance;

public enum AttendanceCalendarClassification : byte
{
    WorkingDay = 1,
    Weekend = 2,
    Holiday = 3,
    ExceptionalWorkingDay = 4,
    FallbackWorkingDay = 5,
    FallbackRestDay = 6
}

public enum AttendanceDailyStatus : byte
{
    Normal = 1,
    Late = 2,
    EarlyLeave = 3,
    LateAndEarlyLeave = 4,
    MissingClockIn = 5,
    MissingClockOut = 6,
    NoPunch = 7,
    AmbiguousPunch = 8,
    RestDay = 9,
    NoShift = 10,
    EmploymentSuspended = 11,
    AttendanceExempted = 12
}

public enum LeaveCoverageStatus : byte
{
    None = 0,
    Partial = 1,
    Full = 2
}

public enum AttendanceAdjustmentReason : byte
{
    OfficialBusiness = 1,
    DeviceFailure = 2,
    ForgotPunch = 3,
    ManagerApproved = 4,
    Other = 5
}

public enum AttendanceAdjustmentAction : byte
{
    Created = 1,
    Revised = 2,
    Reverted = 3
}
