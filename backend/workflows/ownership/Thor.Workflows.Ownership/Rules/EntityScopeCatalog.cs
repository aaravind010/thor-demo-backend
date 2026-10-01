using Thor.Workflows.Ownership.Constants;

namespace Thor.Workflows.Ownership.Rules;

/// <summary>Fixed per-entity-type SQL scoping info: which table, and how to exclude already-deleted rows. Generalizes the old account-only <c>scoped</c> CTE.</summary>
internal sealed record EntityScope(string Table, string? NotDeletedPredicate);

internal static class EntityScopeCatalog
{
    private static readonly IReadOnlyDictionary<string, EntityScope> ByEntityType = new Dictionary<string, EntityScope>
    {
        [ScopeConstants.AccountEntityType] = new EntityScope("tenant.account", "{alias}.is_deleted = FALSE"),
        [ScopeConstants.GroupEntityType] = new EntityScope("tenant.grp", "{alias}.is_deleted = FALSE"),
        [ScopeConstants.AssetEntityType] = new EntityScope("tenant.asset", null),
    };

    public static EntityScope Resolve(string entityType) =>
        ByEntityType.TryGetValue(entityType, out var scope)
            ? scope
            : throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Unknown Ownership entity type.");
}
