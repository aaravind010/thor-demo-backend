using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// An audit event on a <see cref="Campaign"/>, optionally scoped to a single
/// <see cref="CampaignItem"/>.
/// </summary>
[Table("campaign_event")]
public sealed class CampaignEvent
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("campaign_id")]
    [ForeignKey(nameof(Campaign))]
    public Guid CampaignId { get; set; }

    [Column("campaign_item_id")]
    [ForeignKey(nameof(CampaignItem))]
    public Guid CampaignItemId { get; set; }

    [Column("event_type")]
    public string EventType { get; set; } = null!;

    [Column("actor")]
    public string Actor { get; set; } = null!;

    [Column("email")]
    public string Detail { get; set; } = null!;

    [Column("occurred_at")]
    public DateTimeOffset OccurredAt { get; set; }

    public Campaign Campaign { get; set; } = null!;

    public CampaignItem CampaignItem { get; set; } = null!;
}
