using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A canonical asset (file, folder, share, etc.) record, reconciled from staged
/// ingestion rows (see ADR §12 Ingestion &amp; CDC) against this table.
/// </summary>
[Table("asset")]
public sealed class Asset
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

    [Column("parent_asset_id")]
    [ForeignKey(nameof(ParentAsset))]
    public Guid? ParentAssetId { get; set; }

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

    public Asset? ParentAsset { get; set; } 
}
