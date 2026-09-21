using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A canonical relationship (e.g. group membership, ACL grant) between two entities,
/// identified polymorphically by <see cref="FromType"/>/<see cref="FromId"/> and
/// <see cref="ToType"/>/<see cref="ToId"/>, reconciled from staged ingestion rows (see
/// ADR §12 Ingestion &amp; CDC) against this table.
/// </summary>
[Table("edge")]
[Index(nameof(FromId), nameof(ToId), nameof(RelType), IsUnique = true)]
public sealed class Edge
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

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
    public string? Props { get; set; }

    [Column("is_deleted")]
    public bool IsDeleted { get; set; }

    [Column("content_hash")]
    public string ContentHash { get; set; } = null!;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
