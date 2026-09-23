using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A scheduled unit of orchestration run against a <see cref="Scan"/> (see
/// ADR §12). Named "WorkflowEntity" to leave the plain "Workflow" name free for
/// future use elsewhere; the underlying table is still named "workflow".
/// </summary>
[Table("workflow")]
public sealed class WorkflowEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("scan_id")]
    [ForeignKey(nameof(Scan))]
    public Guid? ScanId { get; set; }

    [Column("scan_manifest_id")]
    [ForeignKey(nameof(ScanManifest))]
    public Guid? ScanManifestId { get; set; }

    [Column("workflow_type")]
    public string WorkflowType { get; set; } = null!;

    [Column("trigger")]
    public string Trigger { get; set; } = null!;

    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("started_at")]
    public DateTimeOffset? StartedAt { get; set; }

    [Column("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    [Column("timeout_at")]
    public DateTimeOffset? TimeoutAt { get; set; }

    [Column("error")]
    public string? Error { get; set; }

    [Column("run_id")]
    public Guid? RunId { get; set; }
}
