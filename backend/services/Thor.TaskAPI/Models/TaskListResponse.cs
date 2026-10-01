namespace Thor.TaskApi.Models;

/// <summary>Response shape for <c>GET /task</c>.</summary>
public sealed record TaskListResponse(IReadOnlyList<ConnectorTaskResponse> Tasks);
