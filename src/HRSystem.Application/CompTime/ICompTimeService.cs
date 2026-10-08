using HRSystem.Domain.CompTime;
using HRSystem.Domain.LeaveRequests;

namespace HRSystem.Application.CompTime;

public interface ICompTimeService
{
    Task<IReadOnlyList<TrainingCompTimeDto>> GetTrainingAsync(CancellationToken cancellationToken = default);
    Task<TrainingCompTimeDto> CreateTrainingAsync(CreateTrainingCompTimeRequest request, CancellationToken cancellationToken = default);
    Task<TrainingCompTimeDto> ApproveTrainingAsync(ApproveTrainingCompTimeRequest request, CancellationToken cancellationToken = default);
    Task<CompTimeBalanceDto> GetMyBalanceAsync(
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompTimeEmployeeSummaryDto>> GetAdminSummariesAsync(
        CancellationToken cancellationToken = default);
    Task<CompTimeBalanceDto> GetAdminBalanceAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default);
    Task<CompTimeBalanceDto> CreateLegacyOpeningBalanceAsync(
        CreateLegacyCompTimeOpeningBalanceRequest request,
        CancellationToken cancellationToken = default);
    Task ConsumeForApprovalAsync(
        LeaveRequest request,
        CancellationToken cancellationToken);
    Task RestoreAfterCancellationAsync(
        LeaveRequest request,
        CancellationToken cancellationToken);
}

public sealed class CreateTrainingCompTimeRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public DateOnly TrainingDate { get; set; }
    public decimal Hours { get; set; }
    public string CourseOrReason { get; set; } = "";
    public string? Notes { get; set; }
    public DateOnly? ExpirationDate { get; set; }
}
public sealed record ApproveTrainingCompTimeRequest(Guid Id, string RowVersion, string EvidenceFingerprint, string? ReviewNote);
public sealed record TrainingCompTimeDto(Guid Id, Guid EmployeeId, string EmployeeNumber, string EmployeeName,
    DateOnly TrainingDate, decimal Hours, decimal UsedHours, decimal RemainingHours,
    string CourseOrReason, string? Notes, DateOnly? ExpirationDate, TrainingCompTimeStatus Status,
    string RowVersion, string EvidenceFingerprint, string? Warning,
    string CalendarSnapshot, IReadOnlyList<TrainingCompTimeHistory> Histories);

public sealed record CompTimeTransactionDto(
    Guid Id,
    CompTimeTransactionType TransactionType,
    decimal Hours,
    decimal SignedHours,
    DateOnly EffectiveDate,
    CompTimeSourceType SourceType,
    Guid? SourceId,
    string Reason,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy);

public sealed record CompTimeBalanceDto(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    decimal GrantedHours,
    decimal ConsumedHours,
    decimal RestoredHours,
    decimal AvailableHours,
    IReadOnlyList<CompTimeTransactionDto> Transactions);

public sealed record CompTimeEmployeeSummaryDto(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    string DepartmentName,
    decimal AvailableHours);

public sealed class CreateLegacyCompTimeOpeningBalanceRequest
{
    public Guid EmployeeId { get; set; }
    public decimal Hours { get; set; }
    public DateOnly CutoverDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}
