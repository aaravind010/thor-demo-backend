using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A named, configured instance of an authentication type (see
/// <see cref="Models.AuthenticationType"/> in the Master metadata DB) that a
/// <see cref="ScanConfig"/> can reference. Lives in the per-tenant database;
/// <see cref="TypeId"/> is a cross-database reference with no local FK.
/// </summary>
[Table("authentication_methods")]
public sealed class AuthenticationMethod
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("type")]
    public Guid TypeId { get; set; }

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("description")]
    public string? Description { get; set; }

    public ICollection<AuthenticationValue> AuthenticationValues { get; set; } = new List<AuthenticationValue>();

    private readonly List<ScanConfig> _scanConfigs = new();
    public IEnumerable<ScanConfig> ScanConfigs => _scanConfigs;
}
