using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A rule used to predict/assign an owning <see cref="IdentityRecord"/> to an account, group, or
/// asset, tracked against its historical precision via <see cref="PartyAssignment"/>. Every rule
/// is fully described by <see cref="RuleType"/> (which generic engine handler applies it),
/// <see cref="AppliesTo"/> (which entity type it targets), and <see cref="RuleDefinition"/>
/// (parameters for that <see cref="RuleType"/>) — the engine never contains logic specific to one
/// named rule.
/// </summary>
[Table("ownership_rule")]
[Index(nameof(RuleName), IsUnique = true)]
public sealed class OwnershipRule
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("rule_name")]
    public string RuleName { get; set; } = null!;

    /// <summary>Which generic engine handler applies this row — e.g. <c>field_match</c>, <c>edge_hop_inheritance</c>, <c>hierarchy_inheritance</c>.</summary>
    [Column("rule_type")]
    public string RuleType { get; set; } = null!;

    [Column("rule_definition")]
    public string RuleDefinition { get; set; } = null!;

    [Column("applies_to")]
    public string AppliesTo { get; set; } = null!;

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("total_predictions")]
    public int TotalPredictions { get; set; }

    [Column("true_positive_count")]
    public int TruePositiveCount { get; set; }

    [Column("false_positive_count")]
    public int FalsePositiveCount { get; set; }

    [Column("precision_score")]
    public decimal PrecisionScore { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
