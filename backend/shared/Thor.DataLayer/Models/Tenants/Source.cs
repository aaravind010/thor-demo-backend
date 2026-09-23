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

    private readonly List<Account> _accounts = new();
    public IEnumerable<Account> Accounts => _accounts;

    private readonly List<Grp> _grps = new();
    public IEnumerable<Grp> Grps => _grps;

    private readonly List<Asset> _assets = new();
    public IEnumerable<Asset> Assets => _assets;

    private readonly List<Entitlement> _entitlements = new();
    public IEnumerable<Entitlement> Entitlements => _entitlements;

    private readonly List<ScanTask> _tasks = new();
    public IEnumerable<ScanTask> Tasks => _tasks;

    private readonly List<ScanSourceMapping> _scanSourceMappings = new();
    public IEnumerable<ScanSourceMapping> ScanSourceMappings => _scanSourceMappings;
}
