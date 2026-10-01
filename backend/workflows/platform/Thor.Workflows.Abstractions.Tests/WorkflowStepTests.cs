using System.Text.Json;
using Thor.Workflows.Abstractions;
using Xunit;

namespace Thor.Workflows.Abstractions.Tests;

public sealed class WorkflowStepTests
{
    private sealed record SampleRequest(Guid TenantId, string ExportLocation, int BatchSeq = 0);

    private sealed class CapturingStep : WorkflowStep<SampleRequest>
    {
        public SampleRequest? Received { get; private set; }

        protected override Task<StepResult> ExecuteAsync(SampleRequest request, CancellationToken cancellationToken)
        {
            Received = request;
            return Task.FromResult(StepResult.Completed(request.BatchSeq));
        }
    }

    [Fact]
    public async Task ExecuteAsync_DeserializesIntoTheRequestType()
    {
        var step = new CapturingStep();
        var tenantId = Guid.NewGuid();

        var result = await step.ExecuteAsync(
            $$"""{"TenantId":"{{tenantId}}","ExportLocation":"s3://thor-uploads-dev","BatchSeq":7}""",
            CancellationToken.None);

        Assert.Equal(tenantId, step.Received!.TenantId);
        Assert.Equal("s3://thor-uploads-dev", step.Received.ExportLocation);
        Assert.Equal(7, result.Value);
    }

    /// <summary>
    /// The two compute targets deliver the payload differently — a THOR_INPUT env var on ECS, the raw
    /// event body on Lambda — and Step Functions emits PascalCase while the connector payloads are
    /// camelCase. Case-insensitive binding is what lets one step class serve both.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_BindsCamelCaseAndPascalCaseAlike()
    {
        var step = new CapturingStep();
        var tenantId = Guid.NewGuid();

        await step.ExecuteAsync(
            $$"""{"tenantId":"{{tenantId}}","exportLocation":"s3://thor-uploads-dev"}""",
            CancellationToken.None);

        Assert.Equal(tenantId, step.Received!.TenantId);
        Assert.Equal("s3://thor-uploads-dev", step.Received.ExportLocation);
    }

    [Fact]
    public async Task ExecuteAsync_NullPayload_ThrowsNamingTheRequestType()
    {
        var step = new CapturingStep();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => step.ExecuteAsync("null", CancellationToken.None));

        Assert.Contains(nameof(SampleRequest), ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_MalformedPayload_ThrowsJsonException()
    {
        var step = new CapturingStep();

        await Assert.ThrowsAsync<JsonException>(
            () => step.ExecuteAsync("{not json", CancellationToken.None));
    }
}
