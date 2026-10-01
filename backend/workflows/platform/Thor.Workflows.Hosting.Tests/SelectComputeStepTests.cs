using Thor.Workflows.Abstractions.Compute;
using Thor.Workflows.Hosting.Compute;
using Xunit;

namespace Thor.Workflows.Hosting.Tests;

public sealed class SelectComputeStepTests
{
    private sealed record SampleRequest(Guid TenantId, string ExportLocation);

    private sealed class StubEstimator(ComputeEstimate estimate) : IComputeEstimator<SampleRequest>
    {
        public SampleRequest? Received { get; private set; }

        public Task<ComputeEstimate> EstimateAsync(SampleRequest request, CancellationToken cancellationToken)
        {
            Received = request;
            return Task.FromResult(estimate);
        }
    }

    private sealed class ThrowingEstimator : IComputeEstimator<SampleRequest>
    {
        public Task<ComputeEstimate> EstimateAsync(SampleRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("no manifest for that id");
    }

    private static string RequestJson(Guid tenantId) =>
        $$"""{"TenantId":"{{tenantId}}","ExportLocation":"s3://thor-uploads-dev"}""";

    [Fact]
    public async Task ExecuteAsync_SmallRun_PlansForLambda()
    {
        var step = new SelectComputeStep<SampleRequest>(
            new StubEstimator(new ComputeEstimate(TotalBytes: 1024, ItemCount: 2)),
            new SizeThresholdPolicy(lambdaMaxBytes: 5 * 1024 * 1024));

        var result = await step.ExecuteAsync(RequestJson(Guid.NewGuid()), CancellationToken.None);

        var plan = Assert.IsType<ComputePlan>(result.Value);
        Assert.Equal(ComputeTarget.Lambda, plan.Target);
        Assert.Equal(1024, plan.TotalBytes);
        Assert.Equal(2, plan.ItemCount);
        Assert.False(result.IsInProgress);
    }

    [Fact]
    public async Task ExecuteAsync_LargeRun_PlansForEcsTask()
    {
        var step = new SelectComputeStep<SampleRequest>(
            new StubEstimator(new ComputeEstimate(TotalBytes: 50 * 1024 * 1024, ItemCount: 900)),
            new SizeThresholdPolicy(lambdaMaxBytes: 5 * 1024 * 1024));

        var result = await step.ExecuteAsync(RequestJson(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ComputeTarget.EcsTask, Assert.IsType<ComputePlan>(result.Value).Target);
    }

    /// <summary>Inherited from WorkflowStep&lt;TRequest&gt;: the estimator sees a typed request, not raw JSON.</summary>
    [Fact]
    public async Task ExecuteAsync_DeserializesBeforeHandingTheRequestToTheEstimator()
    {
        var estimator = new StubEstimator(new ComputeEstimate(0, 0));
        var step = new SelectComputeStep<SampleRequest>(estimator, new SizeThresholdPolicy(1));
        var tenantId = Guid.NewGuid();

        await step.ExecuteAsync(RequestJson(tenantId), CancellationToken.None);

        Assert.Equal(tenantId, estimator.Received!.TenantId);
        Assert.Equal("s3://thor-uploads-dev", estimator.Received.ExportLocation);
    }

    /// <summary>
    /// No fallback target on failure. Guessing here would silently run a multi-gigabyte manifest on a
    /// 15-minute Lambda; failing lets the state machine's Catch send it to the DLQ with the reason.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_EstimatorThrows_Propagates()
    {
        var step = new SelectComputeStep<SampleRequest>(new ThrowingEstimator(), new SizeThresholdPolicy(1));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => step.ExecuteAsync(RequestJson(Guid.NewGuid()), CancellationToken.None));
    }
}
