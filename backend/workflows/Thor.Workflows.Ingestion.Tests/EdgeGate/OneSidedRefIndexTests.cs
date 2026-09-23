using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.Workflows.Ingestion.Constants;
using Thor.Workflows.Ingestion.EdgeGate;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.EdgeGate;

/// <summary>
/// Integration test against a real Postgres (via Testcontainers) — OneSidedRefIndex is bulk SQL
/// against a <c>seed_owner</c> temp table (normally built by <see cref="EdgeResolver"/>), so a
/// fake repository can't exercise it. Seeds <c>seed_owner</c> directly to isolate
/// OneSidedRefIndex's own refresh/heal SQL. The connection is opened for the whole test (kept in
/// <see cref="InitializeAsync"/>/<see cref="DisposeAsync"/>) because a session-scoped temp table
/// must survive across several raw-SQL calls within one test.
/// </summary>
public sealed class OneSidedRefIndexTests : IAsyncLifetime
{
    private static readonly HashSet<string> TwoSided = ["MEMBER_OF"];

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private TenantDbContext _context = null!;
    private Guid _sourceId;
    private OneSidedRefIndex _index = null!;

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

        await _context.Database.OpenConnectionAsync();
        _index = new OneSidedRefIndex();
    }

    public async Task DisposeAsync()
    {
        await _context.Database.CloseConnectionAsync();
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private static string RawAttributesJson(params (string Rel, string Dir, string Key, string? TargetType)[] edgeRefs)
    {
        var refsJson = string.Join(",", edgeRefs.Select(r =>
            $$"""{"Rel":"{{r.Rel}}","Dir":"{{r.Dir}}","Key":"{{r.Key}}","TargetType":{{(r.TargetType is null ? "null" : $"\"{r.TargetType}\"")}}}"""));
        return $$"""{"AliasKeys":[],"EdgeRefs":[{{refsJson}}]}""";
    }

    private async Task<Guid> InsertAccountAsync(string nativeId, params (string Rel, string Dir, string Key, string? TargetType)[] edgeRefs)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _context.Accounts.Add(new Account
        {
            Id = id, SourceId = _sourceId, ConnectorType = ConnectorTypes.ActiveDirectory, NativeId = nativeId,
            AccountKind = "user", IsHuman = true, DisplayName = nativeId, SamAccountName = nativeId,
            Upn = $"{nativeId}@test.com", Email = $"{nativeId}@test.com", DomainName = "test.com", FilerName = "",
            NativeAccountId = "S-1-5-1", IsDeleted = false, IsDisabled = false, AccountTypeId = WellKnownAccountTypes.Unclassified,
            RawAttributes = RawAttributesJson(edgeRefs), ContentHash = "hash-" + nativeId, HashVersion = 0,
            CreatedAt = now, UpdatedAt = now,
        });
        await _context.SaveChangesAsync();
        return id;
    }

    private async Task SeedOwnerAsync(params (Guid Id, string OwnerType)[] owners)
    {
        await _context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS seed_owner");
        await _context.Database.ExecuteSqlRawAsync("CREATE TEMP TABLE seed_owner (id uuid PRIMARY KEY, owner_type text NOT NULL)");
        foreach (var (id, ownerType) in owners)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO seed_owner (id, owner_type) VALUES ({id}, {ownerType})");
        }
    }

    [Fact]
    public async Task RefreshForSeedAsync_KeepsOnlyOneSidedRefs_FiltersOutTwoSided()
    {
        var ownerId = await InsertAccountAsync("user-1",
            ("MEMBER_OF", "out", "cn=grp,dc=test", "grp"), // two-sided — filtered out
            ("REPORTS_TO", "out", "cn=manager,dc=test", "account")); // one-sided — kept
        await SeedOwnerAsync((ownerId, "account"));

        await _index.RefreshForSeedAsync(_context, TwoSided);

        var rows = await _context.OneSidedRefs.Where(r => r.OwnerId == ownerId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("REPORTS_TO", rows[0].Rel);
        Assert.Equal("cn=manager,dc=test", rows[0].Key);
    }

    [Fact]
    public async Task RefreshForSeedAsync_SecondCall_ReplacesPreviousSet()
    {
        var ownerId = await InsertAccountAsync("user-2", ("REPORTS_TO", "out", "cn=manager-old,dc=test", "account"));
        await SeedOwnerAsync((ownerId, "account"));
        await _index.RefreshForSeedAsync(_context, TwoSided);

        var account = await _context.Accounts.SingleAsync(a => a.Id == ownerId);
        account.RawAttributes = RawAttributesJson(("REPORTS_TO", "out", "cn=manager-new,dc=test", "account"));
        await _context.SaveChangesAsync();

        await _index.RefreshForSeedAsync(_context, TwoSided);

        var rows = await _context.OneSidedRefs.Where(r => r.OwnerId == ownerId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("cn=manager-new,dc=test", rows[0].Key);
    }

    [Fact]
    public async Task RefreshForSeedAsync_EmptyEdgeRefs_ClearsExistingWithNoneAdded()
    {
        var ownerId = await InsertAccountAsync("user-3", ("REPORTS_TO", "out", "cn=manager,dc=test", "account"));
        await SeedOwnerAsync((ownerId, "account"));
        await _index.RefreshForSeedAsync(_context, TwoSided);
        Assert.NotEmpty(await _context.OneSidedRefs.Where(r => r.OwnerId == ownerId).ToListAsync());

        var account = await _context.Accounts.SingleAsync(a => a.Id == ownerId);
        account.RawAttributes = RawAttributesJson();
        await _context.SaveChangesAsync();
        await _index.RefreshForSeedAsync(_context, TwoSided);

        Assert.Empty(await _context.OneSidedRefs.Where(r => r.OwnerId == ownerId).ToListAsync());
    }

    [Fact]
    public async Task RefreshForSeedAsync_OwnerNotInSeed_LeavesItsParkedRefsUntouched()
    {
        var seededOwnerId = await InsertAccountAsync("user-seeded", ("REPORTS_TO", "out", "cn=manager,dc=test", "account"));
        var otherOwnerId = await InsertAccountAsync("user-other", ("REPORTS_TO", "out", "cn=manager,dc=test", "account"));
        await SeedOwnerAsync((seededOwnerId, "account"), (otherOwnerId, "account"));
        await _index.RefreshForSeedAsync(_context, TwoSided);
        Assert.NotEmpty(await _context.OneSidedRefs.Where(r => r.OwnerId == otherOwnerId).ToListAsync());

        await SeedOwnerAsync((seededOwnerId, "account")); // otherOwnerId no longer seeded
        await _index.RefreshForSeedAsync(_context, TwoSided);

        Assert.NotEmpty(await _context.OneSidedRefs.Where(r => r.OwnerId == otherOwnerId).ToListAsync());
    }
}
