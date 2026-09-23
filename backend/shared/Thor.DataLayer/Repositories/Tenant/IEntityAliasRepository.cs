using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IEntityAliasRepository : IRepository<EntityAlias>
{
    /// <summary>Alias rows matching any of <paramref name="keys"/>, scoped to one connector instance.</summary>
    Task<IReadOnlyList<EntityAlias>> FindByKeysAsync(
        IReadOnlyCollection<string> keys, short connectorType, Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>Replaces every alias row for one entity — simplest correct way to keep aliases in sync with a re-promoted entity.</summary>
    Task ReplaceForEntityAsync(
        string entityType, Guid entityId, Guid sourceId, short connectorType,
        IReadOnlyList<string> aliasKeys, CancellationToken cancellationToken = default);
}
