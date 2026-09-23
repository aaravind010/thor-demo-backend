namespace Thor.Api.Models;

public sealed record ScanConnectorConfigValueRequest(Guid ConfigId, string Value);
