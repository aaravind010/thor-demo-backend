using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A single message (email or similar) exchanged as part of resolving a
/// <see cref="CampaignItem"/>.
/// </summary>
[Table("campaign_exchange")]
public sealed class CampaignExchange
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("campaign_item_id")]
    [ForeignKey(nameof(CampaignItem))]
    public Guid CampaignItemId { get; set; }

    [Column("sender_identity_id")]
    [ForeignKey(nameof(SenderIdentity))]
    public Guid SenderIdentityId { get; set; }

    [Column("recipient_identity_ids")]
    public Guid[] RecipientIdentityIds { get; set; } = null!;

    [Column("direction")]
    public string Direction { get; set; } = null!;

    [Column("channel")]
    public string Channel { get; set; } = null!;

    [Column("subject")]
    public string? Subject { get; set; }

    [Column("body")]
    public string? Body { get; set; }

    [Column("external_url")]
    public string? ExternalUrl { get; set; }

    [Column("email_message_id")]
    public string? EmailMessageId { get; set; }

    [Column("in_reply_to")]
    [ForeignKey(nameof(ReplyExchange))]
    public Guid? InReplyTo { get; set; }

    [Column("sent_at")]
    public DateTimeOffset? SentAt { get; set; }

    [Column("received_at")]
    public DateTimeOffset? ReceivedAt { get; set; }

    public CampaignItem CampaignItem { get; set; } = null!;

    public IdentityRecord SenderIdentity { get; set; } = null!;

    public CampaignExchange? ReplyExchange { get; set; }
}
