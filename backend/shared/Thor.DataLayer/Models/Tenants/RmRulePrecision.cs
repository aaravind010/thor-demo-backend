using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A read-model row summarizing an <see cref="AccountTypeRule"/>'s prediction
/// precision as of its <see cref="LastScan"/>.
/// </summary>
[Table("rm_rule_precision")]
[PrimaryKey(nameof(RuleId), nameof(RuleContext))]
public sealed class RmRulePrecision
{
    [Key]
    [Column("rule_id")]
    public Guid RuleId { get; set; }

    [Column("rule_context")]
    public string RuleContext { get; set; } = null!;

    [Column("rule_name")]
    public string RuleName { get; set; } = null!;

    [Column("applies_to")]
    public string AppliesTo { get; set; } = null!;

    [Column("target_type_name")]
    public string TargetTypeName { get; set; } = null!;

    [Column("total_predictions")]
    public int TotalPredictions { get; set; }

    [Column("true_positive_count")]
    public int TruePositiveCount { get; set; }

    [Column("false_positive_count")]
    public int FalsePositiveCount { get; set; }

    [Column("precision_score")]
    public decimal PrecisionScore { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("last_scan_id")]
    [ForeignKey(nameof(LastScan))]
    public Guid LastScanId { get; set; }

    [Column("last_refreshed_at")]
    public DateTimeOffset LastRefreshedAt { get; set; }

    public AccountTypeRule Rule { get; set; } = null!;

    public Scan LastScan { get; set; } = null!;
}
