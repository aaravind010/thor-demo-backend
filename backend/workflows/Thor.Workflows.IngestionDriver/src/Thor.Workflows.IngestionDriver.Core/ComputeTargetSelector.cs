using Thor.S3;

namespace Thor.Workflows.IngestionDriver.Core;

/// <summary>
/// Sums the size of every file a manifest lists and picks a compute target: <see cref="Core.ComputeTarget.Lambda"/>
/// when the total is under <paramref name="lambdaMaxBytes"/>, otherwise <see cref="Core.ComputeTarget.EcsTask"/>.
/// </summary>
public sealed class ComputeTargetSelector(IS3ObjectStore s3ObjectStore, long lambdaMaxBytes)
{
    public async Task<ComputeSelectionResult> SelectAsync(string bucket, IReadOnlyList<string> fileLocations, CancellationToken cancellationToken = default)
    {
        long totalSizeBytes = 0;
        foreach (var key in fileLocations)
        {
            totalSizeBytes += await s3ObjectStore.GetObjectSizeAsync(bucket, key, cancellationToken);
        }

        var target = totalSizeBytes < lambdaMaxBytes ? ComputeTarget.Lambda : ComputeTarget.EcsTask;
        return new ComputeSelectionResult(target, totalSizeBytes, fileLocations.Count);
    }
}
