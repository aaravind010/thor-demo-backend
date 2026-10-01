using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// Per-run staging for Ownership's <c>inherit_owner</c> walk rules: the nearest owned ancestor one
/// entity inherits from under one rule, found by the walk step's breadth-first propagation before
/// the phase's vote chunks read it back as that rule's candidate.
///
/// <para>One row per (run, rule, entity) is the whole visited-set of the traversal: a level only
/// inserts entities not already reached, so every node is expanded at most once per rule however
/// many paths lead to it. <see cref="Depth"/> is the level that reached it — the frontier the next
/// level expands from, and what lets an interrupted walk resume where it stopped.</para>
///
/// <para>No foreign keys: this is working state keyed to a run, deleted once that run finalizes.
/// <see cref="EntityId"/> is polymorphic in the same way <see cref="PartyAssignment.EntityId"/> is,
/// and <see cref="RunId"/> matches <see cref="PartyAssignment.RunId"/>'s text form.</para>
/// </summary>
[Table("ownership_walk_candidate")]
[PrimaryKey(nameof(RunId), nameof(RuleId), nameof(EntityId))]
[Index(nameof(RunId), nameof(RuleId), nameof(Depth))]
public sealed class OwnershipWalkCandidate
{
    [Column("run_id")]
    public string RunId { get; set; } = null!;

    [Column("rule_id")]
    public Guid RuleId { get; set; }

    [Column("entity_id")]
    public Guid EntityId { get; set; }

    [Column("identity_id")]
    public Guid IdentityId { get; set; }

    [Column("depth")]
    public int Depth { get; set; }
}
