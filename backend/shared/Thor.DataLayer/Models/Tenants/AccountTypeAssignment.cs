using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// The current <see cref="AccountType"/> assigned to an entity (account or group,
/// identified polymorphically by <see cref="EntityType"/>/<see cref="EntityId"/>),
/// with the voting/precision context that produced it.
/// </summary>
[Table("account_type_assignment")]
public sealed class AccountTypeAssignment
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("entity_type")]
    public string EntityType { get; set; } = null!;

    [Column("entity_id")]
    public Guid EntityId { get; set; }

    [Column("account_type_id")]
    [ForeignKey(nameof(AccountType))]
    public Guid AccountTypeId { get; set; }

    [Column("method")]
    public string Method { get; set; } = null!;

    [Column("is_override")]
    public bool IsOverride { get; set; }

    [Column("vote_distribution")]
    public string VoteDistribution { get; set; } = null!;

    [Column("contributing_rule_ids")]
    public Guid[] ContributingRuleIds { get; set; } = null!;

    [Column("precision_score_snapshot")]
    public string PrecisionScoreSnapshot { get; set; } = null!;

    [Column("run_id")]
    public string RunId { get; set; } = null!;

    [Column("assigned_at")]
    public DateTimeOffset AssignedAt { get; set; }

    [Column("overridden_at")]
    public DateTimeOffset? OverriddenAt { get; set; }

    public AccountType AccountType { get; set; } = null!;

    public ICollection<AccountTypeAssignmentEvent> Events { get; set; } = new List<AccountTypeAssignmentEvent>();
}
