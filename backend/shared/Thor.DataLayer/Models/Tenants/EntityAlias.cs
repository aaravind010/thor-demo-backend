using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A queryable alias key for a canonical entity (<see cref="EntityType"/>/<see cref="EntityId"/>),
/// mirroring the <c>raw_attributes.alias_keys</c> every ingestor contract entity carries.
/// <c>RawAttributes</c> stores alias keys as opaque JSON
/// text — this table is what makes them queryable for edge resolution (§7.1's alias lookup).
/// One row per (entity, alias key) — most entities have exactly one, but the contract allows more.
/// </summary>
[Table("entity_alias")]
[Index(nameof(ConnectorType), nameof(SourceId), nameof(AliasKey))]
[Index(nameof(EntityType), nameof(EntityId))]
public sealed class EntityAlias
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("entity_type")]
    public string EntityType { get; set; } = null!;

    [Column("entity_id")]
    public Guid EntityId { get; set; }

    [Column("source_id")]
    public Guid SourceId { get; set; }

    [Column("connector_type")]
    public short ConnectorType { get; set; }

    [Column("alias_key")]
    public string AliasKey { get; set; } = null!;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
