using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A connector-configured data source (e.g. a specific AD domain, file share root, or
/// SaaS tenant) that accounts/assets/entitlements/groups are ingested from.
/// </summary>
[Table("source")]
public sealed class Source
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("connector_type")]
    public short ConnectorType { get; set; }

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("config")]
    public string Config { get; set; } = null!;

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<Account> Accounts { get; set; } = new List<Account>();

    public ICollection<Grp> Grps { get; set; } = new List<Grp>();

    public ICollection<Asset> Assets { get; set; } = new List<Asset>();

    public ICollection<Entitlement> Entitlements { get; set; } = new List<Entitlement>();

    public ICollection<ScanTask> Tasks { get; set; } = new List<ScanTask>();

    public ICollection<ScanSourceMapping> ScanSourceMappings { get; set; } = new List<ScanSourceMapping>();
}
