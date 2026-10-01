using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A staged person row landed by an HR feed ingestion run (see ADR §12 Ingestion &amp; CDC),
/// prior to being hashed and diffed against the canonical <see cref="IdentityRecord"/> table.
/// </summary>
[Table("staging_identity")]
[Index(nameof(ScanManifestId), nameof(SourceId), nameof(HrEmployeeId))]
public sealed class StagingIdentity
{
    [Column("scan_manifest_id")]
    public Guid ScanManifestId { get; set; }

    [Column("batch_seq")]
    public int BatchSeq { get; set; }

    [Column("source_id")]
    public Guid SourceId { get; set; }

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

    [Column("ad_match_field")]
    public string? AdMatchField { get; set; }

    [Column("ad_match_value")]
    public string? AdMatchValue { get; set; }

    [Column("marked_to_retire")]
    public bool MarkedToRetire { get; set; }

    [Column("raw_attributes")]
    public string RawAttributes { get; set; } = null!;

    [Column("content_hash")]
    public string ContentHash { get; set; } = null!;

    [Column("received_at")]
    public DateTimeOffset ReceivedAt { get; set; }
}
