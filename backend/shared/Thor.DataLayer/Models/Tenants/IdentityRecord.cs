using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A person (typically sourced from HR/directory data) who can own a campaign, be
/// assigned a campaign item, or hold a party assignment. Maps to the "identity" table;
/// named "IdentityRecord" in C# to avoid colliding with ASP.NET Core Identity /
/// System.Security.Principal.IIdentity.
///
/// Ingestion-promoted rows (the HR feed) upsert on <c>(source_id, hr_employee_id)</c>.
/// <see cref="SourceId"/> is nullable only because identities not landed by ingestion
/// (e.g. seeded directly) have no source row.
/// </summary>
[Table("identity")]
[Index(nameof(SourceId), nameof(HrEmployeeId), IsUnique = true)]
public sealed class IdentityRecord
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("source")]
    public string Source { get; set; } = null!;

    [Column("source_id")]
    [ForeignKey(nameof(SourceRow))]
    public Guid? SourceId { get; set; }

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

    [Column("business_unit")]
    public string? BusinessUnit { get; set; }

    [Column("sam_account_name")]
    public string? SamAccountName { get; set; }

    [Column("upn")]
    public string? Upn { get; set; }

    [Column("manager_employee_id")]
    public string? ManagerEmployeeId { get; set; }

    [Column("manager_name")]
    public string? ManagerName { get; set; }

    /// <summary>Name of the AD attribute this person correlates on (e.g. <c>SamAccountName</c>); see <see cref="AdMatchValue"/>.</summary>
    [Column("ad_match_field")]
    public string? AdMatchField { get; set; }

    [Column("ad_match_value")]
    public string? AdMatchValue { get; set; }

    /// <summary>The HR feed's "Marked To Retire" flag. Stored only — nothing acts on it yet.</summary>
    [Column("marked_to_retire")]
    public bool MarkedToRetire { get; set; }

    /// <summary>Feed fields with no column of their own, as JSON.</summary>
    [Column("raw_attributes")]
    public string? RawAttributes { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("content_hash")]
    public string ContentHash { get; set; } = null!;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public Source? SourceRow { get; set; }

    private readonly List<Campaign> _ownedCampaigns = new();
    public IEnumerable<Campaign> OwnedCampaigns => _ownedCampaigns;

    private readonly List<CampaignItem> _assignedItems = new();
    public IEnumerable<CampaignItem> AssignedItems => _assignedItems;

    private readonly List<CampaignExchange> _sentExchanges = new();
    public IEnumerable<CampaignExchange> SentExchanges => _sentExchanges;

    private readonly List<PartyAssignment> _partyAssignments = new();
    public IEnumerable<PartyAssignment> PartyAssignments => _partyAssignments;
}
