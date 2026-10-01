using Thor.Workflows.Abstractions.Compute;
using Xunit;

namespace Thor.Workflows.Abstractions.Tests;

/// <summary>
/// Ported from Thor.Workflows.IngestionDriver's ComputeTargetSelectorTests. Those needed a fake S3
/// client to reach the one comparison they were actually asserting; splitting the estimate out into
/// <see cref="IComputeEstimator{TRequest}"/> leaves the policy testable on its own.
/// </summary>
public sealed class SizeThresholdPolicyTests
{
    private const long LambdaMaxBytes = 5 * 1024 * 1024;

    private static readonly SizeThresholdPolicy Policy = new(LambdaMaxBytes);

    [Fact]
    public void Decide_UnderThreshold_ChoosesLambda()
    {
        var plan = Policy.Decide(new ComputeEstimate(TotalBytes: LambdaMaxBytes - 1, ItemCount: 3));

        Assert.Equal(ComputeTarget.Lambda, plan.Target);
    }

    /// <summary>The comparison is exclusive — at the threshold exactly, Fargate wins.</summary>
    [Fact]
    public void Decide_ExactlyAtThreshold_ChoosesEcsTask()
    {
        var plan = Policy.Decide(new ComputeEstimate(TotalBytes: LambdaMaxBytes, ItemCount: 1));

        Assert.Equal(ComputeTarget.EcsTask, plan.Target);
    }

    [Fact]
    public void Decide_OverThreshold_ChoosesEcsTask()
    {
        var plan = Policy.Decide(new ComputeEstimate(TotalBytes: LambdaMaxBytes + 1, ItemCount: 2));

        Assert.Equal(ComputeTarget.EcsTask, plan.Target);
    }

    [Fact]
    public void Decide_EmptyRun_ChoosesLambda()
    {
        var plan = Policy.Decide(new ComputeEstimate(TotalBytes: 0, ItemCount: 0));

        Assert.Equal(ComputeTarget.Lambda, plan.Target);
    }

    /// <summary>The evidence rides along on the plan so an execution history shows why a run went the way it did.</summary>
    [Fact]
    public void Decide_CarriesTheEstimateThrough()
    {
        var plan = Policy.Decide(new ComputeEstimate(TotalBytes: 1234, ItemCount: 7));

        Assert.Equal(1234, plan.TotalBytes);
        Assert.Equal(7, plan.ItemCount);
    }

    [Fact]
    public void FromEnvironment_UnsetVariable_FallsBackToTheDefault()
    {
        var original = Environment.GetEnvironmentVariable(SizeThresholdPolicy.LambdaMaxBytesVariable);
        try
        {
            Environment.SetEnvironmentVariable(SizeThresholdPolicy.LambdaMaxBytesVariable, null);

            var plan = SizeThresholdPolicy.FromEnvironment().Decide(new ComputeEstimate(LambdaMaxBytes - 1, 1));

            Assert.Equal(ComputeTarget.Lambda, plan.Target);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SizeThresholdPolicy.LambdaMaxBytesVariable, original);
        }
    }

    [Fact]
    public void FromEnvironment_ReadsTheConfiguredThreshold()
    {
        var original = Environment.GetEnvironmentVariable(SizeThresholdPolicy.LambdaMaxBytesVariable);
        try
        {
            Environment.SetEnvironmentVariable(SizeThresholdPolicy.LambdaMaxBytesVariable, "100");

            var policy = SizeThresholdPolicy.FromEnvironment();

            Assert.Equal(ComputeTarget.Lambda, policy.Decide(new ComputeEstimate(99, 1)).Target);
            Assert.Equal(ComputeTarget.EcsTask, policy.Decide(new ComputeEstimate(100, 1)).Target);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SizeThresholdPolicy.LambdaMaxBytesVariable, original);
        }
    }
}
