namespace Thor.S3;

/// <summary>
/// A parsed <c>s3://bucket/key</c> URI.
/// </summary>
public sealed record S3Location(string Bucket, string Key)
{
    public static S3Location Parse(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme == "s3")
        {
            return new S3Location(parsed.Host, parsed.AbsolutePath.TrimStart('/'));
        }

        throw new ArgumentException($"Expected an 's3://bucket/key' URI, got '{uri}'.", nameof(uri));
    }
}
