namespace Thor.Api.Models;

public sealed record IdentityResponse(
    Guid Id,
    string Source,
    string HrEmployeeId,
    string DisplayName,
    string Email,
    string GivenName,
    string Surname,
    string? Department,
    string? Title,
    string? Company,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
