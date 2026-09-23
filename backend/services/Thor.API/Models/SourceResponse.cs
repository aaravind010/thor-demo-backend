namespace Thor.Api.Models;

public sealed record SourceResponse(
    Guid Id,
    short ConnectorType,
    string Name,
    string Config,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
