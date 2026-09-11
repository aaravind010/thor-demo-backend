namespace Thor.TaskApi.Models;

/// <summary>A presigned S3 PUT URL scoped to the caller's tenant, plus the object key it targets.</summary>
public sealed record PresignedUploadResponse(string UploadUrl, string ObjectKey, DateTime ExpiresAtUtc);
