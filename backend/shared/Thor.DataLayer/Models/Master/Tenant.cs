using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Thor.DataLayer.Models;

/// <summary>
/// A tenant record in the Master metadata DB (see ADR §6.2) — tenant identity plus
/// its tier/isolation/status classification. Distinct from the per-tenant database
/// modeled by <see cref="Data.TenantDbContext"/>: this table exists once, centrally,
/// and is never replicated into a tenant's own database.
/// </summary>
[Table("tenant", Schema = "auth")]
public sealed class Tenant
{
    [Key]
    [Column("tenant_id")]
    public Guid TenantId { get; set; }

    [Column("display_name")]
    public string DisplayName { get; set; } = null!;

    [Column("subdomain")]
    public string Subdomain { get; set; } = null!;

    [Column("tier_id")]
    public short TierId { get; set; }

    [Column("isolation_type_id")]
    public short IsolationTypeId { get; set; }

    [Column("status_id")]
    public short StatusId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public TenantRouting? Routing { get; set; }
}
