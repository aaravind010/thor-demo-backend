namespace Thor.Api.Exceptions;

/// <summary>No ConnectorType with the given id exists in the Master metadata DB.</summary>
public sealed class ConnectorTypeNotFoundException(short connectorType) : Exception($"Connector type '{connectorType}' was not found.")
{
    public short ConnectorType { get; } = connectorType;
}
