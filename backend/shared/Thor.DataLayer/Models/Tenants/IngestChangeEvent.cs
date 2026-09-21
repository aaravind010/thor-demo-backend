using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A detected insert/update/delete on an entity (identified polymorphically by
/// <see cref="EntityType"/>/<see cref="EntityId"/>) produced by the CDC diff for a
/// <see cref="ScanManifest"/> (see ADR §12).
/// </summary>
[Table("ingest_change_event")]
public sealed class IngestChangeEvent
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("scan_manifest_id")]
    [ForeignKey(nameof(ScanManifest))]
    public Guid ScanManifestId { get; set; }

    [Column("scan_id")]
    [ForeignKey(nameof(Scan))]
    public Guid ScanId { get; set; }

    [Column("entity_type")]
    public string EntityType { get; set; } = null!;

    [Column("entity_id")]
    public Guid EntityId { get; set; }

    [Column("change_type")]
    public string ChangeType { get; set; } = null!;

    [Column("occurred_at")]
    public DateTimeOffset OccurredAt { get; set; }

    public ScanManifest ScanManifest { get; set; } = null!;

    public Scan Scan { get; set; } = null!;
}
