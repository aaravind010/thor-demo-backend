namespace Thor.Workflows.Abstractions.Compute;

/// <summary>
/// The shared compute-selection rule: run on Lambda while the estimate fits under a byte threshold,
/// otherwise on Fargate, where there is no 15-minute ceiling and more memory to work with.
///
/// <para>Deliberately the whole policy. It is one comparison, and every workflow wants the same one —
/// what differs between them is the <see cref="ComputeEstimate"/>, which is why that is the interface
/// and this is not.</para>
/// </summary>
public sealed class SizeThresholdPolicy(long lambdaMaxBytes)
{
    public const string LambdaMaxBytesVariable = "THOR_COMPUTE_LAMBDA_MAX_BYTES";

    private const long DefaultLambdaMaxBytes = 5 * 1024 * 1024;

    /// <summary>Terraform sets <see cref="LambdaMaxBytesVariable"/> on every step's task definition and Lambda function.</summary>
    public static SizeThresholdPolicy FromEnvironment() =>
        new(long.TryParse(Environment.GetEnvironmentVariable(LambdaMaxBytesVariable), out var configured)
            ? configured
            : DefaultLambdaMaxBytes);

    /// <summary>The comparison is exclusive: an estimate exactly at the threshold goes to Fargate.</summary>
    public ComputePlan Decide(ComputeEstimate estimate) =>
        new(
            estimate.TotalBytes < lambdaMaxBytes ? ComputeTarget.Lambda : ComputeTarget.EcsTask,
            estimate.TotalBytes,
            estimate.ItemCount);
}
