namespace Thor.Api.Models;

public sealed record CreateSourceRequest(short ConnectorType, string Name, string Config);
