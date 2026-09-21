using System.Text.Json;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Thor.CreateManifest.Core;

namespace Thor.CreateManifest.Function;

/// <summary>Config for the single ingestion Step Functions state machine this Lambda triggers.</summary>
public sealed record StepFunctionsOptions(string StateMachineArn);

/// <summary>
/// Starts the ingestion Step Functions execution for a manifest, using a deterministic execution
/// name so a duplicate trigger is rejected by Step Functions rather than double-triggering.
/// </summary>
public sealed class StepFunctionsIngestionTrigger(IAmazonStepFunctions stepFunctions, StepFunctionsOptions options) : IIngestionTrigger
{
    public async Task TriggerAsync(IngestionTriggerRequest request, CancellationToken cancellationToken = default)
    {
        // RunId is always emitted (as null) rather than omitted: the state machine forwards it with
        // "RunId.$": "$.RunId", and a missing path is an uncatchable States.Runtime error, whereas a
        // null value passes through. A null RunId means workflow tracking keys on ScanManifestId
        // (Thor.Workflows.Abstractions.WorkflowLifecycle).
        var input = JsonSerializer.Serialize(new
        {
            TenantId = request.TenantId,
            ExportLocation = request.ExportLocation,
            ScanId = request.ScanId,
            ScanManifestId = request.ScanManifestId,
            BatchSeq = 0,
            RunId = (Guid?)null,
        });

        try
        {
            await stepFunctions.StartExecutionAsync(new StartExecutionRequest
            {
                StateMachineArn = options.StateMachineArn,
                Name = $"manifest-{request.ScanManifestId}",
                Input = input,
            }, cancellationToken);
        }
        catch (ExecutionAlreadyExistsException)
        {
            // A prior attempt already started this exact execution — already triggered, not an error.
        }
    }
}
