using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A remediation/attestation campaign (e.g. access review, orphaned-account cleanup)
/// made up of <see cref="CampaignItem"/> rows assigned to identities.
/// </summary>
[Table("campaign")]
public sealed class Campaign
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("campaign_type")]
    public string CampaignType { get; set; } = null!;

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("description")]
    public string Description { get; set; } = null!;

    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("owner_identity_id")]
    [ForeignKey(nameof(OwnerIdentity))]
    public Guid OwnerIdentityId { get; set; }

    [Column("due_date")]
    public DateTimeOffset? DueDate { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [Column("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    public IdentityRecord OwnerIdentity { get; set; } = null!;

    private readonly List<CampaignItem> _items = new();
    public IEnumerable<CampaignItem> Items => _items;

    private readonly List<CampaignEvent> _events = new();
    public IEnumerable<CampaignEvent> Events => _events;
}
