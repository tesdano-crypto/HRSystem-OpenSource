using HRSystem.Application.Common.Models;

namespace HRSystem.Application.Attendance;

public interface IAttendancePunchRecordService
{
    Task<IReadOnlyList<AttendancePunchEmployeeOptionDto>> GetEmployeeOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<PagedResult<AttendancePunchRecordDto>> GetListAsync(
        AttendancePunchRecordQuery query,
        CancellationToken cancellationToken = default);
}
