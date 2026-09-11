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

    [Column("connector_type")]
    public string ConnectorType { get; set; } = null!;

    public ICollection<AuthenticationField> AuthenticationFields { get; set; } = new List<AuthenticationField>();
}
