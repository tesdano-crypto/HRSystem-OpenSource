using System.ComponentModel.DataAnnotations;
using HRSystem.Domain.Payroll;

namespace HRSystem.Application.Payroll;

public sealed record PayrollComponentDto(Guid Id, string Code, string Name,
    PayrollComponentCategory Category, PayrollCalculationKind CalculationKind,
    bool IsRecurring, bool IsActive, int SortOrder, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, bool IncludeInOvertimeHourlyBase = false);

public sealed record PayrollTierDto(int MinMonthsInclusive,
    int? MaxMonthsExclusive, decimal Amount);

public sealed record PayrollPlanComponentDto(Guid Id, Guid ComponentId,
    string Code, string Name, decimal? DefaultAmount, PayrollRuleKind RuleKind,
    PayrollProrationKind ProrationKind,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    IReadOnlyList<PayrollTierDto> Tiers);

public sealed record PayrollPlanDto(Guid Id, string Code, string Name,
    string? Description, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    bool IsActive, IReadOnlyList<PayrollPlanComponentDto> Components);

public sealed record PayrollSettingsDto(IReadOnlyList<PayrollComponentDto> Components,
    IReadOnlyList<PayrollPlanDto> Plans);

public sealed record EmployeePayrollSummaryDto(Guid EmployeeId,
    string EmployeeNumber, string EmployeeName, Guid DepartmentId,
    string DepartmentName, Guid? CurrentPlanId, string? CurrentPlanCode,
    string? CurrentPlanName, decimal? CurrentBaseSalary, bool NeedsSetup);

public sealed record EmployeePayrollAssignmentDto(Guid Id, Guid PayrollPlanId,
    string PayrollPlanCode, string PayrollPlanName, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, bool IsActive);

public sealed record EmployeePayrollOverrideDto(Guid Id, Guid ComponentId,
    string ComponentCode, string ComponentName, PayrollOverrideMode Mode,
    decimal? Amount, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    string? ReasonCode);

public sealed record EmployeePayrollPayCycleDto(Guid Id,
    PayrollPayCycleType Type, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    DateOnly? AnchorPayMonth, decimal? FixedPaymentAmount, string? Reason,
    bool IsActive, decimal? MonthlyFixedAmount = null,
    int? CycleMonths = null,
    PayrollPeriodicPaymentTiming? PaymentTiming = null);

public sealed record EmployeeLaborInsuranceEnrollmentDto(Guid Id,
    LaborInsuranceEnrollmentStatus Status,
    decimal? MonthlyLaborInsuredSalary,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive)
{
    public decimal? MonthlyInsuredSalary => MonthlyLaborInsuredSalary;
}
public sealed record EmployeeOccupationalInsuranceEnrollmentDto(Guid Id,
    OccupationalInsuranceEnrollmentStatus Status,
    decimal? MonthlyInsuredSalary,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive);
public sealed record EmployeeHealthInsuranceEnrollmentDto(Guid Id,
    HealthInsuranceEnrollmentStatus Status, decimal? MonthlyInsuredAmount,
    int? DependentCount, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive);

public sealed record EmployeePayrollDetailDto(EmployeePayrollSummaryDto Employee,
    IReadOnlyList<EmployeePayrollAssignmentDto> Assignments,
    IReadOnlyList<EmployeePayrollOverrideDto> Overrides,
    IReadOnlyList<EmployeeLaborInsuranceEnrollmentDto> LaborInsuranceEnrollments,
    bool HasApplicableLaborInsurancePolicy,
    IReadOnlyList<EmployeeHealthInsuranceEnrollmentDto> HealthInsuranceEnrollments,
    bool HasApplicableHealthInsurancePolicy,
    IReadOnlyList<EmployeePayrollPayCycleDto>? PayCycles = null,
    IReadOnlyList<EmployeeOccupationalInsuranceEnrollmentDto>? OccupationalInsuranceEnrollments = null,
    bool HasApplicableOccupationalInsurancePolicy = false);

public sealed record PayrollPeriodDto(Guid Id, int Year, int Month,
    DateOnly PeriodStart, DateOnly PeriodEnd, PayrollPeriodStatus Status,
    Guid? RunId, int SnapshotEmployeeCount, int MissingSetupCount,
    int LatestRevision = 0, decimal GrossPay = 0, decimal TotalDeductions = 0,
    decimal NetPay = 0, int BlockingCount = 0);

