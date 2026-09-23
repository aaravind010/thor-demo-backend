namespace Thor.Api.Exceptions;

/// <summary>The requested sources don't all share the same connector type.</summary>
public sealed class MixedSourceConnectorTypesException(IReadOnlyList<string> connectorTypes)
    : Exception($"All sources must share the same connector type; found: {string.Join(", ", connectorTypes)}.")
{
    public IReadOnlyList<string> ConnectorTypes { get; } = connectorTypes;
}
