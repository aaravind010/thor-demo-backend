using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// One rule's vote for one entity's owning identity in one Ownership run — the audit trail behind a
/// <see cref="PartyAssignment"/>'s <see cref="PartyAssignment.VoteDistribution"/>.
/// Unique on (EntityId, RuleId, RunId) so a retried run collapses instead of duplicating votes.
///
/// <para><see cref="EntityId"/> is polymorphic — an account, group or asset id, whichever the
/// voting <see cref="Rule"/> applies to — so it carries no foreign key, the same way
/// <see cref="PartyAssignment.EntityId"/> does not. A key to <c>account</c> would reject every group
/// and asset vote.</para>
/// </summary>
[Table("ownership_vote")]
[Index(nameof(EntityId), nameof(RuleId), nameof(RunId), IsUnique = true)]
public sealed class OwnershipVote
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("entity_id")]
    public Guid EntityId { get; set; }

    [Column("rule_id")]
    [ForeignKey(nameof(Rule))]
    public Guid RuleId { get; set; }

    [Column("voted_for")]
    [ForeignKey(nameof(VotedForIdentity))]
    public Guid VotedFor { get; set; }

    [Column("vote_weight")]
    public decimal VoteWeight { get; set; }

    [Column("run_id")]
    public string RunId { get; set; } = null!;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    public OwnershipRule Rule { get; set; } = null!;

    public IdentityRecord VotedForIdentity { get; set; } = null!;
}