public sealed record PayrollSnapshotComponentDto(string Code, string Name,
    PayrollComponentCategory Category, PayrollSnapshotSourceType SourceType,
    decimal? StandardAmount, decimal? OverrideAmount, decimal? ResolvedAmount,
    PayrollCalculationStatus CalculationStatus,
    PayrollProrationKind ProrationKind, decimal? FullMonthlyAmount,
    int? PayableDays, decimal? ProrationFactor, decimal? RawProratedAmount,
    PayrollAttendanceAllowanceDto? AttendanceAllowance = null,
    PayrollLeaveDeductionDto? LeaveDeduction = null,
    PayrollLaborInsuranceDto? LaborInsurance = null,
    PayrollHealthInsuranceDto? HealthInsurance = null,
    PayrollPeriodicAccrualDto? PeriodicAccrual = null);

public sealed record PayrollPeriodicAccrualMonthDto(DateOnly CoveredMonth,
    decimal? MonthlyFixedAmount,
    PayrollCalculationStatus CalculationStatus);

public sealed record PayrollPeriodicAccrualDto(DateOnly CoveredFrom,
    DateOnly CoveredTo, int CycleMonths,
    PayrollPeriodicPaymentTiming PaymentTiming, decimal? TotalAmount,
    PayrollCalculationStatus CalculationStatus,
    IReadOnlyList<PayrollPeriodicAccrualMonthDto> Months);

public sealed record PayrollHealthInsuranceDto(
    HealthInsuranceEnrollmentStatus? EnrollmentStatus,
    decimal? MonthlyInsuredAmount, int? ActualDependentCount,
    DateOnly? EnrollmentFrom, DateOnly? EnrollmentTo,
    string? PolicyVersion, DateOnly? PolicyFrom, DateOnly? PolicyTo,
    decimal? GeneralPremiumRate, decimal? EmployeeShareRate,
    int? DependentCap, int? ChargeableDependentCount, int? ContributionUnits,
    decimal? RawEmployeeAmount, decimal? FinalEmployeeDeduction,
    PayrollCalculationStatus CalculationStatus);

public sealed record PayrollLaborInsuranceContributionDto(
    LaborInsuranceContributionKind Kind, decimal Rate,
    decimal EmployeeShareRate, decimal RawEmployeeAmount,
    decimal RoundedDisplayAmount);

public sealed record PayrollLaborInsuranceDto(
    LaborInsuranceEnrollmentStatus? EnrollmentStatus,
    decimal? MonthlyLaborInsuredSalary,
    decimal? MonthlyOccupationalInsuredSalary,
    DateOnly? EnrollmentFrom,
    DateOnly? EnrollmentTo, string? PolicyVersion,
    DateOnly? PolicyFrom, DateOnly? PolicyTo,
    LaborInsuranceCoverage Coverage,
    decimal? OrdinaryAccidentInsuranceRate,
    decimal? EmploymentInsuranceRate, decimal? EmployeeShareRate,
    decimal? FinalEmployeeDeduction,
    PayrollCalculationStatus CalculationStatus,
    IReadOnlyList<PayrollLaborInsuranceContributionDto> Contributions)
{
    public decimal? MonthlyInsuredSalary => MonthlyLaborInsuredSalary;

    public PayrollLaborInsuranceDto(
        LaborInsuranceEnrollmentStatus? enrollmentStatus,
        decimal? monthlyInsuredSalary, DateOnly? enrollmentFrom,
        DateOnly? enrollmentTo, string? policyVersion,
        DateOnly? policyFrom, DateOnly? policyTo,
        LaborInsuranceCoverage coverage,
        decimal? ordinaryAccidentInsuranceRate,
        decimal? employmentInsuranceRate, decimal? employeeShareRate,
        decimal? finalEmployeeDeduction,
        PayrollCalculationStatus calculationStatus,
        IReadOnlyList<PayrollLaborInsuranceContributionDto> contributions)
        : this(enrollmentStatus, monthlyInsuredSalary, null, enrollmentFrom,
            enrollmentTo, policyVersion, policyFrom, policyTo, coverage,
            ordinaryAccidentInsuranceRate, employmentInsuranceRate,
            employeeShareRate, finalEmployeeDeduction, calculationStatus,
            contributions)
    {
    }
}

