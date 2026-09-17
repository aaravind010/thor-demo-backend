using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.Workflows.Ingestion.Extraction.Sources;
using Thor.Workflows.Ingestion.Steps;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Steps;

/// <summary>
/// Exercises <see cref="ExtractAndStageStep"/> against a real Postgres (Testcontainers).
/// </summary>
public sealed class ExtractAndStageStepTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private TenantDbContext _context = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _context = NewContext();
        await _context.Database.EnsureCreatedAsync();
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

    private static byte[] BuildZip(string entryName, string content)
    {
        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(entryName);
            using var entryStream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            entryStream.Write(bytes, 0, bytes.Length);
        }
        return zipStream.ToArray();
    }

    private static byte[] BuildAdExportZip(string userDn, string groupDn)
    {
        var accountJson =
            $$"""{"objectGUID":["guid-user-1"],"objectClass":["user"],"dn":["{{userDn}}"],"sAMAccountName":["tuser"],"userPrincipalName":["tuser@test.com"],"mail":["tuser@test.com"],"displayName":["Test User"],"memberOf":["{{groupDn}}"],"userAccountControl":["512"]}""";
        var groupJson =
            $$"""{"objectGUID":["guid-group-1"],"objectClass":["group"],"dn":["{{groupDn}}"],"cn":["Test Group"],"member":["{{userDn}}"]}""";
        var accountsPath = $$"""{"Domain":"test.com","Accounts":[{{accountJson}},{{groupJson}}]}""".Replace("\"", "\\\"");
        var doc = $$"""{"Domains":[{"ServerId":1,"AccountsPaths":["{{accountsPath}}"]}]}""";
        return BuildZip("export.json", doc);
    }

    private static byte[] BuildCyberArkExportZip()
    {
        var doc = """
            {
              "Safes": [
                {"SafeNumber":7,"Location":"\\","SafeName":"PVWAReports"}
              ],
              "Accounts": { "AccountDetails": [] },
              "Members": []
            }
            """;
        return BuildZip("export.json", doc);
    }

    private static async Task<Guid> SeedSourceAsync(TenantDbContext context, short connectorType = 308)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            ConnectorType = connectorType,
            Name = "test-source",
            Config = "{}",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        context.Sources.Add(source);
        await context.SaveChangesAsync();
        return source.Id;
    }

    private static async Task<Guid> SeedScanAsync(TenantDbContext context)
    {
        var authMethod = new AuthenticationMethod { Id = Guid.NewGuid(), TypeId = Guid.NewGuid(), Name = "test-auth" };
        context.AuthenticationMethods.Add(authMethod);

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
        context.ScanConfigs.Add(scanConfig);

        var scan = new Scan { Id = Guid.NewGuid(), ScanConfigId = scanConfig.Id, ScanType = "initial", Status = "running" };
        context.Scans.Add(scan);

        await context.SaveChangesAsync();
        return scan.Id;
    }

    private static async Task<Guid> SeedScanManifestAsync(TenantDbContext context, Guid scanId, string[]? fileLocations = null)
    {
        var manifest = new ScanManifest
        {
            Id = Guid.NewGuid(),
            ScanId = scanId,
            FileLocations = fileLocations ?? [],
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        context.ScanManifests.Add(manifest);
        await context.SaveChangesAsync();
        return manifest.Id;
    }

    private static string UploadKey(Guid tenantId, Guid sourceId, Guid scanId, string fileName) =>
        $"tenants/{tenantId}/uploads/{scanId}/{sourceId}/{fileName}";

    private sealed class FixedExportSource(byte[] zipBytes) : IExportSource
    {
        public Task<byte[]> ReadAsync(string identifier) =>
            Task.FromResult(zipBytes);
    }

    /// <summary>Returns different zip bytes per identifier — needed once a manifest spans more than one file.</summary>
    private sealed class MappedExportSource(IReadOnlyDictionary<string, byte[]> zipBytesByIdentifier) : IExportSource
    {
        public Task<byte[]> ReadAsync(string identifier) =>
            Task.FromResult(zipBytesByIdentifier[identifier]);
    }

    private sealed class ThrowingExportSource : IExportSource
    {
        public Task<byte[]> ReadAsync(string identifier) =>
            throw new InvalidOperationException("simulated S3 read failure");
    }

    private sealed class FakeTenantConnectionManager(Func<Guid, TenantDbContext> factory) : ITenantConnectionManager
    {
        public Task<Npgsql.NpgsqlConnection> GetValidatedConnectionAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TenantDbContext> GetTenantDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(factory(tenantId));
    }

    [Fact]
    public async Task RunAsync_ValidExport_StagesAccountsAndGroupsAndReturnsCounts()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = await SeedSourceAsync(_context);
        var scanId = await SeedScanAsync(_context);
        var fileLocation = UploadKey(tenantId, sourceId, scanId, "export.zip");
        var scanManifestId = await SeedScanManifestAsync(_context, scanId, [fileLocation]);
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new FixedExportSource(BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test")),
            new FakeTenantConnectionManager(_ => _context));

        var result = await step.RunAsync(_context, tenantId, scanManifestId, "s3://bucket", batchSeq: 0);

        Assert.Equal(1, result.FilesProcessed);
        Assert.Equal(1, result.AccountsStaged);
        Assert.Equal(1, result.GroupsStaged);

        var stagedAccounts = await _context.StagingAccounts.ToListAsync();
        var stagedGroups = await _context.StagingGrps.ToListAsync();
        Assert.Single(stagedAccounts);
        Assert.Equal("guid-user-1", stagedAccounts[0].NativeId);
        Assert.Equal(scanManifestId, stagedAccounts[0].ScanManifestId);
        Assert.Single(stagedGroups);
        Assert.Equal("guid-group-1", stagedGroups[0].NativeId);
    }

    [Fact]
    public async Task ExecuteAsync_ValidInputJson_StagesRows()
    {
        // ExecuteAsync disposes the context it resolves (`await using`) — matching production,
        // where a fresh TenantDbContext is built per invocation. Hand it its own context, not
        // the fixture's `_context`, which stays open for verification below.
        var runContext = NewContext();
        var tenantId = Guid.NewGuid();
        var sourceId = await SeedSourceAsync(runContext);
        var scanId = await SeedScanAsync(runContext);
        var fileLocation = UploadKey(tenantId, sourceId, scanId, "export.zip");
        var scanManifestId = await SeedScanManifestAsync(runContext, scanId, [fileLocation]);
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new FixedExportSource(BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test")),
            new FakeTenantConnectionManager(_ => runContext));

        var request = new IngestionRequest(tenantId, "s3://bucket", scanId, scanManifestId);
        var inputJson = System.Text.Json.JsonSerializer.Serialize(request);

        await step.ExecuteAsync(inputJson, CancellationToken.None);

        var stagedAccounts = await _context.StagingAccounts.ToListAsync();
        Assert.Single(stagedAccounts);
    }

    [Fact]
    public async Task RunAsync_ExportSourceReadFailure_PropagatesException()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = await SeedSourceAsync(_context);
        var scanId = await SeedScanAsync(_context);
        var fileLocation = UploadKey(tenantId, sourceId, scanId, "export.zip");
        var scanManifestId = await SeedScanManifestAsync(_context, scanId, [fileLocation]);
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new ThrowingExportSource(),
            new FakeTenantConnectionManager(_ => _context));

        await Assert.ThrowsAsync<InvalidOperationException>(() => step.RunAsync(
            _context, tenantId, scanManifestId, "s3://bucket", batchSeq: 0));
    }

    [Fact]
    public async Task RunAsync_NoScanManifestFound_Throws()
    {
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new FixedExportSource(BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test")),
            new FakeTenantConnectionManager(_ => _context));

        await Assert.ThrowsAsync<InvalidOperationException>(() => step.RunAsync(
            _context, Guid.NewGuid(), Guid.NewGuid(), "s3://bucket", batchSeq: 0));
    }

    [Fact]
    public async Task RunAsync_FileLocationDoesNotParse_Throws()
    {
        var scanId = await SeedScanAsync(_context);
        var scanManifestId = await SeedScanManifestAsync(_context, scanId, ["not-a-valid-upload-key.zip"]);
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new FixedExportSource(BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test")),
            new FakeTenantConnectionManager(_ => _context));

        await Assert.ThrowsAsync<InvalidOperationException>(() => step.RunAsync(
            _context, Guid.NewGuid(), scanManifestId, "s3://bucket", batchSeq: 0));
    }

    [Fact]
    public async Task RunAsync_FileLocationBelongsToDifferentTenant_Throws()
    {
        var sourceId = await SeedSourceAsync(_context);
        var scanId = await SeedScanAsync(_context);
        var fileLocation = UploadKey(Guid.NewGuid(), sourceId, scanId, "export.zip");
        var scanManifestId = await SeedScanManifestAsync(_context, scanId, [fileLocation]);
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new FixedExportSource(BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test")),
            new FakeTenantConnectionManager(_ => _context));

        // A different tenantId than the one embedded in fileLocation.
        await Assert.ThrowsAsync<InvalidOperationException>(() => step.RunAsync(
            _context, Guid.NewGuid(), scanManifestId, "s3://bucket", batchSeq: 0));
    }

    [Fact]
    public async Task RunAsync_FileLocationBelongsToDifferentScan_Throws()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = await SeedSourceAsync(_context);
        var scanId = await SeedScanAsync(_context);
        // A different scanId than the manifest's own ScanId.
        var fileLocation = UploadKey(tenantId, sourceId, Guid.NewGuid(), "export.zip");
        var scanManifestId = await SeedScanManifestAsync(_context, scanId, [fileLocation]);
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new FixedExportSource(BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test")),
            new FakeTenantConnectionManager(_ => _context));

        await Assert.ThrowsAsync<InvalidOperationException>(() => step.RunAsync(
            _context, tenantId, scanManifestId, "s3://bucket", batchSeq: 0));
    }

    [Fact]
    public async Task RunAsync_ManifestSpansTwoSources_UsesEachFilesOwnNormalizer()
    {
        var tenantId = Guid.NewGuid();
        var adSourceId = await SeedSourceAsync(_context, connectorType: 308);
        var cyberArkSourceId = await SeedSourceAsync(_context, connectorType: 401);
        var scanId = await SeedScanAsync(_context);
        var adFileLocation = UploadKey(tenantId, adSourceId, scanId, "ad-export.zip");
        var cyberArkFileLocation = UploadKey(tenantId, cyberArkSourceId, scanId, "cyberark-export.zip");
        var scanManifestId = await SeedScanManifestAsync(_context, scanId, [adFileLocation, cyberArkFileLocation]);

        var exportSource = new MappedExportSource(new Dictionary<string, byte[]>
        {
            [$"s3://bucket/{adFileLocation}"] = BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test"),
            [$"s3://bucket/{cyberArkFileLocation}"] = BuildCyberArkExportZip(),
        });
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            exportSource,
            new FakeTenantConnectionManager(_ => _context));

        var result = await step.RunAsync(_context, tenantId, scanManifestId, "s3://bucket", batchSeq: 0);

        Assert.Equal(2, result.FilesProcessed);
        Assert.Equal(1, result.AccountsStaged);
        Assert.Equal(1, result.GroupsStaged);
        Assert.Equal(1, result.AssetsStaged);
    }
}
