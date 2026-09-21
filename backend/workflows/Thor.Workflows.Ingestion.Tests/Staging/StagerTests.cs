using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Ingestion.Models;
using Thor.Workflows.Ingestion.Staging;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Staging;

/// <summary>
/// Integration test against a real Postgres (via Testcontainers), with Thor.DataLayer's
/// migrations applied. Requires Docker running locally.
/// </summary>
public sealed class StagerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private TenantDbContext _context = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        _context = new TenantDbContext(options);
        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private static ParsedAccount BuildAccount(Guid sourceId, string nativeId, string? email, string? domainName)
    {
        var account = new ParsedAccount(
            SourceId: sourceId,
            ConnectorType: 308,
            NativeId: nativeId,
            AccountKind: "user",
            IsHuman: true,
            DisplayName: "Test User",
            SamAccountName: "tuser",
            Upn: "tuser@test.com",
            Email: email,
            DomainName: domainName,
            NativeAccountId: "S-1-5-1",
            IsDeleted: false,
            IsDisabled: false,
            RawAttributes: new RawAttributesWithEdges(
                AliasKeys: ["cn=tuser,dc=test"],
                EdgeRefs: [],
                Extra: new Dictionary<string, JsonNode?>()));
        return account with { ContentHash = "hash-" + nativeId };
    }

    private static ParsedGroup BuildGroup(Guid sourceId, string nativeId)
    {
        var group = new ParsedGroup(
            SourceId: sourceId,
            ConnectorType: 308,
            NativeId: nativeId,
            GroupClass: "security_global",
            DisplayName: "Test Group",
            Email: null,
            DomainName: "test.com",
            IsLargeGroup: false,
            IsDeleted: false,
            RawAttributes: new RawAttributesWithEdges(
                AliasKeys: ["cn=tgrp,dc=test"],
                EdgeRefs: [],
                Extra: new Dictionary<string, JsonNode?>()));
        return group with { ContentHash = "hash-" + nativeId };
    }

    [Fact]
    public async Task StageAsync_WritesAccountAndGroupRowsToStagingTables()
    {
        var sourceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var batch = new IngestBatch(
            sourceId,
            [BuildAccount(sourceId, "guid-1", "user1@test.com", "test.com")],
            [BuildGroup(sourceId, "guid-2")],
            [], [], 0, 0);

        var scanManifestId = Guid.NewGuid();
        var stager = new Stager(new StagingAccountRepository(_context), new StagingGrpRepository(_context), new StagingAssetRepository(_context), new StagingEntitlementRepository(_context), NullLogger<Stager>.Instance);
        await stager.StageAsync(batch, tenantId, scanManifestId, batchSeq: 0);

        var stagedAccounts = await _context.StagingAccounts.ToListAsync();
        var stagedGroups = await _context.StagingGrps.ToListAsync();

        Assert.Single(stagedAccounts);
        Assert.Equal("guid-1", stagedAccounts[0].NativeId);
        Assert.Equal("user1@test.com", stagedAccounts[0].Email);
        Assert.Equal(scanManifestId, stagedAccounts[0].ScanManifestId);

        Assert.Single(stagedGroups);
        Assert.Equal("guid-2", stagedGroups[0].NativeId);
        Assert.Equal(tenantId, stagedGroups[0].TenantId);
    }

    [Fact]
    public async Task StageAsync_CoalescesNullTextFieldsToEmptyString_WithoutThrowing()
    {
        var sourceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var batch = new IngestBatch(
            sourceId,
            [BuildAccount(sourceId, "guid-3", email: null, domainName: null)],
            [],
            [], [], 0, 0);

        var stager = new Stager(new StagingAccountRepository(_context), new StagingGrpRepository(_context), new StagingAssetRepository(_context), new StagingEntitlementRepository(_context), NullLogger<Stager>.Instance);
        await stager.StageAsync(batch, tenantId, Guid.NewGuid(), batchSeq: 0);

        var staged = await _context.StagingAccounts.SingleAsync(a => a.NativeId == "guid-3");
        Assert.Equal("", staged.Email);
        Assert.Equal("", staged.DomainName);
    }

    [Fact]
    public async Task StageAsync_EmptyBatch_IsNoOpWithoutThrowing()
    {
        var sourceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var batch = new IngestBatch(sourceId, [], [], [], [], 0, 0);

        var stager = new Stager(new StagingAccountRepository(_context), new StagingGrpRepository(_context), new StagingAssetRepository(_context), new StagingEntitlementRepository(_context), NullLogger<Stager>.Instance);
        await stager.StageAsync(batch, tenantId, Guid.NewGuid(), batchSeq: 0);

        Assert.Empty(await _context.StagingAccounts.ToListAsync());
        Assert.Empty(await _context.StagingGrps.ToListAsync());
    }

    [Fact]
    public async Task StageAsync_TwoCallsSameManifestDifferentBatchSeq_BothLandAsSeparateRows()
    {
        var sourceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var scanManifestId = Guid.NewGuid();
        var stager = new Stager(new StagingAccountRepository(_context), new StagingGrpRepository(_context), new StagingAssetRepository(_context), new StagingEntitlementRepository(_context), NullLogger<Stager>.Instance);

        await stager.StageAsync(
            new IngestBatch(sourceId, [BuildAccount(sourceId, "guid-batch-0", "b0@test.com", "test.com")], [], [], [], 0, 0),
            tenantId, scanManifestId, batchSeq: 0);
        await stager.StageAsync(
            new IngestBatch(sourceId, [BuildAccount(sourceId, "guid-batch-1", "b1@test.com", "test.com")], [], [], [], 0, 0),
            tenantId, scanManifestId, batchSeq: 1);

        var staged = await _context.StagingAccounts.Where(a => a.ScanManifestId == scanManifestId).OrderBy(a => a.BatchSeq).ToListAsync();
        Assert.Equal(2, staged.Count);
        Assert.Equal(0, staged[0].BatchSeq);
        Assert.Equal("guid-batch-0", staged[0].NativeId);
        Assert.Equal(1, staged[1].BatchSeq);
        Assert.Equal("guid-batch-1", staged[1].NativeId);
    }

    [Fact]
    public async Task StageAsync_PreservesContentHashAndRawAttributesJson()
    {
        var sourceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var account = BuildAccount(sourceId, "guid-raw", "raw@test.com", "test.com");
        var batch = new IngestBatch(sourceId, [account], [], [], [], 0, 0);

        var stager = new Stager(new StagingAccountRepository(_context), new StagingGrpRepository(_context), new StagingAssetRepository(_context), new StagingEntitlementRepository(_context), NullLogger<Stager>.Instance);
        await stager.StageAsync(batch, tenantId, Guid.NewGuid(), batchSeq: 0);

        var staged = await _context.StagingAccounts.SingleAsync(a => a.NativeId == "guid-raw");
        Assert.Equal(account.ContentHash, staged.ContentHash);

        var rawAttributes = System.Text.Json.JsonSerializer.Deserialize<RawAttributesWithEdges>(staged.RawAttributes);
        var originalRawAttributes = (RawAttributesWithEdges)account.RawAttributes;
        Assert.NotNull(rawAttributes);
        Assert.Equal(originalRawAttributes.AliasKeys, rawAttributes!.AliasKeys);
    }
}
