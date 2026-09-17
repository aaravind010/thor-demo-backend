using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A parked, not-yet-resolvable edge reference (<c>EdgeRef</c>) whose relationship type is
/// only ever stored on one side of the relationship (e.g. AD's <c>manager</c>/<c>managedBy</c>)
/// — so the edge gate can't wait for a reciprocal ref to resolve it, and instead must find the
/// target later, whenever it's promoted (one-sided reference healing).
/// </summary>
[Table("one_sided_ref")]
[Index(nameof(ConnectorType), nameof(SourceId), nameof(Key))]
[Index(nameof(OwnerId))]
public sealed class OneSidedRef
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("key")]
    public string Key { get; set; } = null!;

    [Column("owner_id")]
    public Guid OwnerId { get; set; }

    [Column("owner_type")]
    public string OwnerType { get; set; } = null!;

    [Column("rel")]
    public string Rel { get; set; } = null!;

    [Column("dir")]
    public string Dir { get; set; } = null!;

    [Column("target_type")]
    public string? TargetType { get; set; }

    /// <summary>The <see cref="EdgeRef"/>'s <c>Props</c> JSON-object text, carried through so a
    /// later-healed edge doesn't lose it (mirrors the POC's <c>onesided_ref.props</c>).</summary>
    [Column("props")]
    public string? Props { get; set; }

    [Column("source_id")]
    public Guid? SourceId { get; set; }

    [Column("connector_type")]
    public short? ConnectorType { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
