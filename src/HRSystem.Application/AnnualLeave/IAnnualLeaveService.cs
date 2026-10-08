using HRSystem.Domain.LeaveRequests;

namespace HRSystem.Application.AnnualLeave;

public interface IAnnualLeaveService
{
    Task<AnnualLeaveBalanceDto> GetMyBalanceAsync(
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AnnualLeaveEmployeeSummaryDto>> GetAdminSummariesAsync(
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default);
    Task<AnnualLeaveBalanceDto> GetAdminBalanceAsync(
        Guid employeeId,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default);
    Task<AnnualLeaveBackfillPreviewDto> PreviewBackfillAsync(
        DateOnly asOf,
        CancellationToken cancellationToken = default);
    Task<AnnualLeaveBackfillApplyResultDto> ApplyBackfillForEmployeeAsync(
        Guid employeeId,
        DateOnly asOf,
        CancellationToken cancellationToken = default);
    Task ApplyCarryForwardAsync(
        Guid sourceEntitlementId,
        Guid targetEntitlementId,
        int minutes,
        CancellationToken cancellationToken = default);
    Task SettleEntitlementAsync(
        Guid entitlementId,
        int minutes,
        HRSystem.Domain.AnnualLeave.AnnualLeaveEntitlementStatus settlementStatus,
        CancellationToken cancellationToken = default);
    Task InitializeEmployeeAsync(
        Guid employeeId,
        DateOnly throughDate,
        CancellationToken cancellationToken = default);

    Task ReserveForSubmissionAsync(LeaveRequest request, CancellationToken cancellationToken);
    Task ConsumeForApprovalAsync(Guid leaveRequestId, CancellationToken cancellationToken);
    Task ReleaseReservationAsync(Guid leaveRequestId, CancellationToken cancellationToken);
    Task RestoreAfterCancellationAsync(Guid leaveRequestId, CancellationToken cancellationToken);
}

public sealed record AnnualLeaveEntitlementDto(
    Guid Id,
    DateOnly GrantedDate,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal GrantedDays,
    int GrantedMinutes,
    int CarriedInMinutes,
    int ReservedMinutes,
    int ConsumedMinutes,
    int SettledMinutes,
    int AvailableMinutes,
    string Status);

public sealed record AnnualLeaveAllocationDto(
    Guid LeaveRequestId,
    string RequestNumber,
    int AllocatedMinutes,
    string Status);

public sealed record AnnualLeaveBalanceDto(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    DateOnly HireDate,
    int CompletedServiceYears,
    int AdditionalServiceMonths,
    DateOnly? NextEntitlementDate,
    int GrantedMinutes,
    int ReservedMinutes,
    int ConsumedMinutes,
    int AvailableMinutes,
    IReadOnlyList<AnnualLeaveEntitlementDto> Entitlements,
    IReadOnlyList<AnnualLeaveAllocationDto> Allocations);

public sealed record AnnualLeaveEmployeeSummaryDto(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    string DepartmentName,
    DateOnly HireDate,
    DateOnly? CurrentPeriodStart,
    DateOnly? CurrentPeriodEnd,
    string? CurrentStatus,
    int GrantedMinutes,
    int ReservedMinutes,
    int ConsumedMinutes,
    int AvailableMinutes);

public sealed record AnnualLeaveBackfillPreviewItemDto(
    Guid EmployeeId,
    string EmployeeNumber,
    DateOnly HireDate,
    string CurrentServiceMilestone,
    int MissingEntitlementCount,
    int HistoricalAnnualRequestCount,
    int AllocatableRequestCount,
    int ManualReviewRequestCount,
    IReadOnlyList<AnnualLeaveBackfillGrantDto> PlannedEntitlements,
    IReadOnlyDictionary<string, int> HistoricalRequestStatusCounts);

public sealed record AnnualLeaveBackfillGrantDto(
    string Milestone,
    DateOnly GrantedDate,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal GrantedDays,
    int GrantedMinutes);

public sealed record AnnualLeaveBackfillPreviewDto(
    DateOnly AsOf,
    int EmployeeCount,
    int MissingEntitlementCount,
    int HistoricalAnnualRequestCount,
    int ManualReviewRequestCount,
    IReadOnlyList<AnnualLeaveBackfillPreviewItemDto> Items);

public sealed record AnnualLeaveBackfillApplyResultDto(
    Guid EmployeeId,
    int EntitlementsCreated,
    int RequestsAllocated,
    int AllocationRowsCreated);
