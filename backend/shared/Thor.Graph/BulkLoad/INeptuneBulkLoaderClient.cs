namespace Thor.Graph.BulkLoad;

/// <summary>
/// Neptune's S3 bulk loader HTTP API (<c>/loader</c>) — distinct from the Gremlin client
/// (<see cref="GraphVertexStore"/>/<see cref="GraphEdgeStore"/>), which never uses this endpoint.
/// </summary>
public interface INeptuneBulkLoaderClient
{
    /// <summary>Starts a load job for every CSV file under <paramref name="s3SourceUri"/>. <paramref name="iamRoleArn"/> is the role the Neptune cluster itself assumes to read from S3 — unrelated to how this client authenticates to Neptune's HTTP endpoint (network-only, per <see cref="NeptuneOptions"/>).</summary>
    Task<BulkLoadStartResult> StartLoadAsync(Uri s3SourceUri, string iamRoleArn, string region, CancellationToken cancellationToken = default);

    Task<BulkLoadStatusResult> GetLoadStatusAsync(string loadId, CancellationToken cancellationToken = default);
}
