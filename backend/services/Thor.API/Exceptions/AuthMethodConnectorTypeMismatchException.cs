namespace Thor.Api.Exceptions;

/// <summary>The authentication method's type isn't mapped to the sources' connector type.</summary>
public sealed class AuthMethodConnectorTypeMismatchException(string authenticationType, string sourceConnectorType)
    : Exception($"Authentication type '{authenticationType}' is not supported for source connector type '{sourceConnectorType}'.")
{
    public string AuthenticationType { get; } = authenticationType;

    public string SourceConnectorType { get; } = sourceConnectorType;
}
