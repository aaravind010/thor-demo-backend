using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// An audit event on a <see cref="PartyAssignment"/> — e.g. an identity change or
/// manual override.
/// </summary>
[Table("party_assignment_event")]
public sealed class PartyAssignmentEvent
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("assignment_id")]
    [ForeignKey(nameof(Assignment))]
    public Guid AssignmentId { get; set; }

    [Column("event_type")]
    public string EventType { get; set; } = null!;

    [Column("previous_identity_id")]
    [ForeignKey(nameof(PreviousIdentity))]
    public Guid? PreviousIdentityId { get; set; }

    [Column("new_identity_id")]
    [ForeignKey(nameof(NewIdentity))]
    public Guid? NewIdentityId { get; set; }

    [Column("actor")]
    public string Actor { get; set; } = null!;

    [Column("campaign_id")]
    [ForeignKey(nameof(Campaign))]
    public Guid? CampaignId { get; set; }

    [Column("run_id")]
    public string RunId { get; set; } = null!;

    [Column("occurred_at")]
    public DateTimeOffset OccurredAt { get; set; }

    public PartyAssignment Assignment { get; set; } = null!;

    public IdentityRecord? PreviousIdentity { get; set; }

    public IdentityRecord? NewIdentity { get; set; }

    public Campaign? Campaign { get; set; }
}
