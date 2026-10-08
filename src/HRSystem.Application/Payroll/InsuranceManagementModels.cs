using System.ComponentModel.DataAnnotations;
using HRSystem.Domain.Payroll;

namespace HRSystem.Application.Payroll;

public enum InsuranceEnrollmentKind : byte
{
    Labor = 1,
    Health = 2,
    Occupational = 3
}

public enum InsuranceEnrollmentDisplayStatus : byte
{
    NotConfigured = 1,
    Enrolled = 2,
    Withdrawn = 3,
    UninsuredPeriod = 4,
    FutureEffective = 5
}

public sealed record InsuranceEmployeeListItemDto(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    string DepartmentName,
    bool IsActive,
    DateOnly HireDate,
    DateOnly? TerminationDate,
    InsuranceEnrollmentDisplayStatus LaborStatus,
    decimal? MonthlyLaborInsuredSalary,
    InsuranceEnrollmentDisplayStatus OccupationalStatus,
    decimal? MonthlyOccupationalInsuredSalary,
    InsuranceEnrollmentDisplayStatus HealthStatus,
    decimal? MonthlyHealthInsuredAmount,
    int? HealthDependentCount,
    bool NeedsSetup);

public sealed record InsuranceEmployeeDetailDto(
    InsuranceEmployeeListItemDto Employee,
    IReadOnlyList<EmployeeLaborInsuranceEnrollmentDto> LaborHistory,
    IReadOnlyList<EmployeeOccupationalInsuranceEnrollmentDto> OccupationalHistory,
    IReadOnlyList<EmployeeHealthInsuranceEnrollmentDto> HealthHistory,
    bool HasApplicableLaborPolicy,
    bool HasApplicableOccupationalPolicy,
    bool HasApplicableHealthPolicy);

public abstract class InsuranceChangeRequest
{
    public Guid EmployeeId { get; set; }
    public bool IsEnrolled { get; set; } = true;
    public DateOnly CoverageFrom { get; set; }
    public DateOnly? WithdrawalDate { get; set; }
    [Required, StringLength(500)]
    public string Reason { get; set; } = "會計維護投保資料";
}

public sealed class PreviewLaborInsuranceChangeRequest : InsuranceChangeRequest
{
    public decimal? MonthlyLaborInsuredSalary { get; set; }
}

public sealed class PreviewOccupationalInsuranceChangeRequest : InsuranceChangeRequest
{
    public decimal? MonthlyInsuredSalary { get; set; }
}

public sealed class PreviewHealthInsuranceChangeRequest : InsuranceChangeRequest
{
    public decimal? MonthlyHealthInsuredAmount { get; set; }
    public int? DependentCount { get; set; }
}

public sealed record InsuranceAffectedPeriodDto(
    Guid PayrollPeriodId,
    int Year,
    int Month,
    PayrollPeriodStatus Status,
    bool HasCurrentSnapshot,
    bool WillMarkSourceChanged,
    bool IsFinalizedProtected);

public sealed record InsuranceChangePreviewDto(
    InsuranceEnrollmentKind Kind,
    Guid EmployeeId,
    DateOnly CoverageFrom,
    DateOnly? WithdrawalDate,
    DateOnly? StoredEffectiveTo,
    Guid? SupersededEnrollmentId,
    bool HasCoverageGap,
    IReadOnlyList<string> Changes,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyList<InsuranceAffectedPeriodDto> AffectedPeriods,
    int CurrentSnapshotsToMarkSourceChanged,
    string PreviewToken)
{
    public bool CanApply => Errors.Count == 0;
}

public sealed record InsuranceApplyResultDto(
    Guid EnrollmentId,
    Guid? SupersededEnrollmentId,
    int CurrentSnapshotsMarkedSourceChanged);

public interface IInsuranceManagementService
{
    Task<IReadOnlyList<InsuranceEmployeeListItemDto>> SearchEmployeesAsync(
        string? keyword = null,
        bool includeInactive = false,
        bool needsSetupOnly = false,
        CancellationToken cancellationToken = default);

    Task<InsuranceEmployeeDetailDto> GetEmployeeAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task<InsuranceChangePreviewDto> PreviewLaborAsync(
        PreviewLaborInsuranceChangeRequest request,
        CancellationToken cancellationToken = default);

    Task<InsuranceChangePreviewDto> PreviewHealthAsync(
        PreviewHealthInsuranceChangeRequest request,
        CancellationToken cancellationToken = default);

    Task<InsuranceChangePreviewDto> PreviewOccupationalAsync(
        PreviewOccupationalInsuranceChangeRequest request,
        CancellationToken cancellationToken = default);

    Task<InsuranceApplyResultDto> ApplyLaborAsync(
        PreviewLaborInsuranceChangeRequest request,
        string previewToken,
        CancellationToken cancellationToken = default);

    Task<InsuranceApplyResultDto> ApplyHealthAsync(
        PreviewHealthInsuranceChangeRequest request,
        string previewToken,
        CancellationToken cancellationToken = default);

    Task<InsuranceApplyResultDto> ApplyOccupationalAsync(
        PreviewOccupationalInsuranceChangeRequest request,
        string previewToken,
        CancellationToken cancellationToken = default);

    Task DeactivateLaborEnrollmentAsync(Guid enrollmentId, string reason,
        CancellationToken cancellationToken = default);
}
