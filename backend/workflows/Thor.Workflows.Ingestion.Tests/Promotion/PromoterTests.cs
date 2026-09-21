using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Ingestion.Promotion;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Promotion;

/// <summary>
/// Integration test against a real Postgres (via Testcontainers), with schema created via
/// <c>EnsureCreatedAsync</c> (migrations aren't tracked in this repo) plus the "Unclassified"
/// AccountType row Promoter depends on, seeded directly since <c>EnsureCreatedAsync</c> only
/// builds schema from the model, not data. Requires Docker running locally.
/// </summary>
public sealed class PromoterTests : IAsyncLifetime
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

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        _context = new TenantDbContext(options);
        await _context.Database.EnsureCreatedAsync();
        await SeedUnclassifiedAccountTypeAsync();

        (_sourceId, _scanId) = await SeedScanChainAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>
    /// The account_type row promotion's raw-SQL insert points new accounts at by default (see
    /// <see cref="WellKnownAccountTypes.Unclassified"/>) — normally seeded by migration, but
    /// migrations aren't tracked in this repo, so <c>EnsureCreatedAsync</c> won't create it.
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

    /// <summary>Minimal FK chain a Scan/Source/Account row needs: AuthenticationMethod -> ScanConfig -> Scan, and a standalone Source.</summary>
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

    private StagingAccount BuildStagingAccount(Guid scanManifestId, string nativeId, string contentHash, int batchSeq = 0, DateTimeOffset? receivedAt = null) => new()
    {
        ScanManifestId = scanManifestId,
        BatchSeq = batchSeq,
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
        ReceivedAt = receivedAt ?? DateTimeOffset.UtcNow,
    };

    private async Task InsertStagingAccountAsync(StagingAccount row)
    {
        var repo = new StagingAccountRepository(_context);
        await repo.BulkInsertAsync([row]);
    }

    private StagingGrp BuildStagingGrp(Guid scanManifestId, string nativeId, string contentHash, int batchSeq = 0, DateTimeOffset? receivedAt = null) => new()
    {
        ScanManifestId = scanManifestId,
        BatchSeq = batchSeq,
        TenantId = Guid.NewGuid(),
        SourceId = _sourceId,
        ConnectorType = 308,
        NativeId = nativeId,
        GroupClass = "security_global",
        DisplayName = "Test Group",
        Email = "",
        DomainName = "test.com",
        IsLargeGroup = false,
        IsDeleted = false,
        RawAttributes = """{"AliasKeys":["cn=tgrp,dc=test"],"EdgeRefs":[]}""",
        ContentHash = contentHash,
        ReceivedAt = receivedAt ?? DateTimeOffset.UtcNow,
    };

    private async Task InsertStagingGrpAsync(StagingGrp row)
    {
        var repo = new StagingGrpRepository(_context);
        await repo.BulkInsertAsync([row]);
    }

    [Fact]
    public async Task PromoteAccountsAsync_FirstInsert_CreatesCanonicalRowAndChangeEventAndAlias()
    {
        var scanManifestId = await SeedScanManifestAsync();
        await InsertStagingAccountAsync(BuildStagingAccount(scanManifestId, "guid-1", "hash-v1"));

        var promoter = new Promoter(new EntityAliasRepository(_context), NullLogger<Promoter>.Instance);
        var result = await promoter.PromoteAccountsAsync(_context, scanManifestId, _scanId);

        Assert.Equal(1, result.Inserted);
        Assert.Equal(0, result.Updated);

        var account = await _context.Accounts.SingleAsync(a => a.NativeId == "guid-1");
        Assert.Equal("hash-v1", account.ContentHash);

        var changeEvents = await _context.IngestChangeEvents.Where(e => e.EntityId == account.Id).ToListAsync();
        Assert.Single(changeEvents);
        Assert.Equal("inserted", changeEvents[0].ChangeType);
        Assert.Equal("account", changeEvents[0].EntityType);

        var aliases = await _context.EntityAliases.Where(a => a.EntityId == account.Id).ToListAsync();
        Assert.Single(aliases);
        Assert.Equal("cn=tuser,dc=test", aliases[0].AliasKey);
    }

    [Fact]
    public async Task PromoteAccountsAsync_EmptiesStagingTableForThatManifest()
    {
        var scanManifestId = await SeedScanManifestAsync();
        await InsertStagingAccountAsync(BuildStagingAccount(scanManifestId, "guid-2", "hash-v1"));

        var promoter = new Promoter(new EntityAliasRepository(_context), NullLogger<Promoter>.Instance);
        await promoter.PromoteAccountsAsync(_context, scanManifestId, _scanId);

        var remaining = await _context.StagingAccounts.Where(a => a.ScanManifestId == scanManifestId).ToListAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task PromoteAccountsAsync_SameContentHashOnRePromote_SkipsWithNoChangeEvent()
    {
        var firstManifestId = await SeedScanManifestAsync();
        await InsertStagingAccountAsync(BuildStagingAccount(firstManifestId, "guid-3", "hash-v1"));
        var promoter = new Promoter(new EntityAliasRepository(_context), NullLogger<Promoter>.Instance);
        await promoter.PromoteAccountsAsync(_context, firstManifestId, _scanId);

        var secondManifestId = await SeedScanManifestAsync();
        await InsertStagingAccountAsync(BuildStagingAccount(secondManifestId, "guid-3", "hash-v1"));
        var result = await promoter.PromoteAccountsAsync(_context, secondManifestId, _scanId);

        Assert.Equal(0, result.Inserted);
        Assert.Equal(0, result.Updated);

        var account = await _context.Accounts.SingleAsync(a => a.NativeId == "guid-3");
        var changeEvents = await _context.IngestChangeEvents.Where(e => e.EntityId == account.Id).ToListAsync();
        Assert.Single(changeEvents); // only from the first promotion
    }

    [Fact]
    public async Task PromoteAccountsAsync_ChangedContentHashOnRePromote_UpdatesCanonicalRowAndEmitsUpdateEvent()
    {
        var firstManifestId = await SeedScanManifestAsync();
        await InsertStagingAccountAsync(BuildStagingAccount(firstManifestId, "guid-4", "hash-v1"));
        var promoter = new Promoter(new EntityAliasRepository(_context), NullLogger<Promoter>.Instance);
        await promoter.PromoteAccountsAsync(_context, firstManifestId, _scanId);

        var secondManifestId = await SeedScanManifestAsync();
        var changed = BuildStagingAccount(secondManifestId, "guid-4", "hash-v2");
        changed.Email = "changed@test.com";
        await InsertStagingAccountAsync(changed);
        var result = await promoter.PromoteAccountsAsync(_context, secondManifestId, _scanId);

        Assert.Equal(0, result.Inserted);
        Assert.Equal(1, result.Updated);

        var account = await _context.Accounts.SingleAsync(a => a.NativeId == "guid-4");
        Assert.Equal("hash-v2", account.ContentHash);
        Assert.Equal("changed@test.com", account.Email);

        var changeEvents = await _context.IngestChangeEvents.Where(e => e.EntityId == account.Id).ToListAsync();
        Assert.Equal(2, changeEvents.Count);
        Assert.Contains(changeEvents, e => e.ChangeType == "updated");
    }

    [Fact]
    public async Task PromoteAccountsAsync_DuplicateNativeIdInSameManifest_LastReceivedWins()
    {
        var scanManifestId = await SeedScanManifestAsync();
        var earlier = BuildStagingAccount(scanManifestId, "guid-5", "hash-old", batchSeq: 0, receivedAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var later = BuildStagingAccount(scanManifestId, "guid-5", "hash-new", batchSeq: 1, receivedAt: DateTimeOffset.UtcNow);
        later.Email = "later@test.com";
        await InsertStagingAccountAsync(earlier);
        await InsertStagingAccountAsync(later);

        var promoter = new Promoter(new EntityAliasRepository(_context), NullLogger<Promoter>.Instance);
        var result = await promoter.PromoteAccountsAsync(_context, scanManifestId, _scanId);

        Assert.Equal(1, result.Inserted);
        var account = await _context.Accounts.SingleAsync(a => a.NativeId == "guid-5");
        Assert.Equal("hash-new", account.ContentHash);
        Assert.Equal("later@test.com", account.Email);
    }

    [Fact]
    public async Task PromoteAccountsAsync_NothingStaged_ReturnsZeroWithoutThrowing()
    {
        var scanManifestId = await SeedScanManifestAsync(); // no staging rows inserted for it

        var promoter = new Promoter(new EntityAliasRepository(_context), NullLogger<Promoter>.Instance);
        var result = await promoter.PromoteAccountsAsync(_context, scanManifestId, _scanId);

        Assert.Equal(0, result.Inserted);
        Assert.Equal(0, result.Updated);
    }

    [Fact]
    public async Task PromoteAccountsAsync_OnContentChange_ReplacesEntityAliasesWithNewKeys()
    {
        var firstManifestId = await SeedScanManifestAsync();
        await InsertStagingAccountAsync(BuildStagingAccount(firstManifestId, "guid-6", "hash-v1"));
        var promoter = new Promoter(new EntityAliasRepository(_context), NullLogger<Promoter>.Instance);
        await promoter.PromoteAccountsAsync(_context, firstManifestId, _scanId);

        var secondManifestId = await SeedScanManifestAsync();
        var changed = BuildStagingAccount(secondManifestId, "guid-6", "hash-v2");
        changed.RawAttributes = """{"AliasKeys":["cn=tuser-renamed,dc=test"],"EdgeRefs":[]}""";
        await InsertStagingAccountAsync(changed);
        await promoter.PromoteAccountsAsync(_context, secondManifestId, _scanId);

        var account = await _context.Accounts.SingleAsync(a => a.NativeId == "guid-6");
        var aliases = await _context.EntityAliases.Where(a => a.EntityId == account.Id).ToListAsync();
        Assert.Single(aliases); // old alias key replaced, not appended alongside the new one
        Assert.Equal("cn=tuser-renamed,dc=test", aliases[0].AliasKey);
    }

    private StagingAsset BuildStagingAsset(Guid scanManifestId, string nativeId, string contentHash, int batchSeq = 0, DateTimeOffset? receivedAt = null) => new()
    {
        ScanManifestId = scanManifestId,
        BatchSeq = batchSeq,
        SourceId = _sourceId,
        ConnectorType = 401,
        NativeId = nativeId,
        AssetType = "cyberark_safe",
        DisplayName = "Test Safe",
        FullPath = "Test Safe",
        FilerName = "",
        FileSize = 0,
        FileCount = 0,
        BrokenAcl = false,
        IsProtected = false,
        RawAttributes = """{"AliasKeys":["7"],"EdgeRefs":[]}""",
        ContentHash = contentHash,
        ReceivedAt = receivedAt ?? DateTimeOffset.UtcNow,
    };

    private async Task InsertStagingAssetAsync(StagingAsset row)
    {
        var repo = new StagingAssetRepository(_context);
        await repo.BulkInsertAsync([row]);
    }

    [Fact]
    public async Task PromoteAssetsAsync_FirstInsert_CreatesCanonicalRowAndChangeEventAndAlias()
    {
        var scanManifestId = await SeedScanManifestAsync();
        await InsertStagingAssetAsync(BuildStagingAsset(scanManifestId, "7", "hash-v1"));

        var promoter = new Promoter(new EntityAliasRepository(_context), NullLogger<Promoter>.Instance);
        var result = await promoter.PromoteAssetsAsync(_context, scanManifestId, _scanId);

        Assert.Equal(1, result.Inserted);
        Assert.Equal(0, result.Updated);

        var asset = await _context.Assets.SingleAsync(a => a.NativeId == "7");
        Assert.Equal("hash-v1", asset.ContentHash);
        Assert.Equal("cyberark_safe", asset.AssetType);
        Assert.Null(asset.ParentAssetId);

        var changeEvents = await _context.IngestChangeEvents.Where(e => e.EntityId == asset.Id).ToListAsync();
        Assert.Single(changeEvents);
        Assert.Equal("inserted", changeEvents[0].ChangeType);
        Assert.Equal("asset", changeEvents[0].EntityType);

        var aliases = await _context.EntityAliases.Where(a => a.EntityId == asset.Id).ToListAsync();
        Assert.Single(aliases);
        Assert.Equal("7", aliases[0].AliasKey);

        var remainingStaged = await _context.StagingAssets.Where(a => a.ScanManifestId == scanManifestId).ToListAsync();
        Assert.Empty(remainingStaged);
    }

    [Fact]
    public async Task PromoteAssetsAsync_SameContentHashOnRePromote_SkipsWithNoChangeEvent()
    {
        var firstManifestId = await SeedScanManifestAsync();
        await InsertStagingAssetAsync(BuildStagingAsset(firstManifestId, "8", "hash-v1"));
        var promoter = new Promoter(new EntityAliasRepository(_context), NullLogger<Promoter>.Instance);
        await promoter.PromoteAssetsAsync(_context, firstManifestId, _scanId);

        var secondManifestId = await SeedScanManifestAsync();
        await InsertStagingAssetAsync(BuildStagingAsset(secondManifestId, "8", "hash-v1"));
        var result = await promoter.PromoteAssetsAsync(_context, secondManifestId, _scanId);

        Assert.Equal(0, result.Inserted);
        Assert.Equal(0, result.Updated);

        var asset = await _context.Assets.SingleAsync(a => a.NativeId == "8");
        var changeEvents = await _context.IngestChangeEvents.Where(e => e.EntityId == asset.Id).ToListAsync();
        Assert.Single(changeEvents); // only from the first promotion
    }

    [Fact]
    public async Task PromoteAssetsAsync_ChangedContentHashOnRePromote_UpdatesCanonicalRowAndEmitsUpdateEvent()
    {
        var firstManifestId = await SeedScanManifestAsync();
        await InsertStagingAssetAsync(BuildStagingAsset(firstManifestId, "9", "hash-v1"));
        var promoter = new Promoter(new EntityAliasRepository(_context), NullLogger<Promoter>.Instance);
        await promoter.PromoteAssetsAsync(_context, firstManifestId, _scanId);

        var secondManifestId = await SeedScanManifestAsync();
        var changed = BuildStagingAsset(secondManifestId, "9", "hash-v2");
        changed.DisplayName = "Renamed Safe";
        await InsertStagingAssetAsync(changed);
        var result = await promoter.PromoteAssetsAsync(_context, secondManifestId, _scanId);

        Assert.Equal(0, result.Inserted);
        Assert.Equal(1, result.Updated);

        var asset = await _context.Assets.SingleAsync(a => a.NativeId == "9");
        Assert.Equal("hash-v2", asset.ContentHash);
        Assert.Equal("Renamed Safe", asset.DisplayName);
    }

    private StagingEntitlement BuildStagingEntitlement(Guid scanManifestId, string nativeId, string contentHash, int batchSeq = 0, DateTimeOffset? receivedAt = null) => new()
    {
        ScanManifestId = scanManifestId,
        BatchSeq = batchSeq,
        SourceId = _sourceId,
        ConnectorType = 401,
        NativeId = nativeId,
        EntitlementType = "SafeMembership",
        Name = "test-member",
        Description = "",
        IsAdmin = false,
        Scope = "TestSafe",
        InstanceName = "",
        SudoPath = "",
        SudoHost = "",
        RawAttributes = "{}",
        ContentHash = contentHash,
        ReceivedAt = receivedAt ?? DateTimeOffset.UtcNow,
    };

    private async Task InsertStagingEntitlementAsync(StagingEntitlement row)
    {
        var repo = new StagingEntitlementRepository(_context);
        await repo.BulkInsertAsync([row]);
    }

    [Fact]
    public async Task PromoteEntitlementsAsync_FirstInsert_CreatesCanonicalRowAndChangeEvent()
    {
        var scanManifestId = await SeedScanManifestAsync();
        await InsertStagingEntitlementAsync(BuildStagingEntitlement(scanManifestId, "1:13", "hash-v1"));

        var promoter = new Promoter(new EntityAliasRepository(_context), NullLogger<Promoter>.Instance);
        var result = await promoter.PromoteEntitlementsAsync(_context, scanManifestId, _scanId);

        Assert.Equal(1, result.Inserted);
        Assert.Equal(0, result.Updated);

        var entitlement = await _context.Entitlements.SingleAsync(e => e.NativeId == "1:13");
        Assert.Equal("hash-v1", entitlement.ContentHash);
        Assert.Equal("TestSafe", entitlement.Scope);

        var changeEvents = await _context.IngestChangeEvents.Where(e => e.EntityId == entitlement.Id).ToListAsync();
        Assert.Single(changeEvents);
        Assert.Equal("inserted", changeEvents[0].ChangeType);
        Assert.Equal("entitlement", changeEvents[0].EntityType);

        var remainingStaged = await _context.StagingEntitlements.Where(e => e.ScanManifestId == scanManifestId).ToListAsync();
        Assert.Empty(remainingStaged);
    }

    [Fact]
    public async Task PromoteEntitlementsAsync_SameContentHashOnRePromote_SkipsWithNoChangeEvent()
    {
        var firstManifestId = await SeedScanManifestAsync();
        await InsertStagingEntitlementAsync(BuildStagingEntitlement(firstManifestId, "1:14", "hash-v1"));
        var promoter = new Promoter(new EntityAliasRepository(_context), NullLogger<Promoter>.Instance);
        await promoter.PromoteEntitlementsAsync(_context, firstManifestId, _scanId);

        var secondManifestId = await SeedScanManifestAsync();
        await InsertStagingEntitlementAsync(BuildStagingEntitlement(secondManifestId, "1:14", "hash-v1"));
        var result = await promoter.PromoteEntitlementsAsync(_context, secondManifestId, _scanId);

        Assert.Equal(0, result.Inserted);
        Assert.Equal(0, result.Updated);

        var entitlement = await _context.Entitlements.SingleAsync(e => e.NativeId == "1:14");
        var changeEvents = await _context.IngestChangeEvents.Where(e => e.EntityId == entitlement.Id).ToListAsync();
        Assert.Single(changeEvents); // only from the first promotion
    }

    [Fact]
    public async Task PromoteEntitlementsAsync_ChangedContentHashOnRePromote_UpdatesCanonicalRowAndEmitsUpdateEvent()
    {
        var firstManifestId = await SeedScanManifestAsync();
        await InsertStagingEntitlementAsync(BuildStagingEntitlement(firstManifestId, "1:15", "hash-v1"));
        var promoter = new Promoter(new EntityAliasRepository(_context), NullLogger<Promoter>.Instance);
        await promoter.PromoteEntitlementsAsync(_context, firstManifestId, _scanId);

        var secondManifestId = await SeedScanManifestAsync();
        var changed = BuildStagingEntitlement(secondManifestId, "1:15", "hash-v2");
        changed.IsAdmin = true;
        await InsertStagingEntitlementAsync(changed);
        var result = await promoter.PromoteEntitlementsAsync(_context, secondManifestId, _scanId);

        Assert.Equal(0, result.Inserted);
        Assert.Equal(1, result.Updated);

        var entitlement = await _context.Entitlements.SingleAsync(e => e.NativeId == "1:15");
        Assert.Equal("hash-v2", entitlement.ContentHash);
        Assert.True(entitlement.IsAdmin);
    }

    [Fact]
    public async Task PromoteGroupsAsync_FirstInsert_CreatesCanonicalRowAndChangeEventAndAlias()
    {
        var scanManifestId = await SeedScanManifestAsync();
        await InsertStagingGrpAsync(BuildStagingGrp(scanManifestId, "grp-1", "hash-v1"));

        var promoter = new Promoter(new EntityAliasRepository(_context), NullLogger<Promoter>.Instance);
        var result = await promoter.PromoteGroupsAsync(_context, scanManifestId, _scanId);

        Assert.Equal(1, result.Inserted);
        Assert.Equal(0, result.Updated);

        var grp = await _context.Grps.SingleAsync(g => g.NativeId == "grp-1");
        Assert.Equal("hash-v1", grp.ContentHash);

        var changeEvents = await _context.IngestChangeEvents.Where(e => e.EntityId == grp.Id).ToListAsync();
        Assert.Single(changeEvents);
        Assert.Equal("inserted", changeEvents[0].ChangeType);
        Assert.Equal("grp", changeEvents[0].EntityType);

        var aliases = await _context.EntityAliases.Where(a => a.EntityId == grp.Id).ToListAsync();
        Assert.Single(aliases);
        Assert.Equal("cn=tgrp,dc=test", aliases[0].AliasKey);

        var remainingStaged = await _context.StagingGrps.Where(g => g.ScanManifestId == scanManifestId).ToListAsync();
        Assert.Empty(remainingStaged);
    }

    [Fact]
    public async Task PromoteGroupsAsync_SameContentHashOnRePromote_SkipsWithNoChangeEvent()
    {
        var firstManifestId = await SeedScanManifestAsync();
        await InsertStagingGrpAsync(BuildStagingGrp(firstManifestId, "grp-2", "hash-v1"));
        var promoter = new Promoter(new EntityAliasRepository(_context), NullLogger<Promoter>.Instance);
        await promoter.PromoteGroupsAsync(_context, firstManifestId, _scanId);

        var secondManifestId = await SeedScanManifestAsync();
        await InsertStagingGrpAsync(BuildStagingGrp(secondManifestId, "grp-2", "hash-v1"));
        var result = await promoter.PromoteGroupsAsync(_context, secondManifestId, _scanId);

        Assert.Equal(0, result.Inserted);
        Assert.Equal(0, result.Updated);

        var grp = await _context.Grps.SingleAsync(g => g.NativeId == "grp-2");
        var changeEvents = await _context.IngestChangeEvents.Where(e => e.EntityId == grp.Id).ToListAsync();
        Assert.Single(changeEvents); // only from the first promotion
    }
}
