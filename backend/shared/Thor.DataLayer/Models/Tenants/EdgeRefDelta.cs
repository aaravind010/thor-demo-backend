using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// One <c>edge_refs</c> entry (see <c>EdgeRef</c>) that a single manifest either added or removed
/// on one owner, keyed by <see cref="SourceId"/>/<see cref="NativeId"/> rather than the owner's
/// canonical <c>Guid</c> id — a brand-new owner has no canonical row (and no id) yet at the point
/// this is written (see <c>EdgeRefDeltaComputer</c>, which writes this BEFORE <c>Promoter</c>'s
/// upsert creates/updates that row). Transient: deleted once <c>EdgeResolver</c>'s incremental
/// resolve has consumed a manifest's rows (mirrors the POC's B7 <c>edge_ref_delta</c> table).
/// </summary>
[Table("edge_ref_delta")]
[Index(nameof(ScanManifestId), nameof(Op), nameof(EntityType))]
public sealed class EdgeRefDelta
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("scan_manifest_id")]
    public Guid ScanManifestId { get; set; }

    [Column("source_id")]
    public Guid SourceId { get; set; }

    [Column("native_id")]
    public string NativeId { get; set; } = null!;

    [Column("entity_type")]
    public string EntityType { get; set; } = null!;

    /// <summary>"add" or "remove".</summary>
    [Column("op")]
    public string Op { get; set; } = null!;

    /// <summary>The raw <c>edge_refs</c> entry (<c>{Rel, Dir, Key, TargetType, Props}</c>), as JSON.</summary>
    [Column("ref")]
    public string Ref { get; set; } = null!;
}
