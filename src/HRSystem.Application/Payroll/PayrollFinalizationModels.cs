using HRSystem.Domain.Approvals;
using HRSystem.Domain.Payroll;

namespace HRSystem.Application.Payroll;

public sealed record PayrollFinalEmployeeDto(Guid Id, Guid EmployeeId,
    string EmployeeNumber, string EmployeeName, string DepartmentName,
    decimal GrossPay, decimal TotalDeductions, decimal NetPay);

public sealed record PayrollFinalizationDto(Guid Id, Guid PayrollPeriodId,
    int Year, int Month, int FinalVersionNumber, PayrollFinalizationStatus Status,
    DateTimeOffset FinalizedAtUtc, string FinalizedByDisplayName,
    Guid ApprovalId, DateTimeOffset ApprovedAtUtc, string ApprovedByDisplayName,
    ApprovalChannel ApprovalChannel, short FingerprintVersion, string Fingerprint,
    int EmployeeCount, decimal GrossPay, decimal TotalDeductions, decimal NetPay,
    IReadOnlyList<PayrollFinalEmployeeDto> Employees);

public sealed record PayrollPayslipLineDto(string Code, string Name,
    PayrollComponentCategory Category, decimal Amount);

public sealed record PayrollPayslipDto(Guid Id, Guid FinalizationId,
    string CompanyName, int Year, int Month, DateOnly PeriodStart, DateOnly PeriodEnd,
    string EmployeeNumber, string EmployeeName, string DepartmentName,
    DateTimeOffset FinalizedAtUtc, string FinalizedByDisplayName,
    decimal GrossPay, decimal TotalDeductions, decimal NetPay,
    IReadOnlyList<PayrollPayslipLineDto> Earnings,
    IReadOnlyList<PayrollPayslipLineDto> Deductions,
    short FingerprintVersion, string Fingerprint,
    string? PayrollPlanCode = null);

public interface IPayrollFinalizationService
{
    Task<PayrollFinalizationDto?> GetForPeriodAsync(Guid periodId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PayrollFinalizationDto>> GetHistoryAsync(
        CancellationToken cancellationToken = default);
    Task<PayrollFinalizationDto> GetAsync(Guid finalizationId,
        CancellationToken cancellationToken = default);
    Task<PayrollFinalizationDto> FinalizeAsync(Guid periodId,
        CancellationToken cancellationToken = default);
    Task<PayrollPayslipDto> GetPayslipAsync(Guid finalEmployeeSnapshotId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PayrollPayslipDto>> GetMyPayslipsAsync(
        CancellationToken cancellationToken = default);
    Task<PayrollPayslipDto> GetMyPayslipAsync(Guid finalEmployeeSnapshotId,
        CancellationToken cancellationToken = default);
}
