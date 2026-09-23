using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IIngestChangeEventRepository : IRepository<IngestChangeEvent>
{
    /// <summary>
    /// Change events for a scan, optionally narrowed to one manifest (manifest-scoped watermark)
    /// and/or a set of entity types.
    /// </summary>
    Task<IReadOnlyList<IngestChangeEvent>> GetByScanAsync(
        Guid scanId, Guid? scanManifestId, IReadOnlyCollection<string>? entityTypes, CancellationToken cancellationToken = default);
}
