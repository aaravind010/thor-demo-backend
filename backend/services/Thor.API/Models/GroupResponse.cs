namespace Thor.Api.Models;

/// <param name="RawAttributes">The connector's raw attribute JSON; only populated by <c>GET /groups/{id}</c>, null in list pages.</param>
public sealed record GroupResponse(
    Guid Id,
    Guid SourceId,
    short ConnectorType,
    string NativeId,
    string GroupClass,
    string? DisplayName,
    string? Email,
    string? DomainName,
    bool IsLargeGroup,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? RawAttributes);
