using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Models;

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

    public DbSet<ConnectorType> ConnectorTypes => Set<ConnectorType>();
    
    public DbSet<TenantApiKey> TenantApiKeys => Set<TenantApiKey>();

    public DbSet<ApiScope> ApiScopes => Set<ApiScope>();

    public DbSet<KeyScopeMap> KeyScopeMaps => Set<KeyScopeMap>();

    public DbSet<TaskApiRefreshToken> TaskApiRefreshTokens => Set<TaskApiRefreshToken>();
    public DbSet<TenantLogSinkConfig> TenantLogSinkConfigs => Set<TenantLogSinkConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("master");
    }
}
