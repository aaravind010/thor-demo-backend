using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A staged group row landed by a connector ingestion run (see ADR §12 Ingestion
/// &amp; CDC), prior to being hashed and diffed against the canonical group table
/// to produce insert/update/delete sets.
/// </summary>
[Table("staging_grp")]
[Index(nameof(ScanManifestId), nameof(SourceId), nameof(NativeId))]
public sealed class StagingGrp
{
    [Column("scan_manifest_id")]
    public Guid ScanManifestId { get; set; }

    [Column("batch_seq")]
    public int BatchSeq { get; set; }
    
    [Column("tenant_id")]
    public Guid TenantId { get; set; }
    
    [Column("source_id")]
    public Guid SourceId { get; set; }
    
    [Column("connector_type")]
    public short ConnectorType { get; set; }
    
    [Column("native_id")]
    public string NativeId { get; set; } = null!;

    [Column("group_class")]
    public string GroupClass { get; set; } = null!;

    [Column("display_name")]
    public string DisplayName { get; set; } = null!;

    [Column("email")]
    public string Email { get; set; } = null!;

    [Column("domain_name")]
    public string DomainName { get; set; } = null!;

    [Column("is_large_group")]
    public bool IsLargeGroup { get; set; }
    
    [Column("is_deleted")]
    public bool IsDeleted { get; set; }
    
    [Column("raw_attributes")]
    public string RawAttributes { get; set; } = null!;

    [Column("content_hash")]
    public string ContentHash { get; set; } = null!;

    [Column("received_at")]
    public DateTimeOffset ReceivedAt { get; set; }
}
