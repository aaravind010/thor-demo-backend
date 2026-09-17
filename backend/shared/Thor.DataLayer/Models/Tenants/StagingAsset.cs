using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A staged asset row (file, folder, share, etc.) landed by a connector ingestion
/// run (see ADR §12 Ingestion &amp; CDC), prior to being hashed and diffed against
/// the canonical asset table to produce insert/update/delete sets.
/// </summary>
[Table("staging_asset")]
public sealed class StagingAsset
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

    [Column("asset_type")]
    public string AssetType { get; set; } = null!;

    [Column("display_name")]
    public string DisplayName { get; set; } = null!;

    [Column("full_path")]
    public string FullPath { get; set; } = null!;

    [Column("filer_name")]
    public string FilerName { get; set; } = null!;

    [Column("file_size")]
    public long FileSize { get; set; }
    
    [Column("file_count")]
    public long FileCount { get; set; }
    
    [Column("broken_acl")]
    public bool BrokenAcl { get; set; }
    
    [Column("is_protected")]
    public bool IsProtected { get; set; }
    
    [Column("raw_attributes")]
    public string RawAttributes { get; set; } = null!;

    [Column("content_hash")]
    public string ContentHash { get; set; } = null!;

    [Column("received_at")]
    public DateTimeOffset ReceivedAt { get; set; }
}
