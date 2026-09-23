using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models;

/// <summary>
/// A connector-specific, non-authentication config field (e.g. a base URL or page
/// size) that a tenant-side ScanConfig can set a value for. Lives in the Master
/// metadata DB — shared, connector-level reference data, not per-tenant.
/// </summary>
[Table("connector_config_fields")]
public sealed class ConnectorConfigField
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("connector_type_id")]
    [ForeignKey(nameof(ConnectorType))]
    public short ConnectorTypeId { get; set; }

    public ConnectorType ConnectorType { get; set; } = null!;

    [Column("field_name")]
    public string FieldName { get; set; } = null!;

    [Column("value")]
    public string? Value { get; set; }

    [Column("display_name")]
    public string DisplayName { get; set; } = null!;

    [Column("description")]
    public string? Description { get; set; }

    [Column("input_type")]
    public string InputType { get; set; } = null!;

    [Column("required")]
    public bool Required { get; set; }
}
