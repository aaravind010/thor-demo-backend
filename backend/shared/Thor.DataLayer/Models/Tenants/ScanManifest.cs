using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

[Table("scan_manifest")]
public sealed class ScanManifest
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("scan_id")]
    [ForeignKey(nameof(Scan))]
    public Guid ScanId { get; set; }

    [Column("file_locations")]
    public string[] FileLocations { get; set; } = null!;

    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("processes_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public Scan Scan { get; set; } = null!;
}
