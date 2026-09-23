namespace Thor.Api.Models;

public sealed record AccountTypeResponse(Guid Id, string Name, string Description, bool IsHuman);
