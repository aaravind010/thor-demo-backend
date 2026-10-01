using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.Workflows.Abstractions;
using Xunit;

namespace Thor.Workflows.Atre.Tests.Fixtures;

/// <summary>
/// A real Postgres with the tenant schema on it, plus the rows every ATRE test needs before it can
/// insert an account (an account has a foreign key to <c>source</c> and to <c>account_type</c>, and a
/// manifest to a scan, so none of this is optional).
///
/// <para>Held as an <see cref="IClassFixture{T}"/>, so xUnit builds one per test class: the classes
/// get a container each and cannot see each other's rows, while the setup itself is written once.
/// Sharing a single container across classes would be faster and would also make one class's
/// leftovers visible to the next.</para>
///
/// <para>A real database rather than the in-memory provider because ATRE's writes are raw
/// <c>INSERT ... ON CONFLICT DO NOTHING</c> and its partitioning is a <c>hashtextextended</c>
/// predicate — none of which any provider but Npgsql can execute.</para>
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

        Context.AccountTypes.AddRange(AtreSeedData.BuildAccountTypes());
        Context.AccountTypeRules.AddRange(AtreSeedData.BuildRules());

        var authMethod = new AuthenticationMethod { Id = Guid.NewGuid(), TypeId = Guid.NewGuid(), Name = "test-auth" };
        var scanConfig = new ScanConfig
        {
            Id = Guid.NewGuid(),
            Name = "test-scan-config",
            AuthMethodId = authMethod.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "test",
            UpdatedBy = "test",
        };
        var scan = new Scan { Id = Guid.NewGuid(), ScanConfigId = scanConfig.Id, ScanType = "initial", Status = "running" };
        var source = new Source
        {
            Id = Guid.NewGuid(),
            ConnectorType = 308,
            Name = "test-source",
            Config = "{}",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
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
    /// A fresh context on its own physical connection. Production gives each ATRE step a separate
    /// read and write context, so a test that asserts on flush or streaming behaviour has to do the
    /// same or it is testing EF's change tracker instead.
    /// </summary>
    public TenantDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(_container.GetConnectionString()).Options);

    public async Task<Guid> SeedManifestAsync()
    {
        var manifest = new ScanManifest
        {
            Id = Guid.NewGuid(),
            ScanId = ScanId,
            FileLocations = [],
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        Context.ScanManifests.Add(manifest);
        await Context.SaveChangesAsync();
        return manifest.Id;
    }

    /// <summary>The ingestion <c>workflow</c> row ATRE links itself to as a child. Absent unless a test asks for it.</summary>
    public async Task<WorkflowEntity> SeedIngestionWorkflowAsync(Guid scanManifestId)
    {
        var workflow = new WorkflowEntity
        {
            Id = Guid.NewGuid(),
            ScanId = ScanId,
            ScanManifestId = scanManifestId,
            WorkflowType = WorkflowTypes.Ingestion,
            Trigger = WorkflowTriggers.StepFunctions,
            Status = "completed",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        };
        Context.Workflows.Add(workflow);
        await Context.SaveChangesAsync();
        return workflow;
    }

    public async Task<List<Account>> SeedAccountsAsync(params (string NativeId, string AccountKind)[] accounts)
    {
        var rows = accounts.Select(a => AtreSeedData.BuildAccount(SourceId, a.NativeId, a.AccountKind)).ToList();
        Context.Accounts.AddRange(rows);
        await Context.SaveChangesAsync();
        return rows;
    }

    /// <summary>Puts accounts in a manifest's scope, which is what the manifest-scoped stream filters on.</summary>
    public async Task MarkChangedInManifestAsync(Guid scanManifestId, IEnumerable<Account> accounts)
    {
        Context.IngestChangeEvents.AddRange(accounts.Select(account => new IngestChangeEvent
        {
            Id = Guid.NewGuid(),
            ScanManifestId = scanManifestId,
            ScanId = ScanId,
            EntityType = "account",
            EntityId = account.Id,
            ChangeType = "inserted",
            OccurredAt = DateTimeOffset.UtcNow,
        }));
        await Context.SaveChangesAsync();
    }
}
