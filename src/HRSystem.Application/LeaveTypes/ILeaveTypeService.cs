using HRSystem.Application.Common.Models;

namespace HRSystem.Application.LeaveTypes;

public interface ILeaveTypeService
{
    Task<PagedResult<LeaveTypeDto>> GetListAsync(LeaveTypeQuery query, CancellationToken cancellationToken = default);
    Task<LeaveTypeDto> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<LeaveTypeDto> CreateAsync(CreateLeaveTypeRequest request, CancellationToken cancellationToken = default);
    Task<LeaveTypeDto> UpdateAsync(UpdateLeaveTypeRequest request, CancellationToken cancellationToken = default);
    Task SetActiveAsync(Guid id, bool isActive, string rowVersion, CancellationToken cancellationToken = default);
}
