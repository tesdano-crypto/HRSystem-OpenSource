using HRSystem.Application.Common.Models;

namespace HRSystem.Application.Employees;

public interface IEmployeeService
{
    Task<PagedResult<EmployeeDto>> GetListAsync(EmployeeQuery query, CancellationToken cancellationToken = default);
    Task<EmployeeDto> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EmployeeDto> CreateAsync(CreateEmployeeRequest request, CancellationToken cancellationToken = default);
    Task<EmployeeDto> UpdateAsync(UpdateEmployeeRequest request, CancellationToken cancellationToken = default);
    Task SetActiveAsync(Guid id, bool isActive, string rowVersion, CancellationToken cancellationToken = default);
}
