using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Abstractions;
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

    private static async Task<Guid> SeedSourceAsync(TenantDbContext context, short connectorType = 1)
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

    private static string UploadKey(Guid tenantId, Guid sourceId, Guid scanId, string fileName) =>
        $"tenants/{tenantId}/uploads/{scanId}/{sourceId}/{fileName}";

    /// <summary>Only needed by tests that go on to call <see cref="PromoteStep"/>, which reads <c>Scan.ScanType</c> via <c>ScanLifecycle</c>.</summary>
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

    /// <summary>
    /// Needed by tests that go on to call <see cref="PromoteStep"/> (whose <see
    /// cref="WorkflowLifecycle"/> row has a real foreign key to <c>scan_manifest</c>) or the
    /// list-files invocation shape of <see cref="ExtractAndStageStep"/>, which reads this row
    /// directly.
    /// </summary>
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

    /// <summary>
    /// Migrations aren't tracked in this repo, so <c>EnsureCreatedAsync</c> (schema-only) won't
    /// seed the "Unclassified" <see cref="AccountType"/> row account.account_type_id requires —
    /// only needed by tests that go on to call <see cref="PromoteStep"/>.
    /// </summary>
    private static async Task SeedUnclassifiedAccountTypeAsync(TenantDbContext context)
    {
        context.AccountTypes.Add(new AccountType
        {
            Id = WellKnownAccountTypes.Unclassified,
            Name = "Unclassified",
            Description = "Default account type for newly-promoted accounts pending classification.",
            IsHuman = false,
        });
        await context.SaveChangesAsync();
    }

    private sealed class FixedExportSource(byte[] zipBytes) : IExportSource
    {
        public Task<byte[]> ReadAsync(string identifier, CancellationToken cancellationToken = default) =>
            Task.FromResult(zipBytes);
    }

    /// <summary>Returns different zip bytes per identifier — needed once a manifest spans more than one file.</summary>
    private sealed class MappedExportSource(IReadOnlyDictionary<string, byte[]> zipBytesByIdentifier) : IExportSource
    {
        public Task<byte[]> ReadAsync(string identifier, CancellationToken cancellationToken = default) =>
            Task.FromResult(zipBytesByIdentifier[identifier]);
    }

    private sealed class ThrowingExportSource : IExportSource
    {
        public Task<byte[]> ReadAsync(string identifier, CancellationToken cancellationToken = default) =>
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
        var scanId = Guid.NewGuid();
        var scanManifestId = Guid.NewGuid();
        var fileLocation = UploadKey(tenantId, sourceId, scanId, "export.zip");
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new FixedExportSource(BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test")),
            new FakeTenantConnectionManager(_ => _context), TestConnectorTypes.Catalog);

        var result = await step.RunAsync(_context, tenantId, scanId, scanManifestId, "s3://bucket", fileLocation, fileSeq: 0);

        Assert.Equal(fileLocation, result.FileLocation);
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
        var scanId = Guid.NewGuid();
        var scanManifestId = Guid.NewGuid();
        var fileLocation = UploadKey(tenantId, sourceId, scanId, "export.zip");
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new FixedExportSource(BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test")),
            new FakeTenantConnectionManager(_ => runContext), TestConnectorTypes.Catalog);

        var request = new IngestionRequest(tenantId, "s3://bucket", scanId, scanManifestId, FileLocation: fileLocation, BatchSeq: 0);
        var inputJson = System.Text.Json.JsonSerializer.Serialize(request);

        await step.ExecuteAsync(inputJson, CancellationToken.None);

        // ExecuteAsync disposed runContext above; query via the fixture's own _context, a
        // separate EF context instance pointed at the same underlying Postgres container.
        var stagedAccounts = await _context.StagingAccounts.ToListAsync();
        Assert.Single(stagedAccounts);
    }

    [Fact]
    public async Task RunAsync_ExportSourceReadFailure_PropagatesException()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = await SeedSourceAsync(_context);
        var scanId = Guid.NewGuid();
        var fileLocation = UploadKey(tenantId, sourceId, scanId, "export.zip");
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new ThrowingExportSource(),
            new FakeTenantConnectionManager(_ => _context), TestConnectorTypes.Catalog);

        await Assert.ThrowsAsync<InvalidOperationException>(() => step.RunAsync(
            _context, tenantId, scanId, Guid.NewGuid(), "s3://bucket", fileLocation, fileSeq: 0));
    }

    [Fact]
    public async Task RunAsync_FileLocationDoesNotParse_Throws()
    {
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new FixedExportSource(BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test")),
            new FakeTenantConnectionManager(_ => _context), TestConnectorTypes.Catalog);

        await Assert.ThrowsAsync<InvalidOperationException>(() => step.RunAsync(
            _context, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "s3://bucket", "not-a-valid-upload-key.zip", fileSeq: 0));
    }

    [Fact]
    public async Task RunAsync_FileLocationBelongsToDifferentTenant_Throws()
    {
        var sourceId = await SeedSourceAsync(_context);
        var scanId = Guid.NewGuid();
        var fileLocation = UploadKey(Guid.NewGuid(), sourceId, scanId, "export.zip");
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new FixedExportSource(BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test")),
            new FakeTenantConnectionManager(_ => _context), TestConnectorTypes.Catalog);

        // A different tenantId than the one embedded in fileLocation.
        await Assert.ThrowsAsync<InvalidOperationException>(() => step.RunAsync(
            _context, Guid.NewGuid(), scanId, Guid.NewGuid(), "s3://bucket", fileLocation, fileSeq: 0));
    }

    [Fact]
    public async Task RunAsync_FileLocationBelongsToDifferentScan_Throws()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = await SeedSourceAsync(_context);
        var scanId = Guid.NewGuid();
        // A different scanId than the one passed to RunAsync.
        var fileLocation = UploadKey(tenantId, sourceId, Guid.NewGuid(), "export.zip");
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new FixedExportSource(BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test")),
            new FakeTenantConnectionManager(_ => _context), TestConnectorTypes.Catalog);

        await Assert.ThrowsAsync<InvalidOperationException>(() => step.RunAsync(
            _context, tenantId, scanId, Guid.NewGuid(), "s3://bucket", fileLocation, fileSeq: 0));
    }

    [Fact]
    public async Task RunAsync_TwoFilesFromDifferentSources_EachUsesItsOwnNormalizer()
    {
        var tenantId = Guid.NewGuid();
        var adSourceId = await SeedSourceAsync(_context, connectorType: 1);
        var cyberArkSourceId = await SeedSourceAsync(_context, connectorType: 7);
        var scanId = Guid.NewGuid();
        var scanManifestId = Guid.NewGuid();
        var adFileLocation = UploadKey(tenantId, adSourceId, scanId, "ad-export.zip");
        var cyberArkFileLocation = UploadKey(tenantId, cyberArkSourceId, scanId, "cyberark-export.zip");

        var exportSource = new MappedExportSource(new Dictionary<string, byte[]>
        {
            [$"s3://bucket/{adFileLocation}"] = BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test"),
            [$"s3://bucket/{cyberArkFileLocation}"] = BuildCyberArkExportZip(),
        });
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            exportSource,
            new FakeTenantConnectionManager(_ => _context), TestConnectorTypes.Catalog);

        // Simulates two Map iterations for the same manifest, run independently.
        var adResult = await step.RunAsync(_context, tenantId, scanId, scanManifestId, "s3://bucket", adFileLocation, fileSeq: 0);
        var cyberArkResult = await step.RunAsync(_context, tenantId, scanId, scanManifestId, "s3://bucket", cyberArkFileLocation, fileSeq: 1);

        Assert.Equal(1, adResult.AccountsStaged);
        Assert.Equal(1, adResult.GroupsStaged);
        Assert.Equal(1, cyberArkResult.AssetsStaged);
    }

    [Fact]
    public async Task RunAsync_RetriedForSameFile_PromoteStillProducesExactlyOneCanonicalRowPerNativeId()
    {
        // Proves the retry-safety claim that lets this step be re-invoked by a Distributed Map
        // item retry without any change to Stager/Promoter: Promoter's promotion SQL dedupes by
        // (source_id, native_id) ordered by received_at DESC, and deletes all of the manifest's
        // staging rows afterward, so a duplicate attempt at the same file never produces a
        // duplicate canonical row or blocks promotion.
        await SeedUnclassifiedAccountTypeAsync(_context);
        var tenantId = Guid.NewGuid();
        var sourceId = await SeedSourceAsync(_context);
        var scanId = await SeedScanAsync(_context);
        var scanManifestId = await SeedScanManifestAsync(_context, scanId);
        var fileLocation = UploadKey(tenantId, sourceId, scanId, "export.zip");
        var extractStep = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new FixedExportSource(BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test")),
            new FakeTenantConnectionManager(_ => _context), TestConnectorTypes.Catalog);

        await extractStep.RunAsync(_context, tenantId, scanId, scanManifestId, "s3://bucket", fileLocation, fileSeq: 0);
        await extractStep.RunAsync(_context, tenantId, scanId, scanManifestId, "s3://bucket", fileLocation, fileSeq: 0);

        var stagedAccounts = await _context.StagingAccounts.Where(a => a.ScanManifestId == scanManifestId).ToListAsync();
        Assert.Equal(2, stagedAccounts.Count); // both attempts' rows sit in staging until promote runs

        var promoteStep = new PromoteStep(NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => _context));
        await promoteStep.RunAsync(_context, scanManifestId, scanId);

        var canonicalAccounts = await _context.Accounts.Where(a => a.SourceId == sourceId).ToListAsync();
        Assert.Single(canonicalAccounts);
        Assert.Equal("guid-user-1", canonicalAccounts[0].NativeId);
    }

    /// <summary>
    /// The HR feed connector uploads two files per run — the <c>FeedIngestorData</c> envelope
    /// and its <c>FeedIngestorPostData</c> raw-CSV companion. Only the first carries people; the
    /// second must still end up Ingested (not Failed) while staging nothing.
    /// </summary>
    [Fact]
    public async Task RunAsync_HrFeedEnvelopeAndPostDataFiles_StagesPeopleAndPromotesThem()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = await SeedSourceAsync(_context, connectorType: 17);
        var scanId = await SeedScanAsync(_context);
        var taskId = await SeedScanTaskAsync(_context, scanId, sourceId);
        var scanManifestId = await SeedScanManifestAsync(_context, scanId);
        var dataFileLocation = PresignedUploadKey(tenantId, sourceId, scanId, taskId);
        var postDataFileLocation = PresignedUploadKey(tenantId, sourceId, scanId, taskId);

        var exportSource = new MappedExportSource(new Dictionary<string, byte[]>
        {
            [$"s3://bucket/{dataFileLocation}"] = BuildZip(
                "134036640000000000.Connector-FeedIngestorData-Hr",
                """{"UserSpecs":[{"EmployeeID":"E20481","FirstName":"Jane","LastName":"Doe","FullName":"Jane Doe","Email":"jane.doe@example.com","Department":"Accounts Payable","SupervisorEmployeeID":"E10023","ADFieldToMatch":"SamAccountName","FieldToMatchValue":"jdoe","Delete":false}],"TaskId":-1}"""),
            [$"s3://bucket/{postDataFileLocation}"] = BuildZip(
                "134036640000000001.Connector-FeedIngestorPostData-Hr",
                """{"AppUsers":null,"RawFeedData":"Full Name,Employee ID (Unique ID)\nJane Doe,E20481","TaskId":-1}"""),
        });
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance, exportSource, new FakeTenantConnectionManager(_ => _context), TestConnectorTypes.Catalog);

        var dataResult = await step.RunAsync(_context, tenantId, scanId, scanManifestId, "s3://bucket", dataFileLocation, fileSeq: 0);
        var postDataResult = await step.RunAsync(_context, tenantId, scanId, scanManifestId, "s3://bucket", postDataFileLocation, fileSeq: 1);

        Assert.Equal(1, dataResult.IdentitiesStaged);
        Assert.Equal(0, postDataResult.IdentitiesStaged);
        var fileStatuses = await _context.ScanFiles.AsNoTracking().Where(f => f.ScanId == scanId).Select(f => f.Status).ToListAsync();
        Assert.Equal([ScanFileStatus.Ingested, ScanFileStatus.Ingested], fileStatuses);

        var promoteStep = new PromoteStep(NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => _context));
        var promoteResult = await promoteStep.RunAsync(_context, scanManifestId, scanId);

        Assert.Equal(1, promoteResult.IdentitiesInserted);
        var identity = await _context.Identities.AsNoTracking().SingleAsync(i => i.SourceId == sourceId);
        Assert.Equal("E20481", identity.HrEmployeeId);
        Assert.Equal("hr_feed", identity.Source);
        Assert.Equal("E10023", identity.ManagerEmployeeId);
        Assert.Equal("jdoe", identity.AdMatchValue);
        Assert.True(identity.IsActive);
    }

    [Fact]
    public async Task ListFilesAsync_ManifestWithMultipleFiles_ReturnsOneRequestPerFileWithStableSeq()
    {
        var tenantId = Guid.NewGuid();
        var scanId = await SeedScanAsync(_context);
        var fileLocations = new[] { "file-a.zip", "file-b.zip", "file-c.zip" };
        var scanManifestId = await SeedScanManifestAsync(_context, scanId, fileLocations);
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new ThrowingExportSource(), // never read from during listing
            new FakeTenantConnectionManager(_ => _context), TestConnectorTypes.Catalog);

        var result = await step.ListFilesAsync(_context, tenantId, "s3://bucket", scanId, scanManifestId);

        Assert.Equal(3, result.FileCount);
        Assert.Equal(fileLocations, result.Files.Select(f => f.FileLocation));
        Assert.Equal([0, 1, 2], result.Files.Select(f => f.BatchSeq));
        Assert.All(result.Files, f =>
        {
            Assert.Equal(tenantId, f.TenantId);
            Assert.Equal(scanId, f.ScanId);
            Assert.Equal(scanManifestId, f.ScanManifestId);
        });

        var workflow = await _context.Workflows.SingleAsync(w => w.ScanManifestId == scanManifestId);
        Assert.Equal("started", workflow.Status);
        Assert.Equal(WorkflowTypes.Ingestion, workflow.WorkflowType);
    }

    [Fact]
    public async Task ListFilesAsync_EmptyManifest_ReturnsNoFilesButStillStartsWorkflow()
    {
        var scanId = await SeedScanAsync(_context);
        var scanManifestId = await SeedScanManifestAsync(_context, scanId);
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new ThrowingExportSource(),
            new FakeTenantConnectionManager(_ => _context), TestConnectorTypes.Catalog);

        var result = await step.ListFilesAsync(_context, Guid.NewGuid(), "s3://bucket", scanId, scanManifestId);

        Assert.Equal(0, result.FileCount);
        Assert.Empty(result.Files);

        var workflow = await _context.Workflows.SingleAsync(w => w.ScanManifestId == scanManifestId);
        Assert.Equal("started", workflow.Status);
    }

    [Fact]
    public async Task ListFilesAsync_NoScanManifestFound_ThrowsAndCreatesNoWorkflowRow()
    {
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new ThrowingExportSource(),
            new FakeTenantConnectionManager(_ => _context), TestConnectorTypes.Catalog);

        await Assert.ThrowsAsync<InvalidOperationException>(() => step.ListFilesAsync(
            _context, Guid.NewGuid(), "s3://bucket", Guid.NewGuid(), Guid.NewGuid()));

        Assert.Empty(await _context.Workflows.ToListAsync());
    }

    [Fact]
    public async Task ListFilesAsync_CalledTwiceWithSameRunId_ReusesTheSameWorkflowRow()
    {
        var scanId = await SeedScanAsync(_context);
        var scanManifestId = await SeedScanManifestAsync(_context, scanId, ["file-a.zip"]);
        var runId = Guid.NewGuid();
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new ThrowingExportSource(),
            new FakeTenantConnectionManager(_ => _context), TestConnectorTypes.Catalog);

        await step.ListFilesAsync(_context, Guid.NewGuid(), "s3://bucket", scanId, scanManifestId, runId);
        await step.ListFilesAsync(_context, Guid.NewGuid(), "s3://bucket", scanId, scanManifestId, runId);

        var workflows = await _context.Workflows.Where(w => w.RunId == runId).ToListAsync();
        Assert.Single(workflows);
    }

    [Fact]
    public async Task ExecuteAsync_InputJsonWithNoFileLocation_ListsFilesInsteadOfExtracting()
    {
        var runContext = NewContext();
        var tenantId = Guid.NewGuid();
        var scanId = await SeedScanAsync(runContext);
        var scanManifestId = await SeedScanManifestAsync(runContext, scanId, ["file-a.zip"]);
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new ThrowingExportSource(),
            new FakeTenantConnectionManager(_ => runContext), TestConnectorTypes.Catalog);

        var request = new IngestionRequest(tenantId, "s3://bucket", scanId, scanManifestId);
        var inputJson = System.Text.Json.JsonSerializer.Serialize(request);

        var (result, isInProgress) = await step.ExecuteAsync(inputJson, CancellationToken.None);

        Assert.False(isInProgress);
        var listResult = Assert.IsType<ListIngestionFilesResult>(result);
        Assert.Equal(1, listResult.FileCount);
    }

    // --- scan_file per-file status ---------------------------------------------------------------

    /// <summary>
    /// The object key Thor.TaskApi's UploadService mints. Only this shape carries a task id, and
    /// only files carrying one get a scan_file row.
    /// </summary>
    private static string PresignedUploadKey(Guid tenantId, Guid sourceId, Guid scanId, Guid taskId) =>
        UploadKey(tenantId, sourceId, scanId, $"data_{taskId}_{sourceId}_{Guid.CreateVersion7()}");

    private static async Task<Guid> SeedScanTaskAsync(TenantDbContext context, Guid scanId, Guid sourceId)
    {
        var task = new ScanTask
        {
            Id = Guid.NewGuid(),
            ScanId = scanId,
            SourceId = sourceId,
            Status = ScanTaskStatus.Completed,
        };
        context.Tasks.Add(task);
        await context.SaveChangesAsync();
        return task.Id;
    }

    [Fact]
    public async Task ListFilesAsync_ManifestWithPresignedKeys_RecordsEveryFileAsReceived()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = await SeedSourceAsync(_context);
        var scanId = await SeedScanAsync(_context);
        var taskId = await SeedScanTaskAsync(_context, scanId, sourceId);
        var fileLocations = new[]
        {
            PresignedUploadKey(tenantId, sourceId, scanId, taskId),
            PresignedUploadKey(tenantId, sourceId, scanId, taskId),
        };
        var scanManifestId = await SeedScanManifestAsync(_context, scanId, fileLocations);
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance, new ThrowingExportSource(), new FakeTenantConnectionManager(_ => _context), TestConnectorTypes.Catalog);

        await step.ListFilesAsync(_context, tenantId, "s3://bucket", scanId, scanManifestId);

        var rows = await _context.ScanFiles.AsNoTracking().Where(f => f.ScanId == scanId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(ScanFileStatus.Received, r.Status);
            Assert.Equal(taskId, r.ScanTaskId);
            Assert.Null(r.Error);
        });
    }

    [Fact]
    public async Task RunAsync_FileFails_RecordsItFailedWithTheReasonAndStillRethrows()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = await SeedSourceAsync(_context);
        var scanId = await SeedScanAsync(_context);
        var taskId = await SeedScanTaskAsync(_context, scanId, sourceId);
        var fileLocation = PresignedUploadKey(tenantId, sourceId, scanId, taskId);
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance, new ThrowingExportSource(), new FakeTenantConnectionManager(_ => _context), TestConnectorTypes.Catalog);

        // Rethrown so the ASL Catch still routes the item to the DLQ — recording is additive.
        await Assert.ThrowsAsync<InvalidOperationException>(() => step.RunAsync(
            _context, tenantId, scanId, Guid.NewGuid(), "s3://bucket", fileLocation, fileSeq: 0));

        var row = await _context.ScanFiles.AsNoTracking().SingleAsync(f => f.FileLocation == fileLocation);
        Assert.Equal(ScanFileStatus.Failed, row.Status);
        Assert.Equal(taskId, row.ScanTaskId);
        Assert.Contains("simulated S3 read failure", row.Error);
    }

    /// <summary>
    /// Step Functions retries each Map item, so the failing attempt and the succeeding one both
    /// write. Status is a column keyed by file rather than a counter precisely so the pair
    /// converges on Ingested.
    /// </summary>
    [Fact]
    public async Task RunAsync_FailedFileLaterSucceeds_EndsUpIngestedWithNoLingeringError()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = await SeedSourceAsync(_context);
        var scanId = await SeedScanAsync(_context);
        var taskId = await SeedScanTaskAsync(_context, scanId, sourceId);
        var scanManifestId = await SeedScanManifestAsync(_context, scanId);
        var fileLocation = PresignedUploadKey(tenantId, sourceId, scanId, taskId);
        var connections = new FakeTenantConnectionManager(_ => _context);

        var failing = new ExtractAndStageStep(NullLoggerFactory.Instance, new ThrowingExportSource(), connections, TestConnectorTypes.Catalog);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.RunAsync(
            _context, tenantId, scanId, scanManifestId, "s3://bucket", fileLocation, fileSeq: 0));

        var succeeding = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new FixedExportSource(BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test")),
            connections, TestConnectorTypes.Catalog);
        await succeeding.RunAsync(_context, tenantId, scanId, scanManifestId, "s3://bucket", fileLocation, fileSeq: 0);

        var row = await _context.ScanFiles.AsNoTracking().SingleAsync(f => f.FileLocation == fileLocation);
        Assert.Equal(ScanFileStatus.Ingested, row.Status);
        Assert.Null(row.Error);
    }

    /// <summary>
    /// The list step is re-invoked on retry and on at-least-once SQS delivery. Re-seeding Received
    /// must not undo a status a Map item has since written.
    /// </summary>
    [Fact]
    public async Task ListFilesAsync_RunAgainAfterAFileFailed_DoesNotResetItToReceived()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = await SeedSourceAsync(_context);
        var scanId = await SeedScanAsync(_context);
        var taskId = await SeedScanTaskAsync(_context, scanId, sourceId);
        var fileLocation = PresignedUploadKey(tenantId, sourceId, scanId, taskId);
        var scanManifestId = await SeedScanManifestAsync(_context, scanId, [fileLocation]);
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance, new ThrowingExportSource(), new FakeTenantConnectionManager(_ => _context), TestConnectorTypes.Catalog);

        await step.ListFilesAsync(_context, tenantId, "s3://bucket", scanId, scanManifestId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => step.RunAsync(
            _context, tenantId, scanId, scanManifestId, "s3://bucket", fileLocation, fileSeq: 0));
        await step.ListFilesAsync(_context, tenantId, "s3://bucket", scanId, scanManifestId);

        var row = await _context.ScanFiles.AsNoTracking().SingleAsync(f => f.FileLocation == fileLocation);
        Assert.Equal(ScanFileStatus.Failed, row.Status);
    }

    /// <summary>
    /// "3 of 5 sources ingested" is a per-ScanTask question, and a source's files can scatter across
    /// manifests — so every row carries the task it came from, and one task's files can end a run in
    /// different states without either outcome disturbing the other.
    /// </summary>
    [Fact]
    public async Task RunAsync_OneTaskWithAMixOfOutcomes_RecordsEachFilesOwnStatusAgainstThatTask()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = await SeedSourceAsync(_context);
        var scanId = await SeedScanAsync(_context);
        var taskId = await SeedScanTaskAsync(_context, scanId, sourceId);
        var ingested = PresignedUploadKey(tenantId, sourceId, scanId, taskId);
        var failed = PresignedUploadKey(tenantId, sourceId, scanId, taskId);
        var connections = new FakeTenantConnectionManager(_ => _context);

        await new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new FixedExportSource(BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test")),
            connections, TestConnectorTypes.Catalog)
            .RunAsync(_context, tenantId, scanId, Guid.NewGuid(), "s3://bucket", ingested, fileSeq: 0);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExtractAndStageStep(NullLoggerFactory.Instance, new ThrowingExportSource(), connections, TestConnectorTypes.Catalog)
                .RunAsync(_context, tenantId, scanId, Guid.NewGuid(), "s3://bucket", failed, fileSeq: 1));

        var rows = await _context.ScanFiles.AsNoTracking()
            .Where(f => f.ScanTaskId == taskId)
            .ToDictionaryAsync(f => f.FileLocation, f => f.Status);

        Assert.Equal(2, rows.Count);
        Assert.Equal(ScanFileStatus.Ingested, rows[ingested]);
        Assert.Equal(ScanFileStatus.Failed, rows[failed]);
    }

    /// <summary>
    /// A file that did not come through the presign path has no task to attribute it to. It must
    /// still ingest — tracking is additive, not a new precondition.
    /// </summary>
    [Fact]
    public async Task RunAsync_FileNameCarriesNoTaskId_StagesAnywayAndWritesNoScanFileRow()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = await SeedSourceAsync(_context);
        var scanId = Guid.NewGuid();
        var fileLocation = UploadKey(tenantId, sourceId, scanId, "export.zip");
        var step = new ExtractAndStageStep(
            NullLoggerFactory.Instance,
            new FixedExportSource(BuildAdExportZip("cn=tuser,dc=test", "cn=tgroup,dc=test")),
            new FakeTenantConnectionManager(_ => _context), TestConnectorTypes.Catalog);

        var result = await step.RunAsync(_context, tenantId, scanId, Guid.NewGuid(), "s3://bucket", fileLocation, fileSeq: 0);

        Assert.Equal(1, result.AccountsStaged);
        Assert.Empty(await _context.ScanFiles.AsNoTracking().Where(f => f.ScanId == scanId).ToListAsync());
    }
}
