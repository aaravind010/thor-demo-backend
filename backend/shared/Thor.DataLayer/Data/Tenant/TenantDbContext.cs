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

    public DbSet<ScanManifest> ScanManifests => Set<ScanManifest>();

    public DbSet<ScanTask> Tasks => Set<ScanTask>();

    public DbSet<WorkflowEntity> Workflows => Set<WorkflowEntity>();

    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();

    public DbSet<WorkflowGraph> WorkflowGraphs => Set<WorkflowGraph>();

    public DbSet<Source> Sources => Set<Source>();

    public DbSet<ScanSourceMapping> ScanSourceMappings => Set<ScanSourceMapping>();

    public DbSet<AuthenticationMethod> AuthenticationMethods => Set<AuthenticationMethod>();

    public DbSet<AuthenticationValue> AuthenticationValues => Set<AuthenticationValue>();

    public DbSet<ScanConnectorConfigValue> ScanConnectorConfigValues => Set<ScanConnectorConfigValue>();

    public DbSet<StagingAccount> StagingAccounts => Set<StagingAccount>();

    public DbSet<StagingAsset> StagingAssets => Set<StagingAsset>();

    public DbSet<StagingEdge> StagingEdges => Set<StagingEdge>();

    public DbSet<StagingEntitlement> StagingEntitlements => Set<StagingEntitlement>();

    public DbSet<StagingGrp> StagingGrps => Set<StagingGrp>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<AccountType> AccountTypes => Set<AccountType>();

    public DbSet<AccountTypeAssignment> AccountTypeAssignments => Set<AccountTypeAssignment>();

    public DbSet<AccountTypeAssignmentEvent> AccountTypeAssignmentEvents => Set<AccountTypeAssignmentEvent>();

    public DbSet<AccountTypeRule> AccountTypeRules => Set<AccountTypeRule>();

    public DbSet<Asset> Assets => Set<Asset>();

    public DbSet<Campaign> Campaigns => Set<Campaign>();

    public DbSet<CampaignEvent> CampaignEvents => Set<CampaignEvent>();

    public DbSet<CampaignExchange> CampaignExchanges => Set<CampaignExchange>();

    public DbSet<CampaignItem> CampaignItems => Set<CampaignItem>();

    public DbSet<ControlRule> ControlRules => Set<ControlRule>();

    public DbSet<Edge> Edges => Set<Edge>();

    public DbSet<Entitlement> Entitlements => Set<Entitlement>();

    public DbSet<Grp> Grps => Set<Grp>();

    public DbSet<IdentityRecord> Identities => Set<IdentityRecord>();

    public DbSet<IngestChangeEvent> IngestChangeEvents => Set<IngestChangeEvent>();

    public DbSet<PaiResult> PaiResults => Set<PaiResult>();

    public DbSet<PartyAssignment> PartyAssignments => Set<PartyAssignment>();

    public DbSet<PartyAssignmentEvent> PartyAssignmentEvents => Set<PartyAssignmentEvent>();

    public DbSet<PrivilegeDefinition> PrivilegeDefinitions => Set<PrivilegeDefinition>();

    public DbSet<RmAccountSummary> RmAccountSummaries => Set<RmAccountSummary>();

    public DbSet<RmCampaignStatus> RmCampaignStatuses => Set<RmCampaignStatus>();

    public DbSet<RmPrivilegedAccount> RmPrivilegedAccounts => Set<RmPrivilegedAccount>();

    public DbSet<RmRulePrecision> RmRulePrecisions => Set<RmRulePrecision>();

    public DbSet<RmViolationSummary> RmViolationSummaries => Set<RmViolationSummary>();

    public DbSet<Violation> Violations => Set<Violation>();

    public DbSet<ViolationEvent> ViolationEvents => Set<ViolationEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("tenant");
        modelBuilder.Entity<StagingAccount>().HasNoKey();
        modelBuilder.Entity<StagingAsset>().HasNoKey();
        modelBuilder.Entity<StagingEdge>().HasNoKey();
        modelBuilder.Entity<StagingEntitlement>().HasNoKey();
        modelBuilder.Entity<StagingGrp>().HasNoKey();

        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
