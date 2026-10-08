namespace HRSystem.Domain.LeaveRequests;

public enum ApprovalAction : byte
{
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Withdrawn = 4,
    CancellationRequested = 5,
    CancellationApproved = 6,
    CancellationRejected = 7
}
