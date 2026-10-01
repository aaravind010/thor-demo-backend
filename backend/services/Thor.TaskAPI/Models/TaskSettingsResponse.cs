namespace Thor.TaskApi.Models;

/// <summary>Response shape for <c>GET /tasks/{taskId}/settings</c>.</summary>
public sealed record TaskSettingsResponse(
    Guid TaskId,
    Guid ScanId,
    Guid ScanConfigId,
    string Settings,
    string AuthMethod);
