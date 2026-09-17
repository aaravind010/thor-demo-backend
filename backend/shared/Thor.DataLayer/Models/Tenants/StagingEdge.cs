using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A staged relationship row (e.g. group membership, ACL grant) between two
/// entities, landed by a connector ingestion run (see ADR §12 Ingestion &amp; CDC),
/// prior to being hashed and diffed against the canonical edge table to produce
/// insert/update/delete sets.
/// </summary>
[Table("staging_edge")]
[Index(nameof(ScanManifestId), nameof(FromId), nameof(ToId), nameof(RelType))]
public sealed class StagingEdge
{
    [Column("scan_manifest_id")]
    public Guid ScanManifestId { get; set; }
    
    [Column("batch_seq")]
    public int BatchSeq { get; set; }
    
    [Column("from_id")]
    public Guid FromId { get; set; }
    
    [Column("from_type")]
    public string FromType { get; set; } = null!;

    [Column("to_id")]
    public Guid ToId { get; set; }
    
    [Column("to_type")]
    public string ToType { get; set; } = null!;

    [Column("rel_type")]
    public string RelType { get; set; } = null!;

    [Column("props")]
    public string Props { get; set; } = null!;

    [Column("content_hash")]
    public string ContentHash { get; set; } = null!;

    [Column("received_at")]
    public DateTimeOffset ReceivedAt { get; set; }
}
