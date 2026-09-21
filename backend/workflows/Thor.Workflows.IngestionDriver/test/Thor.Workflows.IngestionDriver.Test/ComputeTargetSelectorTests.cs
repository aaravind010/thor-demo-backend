using FluentAssertions;
using NSubstitute;
using Thor.S3;
using Thor.Workflows.IngestionDriver.Core;

namespace Thor.Workflows.IngestionDriver.Test;

public sealed class ComputeTargetSelectorTests
{
    private const string Bucket = "thor-uploads-dev";
    private const long LambdaMaxBytes = 5 * 1024 * 1024;

    [Fact]
    public async Task SelectAsync_WithNoFiles_ReturnsLambdaAndZeroBytes()
    {
        var s3ObjectStore = Substitute.For<IS3ObjectStore>();
        var selector = new ComputeTargetSelector(s3ObjectStore, LambdaMaxBytes);

        var result = await selector.SelectAsync(Bucket, []);

        result.ComputeTarget.Should().Be(ComputeTarget.Lambda);
        result.TotalSizeBytes.Should().Be(0);
        result.FileCount.Should().Be(0);
    }

    [Fact]
    public async Task SelectAsync_WithTotalUnderThreshold_ReturnsLambda()
    {
        var s3ObjectStore = Substitute.For<IS3ObjectStore>();
        s3ObjectStore.GetObjectSizeAsync(Bucket, "file1.zip", Arg.Any<CancellationToken>()).Returns(1024L);
        s3ObjectStore.GetObjectSizeAsync(Bucket, "file2.zip", Arg.Any<CancellationToken>()).Returns(2048L);
        var selector = new ComputeTargetSelector(s3ObjectStore, LambdaMaxBytes);

        var result = await selector.SelectAsync(Bucket, ["file1.zip", "file2.zip"]);

        result.ComputeTarget.Should().Be(ComputeTarget.Lambda);
        result.TotalSizeBytes.Should().Be(3072);
        result.FileCount.Should().Be(2);
    }

    [Fact]
    public async Task SelectAsync_WithTotalAtThreshold_ReturnsEcsTask()
    {
        var s3ObjectStore = Substitute.For<IS3ObjectStore>();
        s3ObjectStore.GetObjectSizeAsync(Bucket, "big.zip", Arg.Any<CancellationToken>()).Returns(LambdaMaxBytes);
        var selector = new ComputeTargetSelector(s3ObjectStore, LambdaMaxBytes);

        var result = await selector.SelectAsync(Bucket, ["big.zip"]);

        result.ComputeTarget.Should().Be(ComputeTarget.EcsTask);
        result.TotalSizeBytes.Should().Be(LambdaMaxBytes);
    }

    [Fact]
    public async Task SelectAsync_WithTotalOverThreshold_ReturnsEcsTask()
    {
        var s3ObjectStore = Substitute.For<IS3ObjectStore>();
        s3ObjectStore.GetObjectSizeAsync(Bucket, "file1.zip", Arg.Any<CancellationToken>()).Returns(LambdaMaxBytes);
        s3ObjectStore.GetObjectSizeAsync(Bucket, "file2.zip", Arg.Any<CancellationToken>()).Returns(1L);
        var selector = new ComputeTargetSelector(s3ObjectStore, LambdaMaxBytes);

        var result = await selector.SelectAsync(Bucket, ["file1.zip", "file2.zip"]);

        result.ComputeTarget.Should().Be(ComputeTarget.EcsTask);
        result.TotalSizeBytes.Should().Be(LambdaMaxBytes + 1);
        result.FileCount.Should().Be(2);
    }
}
