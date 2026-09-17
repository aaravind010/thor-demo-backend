using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A canonical group record, reconciled from staged ingestion rows (see ADR §12
/// Ingestion &amp; CDC) against this table.
/// </summary>
[Table("grp")]
[Index(nameof(SourceId), nameof(NativeId), IsUnique = true)]
public sealed class Grp
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

    [Column("group_class")]
    public string GroupClass { get; set; } = null!;

    [Column("display_name")]
    public string? DisplayName { get; set; }

    [Column("email")]
    public string? Email { get; set; }

    [Column("domain_name")]
    public string? DomainName { get; set; }

    [Column("is_large_group")]
    public bool IsLargeGroup { get; set; }

    [Column("is_deleted")]
    public bool IsDeleted { get; set; }

    [Column("raw_attributes")]
    public string? RawAttributes { get; set; }

    [Column("content_hash")]
    public string? ContentHash { get; set; }

    [Column("hash_version")]
    public short HashVersion { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public Source Source { get; set; } = null!;
}
