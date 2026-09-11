using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// One CDC scan run for a tenant (see ADR §12) — tracks scan lifecycle state and the
/// tasks executed within it. Lives in the per-tenant database.
/// </summary>
[Table("scan")]
public sealed class Scan
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("scan_config_id")]
    [ForeignKey(nameof(ScanConfig))]
    public Guid ScanConfigId { get; set; }

    [Column("total_tasks")]
    public int TotalTasks { get; set; }

    [Column("completed_tasks")]
    public int CompletedTasks { get; set; }

    [Column("started_at")]
    public DateTimeOffset? StartedAt { get; set; }

    [Column("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    [Column("ingestion_completed_at")]
    public DateTimeOffset? IngestionCompletedAt { get; set; }

    [Column("analysis_completed_at")]
    public DateTimeOffset? AnalysisCompletedAt { get; set; }

    public ScanConfig ScanConfig { get; set; } = null!;

    public ICollection<ScanTask> Tasks { get; set; } = new List<ScanTask>();

    public ICollection<WorkflowEntity> Workflows { get; set; } = new List<WorkflowEntity>();
}
