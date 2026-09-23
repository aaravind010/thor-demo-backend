using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A single deliverable within a <see cref="Campaign"/> — a decision to be made on an
/// entity (identified polymorphically by <see cref="EntityType"/>/<see cref="EntityId"/>)
/// by the assigned identity.
/// </summary>
[Table("campaign_item")]
public sealed class CampaignItem
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("campaign_id")]
    [ForeignKey(nameof(Campaign))]
    public Guid CampaignId { get; set; }

    [Column("entity_type")]
    public string EntityType { get; set; } = null!;

    [Column("entity_id")]
    public Guid EntityId { get; set; }

    [Column("assigned_to_identity_id")]
    [ForeignKey(nameof(AssignedToIdentity))]
    public Guid? AssignedToIdentityId { get; set; }

    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("decision")]
    public string? Decision { get; set; }

    [Column("decision_detail")]
    public string? DecisionDetail { get; set; }

    [Column("due_date")]
    public DateTimeOffset? DueDate { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [Column("resolved_at")]
    public DateTimeOffset? ResolvedAt { get; set; }

    public Campaign Campaign { get; set; } = null!;

    public IdentityRecord? AssignedToIdentity { get; set; }

    private readonly List<CampaignExchange> _exchanges = new();
    public IEnumerable<CampaignExchange> Exchanges => _exchanges;
}
