namespace HRSystem.Domain.Overtime;

public enum OvertimeRecognitionStatus : byte
{
    Pending = 1,
    Confirmed = 2,
    NeedsReview = 3,
    Reopened = 4
}

public enum OvertimeRecognitionHistoryAction : byte
{
    Created = 1,
    Confirmed = 2,
    Adjusted = 3,
    Reopened = 4
}

public enum OvertimeRecognitionReason : byte
{
    ActualAttendanceConfirmed = 1,
    WorkedBeyondApprovedPeriod = 2,
    LeftBeforeApprovedEnd = 3,
    MissingPunchVerified = 4,
    NoActualOvertime = 5,
    NonWorkActivityExcluded = 6,
    AttendanceAdjustmentReference = 7,
    Other = 99
}