public sealed record PayrollAttendanceAllowanceEvidenceDto(DateOnly WorkDate,
    AttendanceAllowanceIneligibilityReason Reasons);

public sealed record PayrollAttendanceAllowanceDto(decimal FullMonthlyAmount,
    decimal EmploymentProratedMaximum, int EmploymentPayableDays,
    int EligibleDays, int IneligibleDays, decimal RawCalculatedAmount,
    IReadOnlyList<PayrollAttendanceAllowanceEvidenceDto> Evidence);

public sealed record PayrollLeaveDeductionEvidenceDto(DateOnly WorkDate,
    string LeaveTypeCode, int LeaveMinutes, int ScheduledMinutes,
    decimal? DeductionRate, decimal? RawAmount,
    PayrollCalculationStatus CalculationStatus);

public sealed record PayrollLeaveDeductionDto(decimal FullMonthlyBaseAmount,
    int PersonalLeaveMinutes, int SickLeaveMinutes, int UnsupportedLeaveMinutes,
    decimal PersonalLeaveRawAmount, decimal SickLeaveRawAmount,
    decimal TotalRawAmount,
    IReadOnlyList<PayrollLeaveDeductionEvidenceDto> Evidence);

public sealed record PayrollOvertimeBaseComponentDto(string Code,
    decimal? FullMonthlyAmount);
public sealed record PayrollOvertimeBucketDto(OvertimePayBucket Bucket,
    int Minutes, decimal Multiplier, decimal RawPay, decimal FinalPay,
    string RatePolicyVersion);
public sealed record PayrollOvertimeDayDto(DateOnly WorkDate,
    int RecognizedMinutes, int FirstTwoHoursMinutes, int AfterTwoHoursMinutes,
    int AfterEightHoursMinutes);
public sealed record PayrollOvertimePayDto(decimal? MonthlyOvertimeBase,
    decimal? HourlyBase, int TotalRecognizedMinutes, decimal? TotalOvertimePay,
    PayrollCalculationStatus CalculationStatus,
    IReadOnlyList<PayrollOvertimeBaseComponentDto> IncludedComponents,
    IReadOnlyList<PayrollOvertimeBucketDto> Buckets,
    IReadOnlyList<PayrollOvertimeDayDto> Days);

public sealed record PayrollTotalBlockingDto(Guid? ComponentDefinitionId,
    string ComponentCode, PayrollCalculationStatus ComponentStatus,
    PayrollTotalBlockingReason Reason);

public sealed record PayrollEmployeeSnapshotDto(Guid Id, string EmployeeCode,
    string EmployeeName, string DepartmentName, string? PlanCode,
    PayrollEmployeeSetupStatus SetupStatus, decimal FixedEarningsSubtotal,
    IReadOnlyList<PayrollSnapshotComponentDto> Components,
    PayrollOvertimePayDto? OvertimePay = null,
    decimal GrossPay = 0, decimal TotalDeductions = 0, decimal? NetPay = null,
    PayrollCalculationStatus TotalCalculationStatus = PayrollCalculationStatus.NotCalculated,
    bool IsTotalSourceCurrent = true,
    IReadOnlyList<PayrollTotalBlockingDto>? TotalBlockingEvidence = null);

public sealed record PayrollFixedEarningsPreviewDto(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    int Year,
    int Month,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly EmploymentStart,
    DateOnly? EmploymentEnd,
    string PayrollPlanCode,
    decimal FixedEarningsSubtotal,
    IReadOnlyList<PayrollSnapshotComponentDto> Components,
    PayrollOvertimePayDto? OvertimePay = null,
    PayrollPayCycleType PayCycleType = PayrollPayCycleType.Monthly,
    PayrollParticipationStatus ParticipationStatus =
        PayrollParticipationStatus.Eligible,
    string? ParticipationMessage = null,
    decimal? PeriodicFixedAmount = null,
    PayrollOccupationalInsuranceDto? OccupationalInsurance = null);

