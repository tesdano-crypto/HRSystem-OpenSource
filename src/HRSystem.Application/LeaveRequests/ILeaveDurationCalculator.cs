namespace HRSystem.Application.LeaveRequests;

public interface ILeaveDurationCalculator
{
    Task<LeaveDurationEstimateDto> CalculateAsync(
        Guid employeeId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        CancellationToken cancellationToken = default);

    Task<LeaveDurationEstimateDto> CalculateAsync(
        Guid employeeId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        HRSystem.Domain.MasterData.LeaveCalculationMode calculationMode,
        CancellationToken cancellationToken = default);
}
