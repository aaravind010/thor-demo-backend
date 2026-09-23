using System.Text.Json;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using FluentAssertions;
using NSubstitute;
using Thor.CreateManifest.Core;
using Thor.CreateManifest.Function;

namespace Thor.CreateManifest.Test;

/// <summary>
/// Covers the execution input <see cref="StepFunctionsIngestionTrigger"/> hands the ingestion state machine.
///
/// <para><b>Where this sits in CreateManifest.</b> An upload lands in S3, flows S3 → SQS → EventBridge Pipe → this
/// Lambda. <c>ManifestBatchProcessor</c> groups the uploaded keys into a <c>ScanManifest</c> row, then calls
/// <see cref="IIngestionTrigger.TriggerAsync"/>. <see cref="StepFunctionsIngestionTrigger"/> is the real
/// implementation: it serialises the manifest's identity into JSON and calls <c>states:StartExecution</c> on
/// <c>thor-&lt;env&gt;-ingestion-sf</c> (infra/src/modules/ingestion/state_machine.tf). That JSON is the
/// state machine's execution input — every downstream step (driver, extract-stage, promote, graph-load)
/// reads its <c>TenantId</c>/<c>ScanId</c>/<c>ScanManifestId</c> from it.</para>
///
/// <para><b>Why this test exists.</b> The state machine forwards fields by JSONPath, e.g.
/// <c>"RunId.$": "$.RunId"</c>. In Step Functions a path that points at a <i>missing</i> key raises
/// <c>States.Runtime</c>, which no <c>Retry</c> or <c>Catch</c> can intercept — the execution dies on its
/// first step with nothing sent to the DLQ. A <c>null</c> value, by contrast, passes through fine. So the
/// trigger must always emit <c>RunId</c> (as <c>null</c> until something mints real run ids), and this test
/// is the only thing that would catch someone dropping it from the anonymous object again. It also pins the
/// deterministic execution name (<c>manifest-{ScanManifestId}</c>) and that a duplicate trigger is treated as
/// already-started rather than an error.</para>
/// </summary>
public class StepFunctionsIngestionTriggerTests
{
    private const string StateMachineArn = "arn:aws:states:us-east-1:123456789012:stateMachine:thor-dev-ingestion-sf";

    private readonly IAmazonStepFunctions _stepFunctions = Substitute.For<IAmazonStepFunctions>();
    private readonly StepFunctionsIngestionTrigger _trigger;

    public StepFunctionsIngestionTriggerTests()
    {
        _trigger = new StepFunctionsIngestionTrigger(_stepFunctions, new StepFunctionsOptions(StateMachineArn));
    }

    [Fact]
    public async Task TriggerAsync_ExecutionInput_CarriesEveryFieldTheStateMachineReferences()
    {
        // The state machine forwards RunId with "RunId.$": "$.RunId" — a missing key is an uncatchable
        // States.Runtime error, whereas a null value passes through — so RunId must always be present.
        var request = new IngestionTriggerRequest(Guid.NewGuid(), "s3://thor-uploads-dev", Guid.NewGuid(), Guid.NewGuid());
        StartExecutionRequest? started = null;
        _stepFunctions.StartExecutionAsync(Arg.Do<StartExecutionRequest>(r => started = r), Arg.Any<CancellationToken>())
            .Returns(new StartExecutionResponse());

        await _trigger.TriggerAsync(request);

        started.Should().NotBeNull();
        started!.StateMachineArn.Should().Be(StateMachineArn);
        started.Name.Should().Be($"manifest-{request.ScanManifestId}");

        using var input = JsonDocument.Parse(started.Input);
        var root = input.RootElement;
        root.GetProperty("TenantId").GetGuid().Should().Be(request.TenantId);
        root.GetProperty("ExportLocation").GetString().Should().Be(request.ExportLocation);
        root.GetProperty("ScanId").GetGuid().Should().Be(request.ScanId);
        root.GetProperty("ScanManifestId").GetGuid().Should().Be(request.ScanManifestId);
        root.GetProperty("BatchSeq").GetInt32().Should().Be(0);
        root.TryGetProperty("RunId", out var runId).Should().BeTrue("the state machine reads $.RunId on its first step");
        runId.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task TriggerAsync_DuplicateExecution_IsNotAnError()
    {
        _stepFunctions.StartExecutionAsync(Arg.Any<StartExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns<StartExecutionResponse>(_ => throw new ExecutionAlreadyExistsException("already running"));

        var act = () => _trigger.TriggerAsync(new IngestionTriggerRequest(Guid.NewGuid(), "s3://thor-uploads-dev", Guid.NewGuid(), Guid.NewGuid()));

        await act.Should().NotThrowAsync();
    }
}
