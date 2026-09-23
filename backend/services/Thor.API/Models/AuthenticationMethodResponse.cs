namespace Thor.Api.Models;

// Field values are intentionally omitted: AuthenticationValue has no encryption-at-rest or
// per-field secret marking today, so echoing raw values back in a response is avoided.
public sealed record AuthenticationMethodResponse(
    Guid Id,
    Guid TypeId,
    string Name,
    string? Description,
    IReadOnlyList<Guid> FieldIds);
