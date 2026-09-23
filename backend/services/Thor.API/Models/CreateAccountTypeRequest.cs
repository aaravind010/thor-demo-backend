namespace Thor.Api.Models;

public sealed record CreateAccountTypeRequest(string Name, string Description, bool IsHuman);
