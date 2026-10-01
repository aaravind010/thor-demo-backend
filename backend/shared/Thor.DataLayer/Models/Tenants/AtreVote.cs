using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// One rule's vote for one account in one ATRE run — the audit trail behind an
/// <see cref="AccountTypeAssignment"/>'s <see cref="AccountTypeAssignment.VoteDistribution"/>.
/// Unique on (EntityId, RuleId, RunId) so a retried/re-driven run of the same manifest
/// (deterministic RunId — see AtreRunIdentity in Thor.Workflows.Atre) collapses instead of
/// duplicating vote history.
/// </summary>
[Table("atre_vote")]
[Index(nameof(EntityId), nameof(RuleId), nameof(RunId), IsUnique = true)]
public sealed class AtreVote
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("entity_id")]
    [ForeignKey(nameof(Account))]
    public Guid EntityId { get; set; }

    [Column("rule_id")]
    [ForeignKey(nameof(Rule))]
    public Guid RuleId { get; set; }

    [Column("voted_for")]
    [ForeignKey(nameof(VotedForType))]
    public Guid VotedFor { get; set; }

    [Column("vote_weight")]
    public decimal VoteWeight { get; set; }

    [Column("run_id")]
    public string RunId { get; set; } = null!;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    public Account Account { get; set; } = null!;

    public AccountTypeRule Rule { get; set; } = null!;

    public AccountType VotedForType { get; set; } = null!;
}
