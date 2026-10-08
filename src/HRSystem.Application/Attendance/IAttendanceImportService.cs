namespace HRSystem.Application.Attendance;

public interface IAttendanceImportService
{
    Task<AttendanceImportStatusDto> GetStatusAsync(
        CancellationToken cancellationToken = default);

    Task<AttendanceSyncResultDto> SyncNowAsync(
        CancellationToken cancellationToken = default);
}
