using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Thor.CreateManifest.Core;
using Thor.DataConnectionManager.Routing;
using Thor.DataLayer.Models;
using Thor.DataLayer.Models.Tenants;

namespace Thor.CreateManifest.Test;

/// <summary>Covers <see cref="ManifestBatchProcessor"/>'s segregate-and-write and dedup/retry behavior.</summary>
public class ManifestBatchProcessorTests
{
    private readonly IScanManifestStore _manifestStore = Substitute.For<IScanManifestStore>();
    private readonly ITenantRoutingResolver _tenantRoutingResolver = Substitute.For<ITenantRoutingResolver>();
    private readonly IIngestionTrigger _ingestionTrigger = Substitute.For<IIngestionTrigger>();

    private ManifestBatchProcessor CreateProcessor() =>
        new(_manifestStore, _tenantRoutingResolver, _ingestionTrigger, NullLogger<ManifestBatchProcessor>.Instance);

    private static string Key(Guid tenantId, Guid sourceId, Guid scanId, string fileName) =>
        $"tenants/{tenantId}/uploads/{scanId}/{sourceId}/{fileName}";

    private static ScanManifest Manifest(Guid scanId, string status, params string[] fileLocations) => new()
    {
        Id = Guid.NewGuid(),
        ScanId = scanId,
        FileLocations = fileLocations,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    public ManifestBatchProcessorTests()
    {
        _tenantRoutingResolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new TenantRouting());
        // Default: no manifests exist yet for any scan — individual tests override this to
        // exercise the dedup/retry paths.
        _manifestStore.GetManifestsForScanAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ScanManifest>());
        _manifestStore.CreateManifestAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Manifest(callInfo.ArgAt<Guid>(1), "pending", callInfo.ArgAt<IReadOnlyList<string>>(2).ToArray()));
    }

    [Fact]
    public async Task ProcessBatchAsync_BatchSpansTwoScans_CreatesTwoManifestsAndTriggersBoth()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var scanId1 = Guid.NewGuid();
        var scanId2 = Guid.NewGuid();
        var events = new[]
        {
            new UploadEvent("bucket", Key(tenantId, sourceId, scanId1, "a.zip")),
            new UploadEvent("bucket", Key(tenantId, sourceId, scanId2, "b.zip")),
        };

        await CreateProcessor().ProcessBatchAsync(events);

        await _manifestStore.Received(1).CreateManifestAsync(tenantId, scanId1, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await _manifestStore.Received(1).CreateManifestAsync(tenantId, scanId2, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await _ingestionTrigger.Received(2).TriggerAsync(Arg.Any<IngestionTriggerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_TenantFailsVerification_DropsRecordWithoutWriting()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var scanId = Guid.NewGuid();
        var events = new[] { new UploadEvent("bucket", Key(tenantId, sourceId, scanId, "a.zip")) };
        _tenantRoutingResolver.ResolveAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns<Task<TenantRouting>>(_ => throw new InvalidOperationException("tenant not found"));

        await CreateProcessor().ProcessBatchAsync(events);

        await _manifestStore.DidNotReceiveWithAnyArgs().CreateManifestAsync(default, default, default!, default);
        await _ingestionTrigger.DidNotReceiveWithAnyArgs().TriggerAsync(default!, default);
    }

    [Fact]
    public async Task ProcessBatchAsync_BatchSpansOneScanTwoSources_CreatesOneManifestWithAllKeys()
    {
        var tenantId = Guid.NewGuid();
        var adSourceId = Guid.NewGuid();
        var cyberArkSourceId = Guid.NewGuid();
        var scanId = Guid.NewGuid();
        var adKey = Key(tenantId, adSourceId, scanId, "ad.zip");
        var cyberArkKey = Key(tenantId, cyberArkSourceId, scanId, "cyberark.zip");
        var events = new[] { new UploadEvent("bucket", adKey), new UploadEvent("bucket", cyberArkKey) };

        await CreateProcessor().ProcessBatchAsync(events);

        await _manifestStore.Received(1).CreateManifestAsync(
            tenantId, scanId,
            Arg.Is<IReadOnlyList<string>>(keys => keys != null && keys.Count == 2 && keys.Contains(adKey) && keys.Contains(cyberArkKey)),
            Arg.Any<CancellationToken>());
        await _ingestionTrigger.Received(1).TriggerAsync(Arg.Any<IngestionTriggerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_TriggerSucceeds_MarksManifestProcessing()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var scanId = Guid.NewGuid();
        var events = new[] { new UploadEvent("bucket", Key(tenantId, sourceId, scanId, "a.zip")) };

        await CreateProcessor().ProcessBatchAsync(events);

        await _manifestStore.Received(1).MarkProcessingAsync(tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_UnparseableKey_IsSkippedWithoutThrowing()
    {
        var events = new[] { new UploadEvent("bucket", "not-a-valid-upload-key.zip") };

        await CreateProcessor().ProcessBatchAsync(events);

        await _manifestStore.DidNotReceiveWithAnyArgs().CreateManifestAsync(default, default, default!, default);
    }

    [Fact]
    public async Task ProcessBatchAsync_AllKeysAlreadyCoveredByProcessingManifest_IsANoOp()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var scanId = Guid.NewGuid();
        var key = Key(tenantId, sourceId, scanId, "a.zip");
        var events = new[] { new UploadEvent("bucket", key) };
        _manifestStore.GetManifestsForScanAsync(tenantId, scanId, Arg.Any<CancellationToken>())
            .Returns([Manifest(scanId, "processing", key)]);

        await CreateProcessor().ProcessBatchAsync(events);

        await _manifestStore.DidNotReceiveWithAnyArgs().CreateManifestAsync(default, default, default!, default);
        await _ingestionTrigger.DidNotReceiveWithAnyArgs().TriggerAsync(default!, default);
        await _manifestStore.DidNotReceiveWithAnyArgs().MarkProcessingAsync(default, default, default);
    }

    [Fact]
    public async Task ProcessBatchAsync_SomeKeysAlreadyCovered_CreatesManifestWithOnlyNewKeys()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var scanId = Guid.NewGuid();
        var existingKey = Key(tenantId, sourceId, scanId, "a.zip");
        var newKey = Key(tenantId, sourceId, scanId, "b.zip");
        var events = new[] { new UploadEvent("bucket", existingKey), new UploadEvent("bucket", newKey) };
        _manifestStore.GetManifestsForScanAsync(tenantId, scanId, Arg.Any<CancellationToken>())
            .Returns([Manifest(scanId, "processing", existingKey)]);

        await CreateProcessor().ProcessBatchAsync(events);

        await _manifestStore.Received(1).CreateManifestAsync(
            tenantId, scanId, Arg.Is<IReadOnlyList<string>>(keys => keys != null && keys.Count == 1 && keys[0] == newKey), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_RetryOfPendingManifestNeverTriggered_ReTriggersSameManifest()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var scanId = Guid.NewGuid();
        var key = Key(tenantId, sourceId, scanId, "a.zip");
        var events = new[] { new UploadEvent("bucket", key) };
        var pendingManifest = Manifest(scanId, "pending", key);
        _manifestStore.GetManifestsForScanAsync(tenantId, scanId, Arg.Any<CancellationToken>())
            .Returns([pendingManifest]);

        await CreateProcessor().ProcessBatchAsync(events);

        await _manifestStore.DidNotReceiveWithAnyArgs().CreateManifestAsync(default, default, default!, default);
        await _ingestionTrigger.Received(1).TriggerAsync(
            Arg.Is<IngestionTriggerRequest>(r => r != null && r.ScanManifestId == pendingManifest.Id), Arg.Any<CancellationToken>());
        await _manifestStore.Received(1).MarkProcessingAsync(tenantId, pendingManifest.Id, Arg.Any<CancellationToken>());
    }
}
