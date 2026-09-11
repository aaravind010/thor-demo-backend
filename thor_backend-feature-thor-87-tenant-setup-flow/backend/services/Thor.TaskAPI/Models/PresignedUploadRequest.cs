namespace Thor.TaskApi.Models;


public sealed record PresignedUploadRequest(Guid TaskId, Guid SourceId, string ContentType);
