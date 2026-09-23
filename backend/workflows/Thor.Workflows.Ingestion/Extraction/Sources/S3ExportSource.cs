using Thor.S3;

namespace Thor.Workflows.Ingestion.Extraction.Sources;

/// <summary>Reads export archives from S3. Identifiers are <c>s3://bucket/key</c> URIs.</summary>
public sealed class S3ExportSource(IS3ObjectStore objectStore) : IExportSource
{
    public async Task<byte[]> ReadAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var loc = S3Location.Parse(identifier);
        return await objectStore.GetObjectAsync(loc.Bucket, loc.Key, cancellationToken);
    }
}
