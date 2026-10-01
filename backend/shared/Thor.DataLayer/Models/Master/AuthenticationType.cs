using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models;

/// <summary>
/// A connector-specific authentication scheme (e.g. API key, OAuth) that
/// <see cref="AuthenticationField"/> rows are defined against. Lives in the
/// Master metadata DB — shared, connector-level reference data, not per-tenant.
/// Which connectors a type may be used with is held in
/// <see cref="AuthenticationTypeConnectorType"/>; one type can support many connectors.
/// </summary>
[Table("authentication_types")]
public sealed class AuthenticationType
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = null!;

    private readonly List<AuthenticationField> _authenticationFields = new();
    public IEnumerable<AuthenticationField> AuthenticationFields => _authenticationFields;

    private readonly List<AuthenticationTypeConnectorType> _connectorTypes = new();
    public IEnumerable<AuthenticationTypeConnectorType> ConnectorTypes => _connectorTypes;
}
