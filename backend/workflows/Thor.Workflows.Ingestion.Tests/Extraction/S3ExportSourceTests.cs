using Thor.S3;
using Thor.Workflows.Ingestion.Extraction.Sources;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Extraction;

public sealed class S3ExportSourceTests
{
    private sealed class FakeS3ObjectStore : IS3ObjectStore
    {
        public byte[] ObjectBytesToReturn { get; set; } = [];
        public (string Bucket, string Key)? LastGetArgs { get; private set; }

        public Task<IReadOnlyList<string>> ListKeysAsync(string bucket, string prefix) =>
            throw new NotSupportedException("S3ExportSource never lists S3 objects.");

        public Task<byte[]> GetObjectAsync(string bucket, string key)
        {
            LastGetArgs = (bucket, key);
            return Task.FromResult(ObjectBytesToReturn);
        }

        public Task PutObjectAsync(string bucket, string key, byte[] content) =>
            throw new NotSupportedException("S3ExportSource never writes to S3.");
    }

    [Fact]
    public async Task ReadAsync_ParsesIdentifierAndDelegatesToObjectStore()
    {
        var expectedBytes = new byte[] { 1, 2, 3, 4 };
        var store = new FakeS3ObjectStore { ObjectBytesToReturn = expectedBytes };
        var source = new S3ExportSource(store);

        var result = await source.ReadAsync("s3://bucket/ad/2026/08/export.zip");

        Assert.Equal(("bucket", "ad/2026/08/export.zip"), store.LastGetArgs);
        Assert.Equal(expectedBytes, result);
    }
}
