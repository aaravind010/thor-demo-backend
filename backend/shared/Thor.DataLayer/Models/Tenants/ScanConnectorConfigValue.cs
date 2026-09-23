using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// The value of one connector config field (see <see cref="Models.ConnectorConfigField"/>
/// in the Master metadata DB) for one <see cref="ScanConfig"/>. Lives in the per-tenant
/// database; <see cref="ConfigId"/> and <see cref="ConnectorType"/> are cross-database
/// references (to <c>Models.ConnectorConfigField.Id</c> and
/// <c>Models.ConnectorType.Id</c> respectively, both in the Master metadata DB) with no
/// local FK.
/// </summary>
[Table("scan_connector_config_values")]
public sealed class ScanConnectorConfigValue
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("scan_config_id")]
    [ForeignKey(nameof(ScanConfig))]
    public Guid ScanConfigId { get; set; }

    [Column("connector_type")]
    public short ConnectorType { get; set; }

    [Column("config_id")]
    public Guid ConfigId { get; set; }

    [Column("value")]
    public string Value { get; set; } = null!;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [Column("created_by")]
    public string CreatedBy { get; set; } = null!;

    [Column("updated_by")]
    public string UpdatedBy { get; set; } = null!;

    public ScanConfig ScanConfig { get; set; } = null!;
}
