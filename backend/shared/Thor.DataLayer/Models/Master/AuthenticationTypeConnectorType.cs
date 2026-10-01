using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models;

/// <summary>
/// Join row declaring that one <see cref="AuthenticationType"/> may be used with one
/// <see cref="ConnectorType"/>. An authentication type can support many connector types; a
/// ScanConfig is only valid when its auth method's type has a row here for the sources'
/// connector type. Composite key (<see cref="AuthenticationTypeId"/>,
/// <see cref="ConnectorTypeId"/>) is configured in <c>MasterDbContext</c>.
/// </summary>
[Table("authentication_type_connector_types")]
public sealed class AuthenticationTypeConnectorType
{
    [Column("authentication_type_id")]
    [ForeignKey(nameof(AuthenticationType))]
    public Guid AuthenticationTypeId { get; set; }

    [Column("connector_type_id")]
    [ForeignKey(nameof(ConnectorType))]
    public short ConnectorTypeId { get; set; }

    public AuthenticationType AuthenticationType { get; set; } = null!;

    public ConnectorType ConnectorType { get; set; } = null!;
}
