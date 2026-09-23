namespace Thor.Api.Exceptions;

/// <summary>No ApiScope with the given scope text exists in the Master metadata DB.</summary>
public sealed class ScopeNotFoundException(string scope) : Exception($"Scope '{scope}' was not found.")
{
    public string Scope { get; } = scope;
}
