namespace HRSystem.Application.Attendance;

public interface IBioWebPersonMappingService
{
    Task<IReadOnlyList<BioWebPersonMappingDto>> GetMappingsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UnmappedBioWebPinDto>> GetUnmappedPinsAsync(
        CancellationToken cancellationToken = default);

    Task<BioWebPersonMappingDto> CreateAsync(
        CreateBioWebPersonMappingRequest request,
        CancellationToken cancellationToken = default);

    Task<BioWebPersonMappingDto> UpdateAsync(
        UpdateBioWebPersonMappingRequest request,
        CancellationToken cancellationToken = default);

    Task DeactivateAsync(
        Guid id,
        string rowVersion,
        CancellationToken cancellationToken = default);

    Task<BioWebRelinkPreviewDto> GetRelinkPreviewAsync(
        Guid mappingId,
        CancellationToken cancellationToken = default);

    Task<BioWebRelinkResultDto> RelinkUnmappedEventsAsync(
        Guid mappingId,
        string rowVersion,
        CancellationToken cancellationToken = default);
}
