namespace HRSystem.Domain.AttendanceExceptions;

public enum AttendanceExceptionType : byte
{
    NaturalDisaster = 1
}

public enum NaturalDisasterReasonType : byte
{
    WorkplaceClosure = 1,
    ResidenceClosure = 2,
    CommuteRouteClosure = 3,
    TransportationBlocked = 4
}

public enum AttendanceExceptionImpactType : byte
{
    FullDay = 1,
    LateArrival = 2,
    EarlyDeparture = 3
}

public enum AttendanceExceptionStatus : byte
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
    Withdrawn = 5,
    CancellationRequested = 6,
    Cancelled = 7
}

public enum AttendanceExceptionHistoryAction : byte
{
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Withdrawn = 4,
    CancellationRequested = 5,
    CancellationApproved = 6,
    CancellationRejected = 7
}
