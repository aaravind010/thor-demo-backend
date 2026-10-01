using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// The current identity assigned a role against an entity (identified polymorphically
/// by <see cref="EntityType"/>/<see cref="EntityId"/>) — e.g. an asset owner or
/// account manager — with the voting/precision context that produced it.
/// Unique on (EntityType, EntityId, IdentityId, RunId): the conflict target of Ownership's
/// <c>ON CONFLICT DO NOTHING</c> upsert, which Postgres rejects without a matching unique index.
/// </summary>
[Table("party_assignment")]
[Index(nameof(EntityType), nameof(EntityId), nameof(IdentityId), nameof(RunId), IsUnique = true)]
public sealed class PartyAssignment
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("entity_type")]
    public string EntityType { get; set; } = null!;

    [Column("entity_id")]
    public Guid EntityId { get; set; }

    [Column("identity_id")]
    [ForeignKey(nameof(Identity))]
    public Guid IdentityId { get; set; }

    [Column("rank")]
    public int Rank { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

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

    public IdentityRecord Identity { get; set; } = null!;

    private readonly List<PartyAssignmentEvent> _events = new();
    public IEnumerable<PartyAssignmentEvent> Events => _events;
}
