using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A canonical account/identity record, reconciled from staged ingestion rows (see
/// ADR §12 Ingestion &amp; CDC) against this table.
/// </summary>
[Table("account")]
[Index(nameof(SourceId), nameof(NativeId), IsUnique = true)]
public sealed class Account
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("source_id")]
    [ForeignKey(nameof(Source))]
    public Guid SourceId { get; set; }

    [Column("connector_type")]
    public short ConnectorType { get; set; }

    [Column("native_id")]
    public string NativeId { get; set; } = null!;

    [Column("account_kind")]
    public string AccountKind { get; set; } = null!;

    [Column("is_human")]
    public bool IsHuman { get; set; }

    [Column("display_name")]
    public string DisplayName { get; set; } = null!;

    [Column("sam_account_name")]
    public string SamAccountName { get; set; } = null!;

    [Column("upn")]
    public string Upn { get; set; } = null!;

    [Column("email")]
    public string Email { get; set; } = null!;

    [Column("domain_name")]
    public string DomainName { get; set; } = null!;

    [Column("filer_name")]
    public string FilerName { get; set; } = null!;

    [Column("native_account_id")]
    public string NativeAccountId { get; set; } = null!;

    [Column("is_deleted")]
    public bool IsDeleted { get; set; }

    [Column("is_disabled")]
    public bool IsDisabled { get; set; }

    [Column("account_type_id")]
    [ForeignKey(nameof(AccountType))]
    public Guid AccountTypeId { get; set; }

    [Column("raw_attributes")]
    public string RawAttributes { get; set; } = null!;

    [Column("content_hash")]
    public string ContentHash { get; set; } = null!;

    [Column("hash_version")]
    public short HashVersion { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public Source Source { get; set; } = null!;

    public AccountType AccountType { get; set; } = null!;

    public ICollection<PaiResult> PaiResults { get; set; } = new List<PaiResult>();
}
