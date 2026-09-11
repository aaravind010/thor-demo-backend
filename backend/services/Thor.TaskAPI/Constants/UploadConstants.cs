namespace Thor.TaskApi.Constants;

/// <summary>Shared constants for the presigned upload endpoint.</summary>
public static class UploadConstants
{
    public const string TenantHeaderName = "X-THOR-TENANT-ID";

    public static readonly TimeSpan PresignedUrlLifetime = TimeSpan.FromMinutes(15);
}
