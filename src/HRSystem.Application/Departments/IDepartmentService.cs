using HRSystem.Application.Common.Models;

namespace HRSystem.Application.Departments;

public interface IDepartmentService
{
    Task<PagedResult<DepartmentDto>> GetListAsync(DepartmentQuery query, CancellationToken cancellationToken = default);
    Task<DepartmentDto> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DepartmentDto> CreateAsync(CreateDepartmentRequest request, CancellationToken cancellationToken = default);
    Task<DepartmentDto> UpdateAsync(UpdateDepartmentRequest request, CancellationToken cancellationToken = default);
    Task SetActiveAsync(Guid id, bool isActive, string rowVersion, CancellationToken cancellationToken = default);
}
