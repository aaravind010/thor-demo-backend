namespace Thor.TaskApi.Models;

/// <summary>A request to get the S3 presigned PUT scoped URL to upload file.</summary>
public sealed record PresignedUploadRequest(Guid TaskId, Guid SourceId, string ContentType);
