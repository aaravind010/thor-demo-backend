namespace Thor.Api.Models;

public sealed record CreateScanConfigRequest(
    string Name,
    string? Description,
    IReadOnlyList<Guid> SourceIds,
    Guid AuthMethodId,
    IReadOnlyList<ScanConnectorConfigValueRequest> ConfigValues);
