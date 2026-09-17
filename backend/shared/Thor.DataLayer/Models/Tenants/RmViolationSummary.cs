using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A read-model row summarizing a <see cref="Violation"/> as of its
/// <see cref="LastScan"/>, for reporting without joining the live entity graph.
/// </summary>
[Table("rm_violation_summary")]
public sealed class RmViolationSummary
{
    [Key]
    [Column("violation_id")]
    public Guid ViolationId { get; set; }

    [Column("entity_type")]
    public string EntityType { get; set; } = null!;

    [Column("entity_id")]
    public Guid EntityId { get; set; }

    [Column("severity")]
    public string Severity { get; set; } = null!;

    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("detail")]
    public string Detail { get; set; } = null!;

    [Column("detected_at")]
    public DateTimeOffset DetectedAt { get; set; }

    [Column("rule_name")]
    public string RuleName { get; set; } = null!;

    [Column("applies_to")]
    public string AppliesTo { get; set; } = null!;

    [Column("last_scan_id")]
    [ForeignKey(nameof(LastScan))]
    public Guid LastScanId { get; set; }

    [Column("last_refreshed_at")]
    public DateTimeOffset LastRefreshedAt { get; set; }

    public Violation Violation { get; set; } = null!;

    public Scan LastScan { get; set; } = null!;
}
