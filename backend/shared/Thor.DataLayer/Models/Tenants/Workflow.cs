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
    [Column("workflow_id")]
    public Guid WorkflowId { get; set; }

    [Column("batch_id")]
    public Guid BatchId { get; set; }

    [Column("scan_id")]
    [ForeignKey(nameof(Scan))]
    public Guid ScanId { get; set; }

    [Column("workflow_type")]
    public string WorkflowType { get; set; } = null!;

    [Column("scheduled_by")]
    public string ScheduledBy { get; set; } = null!;

    [Column("state")]
    public string State { get; set; } = null!;

    [Column("started_at")]
    public DateTimeOffset? StartedAt { get; set; }

    [Column("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [Column("timeout_at")]
    public DateTimeOffset? TimeoutAt { get; set; }

    public Scan Scan { get; set; } = null!;
}
