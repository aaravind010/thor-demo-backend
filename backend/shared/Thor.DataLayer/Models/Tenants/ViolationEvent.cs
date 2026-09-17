using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// An audit event on a <see cref="Violation"/> — e.g. a status change or remediation
/// note, optionally tied to the <see cref="Campaign"/> that drove it.
/// </summary>
[Table("violation_event")]
public sealed class ViolationEvent
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("violation_id")]
    [ForeignKey(nameof(Violation))]
    public Guid ViolationId { get; set; }

    [Column("event_type")]
    public string EventType { get; set; } = null!;

    [Column("previous_status")]
    public string PreviousStatus { get; set; } = null!;

    [Column("new_status")]
    public string NewStatus { get; set; } = null!;

    [Column("actor")]
    public string Actor { get; set; } = null!;

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("campaign_id")]
    [ForeignKey(nameof(Campaign))]
    public Guid? CampaignId { get; set; }

    [Column("occurred_at")]
    public DateTimeOffset OccurredAt { get; set; }

    public Violation Violation { get; set; } = null!;

    public Campaign? Campaign { get; set; }
}
