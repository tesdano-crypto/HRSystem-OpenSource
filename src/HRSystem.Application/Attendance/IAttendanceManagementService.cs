using HRSystem.Application.Common.Models;

namespace HRSystem.Application.Attendance;

public interface IAttendanceManagementService
{
    Task<IReadOnlyList<AttendanceShiftDto>> GetShiftsAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default);

    Task<AttendanceShiftDto> SaveShiftAsync(
        SaveAttendanceShiftRequest request,
        CancellationToken cancellationToken = default);

    Task SetShiftActiveAsync(
        Guid id,
        bool isActive,
        string rowVersion,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeShiftAssignmentDto>> GetAssignmentsAsync(
        Guid? employeeId,
        bool includeInactive,
        CancellationToken cancellationToken = default);

    Task<EmployeeShiftAssignmentDto> SaveAssignmentAsync(
        SaveEmployeeShiftAssignmentRequest request,
        CancellationToken cancellationToken = default);

    Task SetAssignmentActiveAsync(
        Guid id,
        bool isActive,
        string rowVersion,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttendanceEmployeeOptionDto>> GetEmployeeOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttendanceEmployeeOptionDto>>
        GetDailyEmployeeOptionsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        bool includeInactiveEmployees = false,
        CancellationToken cancellationToken = default);

    Task<PagedResult<DailyAttendanceResultDto>> GetDailyResultsAsync(
        DailyAttendanceQuery query,
        CancellationToken cancellationToken = default);

    Task<AttendanceRecalculationPreview> GetRecalculationPreviewAsync(
        AttendanceRecalculationRequest request,
        CancellationToken cancellationToken = default);

    Task<AttendanceRecalculationResultDto> RecalculateAsync(
        AttendanceRecalculationRequest request,
        CancellationToken cancellationToken = default);

    Task<DailyAttendanceResultDto> AdjustAsync(
        AttendanceAdjustmentRequest request,
        CancellationToken cancellationToken = default);

    Task<AttendanceCorrectionAdjustmentResult> ApplyCorrectionAdjustmentAsync(
        AttendanceAdjustmentRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Attendance correction adjustment is not supported by this implementation.");

    Task<DailyAttendanceResultDto> RevertAdjustmentAsync(
        RevertAttendanceAdjustmentRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttendanceAdjustmentDto>> GetAdjustmentHistoryAsync(
        Guid dailyAttendanceResultId,
        CancellationToken cancellationToken = default);
}
