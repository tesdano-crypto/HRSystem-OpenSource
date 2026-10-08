namespace HRSystem.Domain.Payroll;

public enum PayrollComponentCategory { Earning = 1, Deduction = 2, Informational = 3 }
public enum PayrollCalculationKind { FixedAmount = 1, RuleBased = 2, ManualAdjustment = 3, ExternalCalculated = 4 }
public enum PayrollRuleKind { None = 0, SeniorityTier = 1, AttendanceProrated = 2, ExternalPending = 3, ManualOnly = 4 }
public enum PayrollProrationKind { None = 0, Monthly30Day = 1, PendingPolicy = 2 }
public enum SeniorityEvaluationPolicy { PeriodEnd = 1 }
public enum PayrollOverrideMode { Replace = 1, Add = 2, Disable = 3 }
public enum PayrollAdjustmentDirection { Earning = 1, Deduction = 2 }
public enum PayrollPeriodStatus { Open = 1, DraftCreated = 2, Finalized = 3 }
public enum PayrollFinalizationStatus : byte { Completed = 1 }
public enum PayrollRunStatus { Draft = 1, Finalized = 2 }
public enum PayrollRunTrigger : byte
{
    InitialBatch = 1,
    EmployeeRecalculation = 2,
    ChangedSources = 3
}
public enum PayrollSnapshotSourceType { PayrollPlan = 1, EmployeeOverride = 2, ManualAdjustment = 3, RulePending = 4, ExternalPending = 5 }
public enum PayrollCalculationStatus
{
    Resolved = 1,
    Pending = 2,
    NeedsSetup = 3,
    Disabled = 4,
    PolicyPending = 5,
    NotCalculated = 6,
    NeedsReview = 7,
    SourceChanged = 8
}
public enum PayrollTotalComponentRequirement { Required = 1, Optional = 2, NotApplicable = 3 }
public enum PayrollTotalBlockingReason
{
    ComponentUnresolved = 1,
    MissingResolvedAmount = 2,
    InvalidSign = 3,
    NegativeNetPay = 4,
    SourceChanged = 5
}
public enum PayrollEmployeeSetupStatus { Ready = 1, NeedsSetup = 2 }
public enum PayrollPayCycleType : byte
{
    Monthly = 1,
    SemiannualFixed = 2,
    PeriodicAccruedFixed = 3
}
public enum PayrollPeriodicPaymentTiming : byte { CycleStart = 1 }
public enum PayrollParticipationStatus : byte
{
    Eligible = 1,
    NotEmployed = 2,
    InactiveWithoutTermination = 3,
    NonPayMonth = 4,
    AmbiguousConfiguration = 5
}
public enum PayrollLeaveDeductionBasis { BaseSalaryOnly = 1 }
public enum LaborInsuranceEnrollmentStatus : byte { Enrolled = 1, NotEnrolled = 2 }
public enum OccupationalInsuranceEnrollmentStatus : byte { Enrolled = 1, NotEnrolled = 2 }
[Flags]
public enum LaborInsuranceCoverage : byte
{
    None = 0,
    OrdinaryAccident = 1,
    Employment = 2
}
public enum LaborInsuranceContributionPeriodPolicy : byte
{
    FullPeriodOnly = 1,
    PartialPeriodPending = 2,
    ThirtyDayProrated = 3
}
public enum LaborInsuranceContributionKind : byte
{
    OrdinaryAccident = 1,
    Employment = 2
}
public enum HealthInsuranceEnrollmentStatus : byte { Enrolled = 1, NotEnrolled = 2 }
public enum HealthInsuranceContributionPeriodPolicy : byte
{
    FullPeriodOnly = 1,
    PartialPeriodPending = 2
}
public enum HealthInsuranceDependentBillingRule : byte
{
    EmployeeAndCappedDependents = 1
}
public enum OvertimePayBucket : byte
{
    FirstTwoHours = 1,
    AfterTwoHours = 2,
    AfterEightHours = 3
}

[Flags]
public enum AttendanceAllowanceIneligibilityReason
{
    None = 0,
    Late = 1,
    EarlyLeave = 2,
    MissingClockIn = 4,
    MissingClockOut = 8,
    ApprovedLeave = 16,
    AbsencePolicyPending = 32
}
