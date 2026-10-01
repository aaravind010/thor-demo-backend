using System.Text.Json;

namespace Thor.Workflows.Abstractions;

/// <summary>
/// Base for a step whose input is a single request object. Handles the deserialization every step
/// would otherwise repeat verbatim.
///
/// <para>The options instance is static on purpose. System.Text.Json caches its type metadata per
/// <see cref="JsonSerializerOptions"/> instance, so constructing a fresh one per call — which is
/// what each step used to do — rebuilds that cache on every warm Lambda invocation.</para>
/// </summary>
public abstract class WorkflowStep<TRequest> : IWorkflowStep
    where TRequest : class
{
    private static readonly JsonSerializerOptions DeserializationOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public Task<StepResult> ExecuteAsync(string inputJson, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<TRequest>(inputJson, DeserializationOptions)
            ?? throw new InvalidOperationException(
                $"Step input did not deserialize to a valid {typeof(TRequest).Name}.");

        return ExecuteAsync(request, cancellationToken);
    }

    protected abstract Task<StepResult> ExecuteAsync(TRequest request, CancellationToken cancellationToken);
}
