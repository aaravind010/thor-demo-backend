using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Data;

/// <summary>
/// EF Core context for a single tenant database. Holds no tenant identity of its own —
/// which database it talks to is entirely determined by the <see cref="DbContextOptions{TContext}"/>
/// it's constructed with (see <see cref="TenantDbContextFactory"/>).
/// </summary>
public class TenantDbContext(DbContextOptions<TenantDbContext> options) : DbContext(options)
{
    public DbSet<ScanConfig> ScanConfigs => Set<ScanConfig>();

    public DbSet<Scan> Scans => Set<Scan>();

    public DbSet<ScanTask> Tasks => Set<ScanTask>();

    public DbSet<WorkflowEntity> Workflows => Set<WorkflowEntity>();

    public DbSet<WorkflowGraph> WorkflowGraphs => Set<WorkflowGraph>();

    public DbSet<Source> Sources => Set<Source>();

    public DbSet<ScanSourceMapping> ScanSourceMappings => Set<ScanSourceMapping>();

    public DbSet<AuthenticationMethod> AuthenticationMethods => Set<AuthenticationMethod>();

    public DbSet<AuthenticationValue> AuthenticationValues => Set<AuthenticationValue>();

    public DbSet<ScanConnectorConfigValue> ScanConnectorConfigValues => Set<ScanConnectorConfigValue>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("tenant");
    }
}
