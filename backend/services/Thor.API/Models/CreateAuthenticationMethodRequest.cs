namespace Thor.Api.Models;

public sealed record CreateAuthenticationMethodRequest(
    Guid TypeId,
    string Name,
    string? Description,
    IReadOnlyList<AuthenticationValueRequest> Values);
