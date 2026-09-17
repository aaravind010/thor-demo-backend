namespace Thor.TaskApi.Services;

/// <summary>Config for the tenant uploads bucket, resolved once at startup from environment (see Program.cs).</summary>
public sealed record UploadOptions(string BucketName)
{
    public string BucketName { get; init; } = string.IsNullOrWhiteSpace(BucketName)
        ? throw new ArgumentException($"{nameof(BucketName)} is required.", nameof(BucketName))
        : BucketName;
}
