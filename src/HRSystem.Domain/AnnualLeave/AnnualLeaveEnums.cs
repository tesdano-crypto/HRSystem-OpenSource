namespace HRSystem.Domain.AnnualLeave;

public enum AnnualLeaveMilestone : byte
{
    HalfYear = 1,
    Anniversary = 2
}

public enum AnnualLeaveEntitlementStatus : byte
{
    Open = 1,
    ExpiredPendingSettlement = 2,
    CarriedForward = 3,
    PaidOut = 4,
    Closed = 5,
    ManualReview = 6
}

public enum AnnualLeaveAllocationStatus : byte
{
    Reserved = 1,
    Consumed = 2,
    Released = 3,
    Restored = 4
}

public enum AnnualLeaveCarryForwardStatus : byte
{
    Available = 1,
    Consumed = 2,
    Expired = 3,
    Cancelled = 4
}