// Read-only readiness: this phase does not calculate an occupational premium
// or add an employee deduction component to Payroll.
public sealed record PayrollOccupationalInsuranceDto(
    OccupationalInsuranceEnrollmentStatus? EnrollmentStatus,
    Guid? EnrollmentId, decimal? MonthlyInsuredSalary,
    DateOnly? EnrollmentFrom, DateOnly? EnrollmentTo,
    int CoveredDays, decimal CoverageFactor,
    PayrollCalculationStatus CalculationStatus,
    short SourceFingerprintVersion, string SourceFingerprint);

public sealed record PayrollRunDto(Guid Id, Guid PeriodId, PayrollRunStatus Status,
    DateTimeOffset CreatedAtUtc, string CreatedBy,
    IReadOnlyList<PayrollEmployeeSnapshotDto> Employees,
    int RevisionNumber = 1,
    PayrollRunTrigger Trigger = PayrollRunTrigger.InitialBatch);

public enum PayrollBatchItemOutcome
{
    Created = 1,
    AlreadyCurrent = 2,
    NeedsSetup = 3,
    PolicyPending = 4,
    NeedsReview = 5,
    Failed = 6,
    Excluded = 7
}

public sealed record PayrollBatchItemResultDto(Guid EmployeeId,
    string EmployeeCode, PayrollBatchItemOutcome Outcome, string Message,
    Guid? SnapshotId = null);

public sealed record PayrollBatchResultDto(Guid PayrollPeriodId, Guid? RunId,
    int? RevisionNumber, IReadOnlyList<PayrollBatchItemResultDto> Items)
{
    public int Created => Items.Count(x => x.Outcome == PayrollBatchItemOutcome.Created);
    public int AlreadyCurrent => Items.Count(x => x.Outcome == PayrollBatchItemOutcome.AlreadyCurrent);
    public int NeedsSetup => Items.Count(x => x.Outcome == PayrollBatchItemOutcome.NeedsSetup);
    public int PolicyPending => Items.Count(x => x.Outcome == PayrollBatchItemOutcome.PolicyPending);
    public int NeedsReview => Items.Count(x => x.Outcome == PayrollBatchItemOutcome.NeedsReview);
    public int Failed => Items.Count(x => x.Outcome == PayrollBatchItemOutcome.Failed);
}

public sealed record PayrollCurrentEmployeeSnapshotDto(Guid EmployeeId, Guid RunId,
    int RevisionNumber, DateTimeOffset UpdatedAtUtc, bool IsSourceChanged,
    PayrollEmployeeSnapshotDto Snapshot);

public sealed record PayrollAdjustmentDto(Guid Id, Guid EmployeeId,
    string EmployeeCode, string EmployeeName, string ComponentCode,
    string ComponentName, decimal Amount, PayrollAdjustmentDirection Direction,
    string Reason, string RowVersion);

public sealed record PayrollMonthDto(Guid PeriodId, int Year, int Month,
    DateOnly PeriodStart, DateOnly PeriodEnd, PayrollPeriodStatus Status,
    int EligibleEmployeeCount, int CurrentEmployeeCount, int ResolvedCount,
    int BlockingCount, decimal GrossPay, decimal TotalDeductions,
    decimal NetPay, short FingerprintVersion, string Fingerprint,
    int LatestRevision, IReadOnlyList<PayrollCurrentEmployeeSnapshotDto> Employees,
    IReadOnlyList<PayrollAdjustmentDto>? Adjustments = null);

