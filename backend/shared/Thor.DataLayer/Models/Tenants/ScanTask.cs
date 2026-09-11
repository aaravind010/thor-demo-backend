using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A unit of work executed within a <see cref="Scan"/> (see ADR §12). Maps to
/// the "scan_task" table.
/// </summary>
[Table("scan_task")]
public sealed class ScanTask
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("scan_id")]
    [ForeignKey(nameof(Scan))]
    public Guid ScanId { get; set; }

    [Column("source_id")]
    [ForeignKey(nameof(Source))]
    public Guid SourceId { get; set; }

    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("started_at")]
    public DateTimeOffset? StartedAt { get; set; }

    [Column("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    public Scan Scan { get; set; } = null!;

    public Source Source { get; set; } = null!;
}
