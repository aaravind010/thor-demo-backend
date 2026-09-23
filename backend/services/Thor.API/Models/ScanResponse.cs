namespace Thor.Api.Models;

public sealed record ScanResponse(
    Guid ScanId,
    Guid ScanConfigId,
    string Status,
    IReadOnlyList<ScanTaskResponse> Tasks);
