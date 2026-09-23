namespace Thor.Api.Models;

public sealed record ScanTaskResponse(
    Guid TaskId,
    short ConnectorType,
    Guid SourceId,
    string Status);
