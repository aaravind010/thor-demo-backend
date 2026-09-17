using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A canonical entitlement/permission record (e.g. an admin right or sudo grant),
/// reconciled from staged ingestion rows (see ADR §12 Ingestion &amp; CDC) against this
/// table.
/// </summary>
[Table("entitlement")]
public sealed class Entitlement
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("source_id")]
    [ForeignKey(nameof(Source))]
    public Guid SourceId { get; set; }

    [Column("connector_type")]
    public short ConnectorType { get; set; }

    [Column("native_id")]
    public string NativeId { get; set; } = null!;

    [Column("entitlement_type")]
    public string EntitlementType { get; set; } = null!;

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("description")]
    public string? Description { get; set; }

    [Column("is_admin")]
    public bool IsAdmin { get; set; }

    [Column("scope")]
    public string? Scope { get; set; }

    [Column("instance_name")]
    public string? InstanceName { get; set; }

    [Column("asset_id")]
    [ForeignKey(nameof(Asset))]
    public Guid? AssetId { get; set; }

    [Column("sudo_path")]
    public string? SudoPath { get; set; }

    [Column("sudo_host")]
    public string? SudoHost { get; set; }

    [Column("raw_attributes")]
    public string? RawAttributes { get; set; }

    [Column("content_hash")]
    public string ContentHash { get; set; } = null!;

    [Column("hash_version")]
    public string HashVersion { get; set; } = null!;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public Source Source { get; set; } = null!;
    public Asset? Asset { get; set; }
}
