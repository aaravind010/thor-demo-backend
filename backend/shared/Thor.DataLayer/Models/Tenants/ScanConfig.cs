using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A saved, reusable scan configuration — the authentication method and connector
/// settings a tenant scan is run with. Lives in the per-tenant database.
/// </summary>
[Table("scan_config")]
public sealed class ScanConfig
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("auth_method")]
    [ForeignKey(nameof(AuthenticationMethod))]
    public Guid AuthMethodId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [Column("created_by")]
    public string CreatedBy { get; set; } = null!;

    [Column("updated_by")]
    public string UpdatedBy { get; set; } = null!;

    public AuthenticationMethod AuthenticationMethod { get; set; } = null!;

    public ICollection<Scan> Scans { get; set; } = new List<Scan>();

    public ICollection<ScanSourceMapping> SourceMappings { get; set; } = new List<ScanSourceMapping>();

    public ICollection<ScanConnectorConfigValue> ConnectorConfigValues { get; set; } = new List<ScanConnectorConfigValue>();
}
