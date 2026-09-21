using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A read-model row summarizing an <see cref="Account"/>'s type/ownership
/// classification as of its <see cref="LastScan"/>.
/// </summary>
[Table("rm_account_summary")]
public sealed class RmAccountSummary
{
    [Key]
    [Column("account_id")]
    public Guid AccountId { get; set; }

    [Column("display_name")]
    public string DisplayName { get; set; } = null!;

    [Column("connector_type")]
    public short ConnectorType { get; set; }

    [Column("account_kind")]
    public string AccountKind { get; set; } = null!;

    [Column("is_human")]
    public bool IsHuman { get; set; }

    [Column("account_type_name")]
    public string AccountTypeName { get; set; } = null!;

    [Column("type_method")]
    public string TypeMethod { get; set; } = null!;

    [Column("type_is_override")]
    public bool TypeIsOverride { get; set; }

    [Column("owner_name")]
    public string OwnerName { get; set; } = null!;

    [Column("owner_email")]
    public string OwnerEmail { get; set; } = null!;

    [Column("ownership_is_override")]
    public bool OwnershipIsOverride { get; set; }

    [Column("last_scan_id")]
    [ForeignKey(nameof(LastScan))]
    public Guid LastScanId { get; set; }

    [Column("last_refreshed_at")]
    public DateTimeOffset LastRefreshedAt { get; set; }

    public Scan LastScan { get; set; } = null!;
}
