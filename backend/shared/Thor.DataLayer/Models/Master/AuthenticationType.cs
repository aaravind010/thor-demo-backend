using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models;

/// <summary>
/// A connector-specific authentication scheme (e.g. API key, OAuth) that
/// <see cref="AuthenticationField"/> rows are defined against. Lives in the
/// Master metadata DB — shared, connector-level reference data, not per-tenant.
/// </summary>
[Table("authentication_types")]
public sealed class AuthenticationType
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("connector_type_id")]
    [ForeignKey(nameof(ConnectorType))]
    public short ConnectorTypeId { get; set; }

    public ConnectorType ConnectorType { get; set; } = null!;

    private readonly List<AuthenticationField> _authenticationFields = new();
    public IEnumerable<AuthenticationField> AuthenticationFields => _authenticationFields;
}
