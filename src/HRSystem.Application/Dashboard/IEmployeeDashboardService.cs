namespace HRSystem.Application.Dashboard;

public interface IEmployeeDashboardService
{
    Task<EmployeeDashboardDto> GetAsync(CancellationToken cancellationToken = default);
}
