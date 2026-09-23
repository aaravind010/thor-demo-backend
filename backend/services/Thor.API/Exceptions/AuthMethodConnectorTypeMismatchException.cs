namespace Thor.Api.Exceptions;

/// <summary>The authentication method's connector type doesn't match the sources' connector type.</summary>
public sealed class AuthMethodConnectorTypeMismatchException(string authMethodConnectorType, string sourceConnectorType)
    : Exception($"Authentication method connector type '{authMethodConnectorType}' does not match source connector type '{sourceConnectorType}'.")
{
    public string AuthMethodConnectorType { get; } = authMethodConnectorType;

    public string SourceConnectorType { get; } = sourceConnectorType;
}
