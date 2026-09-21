using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models;

/// <summary>
/// A field required by an <see cref="AuthenticationType"/> (e.g. "client_id"),
/// filled in per tenant authentication method via a tenant-side AuthenticationValue.
/// Lives in the Master metadata DB — shared, connector-level reference data, not
/// per-tenant.
/// </summary>
[Table("authentication_fields")]
public sealed class AuthenticationField
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("type_id")]
    [ForeignKey(nameof(AuthenticationType))]
    public Guid TypeId { get; set; }

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("display_name")]
    public string DisplayName { get; set; } = null!;

    [Column("input_type")]
    public string InputType { get; set; } = null!;

    [Column("description")]
    public string? Description { get; set; }

    public AuthenticationType AuthenticationType { get; set; } = null!;
}
