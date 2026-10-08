namespace HRSystem.Domain.ParentalLeave;

public enum ParentalLeaveApplicationType : byte
{
    Standard = 1,
    ShortTerm30DaysOrMore = 2,
    DailyUnder30Days = 3
}

public enum ParentalLeaveStatus : byte
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
    Withdrawn = 5,
    CancellationRequested = 6,
    Cancelled = 7,
    Completed = 8
}

public enum ParentalLeaveNoticeType : byte
{
    Standard = 1,
    EmergencyCare = 2
}

public enum ParentalLeaveApprovalAction : byte
{
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Withdrawn = 4,
    CancellationRequested = 5,
    CancellationApproved = 6,
    CancellationRejected = 7,
    EarlyReturnRequested = 8,
    EarlyReturnApproved = 9
}
