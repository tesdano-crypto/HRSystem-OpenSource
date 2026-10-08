namespace HRSystem.Domain.LeaveRequests;

public enum LeaveRequestStatus : byte
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
    Withdrawn = 5,
    CancellationRequested = 6,
    Cancelled = 7
}
