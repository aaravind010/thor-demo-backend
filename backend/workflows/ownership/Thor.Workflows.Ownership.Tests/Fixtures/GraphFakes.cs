using System.Text;
using Thor.Graph.BulkLoad;
using Thor.S3;

namespace Thor.Workflows.Ownership.Tests.Fixtures;

/// <summary>Records every object the graph-load step writes; nothing reads from it.</summary>
public sealed class FakeS3ObjectStore : IS3ObjectStore
{
    private readonly object _lock = new();

    public List<(string Bucket, string Key, string Content)> PutObjects { get; } = [];

    public Task<IReadOnlyList<string>> ListKeysAsync(string bucket, string prefix, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<byte[]> GetObjectAsync(string bucket, string key, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<long> GetObjectSizeAsync(string bucket, string key, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task PutObjectAsync(string bucket, string key, byte[] content, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            PutObjects.Add((bucket, key, Encoding.UTF8.GetString(content)));
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Starts loads that always succeed and answers status queries from a script: each call to
/// <see cref="GetLoadStatusAsync"/> takes the next status, and the last one repeats.
/// </summary>
public sealed class FakeBulkLoaderClient(params BulkLoadStatus[] statuses) : INeptuneBulkLoaderClient
{
    private int _statusCalls;

    public List<Uri> StartedLoads { get; } = [];

    public bool FailStart { get; init; }

    public long InsertErrors { get; init; }

    public Task<BulkLoadStartResult> StartLoadAsync(Uri s3SourceUri, string iamRoleArn, string region, CancellationToken cancellationToken = default)
    {
        if (FailStart)
        {
            throw new InvalidOperationException("Neptune rejected the load.");
        }

        StartedLoads.Add(s3SourceUri);
        return Task.FromResult(new BulkLoadStartResult($"load-{StartedLoads.Count}"));
    }

    public Task<BulkLoadStatusResult> GetLoadStatusAsync(string loadId, CancellationToken cancellationToken = default)
    {
        var status = statuses.Length == 0 ? BulkLoadStatus.Completed : statuses[Math.Min(_statusCalls, statuses.Length - 1)];
        _statusCalls++;
        return Task.FromResult(new BulkLoadStatusResult(
            loadId, status, status.ToString(), TotalRecords: 1, ParsingErrors: 0, DatatypeMismatchErrors: 0,
            InsertErrors: InsertErrors, ErrorMessages: status == BulkLoadStatus.Failed ? ["boom"] : []));
    }
}
