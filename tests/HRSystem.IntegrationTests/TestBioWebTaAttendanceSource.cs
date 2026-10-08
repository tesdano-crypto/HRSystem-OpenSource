using HRSystem.Application.Attendance;

namespace HRSystem.IntegrationTests;

internal sealed class TestBioWebTaAttendanceSource : IBioWebTaAttendanceSource
{
    private readonly object _gate = new();
    private readonly List<BioWebAttendanceSourceRecord> _records = [];

    public int ReadCount { get; private set; }

    public void Add(params BioWebAttendanceSourceRecord[] records)
    {
        lock (_gate)
        {
            _records.AddRange(records);
        }
    }

    public Task<IReadOnlyList<BioWebAttendanceSourceRecord>> ReadAfterAsync(
        long lastExternalEventId,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ReadCount++;
            return Task.FromResult<IReadOnlyList<BioWebAttendanceSourceRecord>>(
                _records
                    .Where(item => item.ExternalEventId > lastExternalEventId)
                    .OrderBy(item => item.ExternalEventId)
                    .Take(batchSize)
                    .ToArray());
        }
    }

    public Task<IReadOnlyList<BioWebAttendanceSourceRecord>> ReadWindowPageAsync(
        BioWebTaSourceWindowPageRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ReadCount++;
            return Task.FromResult<IReadOnlyList<BioWebAttendanceSourceRecord>>(
                _records
                    .Where(item =>
                        item.EventLocalDateTime >= request.QueryFromLocal &&
                        item.EventLocalDateTime < request.QueryToLocal &&
                        (!request.AfterEventLocalDateTime.HasValue ||
                         item.EventLocalDateTime > request.AfterEventLocalDateTime.Value ||
                         (item.EventLocalDateTime == request.AfterEventLocalDateTime.Value &&
                          item.ExternalEventId > request.AfterExternalEventId)))
                    .OrderBy(item => item.EventLocalDateTime)
                    .ThenBy(item => item.ExternalEventId)
                    .Take(request.PageSize)
                    .ToArray());
        }
    }
}
