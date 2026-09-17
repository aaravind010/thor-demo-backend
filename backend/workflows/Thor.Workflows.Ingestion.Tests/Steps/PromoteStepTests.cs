using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Ingestion.Steps;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Steps;

/// <summary>
/// Exercises <see cref="PromoteStep"/> — CDC promotion split out of the former monolithic
/// pipeline — against a real Postgres (Testcontainers). Reads only the staging rows a prior
/// <see cref="ExtractAndStageStep"/> run would have written for the same manifest.
/// </summary>
public sealed class PromoteStepTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private TenantDbContext _context = null!;
    private Guid _sourceId;
    private Guid _scanId;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _context = NewContext();
        await _context.Database.EnsureCreatedAsync();
        await SeedUnclassifiedAccountTypeAsync();

        (_sourceId, _scanId) = await SeedScanChainAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private TenantDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new TenantDbContext(options);
    }

    /// <summary>
    /// Migrations aren't tracked in this repo, so <c>EnsureCreatedAsync</c> (schema-only) won't
    /// seed the "Unclassified" <see cref="AccountType"/> row account.account_type_id requires.
    /// </summary>
    private async Task SeedUnclassifiedAccountTypeAsync()
    {
        _context.AccountTypes.Add(new AccountType
        {
            Id = WellKnownAccountTypes.Unclassified,
            Name = "Unclassified",
            Description = "Default account type for newly-promoted accounts pending classification.",
            IsHuman = false,
        });
        await _context.SaveChangesAsync();
    }

    private async Task<(Guid SourceId, Guid ScanId)> SeedScanChainAsync()
    {
        var authMethod = new AuthenticationMethod { Id = Guid.NewGuid(), TypeId = Guid.NewGuid(), Name = "test-auth" };
        _context.AuthenticationMethods.Add(authMethod);

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
        _context.ScanConfigs.Add(scanConfig);

        var scan = new Scan { Id = Guid.NewGuid(), ScanConfigId = scanConfig.Id, ScanType = "initial", Status = "running" };
        _context.Scans.Add(scan);

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
        _context.Sources.Add(source);

        await _context.SaveChangesAsync();
        return (source.Id, scan.Id);
    }

    private async Task<Guid> SeedScanManifestAsync()
    {
        var manifest = new ScanManifest
        {
            Id = Guid.NewGuid(),
            ScanId = _scanId,
            FileLocations = [],
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _context.ScanManifests.Add(manifest);
        await _context.SaveChangesAsync();
        return manifest.Id;
    }

    private StagingAccount BuildStagingAccount(Guid scanManifestId, string nativeId, string contentHash) => new()
    {
        ScanManifestId = scanManifestId,
        BatchSeq = 0,
        SourceId = _sourceId,
        ConnectorType = 308,
        NativeId = nativeId,
        AccountKind = "user",
        IsHuman = true,
        DisplayName = "Test User",
        SamAccountName = "tuser",
        Upn = "tuser@test.com",
        Email = "tuser@test.com",
        DomainName = "test.com",
        FilerName = "",
        NativeAccountId = "S-1-5-1",
        IsDeleted = false,
        IsDisabled = false,
        RawAttributes = """{"AliasKeys":["cn=tuser,dc=test"],"EdgeRefs":[]}""",
        ContentHash = contentHash,
        ReceivedAt = DateTimeOffset.UtcNow,
    };

    private async Task InsertStagingAccountAsync(StagingAccount row)
    {
        var repo = new StagingAccountRepository(_context);
        await repo.BulkInsertAsync([row]);
    }

    private sealed class FakeTenantConnectionManager(Func<Guid, TenantDbContext> factory) : ITenantConnectionManager
    {
        public Task<Npgsql.NpgsqlConnection> GetValidatedConnectionAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TenantDbContext> GetTenantDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(factory(tenantId));
    }

    [Fact]
    public async Task RunAsync_StagedAccount_PromotesToCanonicalRowAndReturnsCounts()
    {
        var scanManifestId = await SeedScanManifestAsync();
        await InsertStagingAccountAsync(BuildStagingAccount(scanManifestId, "guid-1", "hash-1"));

        var step = new PromoteStep(NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => _context));
        var result = await step.RunAsync(_context, scanManifestId, _scanId);

        Assert.Equal(1, result.AccountsInserted);
        Assert.Equal(0, result.AccountsUpdated);
        Assert.Equal(0, result.GroupsInserted);

        var account = await _context.Accounts.AsNoTracking().SingleAsync(a => a.NativeId == "guid-1");
        Assert.Equal("tuser@test.com", account.Email);
    }

    [Fact]
    public async Task ExecuteAsync_ValidInputJson_PromotesStagedAccount()
    {
        var scanManifestId = await SeedScanManifestAsync();
        await InsertStagingAccountAsync(BuildStagingAccount(scanManifestId, "guid-2", "hash-2"));

        // ExecuteAsync disposes the context it resolves (`await using`) — matching production,
        // where a fresh TenantDbContext is built per invocation. Hand it its own context, not
        // the fixture's `_context`, which stays open for verification below.
        var runContext = NewContext();
        var step = new PromoteStep(NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => runContext));
        var request = new IngestionRequest(Guid.NewGuid(), "s3://bucket/export.zip", _scanId, scanManifestId);
        var inputJson = System.Text.Json.JsonSerializer.Serialize(request);

        await step.ExecuteAsync(inputJson, CancellationToken.None);

        var account = await _context.Accounts.AsNoTracking().SingleAsync(a => a.NativeId == "guid-2");
        Assert.Equal("tuser@test.com", account.Email);
    }

    [Fact]
    public async Task RunAsync_NothingStaged_ReturnsZeroCountsWithoutThrowing()
    {
        var scanManifestId = await SeedScanManifestAsync();

        var step = new PromoteStep(NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => _context));
        var result = await step.RunAsync(_context, scanManifestId, _scanId);

        Assert.Equal(0, result.AccountsInserted);
        Assert.Equal(0, result.GroupsInserted);
    }
}
