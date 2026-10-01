namespace Thor.Api.Models;

public sealed record AuthenticationFieldResponse(
    Guid Id,
    string Name,
    string DisplayName,
    string InputType,
    string? Description);
