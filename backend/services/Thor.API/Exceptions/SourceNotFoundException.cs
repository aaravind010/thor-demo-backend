namespace Thor.Api.Exceptions;

/// <summary>One or more of the requested Source ids don't exist in the tenant database.</summary>
public sealed class SourceNotFoundException(IReadOnlyList<Guid> sourceIds)
    : Exception($"Source(s) not found: {string.Join(", ", sourceIds)}.")
{
    public IReadOnlyList<Guid> SourceIds { get; } = sourceIds;
}
