using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Ingestion.Constants;
using Thor.Workflows.Ingestion.EdgeGate;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.EdgeGate;

/// <summary>
/// Integration test against a real Postgres (via Testcontainers) — the add/remove diff is a
/// jsonb set-comparison against canonical, not something a fake repository can exercise.
/// </summary>
public sealed class EdgeRefDeltaComputerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private TenantDbContext _context = null!;
    private Guid _sourceId;
    private EdgeRefDeltaComputer _computer = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        _context = new TenantDbContext(options);
        await _context.Database.EnsureCreatedAsync();

        _context.AccountTypes.Add(new AccountType
        {
            Id = WellKnownAccountTypes.Unclassified, Name = "Unclassified",
            Description = "Default account type for newly-promoted accounts pending classification.", IsHuman = false,
        });
        var source = new Source
        {
            Id = Guid.NewGuid(), ConnectorType = ConnectorTypes.ActiveDirectory, Name = "test-source",
            Config = "{}", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _context.Sources.Add(source);
        await _context.SaveChangesAsync();
        _sourceId = source.Id;

        _computer = new EdgeRefDeltaComputer();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private static string RefJson(string rel, string dir, string key, string? targetType) =>
        $$"""{"Rel":"{{rel}}","Dir":"{{dir}}","Key":"{{key}}","TargetType":{{(targetType is null ? "null" : $"\"{targetType}\"")}}}""";

    private static string RawAttributesJson(params string[] refs) =>
        $$"""{"AliasKeys":[],"EdgeRefs":[{{string.Join(",", refs)}}]}""";

    private async Task InsertCanonicalAccountAsync(string nativeId, string rawAttributes)
    {
        var now = DateTimeOffset.UtcNow;
        _context.Accounts.Add(new Account
        {
            Id = Guid.NewGuid(), SourceId = _sourceId, ConnectorType = ConnectorTypes.ActiveDirectory, NativeId = nativeId,
            AccountKind = "user", IsHuman = true, DisplayName = nativeId, SamAccountName = nativeId,
            Upn = $"{nativeId}@test.com", Email = $"{nativeId}@test.com", DomainName = "test.com", FilerName = "",
            NativeAccountId = "S-1-5-1", IsDeleted = false, IsDisabled = false, AccountTypeId = WellKnownAccountTypes.Unclassified,
            RawAttributes = rawAttributes, ContentHash = "hash-" + nativeId, HashVersion = 0, CreatedAt = now, UpdatedAt = now,
        });
        await _context.SaveChangesAsync();
    }

    private async Task StageAccountAsync(Guid scanManifestId, string nativeId, string rawAttributes)
    {
        var row = new StagingAccount
        {
            ScanManifestId = scanManifestId, BatchSeq = 0, SourceId = _sourceId, ConnectorType = ConnectorTypes.ActiveDirectory,
            NativeId = nativeId, AccountKind = "user", IsHuman = true, DisplayName = nativeId, SamAccountName = nativeId,
            Upn = $"{nativeId}@test.com", Email = $"{nativeId}@test.com", DomainName = "test.com", FilerName = "",
            NativeAccountId = "S-1-5-1", IsDeleted = false, IsDisabled = false,
            RawAttributes = rawAttributes, ContentHash = "hash-" + nativeId, ReceivedAt = DateTimeOffset.UtcNow,
        };
        await new StagingAccountRepository(_context).BulkInsertAsync([row]);
    }

    private async Task<List<EdgeRefDelta>> DeltaRowsAsync(Guid scanManifestId) =>
        await _context.EdgeRefDeltas.Where(d => d.ScanManifestId == scanManifestId).OrderBy(d => d.Op).ToListAsync();

    [Fact]
    public async Task ComputeAsync_BrandNewEntity_EverythingIsAnAdd()
    {
        var manifestId = Guid.NewGuid();
        await StageAccountAsync(manifestId, "user-1", RawAttributesJson(RefJson("MEMBER_OF", "out", "cn=grp,dc=test", "grp")));

        await _computer.ComputeAsync(_context, manifestId, "tenant.account", "account");

        var rows = await DeltaRowsAsync(manifestId);
        Assert.Single(rows);
        Assert.Equal("add", rows[0].Op);
        Assert.Equal("user-1", rows[0].NativeId);
        Assert.Equal("account", rows[0].EntityType);
    }

    [Fact]
    public async Task ComputeAsync_RefRemovedFromCanonical_ProducesRemoveRow()
    {
        var manifestId = Guid.NewGuid();
        await InsertCanonicalAccountAsync("user-2", RawAttributesJson(RefJson("MEMBER_OF", "out", "cn=grp,dc=test", "grp")));
        await StageAccountAsync(manifestId, "user-2", RawAttributesJson()); // no more refs

        await _computer.ComputeAsync(_context, manifestId, "tenant.account", "account");

        var rows = await DeltaRowsAsync(manifestId);
        Assert.Single(rows);
        Assert.Equal("remove", rows[0].Op);
    }

    [Fact]
    public async Task ComputeAsync_NoChange_ProducesNoRows()
    {
        var manifestId = Guid.NewGuid();
        var refs = RawAttributesJson(RefJson("MEMBER_OF", "out", "cn=grp,dc=test", "grp"));
        await InsertCanonicalAccountAsync("user-3", refs);
        await StageAccountAsync(manifestId, "user-3", refs);

        await _computer.ComputeAsync(_context, manifestId, "tenant.account", "account");

        Assert.Empty(await DeltaRowsAsync(manifestId));
    }

    [Fact]
    public async Task ComputeAsync_PropsChangedOnOtherwiseIdenticalRef_ProducesOneAddAndOneRemove()
    {
        var manifestId = Guid.NewGuid();
        var oldRef = """{"Rel":"HAS_ACCESS","Dir":"out","Key":"cn=safe,dc=test","TargetType":"asset","Props":"{\"is_admin\":false}"}""";
        var newRef = """{"Rel":"HAS_ACCESS","Dir":"out","Key":"cn=safe,dc=test","TargetType":"asset","Props":"{\"is_admin\":true}"}""";
        await InsertCanonicalAccountAsync("user-4", RawAttributesJson(oldRef));
        await StageAccountAsync(manifestId, "user-4", RawAttributesJson(newRef));

        await _computer.ComputeAsync(_context, manifestId, "tenant.account", "account");

        var rows = await DeltaRowsAsync(manifestId);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Op == "add");
        Assert.Contains(rows, r => r.Op == "remove");
    }

    [Fact]
    public async Task ComputeAsync_CalledTwiceForSameManifest_DoesNotDuplicateRows()
    {
        var manifestId = Guid.NewGuid();
        await StageAccountAsync(manifestId, "user-5", RawAttributesJson(RefJson("MEMBER_OF", "out", "cn=grp,dc=test", "grp")));

        await _computer.ComputeAsync(_context, manifestId, "tenant.account", "account");
        await _computer.ComputeAsync(_context, manifestId, "tenant.account", "account"); // simulates a retried PromoteStep

        Assert.Single(await DeltaRowsAsync(manifestId));
    }
}
