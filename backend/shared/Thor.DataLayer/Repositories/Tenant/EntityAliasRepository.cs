using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class EntityAliasRepository(TenantDbContext context)
    : Repository<EntityAlias>(context), IEntityAliasRepository
{
    public async Task<IReadOnlyList<EntityAlias>> FindByKeysAsync(
        IReadOnlyCollection<string> keys, short connectorType, Guid sourceId, CancellationToken cancellationToken = default) =>
        await context.EntityAliases
            .Where(a => a.ConnectorType == connectorType && a.SourceId == sourceId && keys.Contains(a.AliasKey))
            .ToListAsync(cancellationToken);

    public async Task ReplaceForEntityAsync(
        string entityType, Guid entityId, Guid sourceId, short connectorType,
        IReadOnlyList<string> aliasKeys, CancellationToken cancellationToken = default)
    {
        var existing = await context.EntityAliases
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .ToListAsync(cancellationToken);
        context.EntityAliases.RemoveRange(existing);

        var now = DateTimeOffset.UtcNow;
        foreach (var key in aliasKeys)
        {
            context.EntityAliases.Add(new EntityAlias
            {
                Id = Guid.NewGuid(),
                EntityType = entityType,
                EntityId = entityId,
                SourceId = sourceId,
                ConnectorType = connectorType,
                AliasKey = key,
                CreatedAt = now,
            });
        }
    }
}
