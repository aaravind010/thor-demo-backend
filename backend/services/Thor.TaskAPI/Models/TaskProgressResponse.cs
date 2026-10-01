namespace Thor.TaskApi.Models;

/// <summary>Response shape for <c>POST /tasks/{taskId}/progress</c>.</summary>
public sealed record TaskProgressResponse(Guid TaskProgressId);
