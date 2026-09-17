using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

[Table("workflow_run")]
public sealed class WorkflowRun
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

    [Column("workflow_type")]
    public string WorkflowType { get; set; } = null!;

    [Column("trigger")]
    public string Trigger { get; set; } = null!;

    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("step_fn_execution_arn")]
    public string StepFunctionExecutionArn { get; set; } = null!;

    [Column("started_at")]
    public DateTimeOffset? StartedAt { get; set; }

    [Column("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    [Column("error")]
    public string Error { get; set; } = null!;

    public Scan Scan { get; set; } = null!;

    public ScanManifest ScanManifest { get; set; } = null!;
}
