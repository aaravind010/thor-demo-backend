using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.Workflows.Abstractions;
using Xunit;

namespace Thor.Workflows.Ownership.Tests.Fixtures;

/// <summary>
/// A real Postgres with the tenant schema on it, plus the rows every Ownership step test needs before
/// it can insert an entity: an account has foreign keys to <c>source</c> and <c>account_type</c>, and
/// a manifest to a scan. Rules are not seeded here — <c>start-run</c> seeds them, and tests that skip
/// it call <see cref="SeedRulesAsync"/>.
///
/// <para>Held as an <see cref="IClassFixture{T}"/>, so each test class gets its own container — the
/// same arrangement as ATRE's fixture. A real database because every Ownership read and write is raw
/// SQL no provider but Npgsql can run.</para>
/// </summary>
public sealed class TenantDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    /// <summary>The long-lived context for arranging and asserting. Steps under test get their own.</summary>
    public TenantDbContext Context { get; private set; } = null!;

    public Guid SourceId { get; private set; }

    public Guid ScanId { get; private set; }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Context = NewContext();

        // EnsureCreated, not Migrate: migrations are not tracked in this repo (.gitignore), and this
        // needs the schema TenantDbContext describes rather than a history of it.
        await Context.Database.EnsureCreatedAsync();

        Context.AccountTypes.Add(new AccountType
        {
            Id = WellKnownAccountTypes.Unclassified, Name = "Unclassified", Description = "d", IsHuman = false,
        });

        var authMethod = new AuthenticationMethod { Id = Guid.NewGuid(), TypeId = Guid.NewGuid(), Name = "test-auth" };
        var scanConfig = new ScanConfig
        {
            Id = Guid.NewGuid(), Name = "test-scan-config", AuthMethodId = authMethod.Id,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, CreatedBy = "test", UpdatedBy = "test",
        };
        var scan = new Scan { Id = Guid.NewGuid(), ScanConfigId = scanConfig.Id, ScanType = "initial", Status = "running" };
        var source = new Source
        {
            Id = Guid.NewGuid(), ConnectorType = 308, Name = "test-source", Config = "{}", IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };

        Context.AuthenticationMethods.Add(authMethod);
        Context.ScanConfigs.Add(scanConfig);
        Context.Scans.Add(scan);
        Context.Sources.Add(source);
        await Context.SaveChangesAsync();

        SourceId = source.Id;
        ScanId = scan.Id;
    }

    public async Task DisposeAsync()
    {
        await Context.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>
    /// A fresh context on its own physical connection. Production gives the vote step a separate read
    /// and write context, so a test that asserts on flush or streaming behaviour has to do the same.
    /// </summary>
    public TenantDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(_container.GetConnectionString()).Options);

    /// <summary>The built-in rule catalog, as start-run's seeder would insert it. Idempotent on rule name.</summary>
    public async Task SeedRulesAsync()
    {
        var existing = await Context.OwnershipRules.Select(r => r.RuleName).ToListAsync();
        Context.OwnershipRules.AddRange(OwnershipSeedData.BuildRules().Where(r => !existing.Contains(r.RuleName)));
        await Context.SaveChangesAsync();
    }

    public async Task<Guid> SeedManifestAsync()
    {
        var manifest = new ScanManifest
        {
            Id = Guid.NewGuid(), ScanId = ScanId, FileLocations = [], Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        Context.ScanManifests.Add(manifest);
        await Context.SaveChangesAsync();
        return manifest.Id;
    }

    /// <summary>The ingestion <c>workflow</c> row Ownership links itself to as a child. Absent unless a test asks for it.</summary>
    public async Task<WorkflowEntity> SeedIngestionWorkflowAsync(Guid scanManifestId)
    {
        var workflow = new WorkflowEntity
        {
            Id = Guid.NewGuid(), ScanId = ScanId, ScanManifestId = scanManifestId,
            WorkflowType = WorkflowTypes.Ingestion, Trigger = WorkflowTriggers.StepFunctions,
            Status = "completed", StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow,
        };
        Context.Workflows.Add(workflow);
        await Context.SaveChangesAsync();
        return workflow;
    }

    public async Task<IdentityRecord> AddIdentityAsync(string hrId, string displayName, string email)
    {
        var identity = OwnershipSeedData.BuildIdentity(hrId, displayName, email);
        Context.Identities.Add(identity);
        await Context.SaveChangesAsync();
        return identity;
    }

    public async Task<Account> AddAccountAsync(string nativeId, string? email = null)
    {
        var account = OwnershipSeedData.BuildAccount(SourceId, nativeId, email);
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();
        return account;
    }

    public async Task<Grp> AddGroupAsync(string nativeId, string? email = null)
    {
        var group = OwnershipSeedData.BuildGroup(SourceId, nativeId, email: email);
        Context.Grps.Add(group);
        await Context.SaveChangesAsync();
        return group;
    }

    public async Task<Asset> AddAssetAsync(string nativeId, Guid? parentAssetId = null)
    {
        var asset = OwnershipSeedData.BuildAsset(SourceId, nativeId, parentAssetId: parentAssetId);
        Context.Assets.Add(asset);
        await Context.SaveChangesAsync();
        return asset;
    }

    public async Task AddEdgeAsync(Guid fromId, string fromType, Guid toId, string toType, string relType)
    {
        Context.Edges.Add(OwnershipSeedData.BuildEdge(fromId, fromType, toId, toType, relType));
        await Context.SaveChangesAsync();
    }

    /// <summary>An existing owner from an earlier run — what a walk seeds from and what scopes filter on.</summary>
    public async Task AddPriorOwnerAsync(string entityType, Guid entityId, Guid identityId, bool isOverride = false)
    {
        Context.PartyAssignments.Add(new PartyAssignment
        {
            Id = Guid.NewGuid(), EntityType = entityType, EntityId = entityId, IdentityId = identityId,
            Rank = 1, IsActive = true, IsOverride = isOverride, VoteDistribution = "{}",
            ContributingRuleIds = [], PrecisionScoreSnapshot = "{}", RunId = "prior-run",
            AssignedAt = DateTimeOffset.UtcNow,
        });
        await Context.SaveChangesAsync();
    }

    /// <summary>Puts entities in a manifest's scope, which is what a manifest-scoped run filters on.</summary>
    public async Task MarkChangedInManifestAsync(Guid scanManifestId, string entityType, IEnumerable<Guid> entityIds)
    {
        Context.IngestChangeEvents.AddRange(entityIds.Select(id => new IngestChangeEvent
        {
            Id = Guid.NewGuid(), ScanManifestId = scanManifestId, ScanId = ScanId, EntityType = entityType,
            EntityId = id, ChangeType = "inserted", OccurredAt = DateTimeOffset.UtcNow,
        }));
        await Context.SaveChangesAsync();
    }
}
