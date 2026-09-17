using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A source included in a <see cref="ScanConfig"/>. Lives in the per-tenant database.
/// </summary>
[Table("scan_source_mapping")]
public sealed class ScanSourceMapping
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("scan_config_id")]
    [ForeignKey(nameof(ScanConfig))]
    public Guid ScanConfigId { get; set; }

    [Column("source_id")]
    [ForeignKey(nameof(Source))]
    public Guid? SourceId { get; set; }

    public ScanConfig ScanConfig { get; set; } = null!;

    public Source? Source { get; set; }
}
