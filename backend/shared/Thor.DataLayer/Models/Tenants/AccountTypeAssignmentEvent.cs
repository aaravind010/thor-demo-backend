using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// An audit event on an <see cref="AccountTypeAssignment"/> — e.g. a type change or
/// manual override.
/// </summary>
[Table("account_type_assignment_event")]
public sealed class AccountTypeAssignmentEvent
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("assignment_id")]
    [ForeignKey(nameof(AccountTypeAssignment))]
    public Guid AssignmentId { get; set; }

    [Column("event_type")]
    public string EventType { get; set; } = null!;

    [Column("previous_type_id")]
    [ForeignKey(nameof(PreviousType))]
    public Guid PreviousTypeId { get; set; }

    [Column("new_type_id")]
    [ForeignKey(nameof(NewType))]
    public Guid NewTypeId { get; set; }

    [Column("actor")]
    public string Actor { get; set; } = null!;

    [Column("campaign_id")]
    public Guid? CampaignId { get; set; }

    [Column("run_id")]
    public Guid? RunId { get; set; }

    [Column("occurred_at")]
    public DateTimeOffset OccurredAt { get; set; }

    public AccountTypeAssignment AccountTypeAssignment { get; set; } = null!;

    public AccountType PreviousType { get; set; } = null!;

    public AccountType NewType { get; set; } = null!;
}
