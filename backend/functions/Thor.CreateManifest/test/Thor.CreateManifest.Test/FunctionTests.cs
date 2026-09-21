using System.Text.Json;
using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Thor.CreateManifest.Core;
using Thor.DataConnectionManager.Routing;
using Thor.DataLayer.Models;
using Thor.DataLayer.Models.Tenants;
using LambdaFunction = Thor.CreateManifest.Function.Function;
using SqsPipeMessage = Thor.CreateManifest.Function.SqsPipeMessage;

namespace Thor.CreateManifest.Test;

/// <summary>Covers <see cref="LambdaFunction.FunctionHandler"/>'s EventBridge Pipe SQS envelope parsing.</summary>
public class FunctionTests
{
    private readonly IScanManifestStore _manifestStore = Substitute.For<IScanManifestStore>();
    private readonly ITenantRoutingResolver _tenantRoutingResolver = Substitute.For<ITenantRoutingResolver>();
    private readonly IIngestionTrigger _ingestionTrigger = Substitute.For<IIngestionTrigger>();
    private readonly ILambdaContext _context = Substitute.For<ILambdaContext>();

    public FunctionTests()
    {
        _context.Logger.Returns(Substitute.For<ILambdaLogger>());
        _tenantRoutingResolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new TenantRouting());
        _manifestStore.GetManifestsForScanAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ScanManifest>());
        _manifestStore.CreateManifestAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new ScanManifest
            {
                Id = Guid.NewGuid(),
                ScanId = callInfo.ArgAt<Guid>(1),
                FileLocations = callInfo.ArgAt<IReadOnlyList<string>>(2).ToArray(),
                Status = "pending",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
    }

    private LambdaFunction CreateFunction()
    {
        var processor = new ManifestBatchProcessor(_manifestStore, _tenantRoutingResolver, _ingestionTrigger, NullLogger<ManifestBatchProcessor>.Instance);
        var services = new ServiceCollection();
        services.AddSingleton(processor);
        return new LambdaFunction(services.BuildServiceProvider());
    }

    /// <summary>Builds one envelope shaped like an EventBridge Pipe's raw SQS message: a "body" string containing an S3 event notification.</summary>
    private static SqsPipeMessage BuildEnvelope(string messageId, string bucket, string key)
    {
        var s3NotificationJson = JsonSerializer.Serialize(new
        {
            Records = new[]
            {
                new
                {
                    eventVersion = "2.6",
                    eventSource = "aws:s3",
                    awsRegion = "us-east-1",
                    eventTime = "2026-09-15T13:56:01.697Z",
                    eventName = "ObjectCreated:Put",
                    s3 = new
                    {
                        s3SchemaVersion = "1.0",
                        configurationId = "tf-s3-queue-1",
                        bucket = new { name = bucket, arn = $"arn:aws:s3:::{bucket}" },
                        @object = new { key, size = 1673807, eTag = "98a124664c49a2e45b3d477321ab78e5", sequencer = "006AA94E719C713FF9" },
                    },
                },
            },
        });

        return new SqsPipeMessage(messageId, s3NotificationJson);
    }

    [Fact]
    public async Task FunctionHandler_EventBridgePipeArrayOfSqsEnvelopes_ExtractsUploadEventAndCreatesManifest()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var scanId = Guid.NewGuid();
        var key = $"tenants/{tenantId}/uploads/{scanId}/{sourceId}/0a6aaa4f-e898-4cc7-919b-e6680c7e0a95.zip";
        var envelope = BuildEnvelope("e41acc9e-6285-4731-b165-1d0cd18352fb", "thor-dev-ingestion-877969058937", key);

        // The real invocation payload is a bare JSON array of envelopes (not a `{"Records": [...]}`
        // object) — round-trip through JSON to prove `SqsPipeMessage` deserializes that shape.
        var records = JsonSerializer.Deserialize<List<SqsPipeMessage>>(JsonSerializer.Serialize(new[] { envelope }))!;

        await CreateFunction().FunctionHandler(records, _context);

        await _manifestStore.Received(1).CreateManifestAsync(
            tenantId, scanId,
            Arg.Is<IReadOnlyList<string>>(keys => keys != null && keys.Count == 1 && keys[0] == key),
            Arg.Any<CancellationToken>());
        await _ingestionTrigger.Received(1).TriggerAsync(Arg.Any<IngestionTriggerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FunctionHandler_MultipleEnvelopesInOneBatch_ExtractsAllUploadEvents()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var scanId = Guid.NewGuid();
        var keyA = $"tenants/{tenantId}/uploads/{scanId}/{sourceId}/a.zip";
        var keyB = $"tenants/{tenantId}/uploads/{scanId}/{sourceId}/b.zip";
        var envelopes = new[]
        {
            BuildEnvelope("e41acc9e-6285-4731-b165-1d0cd18352fb", "thor-dev-ingestion-877969058937", keyA),
            BuildEnvelope("5318d05a-4bc9-478f-b032-4c5fd0a072ce", "thor-dev-ingestion-877969058937", keyB),
        };
        var records = JsonSerializer.Deserialize<List<SqsPipeMessage>>(JsonSerializer.Serialize(envelopes))!;

        await CreateFunction().FunctionHandler(records, _context);

        await _manifestStore.Received(1).CreateManifestAsync(
            tenantId, scanId,
            Arg.Is<IReadOnlyList<string>>(keys => keys != null && keys.Count == 2 && keys.Contains(keyA) && keys.Contains(keyB)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FunctionHandler_UnparseableBody_SkipsRecordWithoutThrowing()
    {
        var records = new List<SqsPipeMessage> { new("bad-message-id", "not valid json") };

        await CreateFunction().FunctionHandler(records, _context);

        await _manifestStore.DidNotReceiveWithAnyArgs().CreateManifestAsync(default, default, default!, default);
        await _ingestionTrigger.DidNotReceiveWithAnyArgs().TriggerAsync(default!, default);
    }
}
