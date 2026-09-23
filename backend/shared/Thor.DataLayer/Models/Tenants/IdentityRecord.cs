using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A person (typically sourced from HR/directory data) who can own a campaign, be
/// assigned a campaign item, or hold a party assignment. Maps to the "identity" table;
/// named "IdentityRecord" in C# to avoid colliding with ASP.NET Core Identity /
/// System.Security.Principal.IIdentity.
/// </summary>
[Table("identity")]
public sealed class IdentityRecord
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("source")]
    public string Source { get; set; } = null!;

    [Column("hr_employee_id")]
    public string HrEmployeeId { get; set; } = null!;

    [Column("display_name")]
    public string DisplayName { get; set; } = null!;

    [Column("email")]
    public string Email { get; set; } = null!;

    [Column("given_name")]
    public string GivenName { get; set; } = null!;

    [Column("surname")]
    public string Surname { get; set; } = null!;

    [Column("department")]
    public string? Department { get; set; }

    [Column("title")]
    public string? Title { get; set; }

    [Column("company")]
    public string? Company { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("content_hash")]
    public string ContentHash { get; set; } = null!;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    private readonly List<Campaign> _ownedCampaigns = new();
    public IEnumerable<Campaign> OwnedCampaigns => _ownedCampaigns;

    private readonly List<CampaignItem> _assignedItems = new();
    public IEnumerable<CampaignItem> AssignedItems => _assignedItems;

    private readonly List<CampaignExchange> _sentExchanges = new();
    public IEnumerable<CampaignExchange> SentExchanges => _sentExchanges;

    private readonly List<PartyAssignment> _partyAssignments = new();
    public IEnumerable<PartyAssignment> PartyAssignments => _partyAssignments;
}
