namespace Thor.Api.Models;

/// <param name="RawAttributes">The connector's raw attribute JSON; only populated by <c>GET /accounts/{id}</c>, null in list pages.</param>
public sealed record AccountResponse(
    Guid Id,
    Guid SourceId,
    short ConnectorType,
    string NativeId,
    string AccountKind,
    bool IsHuman,
    string DisplayName,
    string SamAccountName,
    string Upn,
    string Email,
    string DomainName,
    string FilerName,
    string NativeAccountId,
    bool IsDeleted,
    bool IsDisabled,
    Guid AccountTypeId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? RawAttributes);
