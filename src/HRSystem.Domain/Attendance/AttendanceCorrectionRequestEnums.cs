namespace HRSystem.Domain.Attendance;

public enum AttendanceCorrectionRequestType : byte
{
    MissingClockIn = 1,
    MissingClockOut = 2,
    MissingBoth = 3,
    ClockInCorrection = 4,
    ClockOutCorrection = 5,
    LateExplanation = 6,
    EarlyLeaveExplanation = 7
}

public enum AttendanceCorrectionRequestStatus : byte
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
    Withdrawn = 5
}

public enum AttendanceCorrectionReason : byte
{
    ForgotPunch = 1,
    DeviceFailure = 2,
    OfficialBusiness = 3,
    WorkRequirement = 4,
    TrafficIncident = 5,
    PersonalReason = 6,
    IncorrectRecognizedTime = 7,
    Other = 99
}

public enum AttendanceCorrectionRequestHistoryAction : byte
{
    Created = 1,
    Updated = 2,
    Submitted = 3,
    Approved = 4,
    Rejected = 5,
    Withdrawn = 6,
    AdjustmentApplied = 7
}
