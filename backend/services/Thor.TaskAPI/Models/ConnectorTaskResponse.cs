namespace Thor.TaskApi.Models;

/// <summary>One claimed task item in the <c>GET /task</c> response.</summary>
public sealed record ConnectorTaskResponse(
    Guid ScanId,
    Guid ScanConfigId,
    Guid TaskId,
    short ConnectorType,
    Guid AuthMethod,
    Guid SourceId);
