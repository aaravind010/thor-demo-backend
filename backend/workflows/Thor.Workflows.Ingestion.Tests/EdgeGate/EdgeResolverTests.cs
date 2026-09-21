using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.Workflows.Ingestion.Constants;
using Thor.Workflows.Ingestion.EdgeGate;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.EdgeGate;

/// <summary>
/// Integration test against a real Postgres (via Testcontainers). Seeds canonical
/// account/grp/entity_alias rows directly (bypassing Stager/Promoter) to isolate
/// EdgeResolver's own join/heal/removal logic. Requires Docker running locally.
/// </summary>
public sealed class EdgeResolverTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private TenantDbContext _context = null!;
    private Guid _sourceId;
    private Guid _scanId;
    private EdgeResolver _resolver = null!;

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

        _resolver = new EdgeResolver(new OneSidedRefIndex(), NullLogger<EdgeResolver>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
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
            ConnectorType = ConnectorTypes.ActiveDirectory,
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

    private static string RefJson(string rel, string dir, string key, string? targetType) =>
        $$"""{"Rel":"{{rel}}","Dir":"{{dir}}","Key":"{{key}}","TargetType":{{(targetType is null ? "null" : $"\"{targetType}\"")}}}""";

    private static string RawAttributesJson(string aliasKey, params (string Rel, string Dir, string Key, string? TargetType)[] edgeRefs)
    {
        var refsJson = string.Join(",", edgeRefs.Select(r => RefJson(r.Rel, r.Dir, r.Key, r.TargetType)));
        return $$"""{"AliasKeys":["{{aliasKey}}"],"EdgeRefs":[{{refsJson}}]}""";
    }

    /// <summary>
    /// Seeds one <c>edge_ref_delta</c> row — what a real <c>EdgeRefDeltaComputer</c> run would
    /// have produced for this owner's manifest. Needed by every incremental test below: this
    /// class seeds canonical rows directly (bypassing Stager/Promoter, per this file's own
    /// docblock), so it's the only way to simulate an added/removed ref for B7's delta-driven
    /// resolve — <c>ingest_change_event</c> alone is no longer enough.
    /// </summary>
    private async Task SeedEdgeRefDeltaAsync(Guid scanManifestId, string nativeId, string entityType, string op, string refJson)
    {
        _context.EdgeRefDeltas.Add(new EdgeRefDelta
        {
            Id = Guid.NewGuid(), ScanManifestId = scanManifestId, SourceId = _sourceId,
            NativeId = nativeId, EntityType = entityType, Op = op, Ref = refJson,
        });
        await _context.SaveChangesAsync();
    }

    private async Task<Guid> InsertAccountAsync(string nativeId, string aliasKey, params (string Rel, string Dir, string Key, string? TargetType)[] edgeRefs)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _context.Accounts.Add(new Account
        {
            Id = id,
            SourceId = _sourceId,
            ConnectorType = ConnectorTypes.ActiveDirectory,
            NativeId = nativeId,
            AccountKind = "user",
            IsHuman = true,
            DisplayName = nativeId,
            SamAccountName = nativeId,
            Upn = $"{nativeId}@test.com",
            Email = $"{nativeId}@test.com",
            DomainName = "test.com",
            FilerName = "",
            NativeAccountId = "S-1-5-1",
            IsDeleted = false,
            IsDisabled = false,
            AccountTypeId = WellKnownAccountTypes.Unclassified,
            RawAttributes = RawAttributesJson(aliasKey, edgeRefs),
            ContentHash = "hash-" + nativeId,
            HashVersion = 0,
            CreatedAt = now,
            UpdatedAt = now,
        });
        _context.EntityAliases.Add(new EntityAlias
        {
            Id = Guid.NewGuid(), EntityType = "account", EntityId = id, SourceId = _sourceId,
            ConnectorType = ConnectorTypes.ActiveDirectory, AliasKey = aliasKey, CreatedAt = now,
        });
        await _context.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> InsertGroupAsync(string nativeId, string aliasKey, params (string Rel, string Dir, string Key, string? TargetType)[] edgeRefs)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _context.Grps.Add(new Grp
        {
            Id = id,
            SourceId = _sourceId,
            ConnectorType = ConnectorTypes.ActiveDirectory,
            NativeId = nativeId,
            GroupClass = "security_global",
            DisplayName = nativeId,
            Email = null,
            DomainName = "test.com",
            IsLargeGroup = false,
            IsDeleted = false,
            RawAttributes = RawAttributesJson(aliasKey, edgeRefs),
            ContentHash = "hash-" + nativeId,
            HashVersion = 0,
            CreatedAt = now,
            UpdatedAt = now,
        });
        _context.EntityAliases.Add(new EntityAlias
        {
            Id = Guid.NewGuid(), EntityType = "grp", EntityId = id, SourceId = _sourceId,
            ConnectorType = ConnectorTypes.ActiveDirectory, AliasKey = aliasKey, CreatedAt = now,
        });
        await _context.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task ResolveFullAsync_EdgeRefWithProps_PropsFlowToResolvedEdgeRow()
    {
        await InsertGroupAsync("grp-props", "cn=grp-props,dc=test");

        var accountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var rawAttributes = """{"AliasKeys":["cn=user-props,dc=test"],"EdgeRefs":[{"Rel":"HAS_ACCESS","Dir":"out","Key":"cn=grp-props,dc=test","TargetType":"grp","Props":"{\"is_admin\":true}"}]}""";
        _context.Accounts.Add(new Account
        {
            Id = accountId,
            SourceId = _sourceId,
            ConnectorType = ConnectorTypes.ActiveDirectory,
            NativeId = "user-props",
            AccountKind = "user",
            IsHuman = true,
            DisplayName = "user-props",
            SamAccountName = "user-props",
            Upn = "user-props@test.com",
            Email = "user-props@test.com",
            DomainName = "test.com",
            FilerName = "",
            NativeAccountId = "S-1-5-1",
            IsDeleted = false,
            IsDisabled = false,
            AccountTypeId = WellKnownAccountTypes.Unclassified,
            RawAttributes = rawAttributes,
            ContentHash = "hash-user-props",
            HashVersion = 0,
            CreatedAt = now,
            UpdatedAt = now,
        });
        _context.EntityAliases.Add(new EntityAlias
        {
            Id = Guid.NewGuid(), EntityType = "account", EntityId = accountId, SourceId = _sourceId,
            ConnectorType = ConnectorTypes.ActiveDirectory, AliasKey = "cn=user-props,dc=test", CreatedAt = now,
        });
        await _context.SaveChangesAsync();

        await _resolver.ResolveFullAsync(_context, _scanId, await CreateManifestAsync(), RelTypes.TwoSided);

        var edge = await _context.Edges.SingleAsync(e => e.RelType == "HAS_ACCESS");
        Assert.Equal("""{"is_admin":true}""", edge.Props);
    }

    [Fact]
    public async Task ResolveFullAsync_TwoSidedMemberOf_CreatesOneDedupedEdge()
    {
        var groupId = await InsertGroupAsync("grp-1", "cn=grp-1,dc=test");
        await InsertAccountAsync("user-1", "cn=user-1,dc=test",
            ("MEMBER_OF", "out", "cn=grp-1,dc=test", "grp"));

        var result = await _resolver.ResolveFullAsync(_context, _scanId, await CreateManifestAsync(), RelTypes.TwoSided);

        Assert.Equal(1, result.Inserted);
        Assert.Equal(0, result.Updated);

        var edges = await _context.Edges.Where(e => e.RelType == "MEMBER_OF").ToListAsync();
        Assert.Single(edges);
        Assert.Equal("account", edges[0].FromType);
        Assert.Equal("grp", edges[0].ToType);
        Assert.Equal(groupId, edges[0].ToId);
    }

    [Fact]
    public async Task ResolveFullAsync_RerunWithNoChanges_IsIdempotent()
    {
        await InsertGroupAsync("grp-2", "cn=grp-2,dc=test");
        await InsertAccountAsync("user-2", "cn=user-2,dc=test",
            ("MEMBER_OF", "out", "cn=grp-2,dc=test", "grp"));

        var manifestId = await CreateManifestAsync();
        var first = await _resolver.ResolveFullAsync(_context, _scanId, manifestId, RelTypes.TwoSided);
        Assert.Equal(1, first.Inserted);

        var second = await _resolver.ResolveFullAsync(_context, _scanId, manifestId, RelTypes.TwoSided);
        Assert.Equal(0, second.Inserted);
        Assert.Equal(0, second.Updated);

        var edges = await _context.Edges.Where(e => e.RelType == "MEMBER_OF").ToListAsync();
        Assert.Single(edges); // no duplicate row
    }

    [Fact]
    public async Task ResolveIncrementalAsync_OneSidedRefHealsWhenTargetArrivesLater()
    {
        // "employee" arrives first, referencing a manager that doesn't exist yet — REPORTS_TO
        // can't resolve. Each call is scoped to its OWN manifest so the second round's "changed"
        // set contains only the manager — proving the edge is formed via one-sided-ref healing,
        // not because employee happens to still be in the changed set.
        var employeeId = await InsertAccountAsync("employee", "cn=employee,dc=test",
            ("REPORTS_TO", "out", "cn=manager,dc=test", "account"));
        var employeeManifestId = await CreateManifestAsync();
        _context.IngestChangeEvents.Add(new IngestChangeEvent
        {
            Id = Guid.NewGuid(), ScanManifestId = employeeManifestId, ScanId = _scanId,
            EntityType = "account", EntityId = employeeId, ChangeType = "inserted", OccurredAt = DateTimeOffset.UtcNow,
        });
        await _context.SaveChangesAsync();
        await SeedEdgeRefDeltaAsync(employeeManifestId, "employee", "account", "add",
            RefJson("REPORTS_TO", "out", "cn=manager,dc=test", "account"));

        var firstResult = await _resolver.ResolveIncrementalAsync(_context, _scanId, employeeManifestId, RelTypes.TwoSided);
        Assert.Equal(0, firstResult.Inserted); // manager doesn't exist yet — dangling, silently skipped

        // manager arrives in a later manifest; employee is not part of this manifest's changes.
        var managerId = await InsertAccountAsync("manager", "cn=manager,dc=test");
        var managerManifestId = await CreateManifestAsync();
        _context.IngestChangeEvents.Add(new IngestChangeEvent
        {
            Id = Guid.NewGuid(), ScanManifestId = managerManifestId, ScanId = _scanId,
            EntityType = "account", EntityId = managerId, ChangeType = "inserted", OccurredAt = DateTimeOffset.UtcNow,
        });
        await _context.SaveChangesAsync();

        var secondResult = await _resolver.ResolveIncrementalAsync(_context, _scanId, managerManifestId, RelTypes.TwoSided);
        Assert.Equal(1, secondResult.Inserted); // healed via the one-sided ref index

        var edge = await _context.Edges.SingleAsync(e => e.RelType == "REPORTS_TO");
        Assert.Equal(employeeId, edge.FromId);
        Assert.Equal(managerId, edge.ToId);
    }

    [Fact]
    public async Task ResolveIncrementalAsync_RemovesEdgeOnlyWhenNeitherSideReproducesIt()
    {
        var groupId = await InsertGroupAsync("grp-3", "cn=grp-3,dc=test",
            ("MEMBER_OF", "in", "cn=user-3,dc=test", null));
        var accountId = await InsertAccountAsync("user-3", "cn=user-3,dc=test",
            ("MEMBER_OF", "out", "cn=grp-3,dc=test", "grp"));

        await _resolver.ResolveFullAsync(_context, _scanId, await CreateManifestAsync(), RelTypes.TwoSided);
        Assert.Single(await _context.Edges.Where(e => !e.IsDeleted).ToListAsync());

        // Account drops the membership, but the group's own member list still lists the account
        // (both-sided rel, only one side changed) — edge must survive.
        var account = await _context.Accounts.SingleAsync(a => a.Id == accountId);
        account.RawAttributes = RawAttributesJson("cn=user-3,dc=test"); // no more edge_refs
        var manifestId = await CreateManifestAsync();
        _context.IngestChangeEvents.Add(new IngestChangeEvent
        {
            Id = Guid.NewGuid(), ScanManifestId = manifestId, ScanId = _scanId,
            EntityType = "account", EntityId = accountId, ChangeType = "updated", OccurredAt = DateTimeOffset.UtcNow,
        });
        await _context.SaveChangesAsync();
        await SeedEdgeRefDeltaAsync(manifestId, "user-3", "account", "remove",
            RefJson("MEMBER_OF", "out", "cn=grp-3,dc=test", "grp"));

        var afterAccountChange = await _resolver.ResolveIncrementalAsync(_context, _scanId, manifestId, RelTypes.TwoSided);
        Assert.Equal(0, afterAccountChange.Removed);
        Assert.Single(await _context.Edges.Where(e => !e.IsDeleted).ToListAsync());

        // Now the group also drops the membership — neither side reproduces it, edge is removed.
        var group = await _context.Grps.SingleAsync(g => g.Id == groupId);
        group.RawAttributes = RawAttributesJson("cn=grp-3,dc=test"); // no more edge_refs
        var manifestId2 = await CreateManifestAsync();
        _context.IngestChangeEvents.Add(new IngestChangeEvent
        {
            Id = Guid.NewGuid(), ScanManifestId = manifestId2, ScanId = _scanId,
            EntityType = "grp", EntityId = groupId, ChangeType = "updated", OccurredAt = DateTimeOffset.UtcNow,
        });
        await _context.SaveChangesAsync();
        await SeedEdgeRefDeltaAsync(manifestId2, "grp-3", "grp", "remove",
            RefJson("MEMBER_OF", "in", "cn=user-3,dc=test", null));

        var afterGroupChange = await _resolver.ResolveIncrementalAsync(_context, _scanId, manifestId2, RelTypes.TwoSided);
        Assert.Equal(1, afterGroupChange.Removed);
        Assert.Empty(await _context.Edges.Where(e => !e.IsDeleted).ToListAsync());
    }

    private async Task<Guid> CreateManifestAsync()
    {
        var manifest = new ScanManifest
        {
            Id = Guid.NewGuid(), ScanId = _scanId, FileLocations = [], Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _context.ScanManifests.Add(manifest);
        await _context.SaveChangesAsync();
        return manifest.Id;
    }

    [Fact]
    public async Task ResolveFullAsync_EmptyGraph_ProducesNoEdgesAndNoError()
    {
        var result = await _resolver.ResolveFullAsync(_context, _scanId, Guid.NewGuid(), RelTypes.TwoSided);

        Assert.Equal(0, result.Inserted);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Removed);
        Assert.Empty(await _context.Edges.ToListAsync());
    }

    [Fact]
    public async Task ResolveIncrementalAsync_OnlyRecomputesChangedOwners_LeavesUnrelatedEdgeUntouched()
    {
        // Unrelated, stable pair — established on the initial full resolve, never touched again.
        await InsertGroupAsync("grp-unrelated", "cn=grp-unrelated,dc=test");
        await InsertAccountAsync("user-unrelated", "cn=user-unrelated,dc=test",
            ("MEMBER_OF", "out", "cn=grp-unrelated,dc=test", "grp"));
        await _resolver.ResolveFullAsync(_context, _scanId, await CreateManifestAsync(), RelTypes.TwoSided);
        var unrelatedEdgeId = (await _context.Edges.SingleAsync(e => e.RelType == "MEMBER_OF")).Id;
        var unrelatedChangeEventCountBefore = await _context.IngestChangeEvents.CountAsync(e => e.EntityId == unrelatedEdgeId);

        // A brand-new, unrelated pair changed in this manifest.
        var newGroupId = await InsertGroupAsync("grp-new", "cn=grp-new,dc=test");
        var newAccountId = await InsertAccountAsync("user-new", "cn=user-new,dc=test",
            ("MEMBER_OF", "out", "cn=grp-new,dc=test", "grp"));
        var manifestId = await CreateManifestAsync();
        _context.IngestChangeEvents.Add(new IngestChangeEvent
        {
            Id = Guid.NewGuid(), ScanManifestId = manifestId, ScanId = _scanId,
            EntityType = "account", EntityId = newAccountId, ChangeType = "inserted", OccurredAt = DateTimeOffset.UtcNow,
        });
        await _context.SaveChangesAsync();
        await SeedEdgeRefDeltaAsync(manifestId, "user-new", "account", "add",
            RefJson("MEMBER_OF", "out", "cn=grp-new,dc=test", "grp"));

        var result = await _resolver.ResolveIncrementalAsync(_context, _scanId, manifestId, RelTypes.TwoSided);

        Assert.Equal(1, result.Inserted); // only the new pair's edge
        Assert.Equal(0, result.Removed);

        var unrelatedEdge = await _context.Edges.SingleAsync(e => e.Id == unrelatedEdgeId);
        Assert.False(unrelatedEdge.IsDeleted);
        var unrelatedChangeEventCountAfter = await _context.IngestChangeEvents.CountAsync(e => e.EntityId == unrelatedEdgeId);
        Assert.Equal(unrelatedChangeEventCountBefore, unrelatedChangeEventCountAfter); // untouched, not re-processed

        var newEdge = await _context.Edges.SingleAsync(e => e.FromId == newAccountId);
        Assert.Equal(newGroupId, newEdge.ToId);
    }

    private async Task<Guid> InsertAssetAsync(string nativeId, string aliasKey, params (string Rel, string Dir, string Key, string? TargetType)[] edgeRefs)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _context.Assets.Add(new Asset
        {
            Id = id,
            SourceId = _sourceId,
            ConnectorType = ConnectorTypes.ActiveDirectory,
            NativeId = nativeId,
            AssetType = "folder",
            DisplayName = nativeId,
            FullPath = "/" + nativeId,
            FilerName = "filer",
            FileSize = 0,
            FileCount = 0,
            BrokenAcl = false,
            IsProtected = false,
            RawAttributes = RawAttributesJson(aliasKey, edgeRefs),
            ContentHash = "hash-" + nativeId,
            HashVersion = 0,
            CreatedAt = now,
            UpdatedAt = now,
        });
        _context.EntityAliases.Add(new EntityAlias
        {
            Id = Guid.NewGuid(), EntityType = "asset", EntityId = id, SourceId = _sourceId,
            ConnectorType = ConnectorTypes.ActiveDirectory, AliasKey = aliasKey, CreatedAt = now,
        });
        await _context.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> InsertEntitlementAsync(string nativeId, string aliasKey, params (string Rel, string Dir, string Key, string? TargetType)[] edgeRefs)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _context.Entitlements.Add(new Entitlement
        {
            Id = id,
            SourceId = _sourceId,
            ConnectorType = ConnectorTypes.ActiveDirectory,
            NativeId = nativeId,
            EntitlementType = "role",
            Name = nativeId,
            IsAdmin = false,
            RawAttributes = RawAttributesJson(aliasKey, edgeRefs),
            ContentHash = "hash-" + nativeId,
            HashVersion = 0,
            CreatedAt = now,
            UpdatedAt = now,
        });
        _context.EntityAliases.Add(new EntityAlias
        {
            Id = Guid.NewGuid(), EntityType = "entitlement", EntityId = id, SourceId = _sourceId,
            ConnectorType = ConnectorTypes.ActiveDirectory, AliasKey = aliasKey, CreatedAt = now,
        });
        await _context.SaveChangesAsync();
        return id;
    }

    /// <summary>
    /// No current connector populates edge_refs on an asset/entitlement row (CyberArk's
    /// Safe/Asset and entitlements are edge *targets* only today) — proves the 4-table owner scope
    /// (account/grp/asset/entitlement) actually resolves once one does, not just that it compiles.
    /// </summary>
    [Fact]
    public async Task ResolveFullAsync_AssetDeclaresOutboundEdgeRef_ResolvesAgainstAccount()
    {
        var accountId = await InsertAccountAsync("owner-acct", "cn=owner-acct,dc=test");
        var assetId = await InsertAssetAsync("asset-1", "asset-alias-1",
            ("OWNS", "out", "cn=owner-acct,dc=test", "account"));

        var result = await _resolver.ResolveFullAsync(_context, _scanId, await CreateManifestAsync(), RelTypes.TwoSided);

        Assert.Equal(1, result.Inserted);
        var edge = await _context.Edges.SingleAsync(e => e.RelType == "OWNS");
        Assert.Equal(assetId, edge.FromId);
        Assert.Equal("asset", edge.FromType);
        Assert.Equal(accountId, edge.ToId);
        Assert.Equal("account", edge.ToType);
    }

    [Fact]
    public async Task ResolveFullAsync_EntitlementDeclaresOutboundEdgeRef_ResolvesAgainstAccount()
    {
        var accountId = await InsertAccountAsync("grantee", "cn=grantee,dc=test");
        var entitlementId = await InsertEntitlementAsync("ent-1", "ent-alias-1",
            ("GRANTED_TO", "out", "cn=grantee,dc=test", "account"));

        var result = await _resolver.ResolveFullAsync(_context, _scanId, await CreateManifestAsync(), RelTypes.TwoSided);

        Assert.Equal(1, result.Inserted);
        var edge = await _context.Edges.SingleAsync(e => e.RelType == "GRANTED_TO");
        Assert.Equal(entitlementId, edge.FromId);
        Assert.Equal("entitlement", edge.FromType);
        Assert.Equal(accountId, edge.ToId);
    }

    /// <summary>
    /// Not a 9M-row POC-scale benchmark — just correctness evidence that an incremental resolve
    /// scoped to one changed pair stays correct (and doesn't spuriously touch anything) alongside
    /// hundreds of unrelated owners, consistent with the port's seed-scoped SQL replacing the old
    /// full-table-in-memory resolve (verifiable directly by reading EdgeResolver: it no longer
    /// loads every account/grp row per run).
    /// </summary>
    [Fact]
    public async Task ResolveIncrementalAsync_ManyUnrelatedOwners_OnlyResolvesChangedPair()
    {
        const int bulkCount = 500;
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < bulkCount; i++)
        {
            var nativeId = $"bulk-user-{i}";
            _context.Accounts.Add(new Account
            {
                Id = Guid.NewGuid(), SourceId = _sourceId, ConnectorType = ConnectorTypes.ActiveDirectory, NativeId = nativeId,
                AccountKind = "user", IsHuman = true, DisplayName = nativeId, SamAccountName = nativeId,
                Upn = $"{nativeId}@test.com", Email = $"{nativeId}@test.com", DomainName = "test.com", FilerName = "",
                NativeAccountId = "S-1-5-1", IsDeleted = false, IsDisabled = false, AccountTypeId = WellKnownAccountTypes.Unclassified,
                RawAttributes = RawAttributesJson($"cn=bulk-user-{i},dc=test"), ContentHash = "hash-" + nativeId, HashVersion = 0,
                CreatedAt = now, UpdatedAt = now,
            });
        }
        await _context.SaveChangesAsync();
        await _resolver.ResolveFullAsync(_context, _scanId, Guid.NewGuid(), RelTypes.TwoSided); // baseline full resolve over the bulk — none of them declare edge_refs

        var groupId = await InsertGroupAsync("grp-scale", "cn=grp-scale,dc=test");
        var accountId = await InsertAccountAsync("user-scale", "cn=user-scale,dc=test",
            ("MEMBER_OF", "out", "cn=grp-scale,dc=test", "grp"));
        var manifestId = await CreateManifestAsync();
        _context.IngestChangeEvents.Add(new IngestChangeEvent
        {
            Id = Guid.NewGuid(), ScanManifestId = manifestId, ScanId = _scanId,
            EntityType = "account", EntityId = accountId, ChangeType = "inserted", OccurredAt = DateTimeOffset.UtcNow,
        });
        await _context.SaveChangesAsync();
        await SeedEdgeRefDeltaAsync(manifestId, "user-scale", "account", "add",
            RefJson("MEMBER_OF", "out", "cn=grp-scale,dc=test", "grp"));

        var result = await _resolver.ResolveIncrementalAsync(_context, _scanId, manifestId, RelTypes.TwoSided);

        Assert.Equal(1, result.Inserted); // only the new pair's edge — the 500 unrelated accounts produced none
        var edge = await _context.Edges.SingleAsync(e => e.RelType == "MEMBER_OF");
        Assert.Equal(accountId, edge.FromId);
        Assert.Equal(groupId, edge.ToId);
    }
}