public sealed class CreatePayrollAssignmentRequest
{
    public Guid EmployeeId { get; set; }
    public Guid PayrollPlanId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

public sealed class CreatePayrollOverrideRequest
{
    public Guid EmployeeId { get; set; }
    public Guid ComponentDefinitionId { get; set; }
    public PayrollOverrideMode Mode { get; set; }
    public decimal? Amount { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    [StringLength(100)] public string? ReasonCode { get; set; }
}

public sealed class CreatePayrollPayCycleRequest
{
    public Guid EmployeeId { get; set; }
    public PayrollPayCycleType Type { get; set; } = PayrollPayCycleType.Monthly;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public DateOnly? AnchorPayMonth { get; set; }
    public decimal? FixedPaymentAmount { get; set; }
    public decimal? MonthlyFixedAmount { get; set; }
    public int? CycleMonths { get; set; }
    public PayrollPeriodicPaymentTiming? PaymentTiming { get; set; }
    [Required, StringLength(500)] public string Reason { get; set; } = string.Empty;
}

public sealed class CreatePayrollAdjustmentRequest
{
    public Guid PayrollPeriodId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid ComponentDefinitionId { get; set; }
    public decimal Amount { get; set; }
    public PayrollAdjustmentDirection Direction { get; set; }
    [Required, StringLength(500)] public string Reason { get; set; } = string.Empty;
}

public sealed class CreateEmployeeLaborInsuranceEnrollmentRequest
{
    public Guid EmployeeId { get; set; }
    public LaborInsuranceEnrollmentStatus Status { get; set; } =
        LaborInsuranceEnrollmentStatus.Enrolled;
    public decimal? MonthlyLaborInsuredSalary { get; set; }
    public decimal? MonthlyInsuredSalary
    {
        get => MonthlyLaborInsuredSalary;
        set => MonthlyLaborInsuredSalary = value;
    }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

public sealed class CreateEmployeeHealthInsuranceEnrollmentRequest
{
    public Guid EmployeeId { get; set; }
    public HealthInsuranceEnrollmentStatus Status { get; set; } =
        HealthInsuranceEnrollmentStatus.Enrolled;
    public decimal? MonthlyInsuredAmount { get; set; }
    public int? DependentCount { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

public interface IPayrollService
{
    Task<PayrollSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmployeePayrollSummaryDto>> GetEmployeesAsync(Guid? departmentId = null,
        string? keyword = null, CancellationToken cancellationToken = default);
    Task<EmployeePayrollDetailDto> GetEmployeeAsync(Guid employeeId,
        CancellationToken cancellationToken = default);
    Task<PayrollFixedEarningsPreviewDto> PreviewFixedEarningsAsync(
        Guid employeeId, int year, int month,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PayrollPeriodDto>> GetPeriodsAsync(CancellationToken cancellationToken = default);
    Task<PayrollRunDto> GetRunAsync(Guid runId, CancellationToken cancellationToken = default);
    Task<PayrollMonthDto> GetMonthAsync(Guid periodId,
        CancellationToken cancellationToken = default);
    Task<Guid> AssignPlanAsync(CreatePayrollAssignmentRequest request,
        CancellationToken cancellationToken = default);
    Task<Guid> CreateOverrideAsync(CreatePayrollOverrideRequest request,
        CancellationToken cancellationToken = default);
    Task<Guid> CreatePayCycleAsync(CreatePayrollPayCycleRequest request,
        CancellationToken cancellationToken = default);
    Task<Guid> CreateLaborInsuranceEnrollmentAsync(
        CreateEmployeeLaborInsuranceEnrollmentRequest request,
        CancellationToken cancellationToken = default);
    Task<Guid> CreateHealthInsuranceEnrollmentAsync(
        CreateEmployeeHealthInsuranceEnrollmentRequest request,
        CancellationToken cancellationToken = default);
    Task<Guid> CreatePeriodAsync(int year, int month, CancellationToken cancellationToken = default);
    Task<Guid> CreateAdjustmentAsync(CreatePayrollAdjustmentRequest request,
        CancellationToken cancellationToken = default);
    Task UpdateAdjustmentAsync(Guid adjustmentId, decimal amount,
        PayrollAdjustmentDirection direction, string reason, string rowVersion,
        CancellationToken cancellationToken = default);
    Task RemoveAdjustmentAsync(Guid adjustmentId, string rowVersion,
        CancellationToken cancellationToken = default);
    Task<Guid> CreateDraftAsync(Guid periodId, CancellationToken cancellationToken = default);
    Task<PayrollBatchResultDto> CreateInitialDraftAsync(Guid periodId,
        CancellationToken cancellationToken = default);
    Task<PayrollBatchResultDto> RecalculateEmployeeAsync(Guid periodId, Guid employeeId,
        CancellationToken cancellationToken = default);
    Task<PayrollBatchResultDto> RecalculateChangedAsync(Guid periodId,
        CancellationToken cancellationToken = default);
}
