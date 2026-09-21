namespace Thor.S3;

/// <summary>
/// The S3 mechanics a connector export source needs: paginated key listing under a prefix,
/// and buffered object reads. Bucket-wide policy (which keys count, what a "location" string
/// means) belongs to the caller, not here.
/// </summary>
public interface IS3ObjectStore
{
    /// <summary>Lists every object key under <paramref name="prefix"/> in <paramref name="bucket"/>, paginating as needed.</summary>
    Task<IReadOnlyList<string>> ListKeysAsync(string bucket, string prefix, CancellationToken cancellationToken = default);

    /// <summary>Reads one object's full contents into memory.</summary>
    Task<byte[]> GetObjectAsync(string bucket, string key, CancellationToken cancellationToken = default);

    /// <summary>Reads only the size of an object, without downloading its contents.</summary>
    Task<long> GetObjectSizeAsync(string bucket, string key, CancellationToken cancellationToken = default);

    /// <summary>Writes <paramref name="content"/> to <paramref name="key"/>, overwriting any existing object.</summary>
    Task PutObjectAsync(string bucket, string key, byte[] content, CancellationToken cancellationToken = default);
}
