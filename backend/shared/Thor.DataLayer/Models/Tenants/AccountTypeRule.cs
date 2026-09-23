using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A rule used to predict/assign an <see cref="AccountType"/> to an account or group,
/// tracked against its historical precision via <see cref="AccountTypeAssignment"/>.
/// </summary>
[Table("account_type_rule")]
public sealed class AccountTypeRule
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("rule_name")]
    public string RuleName { get; set; } = null!;

    [Column("rule_definition")]
    public string RuleDefinition { get; set; } = null!;

    [Column("applies_to")]
    public string AppliesTo { get; set; } = null!;

    [Column("target_account_type_id")]
    [ForeignKey(nameof(TargetAccountType))]
    public Guid TargetAccountTypeId { get; set; }

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

    public AccountType TargetAccountType { get; set; } = null!;
}
