namespace HRSystem.Application.Attendance;

public interface IBioWebTaAttendanceSource
{
    Task<IReadOnlyList<BioWebAttendanceSourceRecord>> ReadAfterAsync(
        long lastExternalEventId,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BioWebAttendanceSourceRecord>> ReadWindowPageAsync(
        BioWebTaSourceWindowPageRequest request,
        CancellationToken cancellationToken = default);
}
