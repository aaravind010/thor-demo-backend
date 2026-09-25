using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// Well-known values for <see cref="Scan.Status"/>. Plain string constants, matching how
/// <see cref="ScanTask.Status"/> and <see cref="WorkflowEntity.State"/> are modeled in this
/// codebase today.
/// </summary>
public static class ScanStatus
{
    public const string Pending = "Pending";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

/// <summary>Well-known values for <see cref="Scan.ScanType"/> — how the scan was triggered.</summary>
public static class ScanTriggerType
{
    public const string Instant = "instant";
    public const string Scheduled = "scheduled";
}

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

    [Column("status")]
    public string Status { get; set; } = "scheduled";

    [Column("scan_type")]
    public string ScanType { get; set; } = null!;

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

    [Column("notes")]
    public string? Notes { get; set; }

    public ScanConfig ScanConfig { get; set; } = null!;

    private readonly List<ScanTask> _tasks = new();
    public IEnumerable<ScanTask> Tasks => _tasks;

    private readonly List<WorkflowEntity> _workflows = new();
    public IEnumerable<WorkflowEntity> Workflows => _workflows;
}
