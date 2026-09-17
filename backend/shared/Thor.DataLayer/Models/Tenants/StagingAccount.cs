using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A staged account/identity row landed by a connector ingestion run (see ADR §12
/// Ingestion &amp; CDC), prior to being hashed and diffed against the canonical
/// account table to produce insert/update/delete sets.
/// </summary>
[Table("staging_account")]
public sealed class StagingAccount
{
    [Column("run_id")]
    public string RunId { get; set; } = null!;

    [Column("batch_seq")]
    public int BatchSeq { get; set; }
    
    [Column("source_id")]
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
    
    [Column("raw_attributes")]
    public string RawAttributes { get; set; } = null!;

    [Column("content_hash")]
    public string ContentHash { get; set; } = null!;

    [Column("received_at")]
    public DateTimeOffset ReceivedAt { get; set; }
}
