namespace HRSystem.Domain.Overtime;

public enum OvertimeRequestStatus : byte
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
    Withdrawn = 5
}

public enum OvertimeRequestHistoryAction : byte
{
    Created = 1,
    Updated = 2,
    Submitted = 3,
    Approved = 4,
    Rejected = 5,
    Withdrawn = 6
}

public enum OvertimeReviewReason : byte
{
    ApprovedAsRequested = 1,
    BusinessNeedNotConfirmed = 2,
    TimeRangeIncorrect = 3,
    DuplicateRequest = 4,
    InsufficientInformation = 5,
    Other = 99
}
