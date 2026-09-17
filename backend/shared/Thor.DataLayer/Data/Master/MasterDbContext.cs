using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Models;
using Thor.DataLayer.Models.Master;

namespace Thor.DataLayer.Data;

/// <summary>
/// EF Core context for the Master metadata DB (see ADR §6.2) — tenant metadata,
/// routing, and auth data shared across all tenants. Distinct from
/// <see cref="TenantDbContext"/>, which targets a single tenant's own database.
/// </summary>
public class MasterDbContext(DbContextOptions<MasterDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantRouting> TenantRoutings => Set<TenantRouting>();

    public DbSet<AuthenticationType> AuthenticationTypes => Set<AuthenticationType>();

    public DbSet<AuthenticationField> AuthenticationFields => Set<AuthenticationField>();

    public DbSet<ConnectorConfigField> ConnectorConfigFields => Set<ConnectorConfigField>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("master");
    }
}
