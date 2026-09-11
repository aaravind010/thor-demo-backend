using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A connected connector instance that feeds CDC scans for a tenant (see ADR §12).
/// Lives in the per-tenant database.
/// </summary>
[Table("source")]
public sealed class Source
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("connector_type")]
    public string ConnectorType { get; set; } = null!;

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("config")]
    public string? Config { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<ScanTask> Tasks { get; set; } = new List<ScanTask>();

    public ICollection<ScanSourceMapping> ScanSourceMappings { get; set; } = new List<ScanSourceMapping>();
}
